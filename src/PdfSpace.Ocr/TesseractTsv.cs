using System.Globalization;
using PdfSpace.Core;
using PdfSpace.Layout;
namespace PdfSpace.Ocr;

/// <summary>Strict, bounded TSV decoder shared by the native process and WebAssembly providers.</summary>
public static class TesseractTsv
{
    public const int MaximumCharacters = 8 * 1024 * 1024;
    public static PdfOcrLayer Decode(string tsv, OcrImage image, PdfPageState page, string language, string engine)
    {
        if (tsv is null || tsv.Length > MaximumCharacters || image.Width <= 0 || image.Height <= 0) throw new InvalidDataException("Invalid OCR response or raster dimensions.");
        var words = new List<PdfOcrWord>(); var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = new StringReader(tsv);
        var header = reader.ReadLine()?.TrimStart('\uFEFF');
        if (header != "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext") throw new InvalidDataException("Unsupported OCR TSV header.");
        static int Integer(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("Invalid OCR integer.");
        for (var row = reader.ReadLine(); row is not null; row = reader.ReadLine())
        {
            if (row.Length == 0) continue;
            var cells = row.Split('\t', 12); if (cells.Length != 12) throw new InvalidDataException("Invalid OCR row.");
            if (cells[0] != "5") continue;
            var text = cells[11].Trim(); if (text.Length == 0) continue;
            var x = Integer(cells[6]); var y = Integer(cells[7]); var w = Integer(cells[8]); var h = Integer(cells[9]);
            if (!double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || !double.IsFinite(confidence) || confidence is < 0 or > 100 || w <= 0 || h <= 0 || (long)x + w > image.Width || (long)y + h > image.Height) throw new InvalidDataException("OCR word lies outside its raster or has invalid confidence.");
            var first = PageGeometry.ToPage(page, new(x * page.DisplayWidth / image.Width, y * page.DisplayHeight / image.Height));
            var last = PageGeometry.ToPage(page, new((x + w) * page.DisplayWidth / image.Width, (y + h) * page.DisplayHeight / image.Height));
            var key = string.Join(':', cells.Take(5).Skip(1));
            if (!lines.TryGetValue(key, out var line)) { line = lines.Count; lines.Add(key, line); }
            words.Add(new() { Text = text, Bounds = RectD.Between(first, last), Confidence = confidence, Line = line });
            if (words.Count > 20000) throw new InvalidDataException("OCR page exceeds 20,000 words.");
        }
        var result = new PdfOcrLayer { Language = language, Engine = engine, Rotation = page.Rotation, Words = words.ToArray() };
        PdfOcrLayer.Validate(result, page.Width, page.Height); return result;
    }
}
