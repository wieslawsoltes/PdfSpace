using System.Text;
using System.Globalization;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench : UserControl, IDisposable
{
    private sealed record DocumentContext(EditorSession Session, PdfViewport Viewport, PdfDocumentTab Tab)
    { public PdfSearchIndex Search { get; } = new(); }
    private readonly List<DocumentContext> _documents = [];
    private DocumentContext _active = null!;
    private readonly IWorkspaceStorage _storage;
    private readonly IPdfSecurityProvider _security;
    private readonly SKTypeface _typeface;
    private readonly PdfSpace.Ocr.IOcrEngine _ocr;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private bool _savingRecovery, _saveAgain, _disposed;
    private string _mode = "All tools", _right = "", _statusText = "All files stay on your device.";
    private bool _leftOpen = true, _home;
    private readonly PdfDialogHost _dialogs = new();
    public EditorSession Session => _active.Session;
    public PdfViewport Viewport => _active.Viewport;
    public string ActiveMode => _mode;
    public string RightPanel => _right;
    public string Status => _statusText;
    public int DocumentCount => _documents.Count;
    public event Action? StateChanged;
    public PdfWorkbench(PdfWorkspace initial, IWorkspaceStorage storage, SKTypeface typeface, IPdfSecurityProvider? security = null, PdfSpace.Ocr.IOcrEngine? ocr = null)
    {
        _storage = storage; _typeface = typeface; _security = security ?? new NativePdfSecurityProvider(); _ocr = ocr ?? new PdfSpace.Ocr.UnavailableOcrEngine();
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        BuildShell(); AddDocument(initial);
        _autosave.Tick += async (_, _) => { _autosave.Stop(); await SaveRecoveryAsync(); };
        SizeChanged += (_, _) => { AdaptLayout(); StateChanged?.Invoke(); };
        KeyDown += Keyboard;
        AllowDrop = true;
        DragOver += (_, e) => { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy; };
        Drop += async (_, e) =>
        {
            try
            {
                if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                    foreach (var item in await e.DataView.GetStorageItemsAsync())
                        if (item is Windows.Storage.StorageFile file && (file.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || file.FileType.Equals(".pdfspace", StringComparison.OrdinalIgnoreCase)))
                        { using var stream = await file.OpenStreamForReadAsync(); if (stream.Length > WorkspaceJson.MaximumSourceBytes * 2L) throw new InvalidDataException("This file is too large."); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes); await OpenFileAsync(new(file.Name, bytes.ToArray())); }
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        };
    }
    public void AddDocument(PdfWorkspace document)
    {
        if (_documents.Count >= 8) throw new InvalidOperationException("Close a document before opening another. This version keeps up to eight documents open.");
        var session = new EditorSession(document); var viewport = new PdfViewport(session); viewport.Renderer.Typeface = _typeface;
        var tab = new PdfDocumentTab(document.Title); var context = new DocumentContext(session, viewport, tab);
        _documents.Add(context); _tabs.Children.Add(tab);
        tab.Activated += () => Activate(context); tab.CloseRequested += () => Run(() => CloseAsync(context));
        session.Changed += (_, _) => { if (_active == context) { RefreshData(); _autosave.Stop(); if (!context.Session.Document.IsSensitive) _autosave.Start(); } UpdateTabs(); };
        session.ViewChanged += (_, _) => { if (_active == context) { var prior = _imagePageId; RefreshNativeImages(); if (_right == "Original images" && prior != _imagePageId) RefreshRight(); UpdateChrome(); } };
        viewport.ViewChanged += () =>
        {
            if (_active != context) return;
            var previousPage = _imagePageId;
            RefreshNativeImages();
            // The viewport is subscribed to Session.ViewChanged before the
            // workbench. Its callback can update the descriptors first, so
            // refresh the inspector here rather than missing the page change.
            if (_right == "Original images" && previousPage != _imagePageId) RefreshRight();
            UpdateChrome();
        };
        viewport.StatusChanged += text => ShowStatus(text);
        viewport.NoteRequested += (index, point) => Run(async () =>
        {
            var text = await _dialogs.PromptAsync("Add a comment", "Share a thought, question or suggested change.", multiline: true, acceptLabel: "Post");
            if (!string.IsNullOrWhiteSpace(text)) { session.Navigate(index); session.AddAnnotation(new Annotation { Kind = AnnotationKind.Note, Bounds = new(point.X, point.Y, 23, 23), Text = text, Color = session.Color }, index); OpenRight("Comments"); }
        });
        viewport.NativeImageSelected += index => SelectSourceImage(context, index);
        viewport.NativeImageChanged += (index, bounds) => Safe(() => EditSourceImage(context, index, (document, image) => PdfImageEditor.SetBounds(document, image, bounds), "Transform source image"));
        viewport.NativeImageDeleteRequested += _ => Run(DeleteSourceImageAsync);
        viewport.NativeImageInsertRequested += bounds => Safe(() => InsertSourceImage(context, bounds));
        viewport.ContextRequested += point => ShowContextMenu(point);
        viewport.FieldRequested += field => Run(() => FillFieldAsync(context, field));
        viewport.FieldCreated += (kind, bounds) => Run(() => CreateFieldAsync(context, kind, bounds));
        viewport.LinkCreated += bounds => Run(() => CreateLinkAsync(context, bounds));
        viewport.LinkRequested += annotation => Run(() => FollowLinkAsync(context, annotation));
        Activate(context);
    }
    private void Activate(DocumentContext context)
    {
        if (_active is not null) _active.Viewport.FinishText(true);
        _active = context; _imageSnapshot = null; _nativeImages = []; _imageSelection = -1; _home = false; _documentHost.Children.Clear(); _documentHost.Children.Add(context.Viewport);
        _organizerHost.Content = new PdfThumbnailView(context.Viewport) { OrganizeMode = true };
        BuildLeft(); RefreshData(); UpdateModeVisibility(); ShowStatus(Session.Document.IsSensitive ? "Unlocked protected PDF: automatic recovery disabled. Workspace copies would be unencrypted." : "All files stay on your device.");
    }
    private async Task CloseAsync(DocumentContext context)
    {
        if (context.Session.IsDirty && !await _dialogs.ConfirmAsync("Close document?", "Unsaved workspace changes will be closed. Export an editable .pdfspace workspace to keep them permanently.", "Close document")) return;
        if (_ocrContext == context) _ocrCancellation?.Cancel();
        if (_pendingImageContext == context) { _pendingImage = null; _pendingImageContext = null; }
        context.Search.Clear(); context.Viewport.Dispose(); _documents.Remove(context); _tabs.Children.Remove(context.Tab);
        if (_documents.Count == 0) AddDocument(new PdfWorkspace());
        else if (_active == context) Activate(_documents[^1]);
        UpdateTabs();
    }
    private void UpdateTabs() { foreach (var d in _documents) d.Tab.Update(d.Session.Document.Title, d == _active && !_home, d.Session.IsDirty); }
    public void ShowStatus(string text, bool error = false)
    { _statusText = text; _status.Text = text; _status.Foreground = PdfTheme.Brush(error ? "#B12620" : "#686868"); StateChanged?.Invoke(); }
    private void RefreshData()
    {
        UpdateTabs(); RefreshNativeImages(); UpdateChrome(); RefreshRight(); (_organizerHost.Content as PdfThumbnailView)?.Invalidate();
    }
    private PdfWorkspace? _chromeDocument;
    private void UpdateChrome()
    {
        if (_active is null) return;
        if (XamlRoot is null || !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), _pageField)) _pageField.Text = (Session.CurrentPage + 1).ToString(CultureInfo.InvariantCulture); _pageTotal.Text = "/ " + Session.Document.Pages.Length;
        _zoomLabel.Text = $"{Viewport.Zoom * 100:F0}%";
        _undo.IsEnabled = Session.CanUndo; _redo.IsEnabled = Session.CanRedo;
        foreach (var (tool, button) in _toolButtons) button.Select(Session.Tool == tool);
        _selectionBar.Visibility = Session.SelectedAnnotation is not null && !_home && _mode != "Organize pages" ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(_chromeDocument, Session.Document))
        {
            _chromeDocument = Session.Document;
            _documentInfo.Text = $"{Session.Document.Pages.Length} pages  ·  {Session.Document.AnnotationCount} annotations  ·  Local only";
        }
        StateChanged?.Invoke();
    }
    private void SetMode(string mode)
    { _mode = mode; _home = false; _leftOpen = true; BuildLeft(); UpdateModeVisibility(); AdaptLayout(); }
    private void UpdateModeVisibility()
    {
        _homeHost.Visibility = _home ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = _home ? Visibility.Collapsed : Visibility.Visible;
        _organizerHost.Visibility = _mode == "Organize pages" ? Visibility.Visible : Visibility.Collapsed;
        _documentHost.Visibility = _mode == "Organize pages" ? Visibility.Collapsed : Visibility.Visible;
        _quickTools.Visibility = _mode == "Organize pages" ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (name, border) in _modeTabs) border.BorderBrush = PdfTheme.Brush(name == _mode ? "#1473E6" : "#00FFFFFF");
        UpdateTabs(); UpdateChrome();
    }
    private void UseTool(PdfTool tool)
    {
        Viewport.FinishText(true); Viewport.CancelGesture(); Session.SetTool(tool);
        if (_mode == "Organize pages") SetMode("Edit");
        ShowStatus(tool switch { PdfTool.FillForm => "Click a field to fill it. Values are saved as native AcroForm data in Export PDF.", PdfTool.FormText or PdfTool.FormCheckBox or PdfTool.FormChoice => "Drag a rectangle on the page to create a real PDF form field.", PdfTool.Redact => "Mark sensitive areas, then apply raster redactions. Marks alone do not remove data.", PdfTool.Link => "Drag a clickable area, then choose a web address or page number.", PdfTool.Hand => "Drag to pan. Ctrl+wheel zooms around the pointer.", PdfTool.Select => "Select annotations or drag across text. Double-click an added text box to edit.", PdfTool.Text => "Click on a page to add text. Click outside the text box to apply.", PdfTool.Note => "Click on a page to place a comment.", PdfTool.Signature => "Draw your signature. This creates a visual mark, not a digital certificate signature.", PdfTool.Crop => "Drag a crop rectangle. Cropping hides content; it does not securely remove it.", PdfTool.Measure => "Drag between two points to measure the distance in page units.", _ => "Drag on the page to add " + tool.ToString().ToLowerInvariant() + "." });
    }
    private async void Run(Func<Task> action)
    { try { await action(); } catch (Exception ex) { ShowStatus(ex.Message, true); } }
    private void Safe(Action action)
    { try { action(); } catch (Exception ex) { ShowStatus(ex.Message, true); } }
    private async Task SaveRecoveryAsync()
    {
        if (_disposed || Session.Document.IsSensitive) return;
        if (_savingRecovery) { _saveAgain = true; return; }
        _savingRecovery = true;
        try
        {
            do
            {
                _saveAgain = false; var document = Session.Document;
                if (document.IsSensitive) return;
                await _storage.WriteRecoveryAsync(WorkspaceJson.Save(document));
                if (ReferenceEquals(document, Session.Document)) ShowStatus("Recovery copy saved on this device. Export a workspace for a permanent copy.");
                else _saveAgain = true;
            } while (_saveAgain && !_disposed);
        }
        catch (Exception ex) { ShowStatus("Recovery could not be saved: " + ex.Message + ". Export your workspace now.", true); }
        finally { _savingRecovery = false; }
    }
    public async Task OfferRecoveryAsync()
    {
        try
        {
            var json = await _storage.ReadRecoveryAsync(); if (string.IsNullOrWhiteSpace(json)) return;
            if (await _dialogs.ConfirmAsync("Restore your previous workspace?", "A recovery copy is available on this device. Your source PDF has not been changed.", "Restore")) AddDocument(PdfDocumentEngine.PrepareWorkspace(WorkspaceJson.Load(json)));
        }
        catch (Exception ex) { ShowStatus("Recovery is unavailable: " + ex.Message, true); }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _ocrCancellation?.Cancel(); _autosave.Stop(); foreach (var d in _documents) { d.Search.Clear(); d.Viewport.Dispose(); } _documents.Clear(); }
}
