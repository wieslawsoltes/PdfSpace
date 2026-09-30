# Acrobat workflow coverage and remaining gaps

Reviewed against Adobe's desktop help on **2026-09-30**, and the PdfSpace implementation through **0.6.4-alpha.1**. This is an engineering gap inventory, not a product certification, exhaustive enumeration of every Acrobat command, API/binary compatibility claim or pixel-parity score. Adobe availability can vary by product, license, platform and version.

Primary references: [Acrobat tool overview](https://helpx.adobe.com/acrobat/using/explore-acrobat-tools.html), [desktop help / workflow index](https://www.adobe.com/support/acrobat/), [watermarks](https://helpx.adobe.com/acrobat/using/add-watermarks-pdfs.html), [adding attachments and descriptions](https://helpx.adobe.com/acrobat/desktop/edit-documents/use-links-and-attachments/add-attachment.html). Feature names below describe workflows, not copied Adobe assets or implementation.

**Implemented** means the stated bounded workflow exists; **partial** means important compatibility or interaction cases remain; **absent** means no completed equivalent engine/UI is supplied. Preservation of an unknown source object during an ordinary rewrite does not make its authoring or interpretation supported.

| Workflow area | PdfSpace coverage | Material remaining work |
|---|---|---|
| Reading and navigation | Partial: multiple tabs, search/copy, zoom/pan, continuous/single/two-page, virtualized thumbnails, outlines, bookmarks, named/local destinations; native page labels, section editing and exact-label navigation. | Reflow/read mode, complete screen-reader page semantics, richer text selection, corpus-level renderer fidelity. |
| Original text editing | Partial: guarded text-showing operands and nested occurrences; explicit Unicode replacement; bounded new block wrapping. | Reconstruct original paragraphs/lists, complex shaping/bidi, fallback/legacy font coverage, text autoflow, spell-check. |
| Native objects | Partial: images, paths, text objects, Form/shading placements, transforms, geometry, appearance, stacking, grouping and internal clipboard. | Inline images, arbitrary interleaved clips/masks, transparency-group authoring, contour picking, full external editor integration. |
| Precision layout | Implemented within bounded native selections: rotation, axis/aspect modifiers, move/resize/point snapping, reference/page alignment, size matching and equal gaps. | Equal-gap live guides, contour anchors, affine frame handles, live native-content drag rendering, hierarchy/layer selection UI. |
| Headers/footers and numbers | New bounded native workflow: six slots, ranges, margins, date/page/Bates templates, Roman styles, update/remove, placement preview. | Source-content shrink-to-fit, saved presets, cross-file sequence/renaming, arbitrary third-party mark identification. |
| Watermarks/backgrounds | Partial: native text watermark, opacity/rotation/front/behind, range/update/remove. | Image/PDF watermark, arbitrary backgrounds, visibility policies, repeat patterns, preset persistence. |
| Organize/combine | Partial: crop, reorder, duplicate, delete, insert blank/PDF, combine, split/extract. | General existing-form/catalog preservation on assembly, portfolios, collections, arbitrary page replacement semantics. |
| Comments/review | Partial: native notes/replies, markup, drawings, links, visual stamps, resolution and undo. | Full FDF/XFDF comment interchange, attachments/callouts/dynamic stamps, review status workflows and remote review. |
| Forms | Partial: AcroForm text/buttons/single choices, authoring/property edits, native persistence, shared widgets, XFDF/JSON values and required checks. | XFA, script/calculation engine, barcodes, automatic field detection, complete tab-order/accessibility editing, advanced multiselect. |
| Scan/OCR | Partial: local English/Polish/German, ranges/resolution/cancel, confidence review/correction and invisible Unicode export. | Deskew/orientation detection, regional mixed-page OCR, camera/scanner acquisition, dewarping/background cleanup, broader models. |
| PDF optimization | Partial: retained-source encoded-stream audit with JSON export; eligible JPEG passthrough, row-wise RGB/alpha compression, shared resources, bounded caches/history and batched operations. | Exact whole-file element accounting, safe image resampling/recompression presets, full-font subsetting, deduplication across sources, linearization, optimization report. |
| Comparison | Absent document-comparison workflow. | Page pairing, text/geometry/raster changes, synchronized navigation, exclusions and exportable reports. |
| Attachments/portfolios/layers | Partial: bounded catalog EmbeddedFiles inspection/downloads, native add/batch-add/replace/description/remove with undo, paged source-scoped UI and metadata caching. | Assembled/multi-source catalog authoring, annotation-only and AF array authoring, portfolios/collections, additional codecs and OCG visibility/configuration/authoring. |
| Password security | Partial: local AES-256 R6, explicit owner authentication and sensitive-copy recovery rules. | Certificate encryption, policy servers, trust-management UX and broader encryption interoperability. |
| Signatures | Partial visual signatures only; signed-document editing is guarded. | Cryptographic signing, CMS/PAdES, certification permissions, timestamps, chain/revocation validation, long-term validation and incremental updates. |
| Redaction/sanitization | Partial: explicit fresh image-only reconstruction with burned pixels. | Native vector/text selective redaction, code sets, search/redact review, comprehensive nonvisual-data audit. |
| Accessibility/conformance | Absent certification-grade workflow. | Tags/reading order/alt text, PDF/UA authoring/checks, PDF/A/X conversion and machine-verifiable reports. |
| Print production | Partial generic print handoff. | Separations, overprint/ink inspection, ICC output intents, page boxes, trapping, professional preflight/fixups, booklet/poster controls. |
| Conversion/creation | Partial blank/image/PDF input; PDF/PNG/plain text/form/workspace export. | Office/HTML/PostScript conversions, Distiller and printer-driver workflows, rich clipboard conversion. |
| Automation/media/engineering | Partial simple measurements. | Action batches, trusted scripting policy, multimedia/3D, geospatial coordinates and engineering measurement suites. |
| Collaboration/cloud/AI | Absent cloud-equivalent services. | Shared review/presence, permissions/audit, e-sign agreements, cloud storage, AI document analysis. Do not upload user PDFs implicitly. |

## Implementation priorities

Prioritize tasks that can be validated by actual native-file round trips and independent readers. The current increment adds native page-label sections, preservation through page assembly and cached label navigation; the previous increment closed bounded page-mark workflows and an OCR/native-search interaction. Candidate next increments are document comparison, attachment/OCG inspection, optimization reports and a text-layout/shaping architecture. These are priorities, not promises that the features already exist or will be delivered automatically.

Treat incremental writing, certificates, conformance and native sanitization as distinct correctness/security contracts with dedicated fixtures and external validation. A visual imitation, a retained unknown dictionary, or an export that merely opens is insufficient evidence of parity.

## Performance contracts

Track native source parse counts, resource reuse, immutable cache invalidation, pointer preview allocations, bounded UI row counts, time-to-interaction, large-document memory and actual output size separately. Geometry benchmarks cannot establish overall frame rate. Two browser acceptance workers test correctness, not physical GPU speed. Each increment must preserve prior round-trip/independent-reader regressions and state explicitly where CPU-affine native parsing can still block the UI.

## Current gap review (2026-09-29)

Checked [Adobe desktop help](https://www.adobe.com/support/acrobat/), [page-label sections](https://helpx.adobe.com/acrobat/desktop/edit-documents/organize-pages/renumber-pages.html), [Compare Files](https://www.adobe.com/acrobat/features/compare-pdfs.html) and [background authoring](https://helpx.adobe.com/ca/acrobat/desktop/edit-documents/add-backgrounds-and-watermarks/add-background.html). Page labels are now a native implemented workflow with [explicit limits](page-labels.md); comparison, background/image-watermark authoring and the other absent areas above are still gaps. No UI placeholder or source-dictionary preservation is counted as feature completion. AI/PDF Spaces, e-sign service delivery and collaborative review require separate service/data-consent designs; none are simulated or implicitly uploaded.

## Optimization inspection (2026-09-30)

[Adobe Audit space usage](https://helpx.adobe.com/uk/acrobat/desktop/create-documents/optimize-pdfs/audit-space.html) was checked for this increment. PdfSpace now has [encoded-source-stream accounting](space-audit.md), not an exact whole-file byte partition or optimizer. Page labels are merged; document comparison, image/PDF backgrounds/watermarks, attachments/OCG authoring, source paragraph shaping, certificates and conformance remain open as described above.

## Attachment inspection (2026-09-30)

Adobe's [attachment workflow](https://helpx.adobe.com/acrobat/desktop/edit-documents/use-links-and-attachments/add-attachment.html) includes adding files and editing descriptions. This increment implements only catalog browsing and confirmed extraction, as specified in [attachments.md](attachments.md). It does not claim those authoring operations or portfolio equivalence.
