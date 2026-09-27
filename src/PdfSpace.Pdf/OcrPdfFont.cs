using System.Buffers.Binary;
using System.Text;
using PdfSharp.Pdf;
using SkiaSharp;
namespace PdfSpace.Pdf;

/// <summary>Embedded TrueType CID font with an explicit Unicode map for invisible, interoperable OCR text.</summary>
internal sealed class OcrPdfFont
{
    private readonly Dictionary<Rune, ushort> _codes = [];
    private readonly Dictionary<Rune, double> _widths = [];
    public PdfDictionary Font { get; }
    public double Ascent { get; }
    public double Descent { get; }
    public OcrPdfFont(PdfDocument document, SKTypeface typeface, IEnumerable<string> texts)
    {
        var runes = texts.SelectMany(text => text.EnumerateRunes()).Distinct().ToArray();
        if (runes.Length > 60000) throw new InvalidDataException("Too many distinct characters in the OCR layer.");
        using var font = new SKFont(typeface, 1000);
        Ascent = -font.Metrics.Ascent / 1000.0; Descent = font.Metrics.Descent / 1000.0;
        using var stream = typeface.OpenStream(out var collectionIndex) ?? throw new NotSupportedException("OCR PDF export requires an embeddable TrueType font.");
        if (stream.Length > 16 * 1024 * 1024 || stream.Length < 12 || collectionIndex != 0) throw new NotSupportedException("Unsupported OCR fallback font.");
        var fontBytes = new byte[stream.Length];
        if (stream.Read(fontBytes, fontBytes.Length) != fontBytes.Length || BinaryPrimitives.ReadUInt32BigEndian(fontBytes) != 0x00010000)
            throw new NotSupportedException("OCR PDF export currently supports TrueType sfnt fonts, not CFF or font collections.");
        var fontFile = StreamObject(document, fontBytes); fontFile.Elements.SetInteger("/Length1", fontBytes.Length);
        var descriptor = Object(document); descriptor.Elements.SetName("/Type", "/FontDescriptor"); descriptor.Elements.SetName("/FontName", "/PdfSpaceOCR");
        descriptor.Elements.SetInteger("/Flags", 32); descriptor.Elements["/FontBBox"] = PdfObjects.Numbers(document, -1000, -1000, 3000, 3000);
        descriptor.Elements.SetReal("/ItalicAngle", 0); descriptor.Elements.SetReal("/Ascent", Ascent * 1000); descriptor.Elements.SetReal("/Descent", -Descent * 1000);
        descriptor.Elements.SetReal("/CapHeight", Ascent * 1000); descriptor.Elements.SetReal("/StemV", 80); descriptor.Elements["/FontFile2"] = fontFile.Reference!;
        var glyphMap = new byte[(runes.Length + 1) * 2]; var widths = new PdfArray(document);
        var mappings = new List<string>();
        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i]; var code = checked((ushort)(i + 1)); var text = rune.ToString(); var glyphs = font.GetGlyphs(text);
            if (glyphs.Length != 1 || glyphs[0] == 0) throw new NotSupportedException($"The OCR export font does not contain U+{rune.Value:X4}. Correct the recognized word or provide a suitable font.");
            _codes[rune] = code; var advance = Math.Max(1, font.MeasureText(text)); _widths[rune] = advance / 1000.0;
            BinaryPrimitives.WriteUInt16BigEndian(glyphMap.AsSpan(code * 2), glyphs[0]); widths.Elements.Add(new PdfReal(advance));
            mappings.Add($"<{code:X4}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))}>\n");
        }
        var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /PdfSpaceOCRUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in mappings.Chunk(100)) { cmap.Append(chunk.Length).Append(" beginbfchar\n"); foreach (var mapping in chunk) cmap.Append(mapping); cmap.Append("endbfchar\n"); }
        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        var descendant = Object(document); descendant.Elements.SetName("/Type", "/Font"); descendant.Elements.SetName("/Subtype", "/CIDFontType2"); descendant.Elements.SetName("/BaseFont", "/PdfSpaceOCR");
        var system = new PdfDictionary(document); system.Elements.SetString("/Registry", "Adobe"); system.Elements.SetString("/Ordering", "Identity"); system.Elements.SetInteger("/Supplement", 0);
        descendant.Elements["/CIDSystemInfo"] = system; descendant.Elements["/FontDescriptor"] = descriptor.Reference!;
        descendant.Elements["/CIDToGIDMap"] = StreamObject(document, glyphMap).Reference!;
        var widthArray = new PdfArray(document); widthArray.Elements.Add(new PdfInteger(1)); widthArray.Elements.Add(widths); descendant.Elements["/W"] = widthArray;
        Font = Object(document); Font.Elements.SetName("/Type", "/Font"); Font.Elements.SetName("/Subtype", "/Type0"); Font.Elements.SetName("/BaseFont", "/PdfSpaceOCR"); Font.Elements.SetName("/Encoding", "/Identity-H");
        var descendants = new PdfArray(document); descendants.Elements.Add(descendant.Reference!); Font.Elements["/DescendantFonts"] = descendants;
        Font.Elements["/ToUnicode"] = StreamObject(document, Encoding.ASCII.GetBytes(cmap.ToString())).Reference!;
    }
    public string Encode(string text) => string.Concat(text.EnumerateRunes().Select(rune => _codes[rune].ToString("X4")));
    public double Advance(string text) => text.EnumerateRunes().Sum(rune => _widths[rune]);
    private static PdfDictionary Object(PdfDocument document) { var value = new PdfDictionary(document); document.Internals.AddObject(value); return value; }
    private static PdfDictionary StreamObject(PdfDocument document, byte[] bytes) { var value = Object(document); value.CreateStream(bytes); return value; }
}
