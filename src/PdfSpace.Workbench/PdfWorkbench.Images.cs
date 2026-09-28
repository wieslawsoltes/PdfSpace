using System.Globalization;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private PdfImageOccurrence[] _nativeImages = [];
    private readonly WorkspaceSnapshotStamp _imageSnapshot = new();
    private Guid _imagePageId;
    private int _imageSelection = -1;
    private bool _refreshingImages;
    private byte[]? _pendingImage;
    private DocumentContext? _pendingImageContext;
    private void ShowNativeImages()
    {
        _right = "Original images"; UseTool(PdfTool.EditImage); RefreshNativeImages(); RefreshRight(); AdaptLayout();
        ShowStatus("Select an image on the page. Drag to move; drag a handle to resize. Release applies one native PDF edit.");
    }
    private void RefreshNativeImages()
    {
        if (_refreshingImages || _active is null || Session.Tool != PdfTool.EditImage ||
            (_imageSnapshot.Matches(Session.Document) && _imagePageId == Session.Page.Id)) return;
        _refreshingImages = true;
        try
        {
            _imageSnapshot.Remember(Session.Document); _imagePageId = Session.Page.Id; _imageSelection = -1;
            _nativeImages = PdfImageEditor.Read(Session.Document, Session.CurrentPage);
            Viewport.SetNativeImages(_nativeImages.Select((image, index) => new NativeImageTarget(index, image.Bounds,
                [image.UnitToPage.Transform(new(0, 0)), image.UnitToPage.Transform(new(1, 0)), image.UnitToPage.Transform(new(1, 1)), image.UnitToPage.Transform(new(0, 1))], image.Editable)).ToArray());
        }
        catch (Exception ex) { _nativeImages = []; Viewport.SetNativeImages([]); ShowStatus(ex.Message, true); }
        finally { _refreshingImages = false; }
    }
    private void SelectSourceImage(DocumentContext context, int index)
    {
        if (_active != context) return; _imageSelection = index; _right = "Original images"; RefreshRight(); AdaptLayout();
    }
    private void EditSourceImage(DocumentContext context, int index, Func<PdfWorkspace, PdfImageOccurrence, PdfWorkspace> edit, string label)
    {
        if (_active != context || !_imageSnapshot.Matches(context.Session.Document) || (uint)index >= _nativeImages.Length)
            throw new InvalidOperationException("The selected image changed. Select it again.");
        var target = _nativeImages[index]; context.Viewport.FinishText(true);
        context.Session.Execute(label, document => edit(document, target));
        RefreshNativeImages(); _imageSelection = index < _nativeImages.Length ? index : -1; Viewport.SelectNativeImage(_imageSelection);
        RefreshRight(); ShowStatus(label + ". Native content updated; undo restores the prior source. This is not secure redaction.");
    }
    private void BuildNativeImages(StackPanel content)
    {
        RefreshNativeImages();
        content.Children.Add(Paragraph("Select a source image on the page, then drag or edit its geometry. Each shared-resource placement is isolated before changing it."));
        content.Children.Add(new PdfCommandButton("Add image", PdfIconKind.Plus, () => Run(ChooseInsertImageAsync)));
        content.Children.Add(new PdfCommandButton("Open native object example", PdfIconKind.File, () =>
        {
            Safe(() => { AddDocument(NativeObjectSample.Create()); ShowNativeImages(); });
        }));
        content.Children.Add(Paragraph($"{_nativeImages.Length} native image placements on this page", 11));
        var context = _active;
        foreach (var (image, index) in _nativeImages.Select((image, index) => (image, index)).Take(150))
        {
            var button = new PdfCommandButton($"Select image {index + 1}", PdfIconKind.Image, () =>
            { _imageSelection = index; Viewport.SelectNativeImage(index); RefreshRight(); });
            button.Label = $"Image {index + 1} · {image.PixelWidth} × {image.PixelHeight}"; button.Select(index == _imageSelection); content.Children.Add(button);
        }
        if ((uint)_imageSelection >= _nativeImages.Length) return;
        var selected = _nativeImages[_imageSelection]; var selectedIndex = _imageSelection;
        content.Children.Add(PdfTheme.Divider()); content.Children.Add(Paragraph("IMAGE GEOMETRY · PDF POINTS", 10));
        var x = new PdfTextField("Image X") { Text = selected.Bounds.X.ToString("0.###", CultureInfo.InvariantCulture) };
        var y = new PdfTextField("Image Y") { Text = selected.Bounds.Y.ToString("0.###", CultureInfo.InvariantCulture) };
        var w = new PdfTextField("Image width") { Text = selected.Bounds.Width.ToString("0.###", CultureInfo.InvariantCulture) };
        var h = new PdfTextField("Image height") { Text = selected.Bounds.Height.ToString("0.###", CultureInfo.InvariantCulture) };
        var fields = new Grid { ColumnSpacing = 8, RowSpacing = 7, ColumnDefinitions = { new(), new() }, RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } } };
        PdfTheme.Place(fields, x); PdfTheme.Place(fields, y, column: 1); PdfTheme.Place(fields, w, row: 1); PdfTheme.Place(fields, h, row: 1, column: 1); content.Children.Add(fields);
        static double Number(PdfTextField field) => double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : throw new ArgumentException("Enter finite image coordinates and dimensions.");
        void Command(string name, PdfIconKind icon, Func<PdfWorkspace, PdfImageOccurrence, PdfWorkspace> edit)
        {
            content.Children.Add(new PdfCommandButton(name, icon, () => Safe(() => EditSourceImage(context, selectedIndex, edit, name))) { IsEnabled = selected.Editable });
        }
        Command("Apply image geometry", PdfIconKind.Check, (document, image) => PdfImageEditor.SetBounds(document, image, new(Number(x), Number(y), Number(w), Number(h))));
        content.Children.Add(new PdfCommandButton("Replace image", PdfIconKind.Image, () => Run(async () =>
        {
            var snapshot = context.Session.Document; var file = await _storage.OpenImageAsync(); if (file is null) return;
            if (!ReferenceEquals(snapshot, context.Session.Document)) throw new InvalidOperationException("The document changed while choosing the image.");
            EditSourceImage(context, selectedIndex, (document, image) => PdfImageEditor.Replace(document, image, file.Bytes), "Replace image");
        })) { IsEnabled = selected.Editable });
        Command("Restore image proportions", PdfIconKind.FitPage, PdfImageEditor.RestoreAspectRatio);
        Command("Duplicate image", PdfIconKind.Copy, (document, image) => PdfImageEditor.Duplicate(document, image, new PointD(24, 24)));
        Command("Rotate image left", PdfIconKind.Rotate, (document, image) => PdfImageEditor.Rotate(document, image, -90));
        Command("Rotate image right", PdfIconKind.Rotate, (document, image) => PdfImageEditor.Rotate(document, image, 90));
        Command("Flip image horizontally", PdfIconKind.Left, (document, image) => PdfImageEditor.Flip(document, image, true));
        Command("Flip image vertically", PdfIconKind.Up, (document, image) => PdfImageEditor.Flip(document, image, false));
        content.Children.Add(new PdfCommandButton("Delete source image", PdfIconKind.Trash, () => Run(DeleteSourceImageAsync)) { IsEnabled = selected.Editable });
        content.Children.Add(Paragraph(selected.Limitation, 11));
        content.Children.Add(Paragraph("PNG transparency and all EXIF orientations are supported. Eligible JPEGs retain their original compression; other images are normalized to sRGB.", 11));
    }
    private async Task DeleteSourceImageAsync()
    {
        var context = _active; var snapshot = Session.Document; var index = _imageSelection;
        if ((uint)index >= _nativeImages.Length) return;
        if (!await _dialogs.ConfirmAsync("Delete this image placement?", "Removes its native drawing operation, not every use of the shared image. Original resources and workspaces may still contain the image. Use the separate redaction workflow to sanitize content.", "Delete image")) return;
        if (!ReferenceEquals(snapshot, context.Session.Document)) throw new InvalidOperationException("The document changed while confirming deletion.");
        EditSourceImage(context, index, PdfImageEditor.Delete, "Delete source image");
    }
    private async Task ChooseInsertImageAsync()
    {
        var context = _active; var file = await _storage.OpenImageAsync(); if (file is null || _active != context) return;
        _pendingImage = file.Bytes; _pendingImageContext = context;
        UseTool(PdfTool.InsertImage); ShowStatus("Drag a rectangle on the page to insert the selected image.");
    }
    private void InsertSourceImage(DocumentContext context, RectD bounds)
    {
        if (_pendingImage is null || context != _pendingImageContext || _active != context) return;
        var bytes = _pendingImage; _pendingImage = null; _pendingImageContext = null;
        var index = context.Session.CurrentPage;
        context.Session.Execute("Insert source image", document => PdfImageEditor.Insert(document, index, bytes, bounds));
        ShowNativeImages();
    }
}
