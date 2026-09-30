namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private sealed record AttachmentRow(Guid SourceId, int SourceNumber, PdfEmbeddedFileInfo File);
    private readonly WorkspaceSnapshotStamp _attachmentStamp = new();
    private AttachmentRow[] _attachments = [];
    private int _attachmentOffset;
    private const int AttachmentRows = 32;

    private void ShowAttachments()
    {
        _home = false; _right = "Attachments";
        RefreshRight(); AdaptLayout(); UpdateModeVisibility();
    }

    private void BuildAttachments(StackPanel content)
    {
        content.Children.Add(Paragraph("RETAINED SOURCE ATTACHMENTS", 10));
        content.Children.Add(Paragraph("Browse EmbeddedFiles entries in the retained source PDFs. Files are never opened automatically. This list is not a preview of a combined PDF export."));
        content.Children.Add(new PdfCommandButton("Inspect attachments", PdfIconKind.Search, () => Safe(() =>
        {
            var snapshot = Session.Document;
            var rows = new List<AttachmentRow>();
            var seen = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            var sourceNumber = 0;
            foreach (var source in snapshot.Sources)
            {
                if (!seen.Add(source.Bytes)) continue;
                sourceNumber++;
                foreach (var file in _active.Attachments.Read(source.Bytes))
                {
                    if (rows.Count == 1024) throw new InvalidDataException("Attachment browsing is limited to 1,024 entries across all sources.");
                    rows.Add(new(source.Id, sourceNumber, file));
                }
            }
            _attachments = rows.ToArray(); _attachmentOffset = 0; _attachmentStamp.Remember(snapshot);
            RefreshRight(); ShowStatus($"Found {_attachments.Length} source attachments. No PDF was changed.");
        })));
        if (!_attachmentStamp.Matches(Session.Document))
        {
            _attachments = []; _attachmentOffset = 0;
            content.Children.Add(Paragraph("Inspect this snapshot to list attachments. An earlier document's list cannot be downloaded as current."));
            return;
        }
        var start = _attachmentOffset;
        var end = Math.Min(_attachments.Length, start + AttachmentRows);
        content.Children.Add(Paragraph(_attachments.Length == 0 ? "No catalog EmbeddedFiles entries." : $"Attachments {start + 1}–{end} of {_attachments.Length}", 12));
        var list = _attachments;
        if (_attachments.Length > AttachmentRows)
        {
            content.Children.Add(new PdfCommandButton("Previous attachments", PdfIconKind.Left, () => Safe(() =>
            { if (!ReferenceEquals(list, _attachments) || !_attachmentStamp.Matches(Session.Document)) return;
              _attachmentOffset = Math.Max(0, _attachmentOffset - AttachmentRows); RefreshRight(); })) { IsEnabled = start > 0 });
            content.Children.Add(new PdfCommandButton("Next attachments", PdfIconKind.Right, () => Safe(() =>
            { if (!ReferenceEquals(list, _attachments) || !_attachmentStamp.Matches(Session.Document)) return;
              _attachmentOffset = Math.Min((list.Length - 1) / AttachmentRows * AttachmentRows, _attachmentOffset + AttachmentRows); RefreshRight(); })) { IsEnabled = end < _attachments.Length });
        }
        var context = _active;
        for (var index = start; index < end; index++)
        {
            var row = _attachments[index]; var number = index + 1;
            content.Children.Add(PdfTheme.Divider());
            content.Children.Add(Paragraph(row.File.DownloadName, 12, "#333333"));
            content.Children.Add(Paragraph($"Source {row.SourceNumber} · {row.File.EncodedBytes:N0} encoded bytes"));
            if (row.File.DeclaredBytes is { } size) content.Children.Add(Paragraph($"Declared file size: {size:N0} bytes", 10));
            if (row.File.Description.Length > 0) content.Children.Add(Paragraph(row.File.Description, 10));
            if (row.File.MediaType.Length > 0) content.Children.Add(Paragraph("Declared type: " + row.File.MediaType, 10));
            if (!row.File.CanExtract) content.Children.Add(Paragraph(row.File.UnavailableReason!, 10));
            content.Children.Add(new PdfCommandButton($"Download attachment {number}", PdfIconKind.Export, () => Run(async () =>
            {
                void Validate()
                {
                    if (_active != context || !_attachmentStamp.Matches(Session.Document) || !_attachments.Contains(row))
                        throw new InvalidOperationException("The attachment list is stale. Inspect the current document again.");
                }
                Validate();
                if (!await _dialogs.ConfirmAsync("Save attachment?", $"Save {row.File.DownloadName}? Embedded files are untrusted and may contain active content. PdfSpace does not scan them for malware or open them automatically.", "Save attachment")) return;
                Validate(); // The user may have switched documents while confirmation was open.
                var source = Session.Document.Sources.Single(source => source.Id == row.SourceId);
                var bytes = PdfEmbeddedFiles.Extract(source.Bytes, row.File);
                await _storage.SaveAsync(row.File.DownloadName, bytes, "application/octet-stream");
                ShowStatus("Attachment saved without opening it. The PDF and undo history are unchanged.");
            })) { IsEnabled = row.File.CanExtract });
        }
        content.Children.Add(Paragraph("Downloads support unfiltered or FlateDecode payloads up to 16 MiB. Known executable/web extensions receive .download. This is not a malware guarantee. Annotation-only attachments, associated-file arrays, portfolios and attachment authoring are not included.", 10));
    }
}
