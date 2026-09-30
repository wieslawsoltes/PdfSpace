# Source space audit

Open **Convert → Audit PDF space → Run space audit**, or use the entry under All tools. The result inventories the original PDF buffers retained by the active workspace. The audit itself does not write a PDF, add history, clear dirty state, decode image/font/attachment payloads or initiate a remote service.

Opening a tool follows the existing editor focus rules: leaving an active text/form editor may commit that edit. The audit does not force an editor commit; uncommitted overlay text is outside its source-buffer scope.

## What is measured

The exact input byte-array lengths are reported separately from the sum of the encoded stream payload lengths reported by PDFsharp's parser. Each unique indirect stream is counted once per unique source buffer. Categories are page content, images (including image masks), embedded font programs, Form XObjects/appearances, embedded files, metadata and other streams. Classification is based on native dictionary relationships, not a guess from rendered pixels.

**Percentages use the encoded-stream subtotal, not total file bytes.** Dictionaries, nonstream annotations/fields/outlines, cross-reference syntax, delimiters, earlier incremental revisions and trailers are not assigned fabricated sizes. Parsed object streams and repaired streams can differ from their original on-disk spans. Do not subtract the subtotal from file size and call the remainder exact overhead. The audit does not perform a reachability analysis; retained but unused objects can be included. Shared objects are not multiplied by their number of page placements. Direct/malformed or nonstandard dictionaries are not assigned inferred payload sizes.

Only original source buffers are included. A new blank workspace has zero source bytes until a native source is created. Pending workspace annotations, form values and OCR may not yet be written into those source bytes. Cropped/deleted pages and unreferenced retained source buffers may still be present. Preview and undo-history buffers are excluded; Properties has separate retained-history accounting. Therefore this report is **not an estimate of the next exported PDF**, a total-process-memory measurement, an exact Acrobat Audit Space Usage equivalent, a compression guarantee or a sanitization report.

The engine does not call stream decoding, rendering or font APIs. The underlying PDF parser may decode structural object/xref streams and can perform repair. It runs synchronously on the calling thread; large or malformed inputs can still block it. Cancellation is checked before opening, between objects and sources, and before committing a cache result; it cannot interrupt the underlying native parse. Existing owner-password access rules remain in force.

## Bounded results and ownership

`PdfSizeAudit.Read(bytes, cancellationToken)` scans a source once with a limit of 200,000 indirect objects. `PdfSizeAuditCache.Analyze(workspace, cancellationToken)` validates the workspace and limits the aggregate to 500,000 parsed objects. Existing workspace limits also apply. Exceeding a limit fails rather than returning a success-shaped partial report. These post-parse guards are not a parser-memory sandbox.

A twelve-entry priority queue retains the largest streams in O(n log 12), with deterministic native-object-number tie breaks. The inspector shows seven category rows and details for at most eight source buffers; the JSON report includes every audited source. It is not a huge object tree disguised as a panel.

`PdfSizeAuditCache` is single-thread-affine. It retains at most four reports by default (configurable 1–32), with LRU eviction and **weak buffer references**. A report contains counts and identifiers, never source data or a PDF document. Repeated audits of the same immutable buffer reuse the report; a new buffer identity is rescanned, even when the bytes are equal. Within one workspace, reference-identical aliases count once; independently allocated byte-identical sources count separately. Callers must honor the existing immutable-source contract and call `Clear()` when done. Pan/zoom never starts another audit. Snapshot guards prevent exporting an earlier report after edits or tab changes.

## Reusable APIs

```csharp
var cache = new PdfSizeAuditCache(capacity: 4);
PdfWorkspaceSizeReport report = cache.Analyze(workspace, cancellationToken);
byte[] json = report.ToJson(); // source-generated; works in trimmed Uno WebAssembly
long sourceParses = cache.SourceParseCount;
cache.Clear();
```

JSON schema version 1 includes scope, source/alias counts, exact retained buffer lengths, encoded-stream categories and the largest stream references. It omits filenames, extracted text, passwords and stream data, but is still a document-structure report: export it only to intended recipients.

## Verification and performance

`--test-size-audit` exercises repeated page/image/font references, empty inputs, a deliberately undecodable unused image payload, precise stream categories, top-k equivalence with exhaustive sorting, weak lifetime, immutable results, LRU eviction, alias accounting, cancellation and zero-allocation warm cache hits. Browser tests open a real PDF, check source length and shared-stream counts, verify metadata-only cache reuse and prove that a new blank tab cannot export the old report. The native export verifier rechecks the actual downloaded JSON.

```bash
dotnet run --project tests/PdfSpace.Tests -c Release -- --test-size-audit
dotnet run --project benchmarks/PdfSpace.SizeAudit -c Release -- artifacts/engine/audit-sample.pdf
```

The benchmark asserts identical report data before measuring three warmups and nine alternating-order samples. It compares source parsing/classification against a warm identity cache, not different applications or renderers. Results exclude input IO, workspace aggregation, report serialization, UI and native/GPU allocation. There is no universal timing threshold.

Adobe's [Audit space usage workflow](https://helpx.adobe.com/uk/acrobat/desktop/create-documents/optimize-pdfs/audit-space.html) motivates the inspection workflow; this implementation deliberately reports its narrower byte-accounting contract. Safe resampling/recompression, font subsetting, linearization and optimization presets remain separate work.
