# PDF compatibility and release boundary

## 0.4.0-alpha.1

PdfSpace is an independent Uno/Skia workspace. This release adds interoperable PDF workflows; it does not claim complete Acrobat feature or pixel parity.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Native PDF save | Standard annotations and replies, field values/defaults/properties, new fields, widget deletion, safe links, original page content | A rewritten PDF, not an incremental or byte-for-byte save; signatures and XFA block structured changes |
| Catalog preservation | Original catalog retained for one source with the original page sequence | Page assembly creates a new catalog; tags, attachments, named destinations and other document-level data are not guaranteed to survive |
| Text editing | Occurrence-isolated page and nested-form text replacement; original-encoding mode; explicit embedded Unicode font for independent horizontal runs | No paragraph reflow, complex-script shaping/bidi, vertical layout, outlined glyph editing or arbitrary font repair |
| Image editing | Select/move/resize/rotate/flip/replace/insert/delete native image occurrences, including shared nested forms | Not redaction; no image crop/mask editor, inline images, ICC/CMYK-preserving replacement or live pixel deformation |
| Forms | Text, check boxes, imported radio groups, single-select choices; creation of text/check/dropdown widgets; tooltip, flags, defaults, font size, bounds, maximum length and options | No XFA, JavaScript calculations/validation, multi-select choices, push buttons, certificate fields, general hierarchy editing or complete PDF tab-order semantics |
| Protection | AES-256 revision-6 output with separate opening/owner passwords and print/copy/edit flags; owner-authorized reopening | Permission flags rely on reader enforcement; not DRM. No certificate-based encryption or signing |
| Redaction | Explicitly confirmed, separate image-only reconstruction with burned-in marks on all pages | Raster output loses text/search, forms, links and vectors. No selective vector/object redaction. Source documents and workspaces remain unredacted |
| Rendering | PdfPig/Skia with bounded picture caches and independently editable overlays | Specialized codecs, fonts, colors and transparency need corpus testing; rendering errors are shown rather than ignored |

## Save operations are different

**Export PDF** performs structured PDF writing. With one original source and unchanged page order it retains the source catalog and page content, then synchronizes supported annotations and form widgets. It does not promise preservation of every unsupported PDF feature. Changes to a signed/certified PDF or XFA form are blocked rather than silently invalidating them.

**Export flattened visual PDF** is an explicitly named alternative that replays visuals into a new Skia PDF. The interactive and structural objects are not retained. It is not the default Save PDF operation and it does not securely redact hidden information.

**Save editable workspace** retains original source bytes and reversible editing state. Its JSON is unencrypted. Treat it as containing the entire original document, including data no longer visible after cropping, object deletion or pending redaction marks.

## Browser cryptography

The browser injects `IPdfSecurityProvider` using a self-hosted, hash-pinned QPDF 12.2.0 WebAssembly runtime. Each operation runs in a new worker with a private memory filesystem. Cryptographic randomness uses the browser's secure random generator. The worker is terminated after the result or a timeout; no document or password is sent to a server or persistent filesystem. JavaScript string zeroization is not guaranteed.

QPDF itself does not enforce PDF permission flags. PdfSpace therefore checks `ownerpasswordmatched` before decrypting an encrypted working copy. An opening-only password is not accepted for editing, even when a low-level library could technically decrypt it. Wrong/unsupported operations fail without downloading a plaintext fallback. The native desktop provider uses PDFsharp and the platform .NET cryptography implementation.

Decrypted documents are marked sensitive. Automatic recovery is disabled for them. Exporting an unencrypted workspace or structured PDF requires an explicit warning/confirmation; password protection is not implicitly retained when editing an unlocked copy.

## Form editing behavior

Field values, defaults and common properties are logical-field state shared across its widgets. Geometry belongs to the selected widget. Native saves update `/V`, `/DV`, `/Ff`, `/TU`, `/DA`, `/MaxLen`, `/Opt`, widget rectangles and appearance streams as appropriate. Deleting an imported widget removes its page annotation and field-tree entry, prunes empty ancestors and removes obsolete calculation-order references, without deleting sibling widgets. This is ordinary field deletion, not confidential-data erasure.

Tab and Shift+Tab navigate supported editable widgets in page/annotation-array order. Text is committed on traversal. Space activates a focused check/radio button; arrows change a focused choice. This is not complete `/Tabs` structure-tree ordering or document screen-reader semantics. Supported text appearances use the host font; subsequent third-party viewer editing may use the declared Helvetica fallback. Existing zero-size automatic text fitting uses an explicit 12-point viewer fallback.

## Not implemented

Arbitrary PDF layout editing; general font/shaping replacement and paragraph reflow; certificate signing/validation and trusted timestamping; complete PDF accessibility tagging; PDF/A, PDF/X or PDF/UA certification; professional print-production/preflight; complete Office conversion; portfolios/multimedia; cloud identity, audit trails, real-time review and Adobe plug-in compatibility. Visual drawn signatures are not cryptographic signatures.

## Limits and verification

The workspace limits source data to 64 MB, 1–4096 pages, eight open documents, 50,000 annotations, 100,000 points per path, two million total annotation points and 100 history entries. Browser security processing has a 60-second deadline, 64 MB working-copy limit and 128 MB encrypted-output limit. PNG and raster-export limits are enforced separately by their renderers. These limits are guardrails, not proof against every hostile PDF or decompression bomb.

The tests exercise native PDF round trips, source text replacement, hierarchy-aware widget deletion, encryption authentication, redaction reconstruction, Uno pointer/keyboard interaction, file picking, download/reopen, and recovery. Browser-encrypted output is additionally reopened by the independent native PDFsharp backend. A passing test corpus does not establish universal PDF compatibility or independent security certification.

## Form data and navigation (0.2.1)

Plain-field XFDF and versioned/legacy JSON import/export are implemented with complete-batch validation, explicit unmatched-name reporting and one undo operation. Required-field checks cover supported plain values, not script/XFA/certificate rules. DTD/entity processing is disabled; form-data input is limited to 4 MiB.

The viewer exposes original source outline navigation and resolves local legacy/name-tree destinations. Supported internal links follow retained page identities during page operations; removed destinations remove their links. Native save rewrites these links to page-fit destinations. Source-only outlines/named metadata are not promised during page reassembly. Managed workspace bookmarks round-trip separately from source outlines. See [form data and navigation](form-data-and-navigation.md).

## Scan & OCR (0.3)

Image-only PDF recognition, PNG/JPEG-to-PDF import, English/Polish/German models, per-word confidence review/correction, cancellation, snapshot-safe batch application and native searchable PDF export are implemented. Existing text pages are skipped; mixed text/image-region recognition, automatic deskew, handwriting guarantees, arbitrary languages and complex shaping remain unsupported. Native hosts need Tesseract 5 installed. The browser hosts all pinned code and models itself. See [OCR](ocr.md) for the exact export and resource limits.

## Native objects and performance (0.4)

See [native object editing](native-object-editing.md) for copy-on-write occurrence isolation, encoding and graphics-state limits, and [performance](performance.md) for indexed viewport/search geometry, source reuse, cache bounds and reproducible measurements. These operations retain original bytes in undo/workspaces and must not be mistaken for sanitization.

## Photo and history update (0.4.1)

PNG/JPEG insertion, replacement and image-to-PDF now support all EXIF orientations. Unrotated 8-bit Gray/RGB/YCbCr JPEGs without ICC metadata retain original compressed samples; others decode to sRGB with row-streamed RGB/alpha compression. Native duplicate and restore-proportions commands are supported. Input is limited to one frame, 32 MiB, 16 megapixels and 8192 pixels per axis. The 0.4.1 image-specific commands do not rewrite inline images or edit existing clipping paths. The unified 0.5 object workflow below adds guarded native clipping, same-scope arrangement and explicit replacement-block layout. Ordinary duplication, replacement and history clearing are not sanitization.

Undo retains at most 100 entries and 128 MiB of distinct original/preview buffers by default, pruning oldest undo entries as required. Current state is always retained. The budget is per session and does not count all managed/native/UI memory; external owners can retain additional snapshots. See [photo-import.md](photo-import.md) and [performance.md](performance.md).

## Unified native object editor (0.5)

The Objects inspector selects native text objects, vector paths, image occurrences, Form placements and shading placements. It supports mixed transforms, native crop clips, alignment/distribution, same-scope stacking, duplication, resource-aware internal copy/cut/paste, consecutive-sibling grouping, and guarded ungrouping. Path painting/control points and explicit page-aligned Unicode text blocks are editable; new rectangles, ellipses and text are native content rather than annotations.

A descriptor belongs to a specific source snapshot and occurrence. Edits revalidate its source, page, scope, operator fingerprint and geometry; batch edits isolate each modified shared invocation prefix once. Pan/zoom do not rebuild the object index, and selection membership/union bounds are cached instead of rescanning every selected object per paint.

Clipping inherited by selected objects remains in force. Clipboard captures reproducible numeric clipping paths; text clipping and clips constructed under unsupported interleaved transforms are rejected rather than broadened. Grouping requires consecutive paints in one native scope and clipping context. Ungroup preserves the actual Form BBox and only flattens supported generated groups. Reordering clears stale selection indices, and already-front/back operations do not add undo entries.

This is not an arbitrary PDF graphics editor: inline images, clipping-text mutation, existing clip-path node authoring, full soft-mask/transparency/pattern authoring, arbitrary cross-scope ordering, complete complex-script paragraph shaping, and incremental signature-preserving saving remain outside this release. Object geometry previews are not live pixel-composited drag previews. The app clipboard can retain private resources and is not sanitization. See [object editing](object-editing.md).

## Object appearance and rendering (0.5.1)

Per-paint alpha/blend edits support paths, image/shading placements and safe text objects; they are not whole-Form group opacity. Existing masks are retained, not authored or removed. Stroke caps/joins/miter and full odd dash arrays are editable. Dash phase authoring is integer-only because the underlying PdfPig rendering operation quantizes phase; fractional imported phases remain in native data but can differ in preview. Spatial picking still uses bounds, not exact fill/alpha/clip geometry. The source-pinned renderer is an Apache-2.0 derivative with retained attribution. See [detailed contracts](object-appearance.md).
