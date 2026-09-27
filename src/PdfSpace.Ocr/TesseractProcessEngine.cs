using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
namespace PdfSpace.Ocr;

/// <summary>Native Tesseract 5 adapter. Pixels enter stdin and TSV leaves stdout: no document temp files are created.</summary>
[UnsupportedOSPlatform("browser")]
public sealed class TesseractProcessEngine(string executable = "tesseract") : IOcrEngine
{
    public string Name => "Tesseract native";
    public async Task<string> RecognizeTsvAsync(OcrImage image, string language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (language is not ("eng" or "pol" or "deu")) throw new ArgumentException("Unsupported OCR language.", nameof(language));
        if (image.Png.Length > 32 * 1024 * 1024) throw new InvalidDataException("OCR input exceeds 32 MB.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "stdin", "stdout", "-l", language, "--oem", "1", "--psm", "3", "--dpi", image.Dpi.ToString(System.Globalization.CultureInfo.InvariantCulture), "tsv" }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("Tesseract did not start."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new NotSupportedException("Install Tesseract 5 and the selected language pack, and make tesseract available on PATH.", ex); }
        using var kill = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stdout = ReadBounded(process.StandardOutput, TesseractTsv.MaximumCharacters, token);
        var stderr = ReadBounded(process.StandardError, 65536, token);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(image.Png, token); process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException("Tesseract could not recognize this page. Verify that the selected language data is installed. " + (await stderr)[..Math.Min(300, (await stderr).Length)]);
            return await stdout;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr); } catch { /* Observe cancelled/faulted readers; preserve the original exception. */ }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[8192];
        for (var length = await reader.ReadAsync(buffer, token); length > 0; length = await reader.ReadAsync(buffer, token))
        { if (text.Length + length > limit) throw new InvalidDataException("OCR process output exceeded its limit."); text.Append(buffer, 0, length); }
        return text.ToString();
    }
}
