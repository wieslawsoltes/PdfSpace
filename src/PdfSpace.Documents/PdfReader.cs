using PdfSpace.Core;
using UglyToad.PdfPig;
namespace PdfSpace.Documents;

public sealed record PdfWord(string Text, RectD Bounds);
public sealed record SearchResult(int PageIndex, string Text, RectD Bounds);

public static class PdfReader
{
    public static PdfWorkspace Open(byte[] bytes, string name)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("PDF files are limited to 64 MB in this version.");
        using var pdf = PdfDocument.Open(bytes);
        if (pdf.NumberOfPages is < 1 or > 4096) throw new InvalidDataException("PDF documents must contain 1–4096 pages.");
        var source = new PdfSource(Guid.NewGuid(), Path.GetFileName(name), bytes);
        var pages = new PdfPageState[pdf.NumberOfPages];
        for (var i = 0; i < pages.Length; i++)
        {
            var page = pdf.GetPage(i + 1);
            pages[i] = new() { SourceId = source.Id, SourcePage = i + 1, Width = page.Width, Height = page.Height };
        }
        var result = new PdfWorkspace { Title = string.IsNullOrWhiteSpace(name) ? "Untitled.pdf" : Path.GetFileName(name), Author = pdf.Information.Author ?? "", Sources = [source], Pages = pages };
        WorkspaceJson.Validate(result); return result;
    }
    public static PdfWord[] Words(PdfWorkspace document, PdfPageState page)
    { using var reader = new PdfTextReader(document); return reader.Words(page); }
    public static IEnumerable<SearchResult> Find(PdfWorkspace document, string query, bool matchCase = false)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        using var reader = new PdfTextReader(document);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var i = 0; i < document.Pages.Length; i++)
        {
            var words = reader.Words(document.Pages[i]);
            var text = string.Join(" ", words.Select(word => word.Text));
            var offset = 0;
            var locations = words.Select(word => { var start = offset; offset += word.Text.Length + 1; return start; }).ToArray();
            for (var start = 0; start < text.Length;)
            {
                var index = text.IndexOf(query, start, comparison); if (index < 0) break;
                var matches = words.Where((word, wi) => locations[wi] < index + query.Length && locations[wi] + word.Text.Length > index).ToArray();
                if (matches.Length > 0) yield return new(i, text.Substring(Math.Max(0, index - 28), Math.Min(text.Length - Math.Max(0, index - 28), query.Length + 75)), matches.Select(word => word.Bounds).Aggregate(RectD.Union));
                start = index + Math.Max(1, query.Length);
            }
            foreach (var annotation in document.Pages[i].Annotations.Where(annotation => annotation.Text.Contains(query, comparison))) yield return new(i, annotation.Text, annotation.Bounds);
        }
    }
    public static string ExtractText(PdfWorkspace document)
    {
        using var reader = new PdfTextReader(document);
        return string.Join("\n\n", document.Pages.Select((page, index) => $"Page {index + 1}\n" + string.Join(" ", reader.Words(page).Select(word => word.Text)) + "\n" + string.Join("\n", page.Annotations.Where(annotation => annotation.Kind is AnnotationKind.Text or AnnotationKind.Note).Select(annotation => annotation.Text))));
    }
}
