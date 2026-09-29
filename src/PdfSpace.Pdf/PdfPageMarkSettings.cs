using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace PdfSpace.Pdf;

public enum PdfPageMarkKind { HeaderFooter, Watermark }
public enum PdfPageNumberStyle { Decimal, LowerRoman, UpperRoman }

/// <summary>Immutable native page-mark settings. Numbers advance in ascending selected-page order.
/// Dates are explicit text, not a clock dependency. This is not Adobe private metadata.</summary>
public sealed record PdfPageMarkSettings
{
    public PdfPageMarkKind Kind { get; init; }
    public string HeaderLeft { get; init; } = "";
    public string HeaderCenter { get; init; } = "";
    public string HeaderRight { get; init; } = "";
    public string FooterLeft { get; init; } = "";
    public string FooterCenter { get; init; } = "Page {page} of {pages}";
    public string FooterRight { get; init; } = "";
    public string WatermarkText { get; init; } = "DRAFT";
    public double FontSize { get; init; } = 10;
    public uint Color { get; init; } = 0xFF505050;
    public double LeftMargin { get; init; } = 24;
    public double RightMargin { get; init; } = 24;
    public double TopMargin { get; init; } = 20;
    public double BottomMargin { get; init; } = 20;
    public double Opacity { get; init; } = 1;
    public double Rotation { get; init; }
    public bool BehindContent { get; init; }
    public int StartNumber { get; init; } = 1;
    public PdfPageNumberStyle NumberStyle { get; init; }
    public int BatesDigits { get; init; } = 6;
    public string BatesPrefix { get; init; } = "";
    public string BatesSuffix { get; init; } = "";
    public string DateText { get; init; } = "";

    public PdfPageMarkSettings Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(NumberStyle)) throw new ArgumentException("Unknown page-mark kind or numbering style.");
        if (!double.IsFinite(FontSize) || FontSize is < 4 or > 300) throw new ArgumentException("Use a font size from 4 to 300 points.");
        if (!double.IsFinite(Opacity) || Opacity is <= 0 or > 1 || !double.IsFinite(Rotation) || Math.Abs(Rotation) > 360000)
            throw new ArgumentException("Use an opacity above zero and at most one, and a finite rotation within ±360,000 degrees.");
        foreach (var margin in new[] { LeftMargin, RightMargin, TopMargin, BottomMargin })
            if (!double.IsFinite(margin) || margin is < 0 or > 10000) throw new ArgumentException("Margins must be between zero and 10,000 points.");
        if ((Color >> 24) != 255) throw new ArgumentException("Use an opaque text color; choose transparency with Opacity.");
        if (StartNumber is < 0 or > 999999999 || BatesDigits is < 3 or > 15) throw new ArgumentException("Use a starting number from 0 to 999,999,999 and 3–15 Bates digits.");
        foreach (var text in new[] { HeaderLeft, HeaderCenter, HeaderRight, FooterLeft, FooterCenter, FooterRight, WatermarkText, BatesPrefix, BatesSuffix, DateText })
            ValidateText(text);
        if (BatesPrefix.Length > 80 || BatesSuffix.Length > 80 || DateText.Length > 80) throw new ArgumentException("Date, prefix and suffix are limited to 80 characters each.");
        if (Kind == PdfPageMarkKind.HeaderFooter && new[] { HeaderLeft, HeaderCenter, HeaderRight, FooterLeft, FooterCenter, FooterRight }.All(string.IsNullOrWhiteSpace) ||
            Kind == PdfPageMarkKind.Watermark && string.IsNullOrWhiteSpace(WatermarkText)) throw new ArgumentException("Enter some page-mark text.");
        return this;
    }
    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 1024) throw new ArgumentException("Each template is limited to 1,024 characters.");
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) { if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("Malformed Unicode text."); }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Malformed Unicode text.");
        }
        foreach (var rune in text.Normalize(NormalizationForm.FormC).EnumerateRunes())
            if (Rune.IsControl(rune) || rune.Value is >= 0x590 and <= 0x109F or >= 0x1780 and <= 0x18AF or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069 ||
                Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                throw new NotSupportedException("Page marks require a single line of independently positioned left-to-right glyphs. Complex shaping and control characters are not supported.");
    }
    /// <summary>Expands only known tokens. Double braces represent literal braces.</summary>
    public string Expand(string template, int selectedOrdinal, int documentPages)
    {
        Validate(); ValidateText(template);
        return ExpandValidated(template, selectedOrdinal, documentPages);
    }
    internal string ExpandValidated(string template, int selectedOrdinal, int documentPages)
    {
        if (selectedOrdinal < 0 || documentPages < 1) throw new ArgumentOutOfRangeException(nameof(selectedOrdinal));
        var number = checked(StartNumber + selectedOrdinal);
        var result = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c is '{' or '}')
            {
                if (i + 1 < template.Length && template[i + 1] == c) { result.Append(c); i++; continue; }
                if (c == '}') throw new ArgumentException("Unmatched closing brace in page-mark template.");
                var end = template.IndexOf('}', i + 1);
                if (end < 0) throw new ArgumentException("Unclosed page-mark token.");
                var token = template[(i + 1)..end];
                result.Append(token switch
                {
                    "page" => FormatNumber(number), "pages" => documentPages.ToString(CultureInfo.InvariantCulture),
                    "bates" => BatesPrefix + number.ToString("D" + BatesDigits, CultureInfo.InvariantCulture) + BatesSuffix,
                    "date" => DateText,
                    _ => throw new ArgumentException("Unknown page-mark token: {" + token + "}.")
                });
                i = end;
            }
            else result.Append(c);
        }
        if (result.Length > 2048) throw new ArgumentException("Expanded page-mark text exceeds 2,048 characters.");
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
    private string FormatNumber(int number)
    {
        if (NumberStyle == PdfPageNumberStyle.Decimal) return number.ToString(CultureInfo.InvariantCulture);
        if (number is < 1 or > 3999) throw new ArgumentException("Roman numbering requires values from 1 to 3,999.");
        var text = new StringBuilder();
        foreach (var (value, letters) in new[] { (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),(50,"L"),(40,"XL"),(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I") })
            while (number >= value) { text.Append(letters); number -= value; }
        return NumberStyle == PdfPageNumberStyle.LowerRoman ? text.ToString().ToLowerInvariant() : text.ToString();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PdfPageMarkSettings))]
internal partial class PdfPageMarkJsonContext : JsonSerializerContext { }
