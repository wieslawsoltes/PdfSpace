# Changelog

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
