# Acrobat workflow coverage

Reviewed 2026-09-30. This is a workflow-level engineering inventory for **PdfSpace 0.6.4**, not a claim of Adobe certification, every individual Acrobat command, or pixel-identical UI. Adobe's [desktop help index](https://www.adobe.com/support/acrobat/) and [feature overview](https://www.adobe.com/acrobat/features.html) provide the public comparison surface. Product entitlement, operating system, document type and subscription can change availability.

A **bounded workflow** implements the stated case with regression coverage. **Partial** means a useful subset exists but important cases remain absent. **Not implemented** means no equivalent end-to-end workflow. No percentage score is inferred from these categories, and merely retaining an unknown PDF dictionary is not counted as supporting its feature.

## Coverage matrix

| Workflow area | PdfSpace implementation | Remaining boundary |
|---|---|---|
| Reading and navigation | Bounded multi-document viewing, text search/copy, zoom/pan, continuous/single/two-page layouts, thumbnails, outlines, links and bookmarks. | Not universal PDF rendering or every accessibility/read-mode interaction. |
| Native object editing | Partial text/image/path/Form/shading selection, transforms, vector points, supported appearance, grouping/ordering and internal clipboard. | Arbitrary inline images, mask/clip authoring, cross-scope ordering, contour-accurate picking and transparency-group operations. |
| Paragraph editing | Partial original text-operand replacement, explicit Unicode font replacement and bounded new text blocks. | Original paragraph reconstruction, full reflow, complex-script shaping and fallback/subset reconstruction. |
| Comments and review | Bounded native notes/replies, ink, highlights, underlines, shapes, links and stamps; review status and undo. | Shared review, every annotation appearance/flag, server-side synchronization and imported custom stamp authoring. |
| Forms | Partial supported AcroForm text, checkboxes, radio groups and single-select choices; authoring/properties, shared widgets, required checks and XFDF/JSON values. | XFA, form JavaScript/calculations, submit actions, full rich text/multiselect, barcode and signature fields. |
| Fill and sign | Partial visual fill and drawn signatures. | Certificate signing, trust/revocation, timestamps, long-term validation and signature-preserving incremental updates. |
| Page organization | Bounded crop, rotate, reorder, duplicate, delete, insert, combine, extract and split. | Unknown catalog preservation across assembled files and unrestricted existing-form reassembly. |
| Headers and footers | Bounded native searchable six-slot marks, templates, selected ranges, margins, crop/rotation-aware placement, validated update/remove and no-op identity. | Arbitrary Adobe private metadata, automatic margin-shrink behavior and recognition of unrelated third-party marks. |
| Bates numbering | Bounded native prefix/suffix/zero-padding and selected ranges within a workspace; undo and saved settings. | Cross-file jobs, file-renaming rules, collision policy and saved multi-document job templates. |
| Watermarks/backgrounds | Partial native text watermark with opacity, angle, front/behind placement, ranges and managed update/remove. | Image/PDF watermarks, background authoring, tiling, automatic shrink and full third-party metadata interoperability. |
| Page labels | Bounded native decimal, Roman, alphabetic and prefix-only sections; preservation through supported page assembly; exact and physical-page navigation. | Not printed page numbering or a guarantee for malformed/hostile name trees. |
| Scan and OCR | Bounded local English/Polish/German recognition, review/correction, cancellation and searchable Unicode output. | Camera pipelines, deskew/dewarp, automatic layout/table recognition and a broad language corpus. |
| Protection and redaction | Partial local AES-256 opening/owner-password workflows and explicit all-page raster redaction reconstruction. | Certificate encryption, permission-policy workflows, content-preserving vector redaction and sanitization certification. |
| File-size audit and optimization | Partial source-based encoded-stream accounting, categories, top stream references, JSON export and weak cached reports. | Safe resampling/recompression, font subsetting, object pruning, linearization and exact whole-file accounting. |
| Comparison | Not implemented as an end-to-end document comparison workflow. | Page pairing, text/layout/vector/image difference classification, navigation, reports and tolerances. |
| Export and conversion | Partial native PDF, visual PDF, PNG, text, XFDF/JSON and editable workspace output. | Word/Excel/PowerPoint/HTML conversion and PDF/A, PDF/X or PDF/UA conversion. |
| Attachments | Bounded catalog browsing, confirmed extraction, native add/batch-add, description editing, payload replacement and catalog-key removal. | Attachment-annotation and associated-file authoring, Portfolio semantics, assembled-catalog authoring, every stream codec and automatic opening. |
| Optional content/layers | Not implemented as a layer explorer/authoring workflow. | OCG/OCMD visibility policy, layer creation, print/export configuration and state interactions. |
| Accessibility and standards | Partial basic UI labels/focus behavior; no full PDF tagging/conformance workflow. | Tag trees, reading order, alternate text, language roles, accessibility checkers and independent PDF/UA validation. |
| Print production | Not implemented as Acrobat-equivalent preflight/output tooling. | Separation preview, ICC/spot-color workflows, trapping, overprint simulation, fixups and validated press standards. |
| Automation and collaboration | Partial local transactional/reusable APIs. | Action Wizard/batch UI, cloud storage/review, shared presence, enterprise integrations and server workflows. |

## Recent completed increments

**0.6.2 — encoded-source inspection.** `PdfSizeAudit` and `PdfSizeAuditCache` inventory parsed original-source streams without decoding their content payloads. Retained-file length and stream subtotal are different quantities; percentages use the latter. This is not a whole-file partition, output-size prediction, reachable-object analysis or optimizer. [Accounting contract](space-audit.md).

**0.6.3 — attachment inspection.** Catalog `/Names /EmbeddedFiles` browsing and explicitly confirmed extraction added bounded metadata traversal, safe download names and unfiltered/Flate decoding. It does not run embedded actions, navigate external references or treat a retained source catalog as the next assembled output. [Explorer contract](attachments.md).

**0.6.4 — attachment authoring.** Adobe's [attachment workflow](https://helpx.adobe.com/acrobat/desktop/edit-documents/use-links-and-attachments/add-attachment.html) includes adding files and editing descriptions. PdfSpace now implements native add/batch-add, description updates, payload replacement and catalog-key removal with undo. Catalog-only edits reuse visual previews; shared specifications are copy-on-write. Authoring is restricted to a blank workspace or one original PDF in complete original page order, with signed/XFA/Portfolio/redaction/assembly guards. This is not automatic execution, annotation/AF authoring, Portfolio equivalence or secure deletion. [Authoring contract](attachments.md#native-authoring-064).

## Next engineering priorities

Prioritize complete workflows that can be tested through actual native files and independent readers: document comparison, OCG visibility, image/PDF backgrounds and watermarks, safe optimization operations, and a text-layout/shaping architecture. These are priorities, not claims that the features already exist or will execute automatically.

Every increment should retain read/modify/write contracts, occurrence isolation, explicit no-op/undo semantics, bounded ownership and actual downloaded-file verification. Performance evidence should separate geometry/cache work from PDF parsing/writing, UI latency, rendering and process/native/GPU memory. Cropping, ordinary deletion and clearing a cache/history are never substitutes for a validated redaction workflow.
