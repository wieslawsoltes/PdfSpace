namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private void InspectAttachments()
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
    }

    private async Task AddAttachmentAsync()
    {
        // End an active editor before capturing the command snapshot. Any separate
        // text edit keeps its own undo entry rather than being silently combined.
        Viewport.FinishText(true);
        var context = _active; var snapshot = Session.Document;
        void Validate()
        {
            if (_active != context || !ReferenceEquals(snapshot, Session.Document))
                throw new InvalidOperationException("The document changed while choosing an attachment. Choose the file again.");
        }
        var file = await _storage.OpenAttachmentAsync(); if (file is null) return;
        Validate();
        var description = await _dialogs.PromptAsync("New attachment description",
            $"Embed {file.Name} ({file.Bytes.LongLength:N0} bytes) in this PDF? Embedded files remain untrusted and are never launched. This rewrites the PDF catalog, not the original disk file.",
            multiline: true, acceptLabel: "Attach file");
        if (description is null) return;
        Validate();
        Session.Execute("Add attachment", document => PdfAttachmentEditor.Add(document, new(file.Name, file.Bytes, description)));
        ShowAttachments(); InspectAttachments(); ShowStatus("Attachment added to the native PDF catalog. Export PDF to save; Undo restores the prior source.");
    }
}
