# Changelog

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
