# Performance architecture and measurements

## Indexed page geometry

`PageLayoutIndex` stores document geometry and row-height prefixes for continuous, single-page and two-page layouts. A geometry change rebuilds the index once. Annotation-only changes keep the same index. Page placement and content extent are constant-time; locating the visible range uses binary search followed by iteration through only candidate visible rows. The Uno viewport uses this index for drawing, pointer hit testing, zoom anchors, scrolling, page navigation and the scroll thumb.

The previous `PageLayout.Arrange` API remains available and is used as a reference implementation in regression tests. Tests compare placement, visibility and hit behavior on 4096 heterogeneous pages across all modes, including crop/rotation, annotation-only changes and odd final spreads.

`LayoutIndexTests` writes `artifacts/structured/layout-performance.json` with runtime, dimensions, iterations, elapsed time and current-thread managed allocations. A local warmed 1000-query, 4096-page run allocated about 492 MB through the legacy full arrangement versus 216 KB through indexed visible enumeration. Timings vary by runtime and machine; this is a narrow layout microbenchmark, **not an overall application frame-rate, startup or export-speed guarantee**. CI records fresh measurements rather than asserting a universal timing threshold.

## Search and native source reuse

Each document context owns a `PdfSearchIndex`. It reuses bounded word, joined-text and offset data across searches. Binary offset lookup identifies intersecting words without scanning every word for every hit. Plain case-sensitive/insensitive and whole-word options share that index; annotations are checked against the current snapshot. Changed source/OCR identities evict stale entries. Closing a tab clears its search cache.

The default search budget is an estimated 16 MiB, with a page-entry cap. It is an estimate of retained word/text data, not a process-memory cap. Query length is bounded and cancellation is checked during iteration. Source metadata and PDF parsing can still require considerable work; the current browser search operation is not a background-worker PDF parser.

Page reassembly opens each distinct source once per operation instead of once per output page. `PdfWriteResult.ImportSourceCount` makes that behavior testable. Native image/text edits rehydrate the edited source's preview rather than rebuilding every untouched source preview.

## Rendering ownership and cache bounds

Picture cache hits now update LRU nodes in constant time. The renderer independently bounds parsed source documents (default four) and pictures (default twelve). Closing a tab disposes both. Cached pictures remain usable when their parsed source is evicted; a regression verifies that lifetime boundary. `CachedSourceCount` and `CachedPictureCount` expose counts for diagnostics.

These are object-count bounds, not a strict memory budget: an individual complex picture or PDF can remain large. The existing file/page/path guards still apply. The renderer is single-thread-affine and source parsing, native rewriting and raster export can block the UI. Native object dragging therefore previews geometry and commits the expensive source rewrite once on release.

## Reproduce

```bash
dotnet run --project tests/PdfSpace.Tests -c Release
# On a host with Tesseract and the English model installed:
PDFSPACE_TEST_NATIVE_OCR=1 dotnet run --project tests/PdfSpace.Tests -c Release
```

The build retains structured fixtures and measurement JSON in `PdfSpace-structured-validation`. Browser tests exercise actual pointer/keyboard/file-picker operations and export native PDFs for independent reopening by the native backend. Physical GPU profiling, large real-world document corpora and process-wide peak-memory measurements remain separate work.

## Photo import and history source budgets (0.4.1)

`PdfImageEditor.OpenImage` is the native image-to-PDF entry point used by the workbench. Unrotated 8-bit Gray/RGB/YCbCr JPEGs without embedded ICC profiles are stored as original `DCTDecode` bytes after bounded marker/frame/scan checks. Their samples are not expanded or recompressed at import. A later renderer will still decode them to display the page. Profiled JPEGs, CMYK and oriented photographs use Skia's sRGB decoder instead of being mislabeled as ordinary RGB.

The fallback path decodes one native RGBA bitmap and writes RGB and optional alpha to zlib streams a row at a time. It normalizes all eight EXIF orientations while walking samples, so no second rotated bitmap is necessary. It replaces the old full-image RGB and alpha arrays with at most `4 * orientedWidth` scratch bytes. The native bitmap and compressed output still consume memory. At 1600 × 1000, the eliminated RGB/alpha staging was 6.4 MB. `PhotoImportTests` records current-thread allocations, elapsed import time and encoded/output sizes in `photo-performance.json`; these are not process-wide peak-memory or application frame-rate measurements.

`EditorHistoryOptions` limits retained history to 100 entries and 128 MiB of distinct original/preview source arrays by default. Reference counting is per immutable snapshot and per byte-array identity: metadata edits and aliases do not multiply the charge. On a new edit the oldest undo entries are pruned until the count and source budget are satisfied. Current state is never discarded even when it alone exceeds the budget. Undo/redo transfer existing registrations; branching releases redo buffers. A weak saved-state marker preserves clean/dirty identity without rooting a discarded source snapshot.

The source budget is **not a whole-application memory limit**. It excludes annotation object graphs, renderer/native allocations, exported copies, and caller-owned snapshots. Eight open tabs can each retain their own budget. `RetainedSourceBytes`, `UndoCount`, `RedoCount` and `PrunedHistoryEntries` expose the behavior. Properties includes a confirmation-gated Clear undo history action; it changes neither the current document nor whether it needs saving.

Toolbar selection setters now skip unchanged state, avoiding brush allocation on each pointer update. Footer document statistics are recomputed only for a new immutable document snapshot; viewport changes no longer explicitly invalidate the right panel's measure.

## Unified object selection (0.5)

The workbench caches native-object descriptors by weak immutable snapshot identity and page ID. Panning, zooming, selection changes and geometry-only drag previews reuse that index. Selected occurrences are applied in one source-edit transaction with a copy-on-write invocation trie, sharing cloned ancestor prefixes across selected descendants. Native writing, PdfPig text-metrics analysis and preview rehydration remain separate parsing stages; this is not a claim that the complete operation parses bytes only once.

The viewer builds an index-to-target dictionary and a selection-membership set when targets or selection change. It computes union bounds at selection time. Drawing object outlines therefore performs constant-time membership lookup per object instead of a nested scan of all selected IDs; pointer moves reuse the cached union. This is a complexity reduction in the selection path, not a claim of universal frame-rate improvement. The browser regressions check that zoom and fitting do not increment the native object-index build counter.

As of 0.5.1, an ordered immutable BVH queries candidates for hit testing, marquee selection and visible outline drawing. Very dense single pages, tens of thousands of path control points, and frequent source-rewriting gestures still warrant independent performance measurement. The native scanner and per-batch limits are documented in the object-editing guide.

The [appearance/performance guide](object-appearance.md#spatial-index) describes the spatial index contract and its synthetic measurements. `object-spatial-performance.json` records query counts, tested leaf bounds, elapsed time and warmed current-thread allocations; source parsing and tree construction are outside that measurement.

## Alignment snapping (0.5.3)

`ObjectSnapIndex` sorts three horizontal and vertical anchors per target once (`O(n log n)` build; up to 20,001 target rectangles including the visible page). Each move query performs bounded binary searches and checks at most twelve candidates (`O(log n)`), without creating per-query collections. Duplicate coordinates resolve through lower-bound lookup, so coincident objects do not cause a linear scan. The index owns only geometry values and is invalidated on source-index or selection changes. Native PDF content is rewritten only at gesture commit.

`ObjectManipulationTests` compares 2,000 queries with exhaustive anchor search, covers duplicate ties and axis restrictions, and records `object-snap-performance.json` for 10,000 warmed queries against 20,000 targets. Timing/allocation measurements exclude index construction, PDF parsing/writing, rendering, and the browser event loop; they are not an application-wide speedup claim.


## Editing guides and inspector reuse (0.5.4)

The snap index now compacts duplicate sorted coordinates. At each coordinate it retains the first target in the existing deterministic priority order; discarded duplicates could never win a query. Both upper and lower neighbors are now available after one binary search, eliminating the former second search for the first lower duplicate. A regular 200 × 100 grid of 20,000 distinct rectangles retains 600 X and 300 Y anchors instead of 120,000 total entries. The target bounds remain retained, and initial sorting still allocates the uncompressed arrays, so this is neither a claim of constant-memory construction nor a whole-process memory reduction.

`SnapPoint` performs two coordinate searches; `SnapResize` searches only active edges and preserves coupled aspect constraints. Regression measurements in `snap-editing-performance.json` record 10,000 point queries plus 10,000 constrained/unconstrained resize queries and current-thread allocations after warmup. This excludes construction, native rewrites, preview rehydration, layout, rendering and GPU memory. Randomized tests check 2,500 point queries against independent exhaustive search and 2,500 resizes against anchor/pivot/aspect/guide invariants. Existing movement-equivalence regressions remain enabled.

The bounded 64 inspector rows are reused across selection changes. Read-only diagnostics expose `objectListBuilds` and `objectListRowsCreated`, allowing browser regressions to assert that selecting on a dense page creates no additional rows. Rows contain labels and index callbacks, not PDF snapshots. Detaching the cached list before discarding its parent prevents the cache from retaining old inspector closures. Native edit commands also defer their intermediate inspector rebuild until the final selection is restored. Identity/no-op edits skip rebuilding altogether.

## Bounded inspector navigation and diagnostic sampling

The object list realizes at most 64 buttons, down from 200, and rebinds the same pool across ranges. All indexed objects remain reachable through previous/next range navigation and an exact object-number command. `IndexWindow` is an allocation-free value model in Layout; it validates indices, handles partial last ranges and clamps extreme navigation without integer overflow. A 4,096-object browser regression checks navigation to object 4,096 while row creation, descriptor-index builds and undo counts remain unchanged.

Opt-in browser diagnostics no longer serialize synchronously for every state, focus and layout callback. One 80 ms timer samples read-only geometry without repairing focus. Immutable object descriptor JSON is cached by weak array identity plus redaction state, with a 16 MiB retention ceiling. Live selection, pointer targets and guides are still sampled afresh; changing descriptors or sensitivity invalidates the payload. No diagnostic timer/cache is active in ordinary sessions. These changes reduce test-observation overhead, not production render time.

The accepted-file picker transitions to a reading phase synchronously and detaches its event handlers before asynchronous I/O. Late cancel or duplicate change events cannot discard the accepted file or start another read; size and read failures still reject. A deterministic Node test fails on the previous implementation and passes with the phase guard. The original intermittent dense-file test passed both isolated reproductions, so that result alone is not proof of a unique root cause.

CI defaults to two isolated Playwright workers, with no retries or omitted cases. Set `PDFSPACE_TEST_WORKERS=1` for single-worker reproduction; values outside 1–4 fail explicitly. This changes test scheduling, not application threading.

## Native batch preflight and identity transforms (0.5.5)

Structural occurrence validation now sorts source ranges and checks complete ancestor scopes rather than testing every selected pair. This removes quadratic temporary string construction for large selections. `ObjectLayoutTests` compares behavior against exhaustive legacy preflight and records one warmed 1,000-object validation in `object-selection-preflight-performance.json`. The measurement excludes native source validation, parsing/writing, UI, rendering and GPU allocations; it is not an overall application speedup.

Exact identity transforms still revalidate native content but skip cloning/serialization and return the original workspace. Mixed batches omit unchanged occurrences after all descriptors have been validated. Consequently repeated exact align/spacing commands preserve history, preview/source buffers and descriptor cache identity. Operator copying accesses the selected range directly rather than enumerating preceding content. See [native object layout](object-layout.md).

## Batch page marks (0.6)

One operation builds one native TrueType font from the union of required glyphs, shares it across page Forms, and reuses a single SKFont/rune-width cache during layout. Original page content is not reserialized by the marking layer; graph hashes cache shared resource identities for the operation. Membership sets avoid repeated selected-page scans. The existing structured writer materializes supported workspace edits, then the mark writer serializes and rehydrates once. These are separate parsing/writing stages, not a one-parse claim. No-op updates still validate current native resources and font identity but skip the write/rehydration.

`--benchmark-page-marks` writes `page-mark-performance.json`: eight blank pages with identical text, batched Apply versus repeated single-page Apply calls, one warmup and three alternating-order samples. Both paths are checked for native marks and actual Type0 font counts (one versus eight). Record timing, current-thread managed bytes and output bytes, excluding UI/rendering/native/GPU allocations. This measures batch resource/work reuse, not speed relative to 0.5.5's former annotation command, whole-app frame rate or a PDF optimizer. Font buffers are shared within a batch, not deduplicated globally across arbitrary source PDFs.

## Encoded-source audits (0.6.2)

`PdfSizeAudit` enumerates the parsed native indirect object table and classifies stream lengths without payload decoding. Shared references do not multiply the count. A bounded top-12 heap avoids retaining/sorting every stream. `PdfSizeAuditCache` holds weak source keys and at most four reports by default; warm identity hits return the same immutable report without parsing. Each tab owns and clears its cache. The inspector is explicit-run and snapshot-guarded rather than auditing on every viewport or annotation event. See [the full accounting contract](space-audit.md) and `benchmarks/PdfSpace.SizeAudit` for scope and reproducible alternating-order samples.

## Catalog attachment authoring (0.6.4)

Attachment changes are catalog-only. A one-source edit retains the page-state array, source ID and prior preview buffer while replacing the primary native PDF bytes. Cached visual pages need not be imported or reconstructed. The native tests exercise unchanged cached picture counts and rendered pixels; export/reopen checks independently verify actual native output. Prior preview/history buffers may still contain removed attachments; these optimizations are not erasure.

`PdfAttachmentEditor.AddRange` validates the entire bounded input batch before opening a PDF, then applies one name-tree edit and one source write for up to 32 files. Serial Add performs those stages repeatedly. The focused attachment-editing test emits an alternating-order benchmark for eight small files with the scope and every sample recorded. Neither path claims to eliminate all PDF parsing, and catalog safety scanning still traverses the bounded direct/indirect object graph.

Desktop picker input now uses `WorkspaceFileReader` instead of accumulating all bytes before checking an updated file length. Seekable oversized files reject before reading; growing/non-seekable files stop at the configured payload limit plus a one-byte probe. Scratch storage is pooled. The returned array and MemoryStream capacity are separate allocations, so this is not a total-memory cap.
