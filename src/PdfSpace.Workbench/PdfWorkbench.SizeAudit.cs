using System.Globalization;

namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private readonly WorkspaceSnapshotStamp _sizeAuditStamp = new();
    private PdfWorkspaceSizeReport? _sizeAuditReport;

    private void ShowSizeAudit()
    {
        // This is a tool activation command, not the rail's toggle command.
        // A tab switch preserves the inspector; activating the audit again
        // must not hide the current document's Run action.
        _home = false;
        if (_right != "Space usage")
        {
            _right = "Space usage";
            RefreshRight();
            AdaptLayout();
        }
        UpdateModeVisibility();
    }

    private void BuildSizeAudit(StackPanel content)
    {
        content.Children.Add(Paragraph("RETAINED PDF SOURCE AUDIT", 10));
        content.Children.Add(Paragraph("Inspect encoded images, fonts and content streams without decoding their payloads. This does not optimize or modify your document."));
        content.Children.Add(new PdfCommandButton("Run space audit", PdfIconKind.Search, () => Safe(() =>
        {
            // Audit retained source bytes only; do not commit an active text editor.
            var snapshot = Session.Document;
            var result = _active.SizeAudit.Analyze(snapshot);
            _sizeAuditReport = result; _sizeAuditStamp.Remember(snapshot);
            RefreshRight(); ShowStatus("Space audit complete. Original PDF and undo history unchanged.");
        })));
        if (_sizeAuditReport is not { } report || !_sizeAuditStamp.Matches(Session.Document))
        {
            content.Children.Add(Paragraph("Run an audit for this document snapshot. Results from another document or an earlier edit are never exported as current."));
            return;
        }
        static string Bytes(long value) => value.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
        content.Children.Add(Paragraph($"{report.UniqueSourceBuffers} unique buffers / {report.SourceEntries} source entries", 12, "#333333"));
        content.Children.Add(Paragraph("Original file buffers: " + Bytes(report.RetainedFileBytes), 12, "#333333"));
        content.Children.Add(Paragraph("Encoded stream subtotal: " + Bytes(report.EncodedStreamBytes), 12, "#333333"));
        content.Children.Add(Paragraph("Percentages below are shares of the stream subtotal, NOT percentages of the whole file. PDF syntax, dictionaries and historical revisions are not partitioned.", 10));
        foreach (var category in report.Categories)
        {
            content.Children.Add(PdfTheme.Divider());
            content.Children.Add(Paragraph(AuditCategory(category.Kind), 12, "#333333"));
            content.Children.Add(Paragraph($"{Bytes(category.EncodedBytes)} · {category.StreamCount} streams · {category.PercentOfStreamBytes.ToString("F1", CultureInfo.InvariantCulture)}%", 11));
        }
        var owner = _active;
        content.Children.Add(new PdfCommandButton("Export space audit JSON", PdfIconKind.Export, () => Run(async () =>
        {
            if (_active != owner || !_sizeAuditStamp.Matches(Session.Document) || !ReferenceEquals(report, _sizeAuditReport))
                throw new InvalidOperationException("The audit is stale. Run it again before exporting.");
            // This report has no file names, extracted document text, passwords or stream bytes.
            await _storage.SaveAsync("pdfspace-space-audit.json", report.ToJson(), "application/json");
            ShowStatus("Space audit report exported. No PDF was changed.");
        })));
        content.Children.Add(Paragraph("Largest encoded streams (up to 12 per unique source)", 12, "#333333"));
        foreach (var source in report.Sources.Take(8))
        {
            content.Children.Add(Paragraph($"Source {source.SourceNumber} · {source.Aliases} aliases · {source.Usage.IndirectObjectCount} indirect objects", 11));
            foreach (var stream in source.Usage.LargestStreams)
                content.Children.Add(Paragraph($"{stream.ObjectNumber} {stream.Generation} R · {AuditCategory(stream.Kind)} · {Bytes(stream.EncodedBytes)}", 10));
        }
        if (report.Sources.Count > 8) content.Children.Add(Paragraph("Showing largest streams for the first eight sources. JSON includes all sources.", 10));
        content.Children.Add(Paragraph(PdfSizeAudit.Scope, 10));
        content.Children.Add(Paragraph("Retained sources can include cropped/deleted or unreferenced pages. Equal byte content in separate buffers counts separately. Removing objects or clearing this report is not sanitization.", 10));
    }

    private static string AuditCategory(PdfStreamKind kind) => kind switch
    {
        PdfStreamKind.PageContent => "Page content",
        PdfStreamKind.Image => "Images and image masks",
        PdfStreamKind.FontProgram => "Embedded font programs",
        PdfStreamKind.FormAppearance => "Form XObjects and appearances",
        PdfStreamKind.EmbeddedFile => "Embedded file streams",
        PdfStreamKind.Metadata => "Metadata streams",
        _ => "Other streams"
    };
}
