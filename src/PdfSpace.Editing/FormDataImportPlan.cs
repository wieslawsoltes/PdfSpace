using PdfSpace.Core;

namespace PdfSpace.Editing;

/// <summary>Validated, snapshot-bound import. No field is changed until the whole plan is applied.</summary>
public sealed class FormDataImportPlan
{
    internal PdfWorkspace Before { get; }
    internal PdfWorkspace After { get; }
    public int ChangedFieldCount { get; }
    public int UnchangedFieldCount { get; }
    public IReadOnlyList<string> UnknownFields { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool CanApply => Errors.Count == 0;
    internal FormDataImportPlan(PdfWorkspace before, PdfWorkspace after, int changed, int unchanged, string[] unknown, string[] errors)
    {
        Before = before; After = after; ChangedFieldCount = changed; UnchangedFieldCount = unchanged;
        UnknownFields = Array.AsReadOnly(unknown); Errors = Array.AsReadOnly(errors);
    }
}
