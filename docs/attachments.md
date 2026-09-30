# Source attachment explorer

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

This increment does **not** add/remove files or edit descriptions; it does not enumerate attachment-only annotations, `/AF` arrays or portfolio/collection semantics. It does not interpret launch/GoToE actions, attachments inside attachments, platform-specific embedded-file alternatives or all PDF stream filters. All ordinary structured-save, signing, XFA and form-assembly restrictions remain unchanged.

Native tests generate unfiltered, compressed, Unicode, active-name, nested-tree and 65/512-file fixtures. They verify raw payload equality, invalid descriptors, checksum/truncation and expansion limits, immutable results, weak lifetimes and zero-allocation warmed metadata lookups. Real Uno browser tests use file pickers, pointer interaction, confirmation dialogs and actual downloads; a native verifier independently checks those downloaded bytes against the original fixture data.
