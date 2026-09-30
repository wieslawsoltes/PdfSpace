using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PdfSpace.Core;

internal static class PageLabelFormattingTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        foreach (var style in Enum.GetValues<PdfPageLabelStyle>())
        {
            var matches = true;
            for (var number = 1; number <= 4096; number++)
            {
                var label = new PdfPageLabel { Style = style, Number = number, Prefix = "IX-章-🧭-" };
                matches &= label.Format() == Reference(label);
            }
            check(matches, $"direct label formatting matches independent reference for {style} over 4096 values");
        }
        var prefix = new string('P', 256);
        var exact = new PdfPageLabel { Style = PdfPageLabelStyle.RomanLower, Prefix = prefix, Number = 768000 };
        check(exact.Format() == prefix + new string('m', 768), "Roman expansion accepts the exact 1024-character boundary");
        reject(() => (exact with { Number = 768001 }).Format(), "Roman expansion beyond the exact boundary is rejected");
        var letters = exact with { Style = PdfPageLabelStyle.LettersUpper, Number = 768 * 26 };
        check(letters.Format() == prefix + new string('Z', 768), "alphabetic expansion accepts the exact boundary");
        reject(() => (letters with { Number = 768 * 26 + 1 }).Format(), "alphabetic expansion beyond the boundary is rejected");
        var literal = new PdfPageLabel { Style = PdfPageLabelStyle.PrefixOnly, Prefix = prefix, Number = int.MaxValue };
        check(ReferenceEquals(literal.Format(), prefix), "prefix-only formatting reuses its immutable string");
        check(new PdfPageLabel { Number = int.MaxValue }.Format() == "2147483647", "maximum decimal numbering remains finite and exact");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            check(new PdfPageLabel { Style = PdfPageLabelStyle.RomanLower, Prefix = "IX-", Number = 49 }.Format() == "IX-xlix",
                "Roman case conversion is culture-independent and never changes the prefix");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            check(new PdfPageLabel { Prefix = "章-", Number = 1234 }.Format() == "章-1234", "decimal label formatting stays invariant");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        foreach (var invalid in new[] { "\ud800", "\udc00", "A\ud800B", "\ud800\ud800", "bad\0text" })
            reject(() => new PdfPageLabel { Prefix = invalid }.Format(), "direct formatter retains invalid-Unicode/control rejection");

        var names = new[] { "#Appendix", "=Cover", "#2", "", "#", "DUP", "DUP", " 1 ", "#99999999999999999999", "2" };
        var pages = names.Select(name => new PdfPageState { Label = new PdfPageLabel { Style = PdfPageLabelStyle.PrefixOnly, Prefix = name } }).ToArray();
        var index = new PageLabelIndex(pages);
        check(index.Resolve("#Appendix", out var page) == PageLabelMatch.Found && page == 0, "nonnumeric hash labels resolve without losing their prefix");
        check(index.Resolve("#2", out page) == PageLabelMatch.Found && page == 1, "hash numeric navigation retains its explicit physical meaning");
        check(index.Resolve("=#2", out page) == PageLabelMatch.Found && page == 2, "escaped hash labels resolve exactly");
        check(index.Resolve("==Cover", out page) == PageLabelMatch.Found && page == 1, "literal navigation handles a label beginning with equals");
        check(index.Resolve("=", out page) == PageLabelMatch.Found && page == 3, "literal navigation can select a unique empty label");
        check(index.Resolve("#", out page) == PageLabelMatch.Found && page == 4, "a solitary hash remains a literal label");
        check(index.Resolve("=DUP", out page) == PageLabelMatch.Ambiguous && page == -1, "literal lookup never selects an arbitrary duplicate");
        check(index.Resolve("= 1 ", out page) == PageLabelMatch.Found && page == 7, "literal navigation preserves leading and trailing spaces");
        check(index.Resolve("#99999999999999999999", out page) == PageLabelMatch.NotFound && page == -1, "overflowing physical selectors cannot become another label silently");
        check(index.Resolve("=#99999999999999999999", out page) == PageLabelMatch.Found && page == 8, "an overflowing physical-looking label remains reachable literally");
        check(index.Resolve("2", out page) == PageLabelMatch.Found && page == 9, "an ordinary exact numeric label wins over physical fallback");
        check(index.Resolve("=#appendix", out page) == PageLabelMatch.NotFound && page == -1, "literal lookup preserves case sensitivity");
        check(index.Resolve("=3", out page) == PageLabelMatch.NotFound && page == -1, "explicit literal lookup never falls back to physical numbering");
        check(index.Resolve("3", out page) == PageLabelMatch.Found && page == 2, "unmatched ordinary numbers still use physical fallback");
        check(names.Select((name, i) => name == "DUP" ? index.ResolveLabel(name, out _) == PageLabelMatch.Ambiguous :
            index.ResolveLabel(name, out var found) == PageLabelMatch.Found && found == i).All(ok => ok), "reusable literal API accepts every command-looking label");
        reject(() => index.ResolveLabel(null!, out _), "literal API rejects null input");
        check(index.Resolve("=" + new string('X', 1025), out page) == PageLabelMatch.NotFound, "escaped labels retain the display length bound");
        var boundary = new PageLabelIndex([new PdfPageState { Label = exact }]);
        check(boundary.Resolve("=" + exact.Format(), out page) == PageLabelMatch.Found && page == 0, "escape marker is excluded from maximum label length");
        var queries = names.Select(name => "=" + name).ToArray();
        for (var i = 0; i < 10000; i++) index.Resolve(queries[i % queries.Length], out _);
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp(); var sum = 0;
        for (var i = 0; i < 100000; i++) { index.Resolve(queries[i % queries.Length], out page); sum += page; }
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocated == 0 && sum > 0, "100000 escaped label lookups allocate no managed substrings after warmup");
        File.WriteAllText("artifacts/structured/labels-literal-performance.json", JsonSerializer.Serialize(new
        { runtime = Environment.Version.ToString(), queries = 100000, elapsedMilliseconds = elapsed, currentThreadManagedBytes = allocated,
            scope = "Warmed exact literal-label lookup including duplicate detection. Prebuilt strings/index; excludes UI, PDF parsing/writing, native/GPU and total memory." }));
    }

    private static string Reference(PdfPageLabel label)
    {
        if (label.Style == PdfPageLabelStyle.PrefixOnly) return label.Prefix;
        if (label.Style == PdfPageLabelStyle.Decimal) return label.Prefix + label.Number.ToString(CultureInfo.InvariantCulture);
        if (label.Style is PdfPageLabelStyle.LettersLower or PdfPageLabelStyle.LettersUpper)
            return label.Prefix + new string((char)((label.Style == PdfPageLabelStyle.LettersUpper ? 'A' : 'a') + (label.Number - 1) % 26), (label.Number - 1) / 26 + 1);
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var text = new StringBuilder(); var remaining = label.Number;
        for (var i = 0; i < values.Length; i++)
            while (remaining >= values[i]) { text.Append(symbols[i]); remaining -= values[i]; }
        var suffix = text.ToString();
        return label.Prefix + (label.Style == PdfPageLabelStyle.RomanLower ? suffix.ToLowerInvariant() : suffix);
    }
}
