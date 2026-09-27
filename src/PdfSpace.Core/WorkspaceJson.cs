using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfSpace.Core;

public static class WorkspaceJson
{
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public static string Save(PdfWorkspace document) => JsonSerializer.Serialize(document, WorkspaceJsonContext.Default.PdfWorkspace);
    public static PdfWorkspace Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumSourceBytes * 2) throw new InvalidDataException("This workspace exceeds the 128 MB recovery-file limit.");
        var result = JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.PdfWorkspace) ?? throw new InvalidDataException("The workspace is empty.");
        Validate(result);
        return result;
    }

    public static void Validate(PdfWorkspace document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion != 1) throw new InvalidDataException("Unsupported workspace format version.");
        if (document.Title is null || document.Title.Length > 1024 || document.Author is null || document.Author.Length > 4096) throw new InvalidDataException("Invalid document information.");
        if (document.Pages is null || document.Pages.Length is < 1 or > 4096) throw new InvalidDataException("A workspace must contain 1–4096 pages.");
        if (document.Sources is null || document.Sources.Length > 4096) throw new InvalidDataException("Invalid source collection.");
        var sourceIds = new HashSet<Guid>();
        long sourceBytes = 0;
        foreach (var source in document.Sources)
        {
            if (source is null || source.Bytes is null || source.Name is null || source.Name.Length > 1024 || !sourceIds.Add(source.Id)) throw new InvalidDataException("Invalid or duplicate PDF source.");
            sourceBytes += source.Bytes.Length;
            if (sourceBytes > MaximumSourceBytes) throw new InvalidDataException("The combined source PDFs exceed 64 MB.");
            if (source.Bytes.Length < 5 || !System.Text.Encoding.ASCII.GetString(source.Bytes, 0, Math.Min(1024, source.Bytes.Length)).Contains("%PDF-", StringComparison.Ordinal)) throw new InvalidDataException("A source is not a PDF document.");
        }
        var pageIds = new HashSet<Guid>();
        var annotationIds = new HashSet<Guid>();
        var annotationCount = 0;
        var fieldIds = new HashSet<Guid>();
        var fieldCount = 0;
        long pointCount = 0;
        foreach (var page in document.Pages)
        {
            if (page is null || !pageIds.Add(page.Id)) throw new InvalidDataException("Invalid or duplicate page identifier.");
            if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width is <= 0 or > 100000 || page.Height is <= 0 or > 100000) throw new InvalidDataException("Invalid page dimensions.");
            if (page.Rotation is not (0 or 90 or 180 or 270)) throw new InvalidDataException("Invalid page rotation.");
            if (page.SourceId is { } id && !sourceIds.Contains(id)) throw new InvalidDataException("Missing source document.");
            if (page.SourcePage is < 1 or > 4096) throw new InvalidDataException("Invalid source page number.");
            if (page.Bookmark is null || page.Bookmark.Length > 4096) throw new InvalidDataException("Invalid bookmark.");
            if (page.Crop is { } crop && (!crop.IsFinite || crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || crop.Right > page.Width + .01 || crop.Bottom > page.Height + .01)) throw new InvalidDataException("Invalid page crop.");
            if (page.Fields is null || page.Fields.Length > 10000) throw new InvalidDataException("Invalid form-field collection.");
            fieldCount += page.Fields.Length;
            if (fieldCount > 10000) throw new InvalidDataException("Too many form fields.");
            foreach (var field in page.Fields)
            {
                if (field is null || !fieldIds.Add(field.Id) || field.Label is null || field.Label.Length > 4096 || field.ExportValue is null || field.ExportValue.Length > 1024 || field.DefaultValue is { Length: > 100000 } || !Enum.IsDefined(field.Kind) || field.Name is null || field.Name.Length > 1024 || field.GroupName is null || field.GroupName.Length > 4096 || field.Value is null || field.Value.Length > 100000 || field.DefaultValue is null || field.Options is null || field.Options.Length > 10000 || field.Options.Any(o => o is null || o.Value is null || o.Value.Length > 4096 || o.Label is null || o.Label.Length > 4096) || !field.Bounds.IsFinite || field.Bounds.Width <= 0 || field.Bounds.Height <= 0 || !double.IsFinite(field.FontSize) || field.FontSize is < 1 or > 1000 || field.MaxLength is < 0 or > 1000000) throw new InvalidDataException("Invalid form field.");
            }
            if (page.Annotations is null) throw new InvalidDataException("Invalid annotation collection.");
            annotationCount += page.Annotations.Length;
            if (annotationCount > 50000) throw new InvalidDataException("Too many annotations.");
            foreach (var annotation in page.Annotations)
            {
                if (annotation is null || !annotationIds.Add(annotation.Id) || !Enum.IsDefined(annotation.Kind)) throw new InvalidDataException("Invalid or duplicate annotation.");
                if (!annotation.Bounds.IsFinite || annotation.Bounds.Width < 0 || annotation.Bounds.Height < 0 || annotation.Text is null || annotation.Text.Length > 100000 || annotation.Author is null || annotation.Author.Length > 4096 || !double.IsFinite(annotation.StrokeWidth) || annotation.StrokeWidth is <= 0 or > 100 || !double.IsFinite(annotation.FontSize) || annotation.FontSize is < 1 or > 1000 || annotation.Points is null || annotation.Points.Length > 100000) throw new InvalidDataException("Invalid annotation geometry or appearance.");
                pointCount += annotation.Points.Length;
                if (pointCount > 2000000 || annotation.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new InvalidDataException("Invalid or excessive annotation path data.");
                if (annotation.Replies is null || annotation.Replies.Length > 1000 || annotation.Replies.Any(r => r is null || r.Author is null || r.Author.Length > 4096 || r.Text is null || r.Text.Length > 100000)) throw new InvalidDataException("Invalid comment replies.");
            }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(PdfWorkspace))]
public partial class WorkspaceJsonContext : JsonSerializerContext;
