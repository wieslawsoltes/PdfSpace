# Security and privacy

PdfSpace is alpha software, not a certified security boundary, signature trust service or compliance validator.

## Local processing

PDFs are processed on the device. There is no document-upload service or analytics integration. The browser fetches application assets, including its hash-pinned QPDF WASM runtime, from the hosting origin. Each security operation uses a separate worker and private memory filesystem; inputs are not written to IndexedDB or uploaded. Workers terminate after completion, failure or timeout. JavaScript strings cannot be reliably zeroized.

AES-256 output uses separate opening and owner passwords. PdfSpace explicitly authenticates the owner password before creating an editable decrypted copy, because QPDF itself does not enforce PDF permission flags. The native provider uses PDFsharp Modify mode. Wrong passwords and unsupported algorithms do not cause an unencrypted fallback download. Permission flags depend on viewer enforcement and are not DRM.

## Recovery and exports

Ordinary recovery stores full original PDF bytes in unencrypted IndexedDB or an atomic desktop recovery file. Decrypted sources are marked sensitive and automatic recovery is disabled. Explicit exports may contain plaintext and must be protected by the user. Browser origin isolation is not path isolation: other applications on the same GitHub Pages origin are not a separate confidentiality boundary.

Desktop recovery lives in `PdfSpace/recovery.pdfspace` below local application data. Printing creates a temporary visual PDF for the system viewer. Downloaded and temporary files remain the user's responsibility. Recovery currently covers the active document, not every open tab.

## Redaction and signatures

Crop, opaque marks, ordinary field/object deletion and pending redaction annotations do not securely remove confidential data. Workspaces always retain original source bytes. The raster-redaction command explicitly reconstructs every page as an image-only document with burned-in marks; the output contains no original text layer or copied original catalog, annotations or form objects. Inspect every page and every mark before sharing. This is not selective vector redaction or a claim of independent security certification.

Drawn signatures are visual marks, not cryptographic signatures. Structured changes to signed/certified PDFs or XFA forms are blocked. Flattening is a separately labeled operation that loses those features. PDF JavaScript and launch actions are not executed by the UI, but native structured saves may preserve unsupported source objects; saving is not a general-purpose sanitizer.

## Reporting

Report security-sensitive issues through private vulnerability reporting when available. Do not post private PDFs, real passwords or credentials in public issues. Use minimal synthetic reproductions with the operation, browser/OS and build commit. Input limits and worker isolation reduce exposure but do not prove resistance to every malformed PDF or resource-exhaustion attack.

## Form interchange

XFDF and JSON imports are plain field-value operations, not document/script execution. XML DTDs and external resolvers are disabled; nesting, fields, values and total input size are bounded. Duplicate/ambiguous properties fail instead of being overwritten. A validated snapshot-bound plan applies atomically, rejecting stale state and invalid/read-only changes. Document URLs in data files are not fetched. Form-data exports are unencrypted; unlocked-source exports require confirmation.

## OCR data

Recognition uses local Tesseract: a self-hosted browser worker or an installed native executable. Browser model caching is disabled and workers end at batch completion/cancellation. Native image/TSV data is piped through process streams without document temp files. OCR text and confidence metadata are sensitive document data and are included in explicit workspace/searchable PDF exports. Recognition and correction are not redaction, and cannot guarantee factual accuracy. Raster-redacted output never carries this text layer.
