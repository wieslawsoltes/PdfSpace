using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfSpace.Core;

public static class WorkspaceJson
{
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public static string Save(PdfWorkspace document) => JsonSerializer.Serialize(document, WorkspaceJsonContext.Default.PdfWorkspace);
    public static PdfWorkspace Load(string json)
    {
        if (json.Length > MaximumSourceBytes * 2) throw new InvalidDataException("This workspace exceeds the 128 MB recovery-file limit.");
        var result = JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.PdfWorkspace) ?? throw new InvalidDataException("The workspace is empty.");
        Validate(result); return result;
    }
    public static void Validate(PdfWorkspace document)
    {
        if (document.FormatVersion != 1) throw new InvalidDataException("Unsupported workspace format version.");
        if (document.Pages is null || document.Pages.Length is < 1 or > 4096) throw new InvalidDataException("A workspace must contain 1–4096 pages.");
        if (document.Sources is null || document.Sources.Sum(s => (long)s.Bytes.Length) > MaximumSourceBytes) throw new InvalidDataException("The combined source PDFs exceed 64 MB.");
        if (document.Sources.Select(s => s.Id).Distinct().Count() != document.Sources.Length || document.Pages.Select(p => p.Id).Distinct().Count() != document.Pages.Length) throw new InvalidDataException("Duplicate source or page identifiers.");
        foreach (var source in document.Sources)
            if (source.Bytes.Length < 5 || !System.Text.Encoding.ASCII.GetString(source.Bytes, 0, Math.Min(1024, source.Bytes.Length)).Contains("%PDF-", StringComparison.Ordinal)) throw new InvalidDataException("A source is not a PDF document.");
        if (document.AnnotationCount > 50000) throw new InvalidDataException("Too many annotations.");
        foreach (var page in document.Pages)
        {
            if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width is <= 0 or > 100000 || page.Height is <= 0 or > 100000) throw new InvalidDataException("Invalid page dimensions.");
            if (page.Rotation is not (0 or 90 or 180 or 270)) throw new InvalidDataException("Invalid page rotation.");
            if (page.SourceId is { } id && !document.Sources.Any(s => s.Id == id)) throw new InvalidDataException("Missing source document.");
            if (page.SourcePage < 1) throw new InvalidDataException("Invalid source page number.");
            if (page.Crop is { } crop && (!crop.IsFinite || crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || crop.Right > page.Width + .01 || crop.Bottom > page.Height + .01)) throw new InvalidDataException("Invalid page crop.");
            foreach (var a in page.Annotations)
            {
                if (!a.Bounds.IsFinite || a.Bounds.Width < 0 || a.Bounds.Height < 0 || a.Text.Length > 100000 || !double.IsFinite(a.StrokeWidth) || a.StrokeWidth is <= 0 or > 100 || !double.IsFinite(a.FontSize) || a.FontSize is < 1 or > 1000 || a.Points.Length > 100000) throw new InvalidDataException("Invalid annotation.");
                if (a.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new InvalidDataException("Invalid annotation geometry.");
            }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(PdfWorkspace))]
public partial class WorkspaceJsonContext : JsonSerializerContext;
