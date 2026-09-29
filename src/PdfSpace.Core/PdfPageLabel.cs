using System.Globalization;
using System.Text;

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

    public void Validate()
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
        var maximumDigits = Style switch
        {
            PdfPageLabelStyle.RomanLower or PdfPageLabelStyle.RomanUpper => Number / 1000L + 12,
            PdfPageLabelStyle.LettersLower or PdfPageLabelStyle.LettersUpper => (Number - 1L) / 26 + 1,
            PdfPageLabelStyle.PrefixOnly => 0,
            _ => 10
        };
        if (Prefix.Length + maximumDigits > MaximumLabelLength)
            throw new InvalidDataException("The expanded page label exceeds the bounded display length.");
    }

    public string Format()
    {
        Validate();
        return Style switch
        {
            PdfPageLabelStyle.PrefixOnly => Prefix,
            PdfPageLabelStyle.Decimal => Prefix + Number.ToString(CultureInfo.InvariantCulture),
            PdfPageLabelStyle.LettersLower or PdfPageLabelStyle.LettersUpper => Prefix +
                new string((char)((Style == PdfPageLabelStyle.LettersUpper ? 'A' : 'a') + (Number - 1) % 26), (Number - 1) / 26 + 1),
            _ => Prefix + Roman(Number, Style == PdfPageLabelStyle.RomanLower)
        };
    }

    private static string Roman(int value, bool lower)
    {
        ReadOnlySpan<int> numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = lower ? ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"]
            : ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var text = new StringBuilder();
        for (var i = 0; i < numbers.Length; i++)
            while (value >= numbers[i]) { text.Append(symbols[i]); value -= numbers[i]; }
        return text.ToString();
    }
}
