# Native PDF attachments

## Workflow

Choose **Convert → Browse attachments → Inspect attachments**. The same command is under All tools. The panel reads catalog `/Names /EmbeddedFiles` entries from the retained original PDFs, deduplicating reference-identical source buffers. Entries show a safe download name, source number, encoded payload length, declared uncompressed size when available, description and declared media type.

The list is explicitly **source-scoped**: combined/reorganized PDF export can have different catalog preservation. An attachment in a retained source may be associated with content no longer visible in the workspace. This is not a rendered-page attachment inventory, a sanitization audit or a prediction of the next exported PDF.

**Download attachment N** asks for confirmation and then saves using `application/octet-stream`. It does not invoke a shell, launch an application, evaluate an embedded script, navigate to a document URL or open external stream paths. The host's existing file-save contract is used. Cancelling confirmation makes no file download. No source edits or undo entries are created.

## Reusable APIs

```csharp
var cache = new PdfEmbeddedFileCache(capacity: 4);
IReadOnlyList<PdfEmbeddedFileInfo> files = cache.Read(sourceBytes, cancellationToken);
var file = files[0];
if (file.CanExtract)
{
    // Ask the user before extraction/saving. Treat every result as untrusted.
    byte[] data = PdfEmbeddedFiles.Extract(sourceBytes, file, cancellationToken);
    await storage.SaveAsync(file.DownloadName, data, "application/octet-stream");
}
cache.Clear();
```

`Read` only parses metadata and encoded stream descriptors; it never decodes attachment payloads. The underlying PDF parser may decode structural object/xref streams. `Extract` rechecks a SHA-256 source fingerprint and every selected descriptor field before reading a payload. These are stale-state checks, **not authentication, signatures or malware detection**. UI callbacks additionally check the current document snapshot both before and after confirmation.

Source buffers must follow the application's immutable ownership contract. Equal data in a new array is a new cache identity. Metadata-only page-label edits can reuse cached catalogs; native source edits reparse. The cache is single-thread-affine, bounded to 1–32 source entries (default four), holds only weak references to source buffers, and never owns a native PDF document or decoded attachment bytes. Closed document contexts explicitly clear it.

## Decoding and limits

Catalog traversal supports direct/indirect nodes, nested `/Kids`, `/Names` pairs and validated `/Limits`. Shared/cyclic nodes, duplicate decoded keys, excessive depth, invalid text metadata and ambiguous node shapes fail explicitly. Noncanonical source key ordering is tolerated for reading, without rewriting the tree.

Limits are 512 entries per source, 1,024 entries per workspace panel, 16 tree levels, 4,096 tree nodes, 200,000 indirect source objects, 1,024 UTF-16 units per key/filename and 4,096 description units. The UI creates at most 32 attachment rows at a time; Previous/Next reach every entry without reparsing.

Extraction supports unfiltered streams or a single `/FlateDecode`, including its one-element filter array form. Nonempty decode parameters, external `/FS` or stream `/F`, and other codecs/filter chains remain visible with an unavailable reason. Both declared and actual decoded length are limited to **16 MiB**. Streaming Flate decoding checks cancellation between bounded reads, explicitly verifies the zlib header and Adler-32 trailer, and rejects size/checksum mismatches. Rented scratch buffers are cleared before returning to the pool. Temporary output buffers are bounded, but their memory is not a cryptographic secure-erasure guarantee.

Filename handling strips both path separator styles, replaces control/format characters and invalid filename punctuation, bounds length without splitting surrogate pairs, handles Windows device stems and appends `.download` to known executable/web extensions. The extension list is defense-in-depth, not a complete active-content classifier. PDFs, office documents and other ordinary files can also be unsafe. Nothing should open automatically after download.

Native parsing is synchronous and the object limit is a post-parse guard, not a parser-memory sandbox. Cancel checks cannot interrupt an individual native parser call. SHA-256 and decompression run on the calling thread. No whole-app frame-rate or process-memory improvement is inferred from warm cache measurements.

## Coverage boundary

Catalog authoring is available in 0.6.4 within the restrictions below. The explorer does not enumerate attachment-only annotations, `/AF` arrays or portfolio/collection semantics. It does not interpret launch/GoToE actions, attachments inside attachments, platform-specific embedded-file alternatives or all PDF stream filters. All ordinary structured-save, signing, XFA and form-assembly restrictions remain unchanged.

Native tests generate unfiltered, compressed, Unicode, active-name, nested-tree and 65/512-file fixtures. They verify raw payload equality, invalid descriptors, checksum/truncation and expansion limits, immutable results, weak lifetimes and zero-allocation warmed metadata lookups. Real Uno browser tests use file pickers, pointer interaction, confirmation dialogs and actual downloads; a native verifier independently checks those downloaded bytes against the original fixture data.

## Native authoring (0.6.4)

**Add attachment** opens a binary file picker and then a description/confirmation dialog. **Edit → Attach file** opens the same inspector. Existing rows have **Edit description**, **Replace file** and **Remove from catalog** commands. Replacement retains the catalog filename and description; removal affects only the selected key. Save the changed document using ordinary **Export PDF** or the editable workspace format. Every effective command is one undo transaction. Cancelling a picker/dialog changes nothing, except an already active text editor can finish through its existing focus-loss behavior; Add explicitly finishes it before capturing its own snapshot.

The engine supports a blank workspace or exactly one retained PDF with its complete original page sequence. Combined, inserted/deleted/reordered-source catalogs require explicit export/reopen first; their unknown catalog preservation is not guaranteed. Catalog `/Collection`, `/Perms`, XFA, any discovered signature field/signature dictionary/ByteRange, and pending redaction marks reject authoring. A signature-aware incremental writer is not implemented. Attachment changes rewrite native PDF bytes; they are not file-in-place or incremental edits.

```csharp
// Caller keeps payload arrays immutable for the synchronous operation.
var file = new PdfAttachmentInput("report.txt", data,
    Description: "Reviewed source data", MediaType: "text/plain");
var added = PdfAttachmentEditor.Add(workspace, file, cancellationToken);
var selection = PdfEmbeddedFiles.Read(added.Sources.Single().Bytes, cancellationToken).Single();
var described = PdfAttachmentEditor.SetDescription(added, selection,
    "Approved data", cancellationToken);

// A new source requires a fresh descriptor, including its new SHA-256 identity.
var current = PdfEmbeddedFiles.Read(described.Sources.Single().Bytes, cancellationToken).Single();
var replaced = PdfAttachmentEditor.Replace(described, current,
    replacementBytes, "text/plain", cancellationToken);
var removed = PdfAttachmentEditor.Remove(replaced,
    PdfEmbeddedFiles.Read(replaced.Sources.Single().Bytes, cancellationToken).Single(), cancellationToken);

// One parse/native write for the entire batch, not one per attachment.
var batch = PdfAttachmentEditor.AddRange(workspace, new[] { file, secondFile }, cancellationToken);
```

Per-file input is limited to **16 MiB**, each batch to **32 files / 32 MiB**, and catalogs to **512 entries**. Output sources must fit the existing **64 MiB** source budget. New filenames are Unicode leaf names (at most 120 UTF-16 units), not filesystem paths; hidden/control/bidi-format characters and invalid Windows punctuation are rejected. Descriptions are validated Unicode with at most 4,096 units; newline/tab are allowed. Declared media types are ASCII type/subtype tokens without parameters. Browser/desktop UI authoring defaults to `application/octet-stream` rather than trusting a filename as a content validator.

The writer preserves existing name-key tokens and sorts the rebuilt flat leaf by encoded key bytes. Shared file specifications are detached before changing one catalog entry. A shared `/AF` or annotation reference can still refer to the old file specification; unrelated catalog name trees are retained. Replacement does not interpret or execute external references and does not follow attachment-in-attachment content. It replaces only the embedded payload of the chosen catalog entry. Payloads are compressed with zlib in bounded blocks only when the compressed result is smaller; incompressible data remains unfiltered. Decompression/launching is not part of authoring.

**Removal is not secure deletion.** Other PDF references, old indirect objects, original preview buffers, editable workspaces and undo history can retain the removed/replaced bytes. The writer does not sanitize hidden content, prune arbitrary unreachable object graphs, or certify the resulting document as free of confidential data or malware. Reusing a preview is safe for catalog-only visual changes, but intentionally retains the old preview buffer.

For one-source edits the page array, annotations, form/OCR state, labels, crop/rotation and source identity are retained. `PreviewBytes` retains the old visual source identity; the primary `Bytes` contains the changed catalog. This avoids PdfPig import, preview reconstruction and invalidating unchanged Skia pictures. Ordinary native export uses the edited primary source and applies pending workspace state through the existing writer. Empty batches and validated identical descriptions return the original workspace reference. No-op description checks still parse and validate the source, and replacement does not currently detect equal payloads as a no-op.

Native parsing/writing remains synchronous; cancellation is checked at graph/chunk/stage boundaries, not inside each native call. Dictionary graph bounds are post-parse defenses, not a parser-memory sandbox. `WorkspaceFileReader.ReadBoundedAsync` provides pooled 32 KiB reads for host pickers, including non-seekable and growing files; it reads at most one byte over the payload budget, rejects before appending it, clears the rented buffer, and leaves the caller's stream open. The limit is not a bound on all temporary or process allocations. Existing third-party storage implementations remain source-compatible: the new default `OpenAttachmentAsync` reports unsupported unless implemented.

## Reproduce authoring verification

```bash
dotnet run --project tests/PdfSpace.Tests -c Release -- --test-attachment-editing
python3 scripts/verify-attachment-editing.py artifacts/structured
# After publishing/serving the Uno app and copying all engine fixtures:
npx playwright test tests/browser/attachment-editing.spec.mjs
dotnet run --project tests/PdfSpace.Tests -c Release -- --verify-browser-attachment-editing artifacts/browser-exports
python3 scripts/verify-attachment-editing.py artifacts/structured --browser artifacts/browser-exports
```

The independent script needs `pypdf` and `PyMuPDF`. It checks actual payloads, Unicode metadata, unchanged decoded page streams and rendered pixels. It is intentionally separate from the native engine. The focused native run also writes `artifacts/engine/attachment-authoring-performance.json`: eight tiny files, three warmups, seven alternating-order samples of batch versus serial Add. Timing includes parsing, guards, compression and writing, but excludes rendering, UI, input IO and native/GPU memory. Result verification is outside timing. This measures batching, not a whole-application speed multiplier.
