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
        content.Children.Add(new PdfCommandButton("Add attachment", PdfIconKind.Plus, () => Run(AddAttachmentAsync)));
        content.Children.Add(new PdfCommandButton("Inspect attachments", PdfIconKind.Search, () => Safe(InspectAttachments)));
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
            void ValidateSelection()
            {
                if (_active != context || !_attachmentStamp.Matches(Session.Document) || !_attachments.Contains(row))
                    throw new InvalidOperationException("The attachment selection changed. Inspect the document again.");
            }
            void Commit(string label, Func<PdfWorkspace, PdfWorkspace> edit)
            {
                ValidateSelection(); Session.Execute(label, edit); InspectAttachments(); ShowStatus(label + ". Undo restores the prior PDF; removal is not secure erasure.");
            }
            content.Children.Add(new PdfCommandButton($"Edit attachment description {number}", PdfIconKind.Edit, () => Run(async () =>
            {
                ValidateSelection();
                var description = await _dialogs.PromptAsync("Attachment description", "Change the description of this catalog entry only.", row.File.Description, multiline: true, acceptLabel: "Save description");
                if (description is not null) Commit("Edit attachment description", document => PdfAttachmentEditor.SetDescription(document, row.File, description));
            })) { Label = "Edit description" });
            content.Children.Add(new PdfCommandButton($"Replace attachment {number}", PdfIconKind.Folder, () => Run(async () =>
            {
                ValidateSelection(); var file = await _storage.OpenAttachmentAsync(); if (file is null) return;
                ValidateSelection();
                if (!await _dialogs.ConfirmAsync("Replace attachment?", $"Use the contents of {file.Name} for {row.File.DownloadName}? The existing attachment name and description remain. Other references may retain the old file.", "Replace attachment")) return;
                Commit("Replace attachment", document => PdfAttachmentEditor.Replace(document, row.File, file.Bytes));
            })) { Label = "Replace file" });
            content.Children.Add(new PdfCommandButton($"Remove attachment {number}", PdfIconKind.Trash, () => Run(async () =>
            {
                ValidateSelection();
                if (!await _dialogs.ConfirmAsync("Remove catalog attachment?", $"Remove {row.File.DownloadName} from the catalog list? Other references, previews and undo history may still contain it. This is not secure erasure.", "Remove attachment")) return;
                Commit("Remove attachment", document => PdfAttachmentEditor.Remove(document, row.File));
            })) { Label = "Remove from catalog" });
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
        content.Children.Add(Paragraph("Downloads support unfiltered or FlateDecode payloads up to 16 MiB. Known executable/web extensions receive .download. This is not a malware guarantee. Authoring requires a blank document or one original source in original page order. Annotation/associated-file references are not edited. Portfolios are not supported.", 10));
    }
}
