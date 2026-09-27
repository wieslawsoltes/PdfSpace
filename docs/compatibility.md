# PDF compatibility and release boundary

## 0.2.0-alpha.1

PdfSpace is an independent Uno/Skia workspace. This release adds interoperable PDF workflows; it does not claim complete Acrobat feature or pixel parity.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Native PDF save | Standard annotations and replies, field values/defaults/properties, new fields, widget deletion, safe links, original page content | A rewritten PDF, not an incremental or byte-for-byte save; signatures and XFA block structured changes |
| Catalog preservation | Original catalog retained for one source with the original page sequence | Page assembly creates a new catalog; tags, attachments, named destinations and other document-level data are not guaranteed to survive |
| Text editing | Replace supported top-level text-showing operands using characters already available in the font encoding | No arbitrary reflow, new font embedding, nested form-XObject editing, general image-object editing or complete typography engine |
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

OCR; arbitrary PDF text/image layout editing; general font replacement and paragraph reflow; certificate signing/validation and trusted timestamping; complete PDF accessibility tagging; PDF/A, PDF/X or PDF/UA certification; professional print-production/preflight; complete Office conversion; portfolios/multimedia; cloud identity, audit trails, real-time review and Adobe plug-in compatibility. Visual drawn signatures are not cryptographic signatures.

## Limits and verification

The workspace limits source data to 64 MB, 1–4096 pages, eight open documents, 50,000 annotations, 100,000 points per path, two million total annotation points and 100 history entries. Browser security processing has a 60-second deadline, 64 MB working-copy limit and 128 MB encrypted-output limit. PNG and raster-export limits are enforced separately by their renderers. These limits are guardrails, not proof against every hostile PDF or decompression bomb.

The tests exercise native PDF round trips, source text replacement, hierarchy-aware widget deletion, encryption authentication, redaction reconstruction, Uno pointer/keyboard interaction, file picking, download/reopen, and recovery. Browser-encrypted output is additionally reopened by the independent native PDFsharp backend. A passing test corpus does not establish universal PDF compatibility or independent security certification.
