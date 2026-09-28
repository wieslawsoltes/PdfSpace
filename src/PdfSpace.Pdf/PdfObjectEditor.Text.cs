using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSpace.Core;
using SkiaSharp;
using static PdfSpace.Pdf.PdfObjectScanner;

namespace PdfSpace.Pdf;
public static partial class PdfObjectEditor
{
    /// <summary>Explicitly reflows replacement text into a page-aligned box with an embedded font.
    /// This is a new block layout, not reconstruction of the original word-processing paragraph model.</summary>
    public static PdfWorkspace ReplaceTextBlock(PdfWorkspace workspace, PdfPageObject target, string text, SKTypeface typeface, double size, RectD box, uint color = 0xFF242424)
    {
        var lines = LayoutText(text, typeface, size, box);
        return Edit(workspace, [target], (native, items) => ChangeScopes(native, items, (content, resources, item) =>
        {
            if (item.Object.Kind != PdfPageObjectKind.Text)
                throw new ArgumentException("Select a native text object.");
            var body = TextOperators(native, resources, lines, typeface, size, box, color);
            Replace(content, item, "q\n" + Matrix(item.Object.LocalToPage.Inverse()), body, "Q\n", true);
        }));
    }

    public static PdfWorkspace InsertText(PdfWorkspace workspace, int pageIndex, string text, SKTypeface typeface, double size, RectD box, uint color = 0xFF242424)
    {
        var lines = LayoutText(text, typeface, size, box);
        return InsertNative(workspace, pageIndex, (native, _, resources, logical) => Matrix(logical.Inverse()) + TextOperators(native, resources, lines, typeface, size, box, color));
    }

    private static string[] LayoutText(string text, SKTypeface typeface, double size, RectD box)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typeface);
        ValidateBounds(box);
        if (!double.IsFinite(size) || size is < 1 or > 1000 || box.Width < 1 || box.Height < 1 || text.Length > 100000)
            throw new ArgumentException("Use a positive text box, font size 1–1,000, and at most 100,000 characters.");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Normalize(NormalizationForm.FormC);
        // Do not silently present unshaped complex text as a faithful replacement.
        foreach (var rune in text.EnumerateRunes())
            if ((rune.Value >= 0x590 && rune.Value <= 0x109F) || (rune.Value >= 0x1780 && rune.Value <= 0x18AF) || Rune.GetUnicodeCategory(rune)is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark || Rune.IsControl(rune) && rune.Value != 10)
                throw new NotSupportedException("This explicit block-layout mode requires left-to-right, independently positioned glyphs. Existing complex-script objects can still be moved without changing their shaping.");
        using var font = new SKFont(typeface, (float)size);
        var output = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            var width = 0d;
            var lastBreak = -1;
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var s = rune.ToString();
                var advance = font.MeasureText(s);
                if (advance > box.Width)
                    throw new ArgumentException("A glyph is wider than the selected text box.");
                if (width + advance > box.Width && line.Length > 0)
                {
                    if (lastBreak >= 0)
                    {
                        output.Add(line.ToString(0, lastBreak).TrimEnd());
                        var remaining = line.ToString(lastBreak + 1, line.Length - lastBreak - 1);
                        line.Clear().Append(remaining);
                        width = font.MeasureText(remaining);
                    }
                    else
                    {
                        output.Add(line.ToString());
                        line.Clear();
                        width = 0;
                    }

                    lastBreak = -1;
                }

                if (rune.Value == 32)
                    lastBreak = line.Length;
                line.Append(s);
                width += advance;
            }

            output.Add(line.ToString());
        }

        if (output.Count * size * 1.25 > box.Height + .01)
            throw new ArgumentException("Replacement text overflows the selected box. Increase its height or reduce the font size.");
        if (output.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Use Delete objects to remove an empty text block.");
        return output.ToArray();
    }

    private static string TextOperators(PdfDocument native, PdfDictionary resources, string[] lines, SKTypeface typeface, double size, RectD box, uint color)
    {
        var font = new OcrPdfFont(native, typeface, lines);
        var name = PdfContentGraph.AddResource(native, resources, "/Font", font.Font);
        var result = new StringBuilder(Color(color, false));
        result.Append("BT\n0 Tc 0 Tw 100 Tz 0 Ts 0 Tr\n").Append(name).Append(' ').Append(F(size)).Append(" Tf\n");
        for (var i = 0; i < lines.Length; i++)
            result.Append("1 0 0 -1 ").Append(F(box.X)).Append(' ').Append(F(box.Y + size + i * size * 1.25)).Append(" Tm\n<").Append(font.Encode(lines[i])).Append("> Tj\n");
        return result.Append("ET\n").ToString();
    }
}
