using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>Catalog attachment authoring for blank workspaces and a single source in original
/// page order. Mutations rewrite the native source, not page pictures or pending workspace edits.
/// Shared specifications are detached. Deletion is not sanitization or secure erasure.</summary>
public static class PdfAttachmentEditor
{
    public const int MaximumBatchFiles = 32;
    public const int MaximumBatchBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PdfWorkspace Add(PdfWorkspace workspace, PdfAttachmentInput attachment,
        CancellationToken cancellationToken = default) => AddRange(workspace, [attachment], cancellationToken);

    /// <summary>Embed up to 32 files with a single native parse/write, atomically.
    /// Empty batches and exact description no-ops preserve workspace reference identity.</summary>
    public static PdfWorkspace AddRange(PdfWorkspace workspace, IReadOnlyList<PdfAttachmentInput> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachments); WorkspaceJson.Validate(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (attachments.Count > MaximumBatchFiles) throw new ArgumentException("An attachment batch is limited to 32 files.");
        // Snapshot the command collection. Payloads are read synchronously and never retained.
        var inputs = attachments.ToArray(); long total = 0;
        foreach (var item in inputs)
        {
            ValidateInput(item); total += item.Bytes.Length;
            if (total > MaximumBatchBytes) throw new ArgumentException("Attachment batch exceeds 32 MiB.");
        }
        if (inputs.Length == 0) return workspace;
        using var native = Open(workspace, cancellationToken);
        var entries = PdfEmbeddedFiles.ReadEntries(native, "", cancellationToken);
        if (entries.Count + inputs.Length > PdfEmbeddedFiles.MaximumFiles)
            throw new InvalidDataException("The PDF cannot contain more than 512 catalog attachment entries.");
        var pairs = entries.Select(e => (e.KeyToken, e.Specification)).ToList();
        var keys = entries.Select(e => e.Info.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key; do { key = "pdfspace:" + Guid.NewGuid().ToString("N"); } while (!keys.Add(key));
            var spec = new PdfDictionary(native); native.Internals.AddObject(spec);
            spec.Elements.SetName("/Type", "/Filespec");
            spec.Elements["/F"] = new PdfString(input.FileName, PdfStringEncoding.Unicode);
            spec.Elements["/UF"] = new PdfString(input.FileName, PdfStringEncoding.Unicode);
            spec.Elements["/Desc"] = new PdfString(input.Description, PdfStringEncoding.Unicode);
            SetPayload(native, spec, input.Bytes, input.MediaType, cancellationToken);
            pairs.Add((new PdfString(key), spec));
        }
        WriteTree(native, pairs);
        return Finish(workspace, native, cancellationToken);
    }

    public static PdfWorkspace SetDescription(PdfWorkspace workspace, PdfEmbeddedFileInfo selection,
        string description, CancellationToken cancellationToken = default)
    {
        ValidateText(description, 4096, "description", multiline: true);
        return Edit(workspace, selection, cancellationToken, (native, entry) =>
        {
            if (PdfObjects.Resolve(entry.Specification.Elements["/Desc"]) is PdfString old && old.Value == description ||
                entry.Specification.Elements["/Desc"] is null && description.Length == 0) return (false, null);
            var spec = Clone(native, entry.Specification);
            spec.Elements["/Desc"] = new PdfString(description, PdfStringEncoding.Unicode);
            return (true, spec);
        });
    }

    /// <summary>Replace only this catalog entry's payload, retaining its name and description.
    /// Another catalog/associated reference to the old specification remains unchanged.</summary>
    public static PdfWorkspace Replace(PdfWorkspace workspace, PdfEmbeddedFileInfo selection, byte[] bytes,
        string mediaType = "application/octet-stream", CancellationToken cancellationToken = default)
    {
        ValidatePayload(bytes, mediaType);
        return Edit(workspace, selection, cancellationToken, (native, entry) =>
        {
            if (entry.Specification.Elements.ContainsKey("/FS") || entry.Stream?.Stream is null)
                throw new NotSupportedException("Only embedded local catalog files can be replaced.");
            var spec = Clone(native, entry.Specification);
            // Platform-specific paths/related files must not silently select the previous payload.
            foreach (var key in new[] { "/DOS", "/Mac", "/Unix", "/RF" }) spec.Elements.Remove(key);
            SetPayload(native, spec, bytes, mediaType, cancellationToken);
            return (true, spec);
        });
    }

    /// <summary>Remove one catalog name-tree entry. Other references, preview buffers and undo
    /// snapshots can still contain the old file; this operation is not confidential-data erasure.</summary>
    public static PdfWorkspace Remove(PdfWorkspace workspace, PdfEmbeddedFileInfo selection,
        CancellationToken cancellationToken = default) => Edit(workspace, selection, cancellationToken, (_, _) => (true, null));

    private static PdfWorkspace Edit(PdfWorkspace workspace, PdfEmbeddedFileInfo selection, CancellationToken token,
        Func<PdfDocument, PdfEmbeddedFiles.Entry, (bool Changed, PdfDictionary? Replacement)> edit)
    {
        ArgumentNullException.ThrowIfNull(selection); WorkspaceJson.Validate(workspace); token.ThrowIfCancellationRequested();
        if (workspace.Sources.Length != 1 || Convert.ToHexString(SHA256.HashData(workspace.Sources[0].Bytes)) != selection.SourceFingerprint)
            throw new InvalidOperationException("The attachment source changed. Inspect the document again.");
        using var native = Open(workspace, token);
        var entries = PdfEmbeddedFiles.ReadEntries(native, selection.SourceFingerprint, token);
        var index = entries.FindIndex(e => e.Info.Key == selection.Key);
        if (index < 0 || entries[index].Info != selection) throw new InvalidOperationException("The attachment descriptor is stale or forged.");
        var result = edit(native, entries[index]);
        token.ThrowIfCancellationRequested();
        if (!result.Changed) return workspace;
        var pairs = entries.Select(e => (e.KeyToken, e.Specification)).ToList();
        if (result.Replacement is { } replacement) pairs[index] = (entries[index].KeyToken, replacement);
        else pairs.RemoveAt(index);
        WriteTree(native, pairs);
        return Finish(workspace, native, token);
    }

    private static PdfDocument Open(PdfWorkspace workspace, CancellationToken token)
    {
        WorkspaceJson.Validate(workspace); token.ThrowIfCancellationRequested();
        if (workspace.Pages.Any(p => p.Annotations.Any(a => a.Kind == AnnotationKind.RedactionMark)))
            throw new InvalidOperationException("Resolve pending redactions before editing catalog attachments.");
        PdfDocument native;
        if (workspace.Sources.Length == 0)
        {
            native = new PdfDocument();
            foreach (var page in workspace.Pages)
            {
                var created = native.AddPage(); created.Width = XUnit.FromPoint(page.Width); created.Height = XUnit.FromPoint(page.Height);
            }
        }
        else
        {
            if (workspace.Sources.Length != 1) throw new NotSupportedException("Attachment authoring needs one original PDF catalog. Export and reopen a copy first.");
            native = PdfDocumentEngine.OpenNative(workspace.Sources[0].Bytes);
        }
        try
        {
            if (workspace.Sources.Length == 1 && (workspace.Pages.Length != native.PageCount ||
                workspace.Pages.Where((p, i) => p.SourceId != workspace.Sources[0].Id || p.SourcePage != i + 1).Any()))
                throw new NotSupportedException("Keep the original page sequence for attachment authoring, or export and reopen a copy.");
            var objects = native.Internals.GetAllObjects();
            if (objects.Length > PdfSizeAudit.MaximumObjects) throw new InvalidDataException("Attachment edit exceeds the native object limit.");
            var catalog = native.Internals.Catalog;
            if (catalog.Elements["/Names"] is { } namesItem &&
                (PdfObjects.Dictionary(namesItem) is not { } names || names.Stream is not null))
                throw new InvalidDataException("Catalog Names must be a non-stream dictionary.");
            if (catalog.Elements.ContainsKey("/Perms") || catalog.Elements.ContainsKey("/Collection") ||
                PdfObjects.Dictionary(catalog.Elements["/AcroForm"])?.Elements.ContainsKey("/XFA") == true)
                throw new NotSupportedException("Certified, XFA and Portfolio documents cannot be edited by this attachment writer.");
            var pending = new Stack<PdfItem>(objects);
            var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
            while (pending.TryPop(out var candidate))
            {
                token.ThrowIfCancellationRequested();
                var item = PdfObjects.Resolve(candidate);
                if (item is not (PdfDictionary or PdfArray) || !seen.Add(item)) continue;
                if (seen.Count > PdfSizeAudit.MaximumObjects) throw new InvalidDataException("Attachment edit exceeds the direct/indirect object graph limit.");
                if (item is PdfDictionary dictionary)
                {
                    if (dictionary.Elements.GetName("/Type") == "/Sig" || dictionary.Elements.GetName("/FT") == "/Sig" || dictionary.Elements.ContainsKey("/ByteRange"))
                        throw new NotSupportedException("Signature fields and signed PDFs require a signature-aware incremental writer.");
                    foreach (var pair in dictionary.Elements) if (pair.Value is { } value) pending.Push(value);
                }
                else foreach (var child in ((PdfArray)item).Elements) pending.Push(child);
            }
            return native;
        }
        catch { native.Dispose(); throw; }
    }

    private static PdfWorkspace Finish(PdfWorkspace workspace, PdfDocument native, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bytes = PdfDocumentEngine.Bytes(native); token.ThrowIfCancellationRequested();
        if (bytes.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("The attachment edit exceeds the 64 MiB PDF source limit.");
        PdfWorkspace result;
        if (workspace.Sources.Length == 1)
        {
            var source = workspace.Sources[0];
            // Catalog-only edits preserve source ID and preview identity. No Skia/PdfPig
            // rehydration, page/field import or page-array reconstruction is needed.
            result = workspace with { Sources = [source with { Bytes = bytes, PreviewBytes = source.PreviewBytes ?? source.Bytes }] };
        }
        else
        {
            var source = new PdfSource(Guid.NewGuid(), workspace.Title, bytes);
            result = workspace with { Sources = [source], Pages = workspace.Pages.Select((p, i) => p with { SourceId = source.Id, SourcePage = i + 1 }).ToArray() };
        }
        WorkspaceJson.Validate(result); return result;
    }

    private static PdfDictionary Clone(PdfDocument native, PdfDictionary original)
    {
        var result = new PdfDictionary(native);
        foreach (var pair in original.Elements) result.Elements[pair.Key] = pair.Value;
        native.Internals.AddObject(result); return result;
    }

    private static void SetPayload(PdfDocument native, PdfDictionary spec, byte[] bytes, string mediaType, CancellationToken token)
    {
        using var compressed = new MemoryStream();
        using (var encoder = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (var offset = 0; offset < bytes.Length; offset += 32768)
            { token.ThrowIfCancellationRequested(); encoder.Write(bytes.AsSpan(offset, Math.Min(32768, bytes.Length - offset))); }
        // Compression is kept only when its actual payload is smaller.
        var useCompression = compressed.Length < bytes.Length;
        var payload = new PdfDictionary(native); native.Internals.AddObject(payload);
        payload.Elements.SetName("/Type", "/EmbeddedFile"); payload.Elements.SetName("/Subtype", "/" + mediaType);
        payload.CreateStream(useCompression ? compressed.ToArray() : bytes.ToArray());
        if (useCompression) payload.Elements.SetName("/Filter", "/FlateDecode");
        PdfObjects.DictionaryValue(payload, "/Params").Elements.SetInteger("/Size", bytes.Length);
        var ef = new PdfDictionary(native); ef.Elements["/F"] = payload.Reference!; ef.Elements["/UF"] = payload.Reference!;
        spec.Elements["/EF"] = ef;
    }

    private static void WriteTree(PdfDocument native, List<(PdfString Key, PdfDictionary Specification)> pairs)
    {
        var catalog = native.Internals.Catalog;
        var oldNames = PdfObjects.Dictionary(catalog.Elements["/Names"]);
        var names = oldNames is null ? new PdfDictionary(native) : Clone(native, oldNames);
        if (pairs.Count == 0) names.Elements.Remove("/EmbeddedFiles");
        else
        {
            // Keep original key tokens and byte ordering; never re-encode legacy name keys.
            var ordered = pairs.Select(p => (Pair: p, Bytes: KeyBytes(p.Key))).ToArray();
            Array.Sort(ordered, (a, b) => a.Bytes.AsSpan().SequenceCompareTo(b.Bytes));
            var values = new PdfArray(native);
            foreach (var item in ordered)
            {
                values.Elements.Add(item.Pair.Key);
                values.Elements.Add(item.Pair.Specification.Reference ?? (PdfItem)item.Pair.Specification);
            }
            var tree = new PdfDictionary(native); native.Internals.AddObject(tree); tree.Elements["/Names"] = values;
            names.Elements["/EmbeddedFiles"] = tree.Reference!;
        }
        if (names.Elements.Count == 0) catalog.Elements.Remove("/Names");
        else catalog.Elements["/Names"] = names.Reference ?? (PdfItem)names;
    }

    private static byte[] KeyBytes(PdfString key) => key.Encoding switch
    {
        PdfStringEncoding.RawEncoding when key.Value.All(c => c <= 255) => Encoding.Latin1.GetBytes(key.Value),
        PdfStringEncoding.Unicode => [0xfe, 0xff, .. Encoding.BigEndianUnicode.GetBytes(key.Value)],
        _ => throw new NotSupportedException("Unsupported attachment name-key encoding. No document was changed.")
    };

    private static void ValidateInput(PdfAttachmentInput attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment); ValidateText(attachment.FileName, 120, "filename");
        if (attachment.FileName.Length == 0 || attachment.FileName.Trim(' ', '.') != attachment.FileName ||
            attachment.FileName.Any(c => "/\\<>:\"|?*".Contains(c)))
            throw new ArgumentException("Choose a leaf attachment filename, without paths or reserved characters.");
        ValidateText(attachment.Description, 4096, "description", multiline: true);
        ValidatePayload(attachment.Bytes, attachment.MediaType);
    }

    private static void ValidatePayload(byte[] bytes, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(mediaType);
        if (bytes.Length > PdfEmbeddedFiles.MaximumExtractedBytes) throw new ArgumentException("An attachment exceeds 16 MiB.");
        if (mediaType.Length is < 3 or > 127 || mediaType.Count(c => c == '/') != 1 || mediaType[0] == '/' || mediaType[^1] == '/' ||
            mediaType.Any(c => !char.IsAsciiLetterOrDigit(c) && !"/!#$&^_.+-".Contains(c)))
            throw new ArgumentException("Enter an ASCII type/subtype media type without parameters.");
    }

    private static void ValidateText(string value, int maximum, string name, bool multiline = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > maximum) throw new ArgumentException($"Attachment {name} exceeds {maximum} characters.");
        _ = StrictUtf8.GetByteCount(value);
        foreach (var rune in value.EnumerateRunes())
            if ((Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format) &&
                !(multiline && rune.Value is 9 or 10 or 13))
                throw new ArgumentException($"Attachment {name} contains hidden/control characters.");
    }
}
