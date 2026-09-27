# Security and privacy

PdfSpace is pre-release software. Do not use it as a security boundary, redaction product, signature trust service or compliance validator.

## Data handling

Documents are processed on the device. The application has no document-upload endpoint, account service or analytics integration. Loading the web application still downloads its application assets and dependencies from its hosting origin. Browser recovery uses IndexedDB and stores full original PDF bytes in the workspace; it is not encrypted. Browser origin isolation is origin-based, not path-based, so other applications on the same GitHub Pages origin are not a separate confidentiality boundary.

Desktop recovery is stored under the user's local application-data directory in `PdfSpace/recovery.pdfspace`. Printing creates a temporary PDF for the operating-system viewer. Downloaded and temporary files are the user's responsibility to protect or delete. The current recovery slot holds the active document only. Export a `.pdfspace` file to preserve edits permanently.

PDF JavaScript, launch actions and embedded executables are not executed by the application. This does not make native parsers or codecs immune to malicious input. Keep dependencies current and do not assume the configured file/page limits prevent every resource-exhaustion attack.

## Important boundaries

Cropping, covering content, highlighting or adding a watermark is not secure redaction. Original source bytes are retained in a workspace. Visual signatures are not cryptographic signatures. PDF export is a new visual document and does not preserve or validate source signatures, forms, tags, protection or compliance status.

## Reporting

Report security-sensitive bugs through the repository's private vulnerability reporting facility when available. Do not attach confidential PDFs or credentials to a public issue. For public reproductions, provide a minimal synthetic PDF and describe the affected operation, browser/OS and build commit.
