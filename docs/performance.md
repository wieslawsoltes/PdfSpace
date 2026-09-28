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

The workbench caches native-object descriptors by weak immutable snapshot identity and page ID. Panning, zooming, selection changes and geometry-only drag previews reuse that index. One native edit batch parses its source once and builds a copy-on-write invocation trie, sharing cloned ancestor prefixes across selected descendants.

The viewer builds an index-to-target dictionary and a selection-membership set when targets or selection change. It computes union bounds at selection time. Drawing object outlines therefore performs constant-time membership lookup per object instead of a nested scan of all selected IDs; pointer moves reuse the cached union. This is a complexity reduction in the selection path, not a claim of universal frame-rate improvement. The browser regressions check that zoom and fitting do not increment the native object-index build counter.

Object bounds are currently scanned for hit testing and outline drawing. Very dense single pages, tens of thousands of path control points, and frequent source-rewriting gestures still warrant independent performance measurement. The native scanner and per-batch limits are documented in the object-editing guide.
