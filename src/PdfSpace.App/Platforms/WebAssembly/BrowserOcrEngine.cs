using System.Runtime.InteropServices.JavaScript;
using PdfSpace.Ocr;
namespace PdfSpace.App;

internal sealed class BrowserOcrEngine : IOcrEngine
{
    public string Name => "Tesseract.js 7.0.0 / LSTM";
    public async Task<string> RecognizeTsvAsync(OcrImage image, string language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        var task = BrowserOcr.Recognize(id, Convert.ToBase64String(image.Png), language, image.Dpi);
        using var registration = cancellationToken.Register(() => BrowserOcr.Cancel(id));
        try { var result = await task; cancellationToken.ThrowIfCancellationRequested(); return result; }
        catch when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }
    public ValueTask ReleaseAsync() { BrowserOcr.Release(); return ValueTask.CompletedTask; }
}
internal static partial class BrowserOcr
{
    [JSImport("globalThis.pdfSpaceOcr.recognize")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Recognize(string id, string png, string language, int dpi);
    [JSImport("globalThis.pdfSpaceOcr.cancel")] internal static partial void Cancel(string id);
    [JSImport("globalThis.pdfSpaceOcr.release")] internal static partial void Release();
}
