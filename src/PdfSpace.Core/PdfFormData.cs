namespace PdfSpace.Core;

/// <summary>Portable field values. No scripts, file URLs, certificates or PDF bytes are carried.</summary>
public sealed record PdfFormData(string Document, PdfFormDataField[] Fields)
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumFields = 10000;
    public const int MaximumValueLength = 100000;

    public void Validate()
    {
        if (Document is null || Document.Length > 1024 || Fields is null || Fields.Length > MaximumFields)
            throw new InvalidDataException("Invalid form-data document or field count.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        long characters = Document.Length;
        foreach (var field in Fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 4096 || !names.Add(field.Name))
                throw new InvalidDataException("Form field names must be nonempty and unique (case-sensitive).");
            if (field.Values is null || field.Values.Length > MaximumFields || field.Kind is { } kind && !Enum.IsDefined(kind))
                throw new InvalidDataException("Invalid form-data values or type.");
            characters += field.Name.Length;
            foreach (var value in field.Values)
            {
                if (value is null || value.Length > MaximumValueLength) throw new InvalidDataException("A form value exceeds the supported length.");
                characters += value.Length;
            }
            if (characters > MaximumBytes) throw new InvalidDataException("Form data exceeds the 4 MB limit.");
        }
    }

    public static PdfFormData FromWorkspace(PdfWorkspace workspace)
    {
        WorkspaceJson.Validate(workspace);
        var fields = new List<PdfFormDataField>();
        foreach (var group in workspace.Pages.SelectMany(page => page.Fields).GroupBy(field => field.GroupName, StringComparer.Ordinal))
        {
            var first = group.First();
            // Unsupported/password/signature fields are never exported as ordinary values.
            if (first.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported) continue;
            if (group.Any(field => field.Kind != first.Kind || field.Value != first.Value))
                throw new InvalidDataException($"The logical field '{first.GroupName}' has inconsistent widget values.");
            fields.Add(new(first.GroupName, [first.Value], first.Kind));
        }
        var result = new PdfFormData(workspace.Title, fields.ToArray());
        result.Validate();
        return result;
    }
}
