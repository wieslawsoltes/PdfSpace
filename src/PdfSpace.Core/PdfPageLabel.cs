using System.Globalization;

namespace PdfSpace.Core;

public enum PdfPageLabelStyle { Decimal, RomanLower, RomanUpper, LettersLower, LettersUpper, PrefixOnly }

/// <summary>Resolved label for one page identity, not printed page content or an outline destination.</summary>
public sealed record PdfPageLabel
{
    public PdfPageLabelStyle Style { get; init; }
    public string Prefix { get; init; } = "";
    public int Number { get; init; } = 1;
    public const int MaximumLabelLength = 1024;
    public const int MaximumPrefixLength = 256;
    private static readonly string[] Hundreds = ["", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM"];
    private static readonly string[] Tens = ["", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC"];
    private static readonly string[] Ones = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

    public void Validate() => _ = ValidatedLength();

    private int ValidatedLength()
    {
        if (!Enum.IsDefined(Style) || Prefix is null || Prefix.Length > MaximumPrefixLength || Number < 1)
            throw new InvalidDataException("Invalid page-label style, prefix or positive starting number.");
        for (var i = 0; i < Prefix.Length; i++)
        {
            var ch = Prefix[i];
            if (char.IsControl(ch)) throw new InvalidDataException("Page-label prefixes cannot contain control characters.");
            if (char.IsHighSurrogate(ch))
            {
                if (++i >= Prefix.Length || !char.IsLowSurrogate(Prefix[i])) throw new InvalidDataException("Invalid Unicode page-label prefix.");
            }
            else if (char.IsLowSurrogate(ch)) throw new InvalidDataException("Invalid Unicode page-label prefix.");
        }
        var digits = Style switch
        {
            PdfPageLabelStyle.RomanLower or PdfPageLabelStyle.RomanUpper => Number / 1000L +
                Hundreds[Number / 100 % 10].Length + Tens[Number / 10 % 10].Length + Ones[Number % 10].Length,
            PdfPageLabelStyle.LettersLower or PdfPageLabelStyle.LettersUpper => (Number - 1L) / 26 + 1,
            PdfPageLabelStyle.PrefixOnly => 0,
            _ => DecimalLength(Number)
        };
        var length = Prefix.Length + digits;
        if (length > MaximumLabelLength)
            throw new InvalidDataException("The expanded page label exceeds the bounded display length.");
        return (int)length;
    }

    /// <summary>Formats directly into the result string; no intermediate number string, builder or symbol array.</summary>
    public string Format()
    {
        var length = ValidatedLength();
        if (Style == PdfPageLabelStyle.PrefixOnly) return Prefix;
        return string.Create(length, this, static (destination, label) =>
        {
            label.Prefix.AsSpan().CopyTo(destination);
            var number = destination[label.Prefix.Length..];
            if (label.Style == PdfPageLabelStyle.Decimal)
            {
                if (!label.Number.TryFormat(number, out var written, provider: CultureInfo.InvariantCulture) || written != number.Length)
                    throw new InvalidOperationException("Page-label length calculation failed.");
            }
            else if (label.Style is PdfPageLabelStyle.LettersLower or PdfPageLabelStyle.LettersUpper)
                number.Fill((char)((label.Style == PdfPageLabelStyle.LettersUpper ? 'A' : 'a') + (label.Number - 1) % 26));
            else
            {
                var thousands = label.Number / 1000;
                number[..thousands].Fill('M');
                var position = thousands;
                Copy(Hundreds[label.Number / 100 % 10], number, ref position);
                Copy(Tens[label.Number / 10 % 10], number, ref position);
                Copy(Ones[label.Number % 10], number, ref position);
                if (label.Style == PdfPageLabelStyle.RomanLower)
                    for (var i = 0; i < number.Length; i++) number[i] = (char)(number[i] + ('a' - 'A'));
            }
        });
    }

    private static void Copy(string text, Span<char> target, ref int offset)
    { text.AsSpan().CopyTo(target[offset..]); offset += text.Length; }

    private static int DecimalLength(int value)
    {
        var length = 1;
        while (value >= 10) { length++; value /= 10; }
        return length;
    }
}
