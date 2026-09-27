namespace PdfSpace.Ocr;

/// <summary>An owned PNG raster. Providers must not persist its contents or send them to a server.</summary>
public sealed record OcrImage(byte[] Png, int Width, int Height, int Dpi);
public interface IOcrEngine
{
    string Name { get; }
    ValueTask ReleaseAsync() => ValueTask.CompletedTask;
    Task<string> RecognizeTsvAsync(OcrImage image, string language, CancellationToken cancellationToken);
}
public sealed class UnavailableOcrEngine : IOcrEngine
{
    public string Name => "Unavailable";
    public Task<string> RecognizeTsvAsync(OcrImage image, string language, CancellationToken cancellationToken) =>
        Task.FromException<string>(new NotSupportedException("Inject an OCR engine for this host. The desktop adapter requires Tesseract 5 and the selected language data."));
}
public sealed record OcrProgress(int Completed, int Total, int PageIndex, string Stage);
