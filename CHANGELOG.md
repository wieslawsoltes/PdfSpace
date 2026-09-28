# Changelog

## 0.5.3-alpha.1 — 2026-09-28

- Direct native-object rotation handle with free angles, Shift 15-degree snapping, exact numeric rotation, one-transaction commit, and Escape cancellation.
- Optional edge/center alignment snapping with visible guides, screen-constant tolerance, Shift axis locking, and temporary Alt bypass.
- Reusable sorted snap index with bounded targets, deterministic ties, and allocation-free warm queries.
- Returning a native drag to its origin restores the exact preview and avoids both a redundant PDF rewrite and unintended additive-selection toggling.
- UI rotation handles can extend into the pasteboard without disabling clipping on rendered/exported PDF content. Existing two-argument transform API is retained.
- Native geometry/property tests, real Uno browser gestures, and independent native verification of downloaded manipulated PDFs.


## 0.5.2-alpha.1

- Added reusable constrained-move/resize/creation geometry: Shift axis/aspect lock, Alt center origin and preserved handle grab offsets; pointer release stays one native transaction.
- Preserved Shift-click selection while allowing Shift-drag of an already-selected object. Escape cancels without rewriting the source.
- Avoided selection-set and inspector rebuilding for an unchanged native selection.
- Retained a persistent focus target between inline form editors and when closing added-text editors, without stealing external input focus.
- Replaced stale scroll-geometry assumptions in acceptance tests with fresh revisioned, read-only compositor observations.
- Added native/browser gesture and focus regressions, plus independent reopening of actual browser gesture exports.

## 0.5.1-alpha.1

- Added native per-paint opacity and sixteen PDF blend names, including text objects whose internal graphics state changes between showing operations. Preserve masks and following text-state side effects.
- Added effective/mixed paint metadata and a custom object appearance inspector for opacity, blend, cap, join, miter, dash lengths and integer phase.
- Added ordered spatial BVH hit testing, marquee selection and outline culling with retained paint order and allocation-free warmed queries.
- Added the separately licensed/source-pinned PdfSpace.Rendering.Skia package. Correct image nonstroking alpha, miter limits, complete odd dash cycles and cache-key equality; retain Apache-2.0 provenance and notices.
- Added native, independent-renderer and real browser export regressions; document exact group-opacity, fractional-phase and bounding-box-picking boundaries.

## 0.5.0-alpha.1 — 2026-09-28

- Unified native object selection and batch editing across text, vector paths, images, shading and Form groups.
- Native object geometry, path-node editing, solid fill/stroke, clipping, stacking, grouping, application clipboard and vector/text insertion.
- Explicit bounded replacement-block reflow with embedded searchable glyphs; original-run editing remains available.
- Copy-on-write edit-scope trie, snapshot-cached object descriptors, geometry-only drag previews and one commit per gesture.
- Mixed-object native and real Uno browser regressions; independent validation of downloaded PDF objects.

Not arbitrary PDF/Acrobat equivalence: clipping text, inline-image streams, complex-script reflow, and cross-clipping group/stack operations remain guarded.


## 0.4.1-alpha.1 — 2026-09-28

- Native image insertion, replacement and image-to-PDF honor all eight EXIF orientations.
- Eligible 8-bit Gray/RGB/YCbCr JPEGs retain original compressed bytes; profiled/rotated/other supported photos decode to sRGB. PNG RGB/soft-mask samples are compressed row by row.
- Native image duplication shares the encoded resource; restoring proportions preserves center and affine orientation.
- Reference-counted original/preview buffer retention limits undo history to 100 entries and 128 MiB by default; current state is never discarded. Saved-state identity no longer pins discarded snapshots. Properties expose history accounting and confirmed clearing.
- Viewport updates no longer rebuild identical toolbar selection brushes or recount unchanged document statistics.
- Additional native/real-browser regression checks and native verification of browser JPEG/orientation exports.

## 0.4.0-alpha.1 — 2026-09-28

- Native image occurrence editing: direct selection/move/resize, precise geometry, rotation/reflection, replacement, insertion and deletion; copy-on-write nested Form-XObject/resource isolation.
- Source text editing in nested forms, with explicit searchable Unicode font replacement for independent horizontal runs; strict stale-target, encoding and text-positioning guards.
- Indexed viewport geometry and visibility, bounded reusable per-document search with whole-word mode, O(1) picture-cache LRU touches and independently bounded parsed-source caches.
- Source parsing reused during page reassembly; only edited sources are rehydrated after native mutations.
- Original shared-object sample, isolation/Unicode/cache/layout regressions, real browser editing tests and native verification of browser downloads.
- Linux HarfBuzz native dependency declared for system-font fallback rendering in engine tests.

This remains a bounded native-object editor, not a general paragraph layout, image-mask, print-production or signature-preserving incremental PDF writer.


## 0.3.0-alpha.1 — 2026-09-27

- Add local Scan & OCR, English/Polish/German recognition, page ranges, cancellation and atomic snapshot application.
- Add per-word confidence review, correction and undo; normal text selection/search includes recognized layers.
- Export interoperable invisible Unicode PDF text with editable OCR metadata, repeated-save deduplication and crop/rotation normalization.
- Add PNG/JPEG scan import with EXIF orientation and an image-only synthetic example.
- Add the eleventh reusable library, `PdfSpace.Ocr`, native process and browser WASM adapters, pinned self-hosted assets and end-to-end tests.
- Preserve original scan pixels and sensitive-source recovery/export rules.

## 0.2.1-alpha.1 — 2026-09-27

- XFDF and versioned/legacy JSON form-data import/export, complete-batch validation, unmatched-name reporting and atomic undo/redo.
- Required-field validation and separate browser/native form-data pickers with 4 MiB input limits.
- Source PDF outline navigation and legacy/name-tree local destination resolution.
- Identity-preserving internal link remapping for page operations and native-save cleanup of deleted destinations.
- Managed workspace-bookmark round-trips without duplicating or deleting unrelated source outlines; repair stale sibling pointers on removal.
- One-shot editor/dialog focus activation prevents delayed selection from erasing newly typed characters.
- Expanded native and browser acceptance coverage, interchange documentation and release fixtures.

## 0.2.0-alpha.1 — 2026-09-27

- Native PDF annotation/reply/link saving and import; catalog retention for original single-source page order.
- Actual supported text-operand replacement with stale-edit and unavailable-glyph rejection.
- AcroForm creation, filling, property/default persistence, imported-widget deletion with hierarchy pruning, and keyboard traversal.
- Browser AES-256 output and owner-authorized opening via an isolated hash-pinned QPDF WASM worker; independent native-backend verification.
- Sensitive-document recovery protection and explicit plaintext-export warnings.
- Separate all-page image-only redaction export with a clear destructive-output contract.
- Ten reusable packages, expanded engine/browser tests and updated capability/security documentation.

Full Acrobat parity, OCR, certificate signatures, XFA/scripts, general content reflow, tagging/compliance and cloud services remain outside this release.

## 0.1.0-alpha.1 — 2026-09-27

Initial independent Uno Platform / SkiaSharp PDF workspace.

- Nine reusable packages and shared WebAssembly/desktop hosts.
- Immutable document editing, bounded undo/redo, real PDF import/rendering, text extraction and search.
- Custom Acrobat-style shell, vector icons, tabs, quick tools, review panels and page organization.
- Text/drawing annotations, comments/replies, visual fill/sign, page operations, workspace recovery and visual PDF/PNG/text exports.
- Native engine tests, real browser interaction tests, desktop build matrix, gated Pages delivery and release workflows.

This alpha does not provide full Adobe Acrobat feature parity, source-object-preserving PDF editing, OCR, secure redaction, PDF protection, interactive forms, digital certificates, tagging/compliance or cloud services. See `docs/compatibility.md` for the precise release boundary.
