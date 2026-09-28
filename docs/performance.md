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
