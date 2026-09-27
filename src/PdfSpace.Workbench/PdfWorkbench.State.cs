namespace PdfSpace.Workbench;
public sealed partial class PdfWorkbench
{
    public bool HasUnsavedChanges => _documents.Any(d => d.Session.IsDirty);
}
