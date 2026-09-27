using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PdfSpace.Controls;
using PdfSpace.Skia;
using PdfSpace.Storage;
using PdfSpace.Workbench;
using SkiaSharp;
using Windows.Storage;
namespace PdfSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private SKTypeface? _font;
    private PdfWorkbench? _workbench;
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
#else
            IWorkspaceStorage storage = new DesktopWorkspaceStorage();
#endif
            var sample = SampleDocument.Create(_font); _workbench = new PdfWorkbench(sample, storage, _font); _window.Content = _workbench;
#if __WASM__
            var diagnostics = BrowserFiles.IsTestMode();
            void Publish()
            {
                BrowserFiles.SetDirty(_workbench.HasUnsavedChanges);
                if (diagnostics && _workbench.XamlRoot is not null)
                {
                    try { BrowserFiles.PublishDiagnostics(_workbench.GetDiagnosticsJson()); } catch { /* Layout may not yet be attached during the first frame. */ }
                }
            }
            _workbench.StateChanged += Publish;
            _workbench.Loaded += (_, _) => Publish();
#endif
            _window.Closed += (_, _) => { _workbench.Dispose(); _font?.Dispose(); };
            await _workbench.OfferRecoveryAsync();
        }
        catch (Exception ex)
        {
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "PdfSpace could not start\n\n" + ex, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32), FontSize = 14 } };
            Console.Error.WriteLine(ex);
        }
    }
}
