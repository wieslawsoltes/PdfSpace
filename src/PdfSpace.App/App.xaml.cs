using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PdfSpace.Controls;
using PdfSpace.Skia;
using PdfSpace.Storage;
using PdfSpace.Workbench;
using PdfSpace.Viewer;
using SkiaSharp;
using Windows.Storage;
namespace PdfSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private SKTypeface? _font;
    private PdfWorkbench? _workbench;
#if __WASM__
    private DispatcherTimer? _diagnosticTimer;
#endif
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "PdfSpace" };
        _window.Content = new Grid { Background = PdfTheme.Brush("#F7F7F7"), Children = { new TextBlock { Text = "PdfSpace\nOpening your document workspace…", FontSize = 21, TextAlignment = TextAlignment.Center, Foreground = PdfTheme.Brush("#3D3D3D"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        _window.Activate();
        try
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/NotoSans.ttf"));
            using var input = await file.OpenStreamForReadAsync(); using var memory = new MemoryStream(); await input.CopyToAsync(memory);
            using var fontData = SKData.CreateCopy(memory.ToArray()); _font = SKTypeface.FromData(fontData) ?? throw new InvalidOperationException("The application font could not be loaded.");
            PdfTheme.Font = new FontFamily("ms-appx:///Assets/Fonts/NotoSans.ttf#Noto Sans");
#if __WASM__
            IWorkspaceStorage storage = new BrowserWorkspaceStorage();
            PdfSpace.Ocr.IOcrEngine ocr = new BrowserOcrEngine();
            PdfSpace.Pdf.IPdfSecurityProvider security = new BrowserPdfSecurityProvider();
#else
            IWorkspaceStorage storage = new DesktopWorkspaceStorage();
            PdfSpace.Ocr.IOcrEngine ocr = new PdfSpace.Ocr.TesseractProcessEngine();
            PdfSpace.Pdf.IPdfSecurityProvider security = new PdfSpace.Pdf.NativePdfSecurityProvider();
#endif
            var sample = SampleDocument.Create(_font); _workbench = new PdfWorkbench(sample, storage, _font, security, ocr); _window.Content = _workbench;
#if __WASM__
            var diagnostics = BrowserFiles.IsTestMode();
            void Publish()
            {
                BrowserFiles.SetDirty(_workbench.HasUnsavedChanges);
                // Uno's browser bridge deliberately leaves Tab to the DOM while
                // document.body is focused. Keep the native canvas focused when
                // managed focus belongs to the document, including the brief editor-attachment gap.
                // The JS bridge itself never takes focus from an active native text input.
                var canvasFocused = false;
                if (_workbench.XamlRoot is { } root)
                {
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) as DependencyObject;
                    while (focused is not null)
                    {
                        if (focused is PdfViewport) { canvasFocused = true; break; }
                        focused = VisualTreeHelper.GetParent(focused);
                    }
                }
                BrowserFiles.SetCanvasFocus(canvasFocused);
                if (diagnostics && _workbench.XamlRoot is not null)
                {
                    try { BrowserFiles.PublishDiagnostics(_workbench.GetDiagnosticsJson()); }
                    catch (InvalidOperationException) { /* A just-detached visual is absent from the next layout snapshot. */ }
                }
            }
            _workbench.StateChanged += Publish;
            _workbench.GotFocus += (_, _) => Publish();
            _workbench.LostFocus += (_, _) => _workbench.DispatcherQueue.TryEnqueue(Publish);
            _workbench.Loaded += (_, _) => Publish();
            if (diagnostics)
            {
                // Panel construction changes desired size before controls receive arranged bounds.
                // Publish geometry after layout, not just when the underlying document state changes.
                _workbench.LayoutUpdated += (_, _) => Publish();
                // Scroll animation changes render transforms without a layout pass. A stale
                // geometry snapshot can appear stable to acceptance tooling while the panel moves.
                // This read-only sampler exists only for ?test=1; it never invalidates the UI.
                _diagnosticTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _diagnosticTimer.Tick += (_, _) => Publish();
                _diagnosticTimer.Start();
            }
#endif
            _window.Closed += (_, _) =>
            {
#if __WASM__
                _diagnosticTimer?.Stop(); _diagnosticTimer = null;
#endif
                _workbench.Dispose(); _font?.Dispose();
            };
            await _workbench.OfferRecoveryAsync();
        }
        catch (Exception ex)
        {
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "PdfSpace could not start\n\n" + ex, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32), FontSize = 14 } };
            Console.Error.WriteLine(ex);
        }
    }
}
