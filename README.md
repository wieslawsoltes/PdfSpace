<div align="center">

# PdfSpace
### Read, edit and review PDFs — on your device.

[![Build](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/build.yml)
[![Desktop](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/desktop.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/desktop.yml)
[![Pages](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/pages.yml)

**[Open the browser app](https://wieslawsoltes.github.io/PdfSpace/)** · **[Components](docs/components.md)** · **[Architecture](docs/architecture.md)** · **[Compatibility](docs/compatibility.md)**

</div>

PdfSpace is an independent, local-first PDF application built in **C# with Uno Platform and SkiaSharp**. The browser runs a real Uno WebAssembly application with the same document, editing and rendering libraries as the desktop host. An Acrobat-inspired shell brings together document tabs, floating quick tools, comments, form preparation, original-text editing, protection, redaction and page organization.

> **0.5.0-alpha.1:** functional PDF workflows, not complete or pixel-identical Adobe Acrobat compatibility. PdfSpace has original branding and icons and is not affiliated with Adobe. Review the [save and security boundaries](docs/compatibility.md) before processing important documents.

## Native object editing in 0.5

**Edit objects** selects native text, paths, images, shading and Form groups directly on the page. Mixed selections support drag/resize/nudge, align/distribute, crop, stacking, grouping, copy/paste across documents, and one-transaction undo. Path controls edit real geometry and fill/stroke; explicit replacement text blocks wrap into a chosen box with overflow checks. Source-run text and native image editing remain available. [Object editing guide and exact limits](docs/object-editing.md).

## Photo compatibility and memory in 0.4.1

Native image import/replacement now honors all EXIF orientations, preserves eligible JPEG compression and streams PNG alpha/RGB compression without full-image staging arrays. Image duplication shares source data; Restore image proportions works after rotation. Undo history now has a distinct-source byte budget alongside its entry cap, with explicit history release in Properties. [Photograph workflows](docs/photo-import.md) · [Performance and memory](docs/performance.md).

## Existing-PDF editing and performance in 0.4

Edit a specific native image placement: select, move, resize, rotate, flip, replace, insert or delete without modifying its other shared uses. Supported original text inside nested Form XObjects can now be changed, with explicit Unicode font replacement for independent horizontal runs. No white-cover or page-rasterization editing simulation is used. [Native object workflows and limits](docs/native-object-editing.md).

The viewer now uses indexed page geometry instead of repeatedly arranging the entire document. Per-document bounded search indexes, constant-time picture LRU touches, bounded parsed-source caches and one-parse-per-source page assembly reduce repeated work. [Performance design and reproducible measurements](docs/performance.md).

## Scan & OCR in 0.3

Recognize image-only PDFs locally in **English, Polish or German**, review and correct words against the original scan, and export a real searchable PDF with an invisible Unicode text layer. Import PNG/JPEG scans, select page ranges, cancel a batch without partial edits, and undo recognition or corrections. The browser self-hosts its pinned Tesseract.js/WASM/model assets; native hosts use an installed Tesseract 5 executable. No document is sent to an OCR service. [OCR workflow, integration and limitations](docs/ocr.md).

## What you can do

**Read and navigate.** Open PDFs and workspaces, use multiple tabs, search and copy text, zoom/pan, switch continuous/single/two-page layouts, browse virtualized thumbnails, navigate original PDF outlines and create workspace bookmarks. Local named destinations and internal links are supported.

**Review without flattening everything.** Add text, highlights, underlines, strikethrough, notes, replies, ink, shapes, arrows, stamps and links. Move, resize and recolor annotations; resolve comments; undo and redo changes. Structured PDF export writes supported annotations and replies as native PDF objects.

**Prepare and fill forms.** Create text, check-box and dropdown fields directly on a page. Fill imported text, check boxes, radio groups and single-select choices. Edit tooltips, defaults, read-only/required/multiline flags, font size, maximum length, geometry and choice labels. Delete imported widgets with undo and native PDF persistence. Tab/Shift+Tab move between fields, Space selects a button, and arrows change choices.

**Edit supported original text.** Replace actual PDF text-showing operands rather than covering them with a white rectangle. Unsupported encodings, absent glyphs and stale edits are rejected. Explicit replacement text blocks provide bounded word wrapping; automatic reconstruction of arbitrary source paragraphs and complex-script shaping remain outside this release.

**Organize and export.** Reorder, rotate, duplicate, delete, crop, combine, extract and split pages. Download native PDF, a separately labeled flattened visual copy, PNG, text, form data or an editable workspace. Reorganized existing form documents are blocked from structured saving when the field tree cannot be preserved safely.

**Protect or redact a copy.** Export AES-256 PDFs with distinct opening and owner passwords. Browser protection uses an isolated QPDF WASM worker; desktop uses PDFsharp. Explicit owner authentication is required before reopening for editing. Redaction creates a separately confirmed, image-only reconstruction of all pages with burned-in marks; original PDFs and workspaces remain unredacted.

The included six-page **Circular futures** report and interactive review form are original synthetic documents for exercising the tools without uploading private data.

## Choose the right save operation

| Operation | Output | Behavior |
|---|---|---|
| **Export PDF** | `.pdf` | Native annotations/forms and source page content. Retains the catalog only for a single source in its original page sequence. Not an incremental or lossless guarantee. |
| **Export flattened visual PDF** | `.pdf` | A new visual document; interactive and structural features are discarded. |
| **Save editable workspace** | `.pdfspace` | Original source bytes and reversible editing state. Unencrypted, including cropped/deleted/pending-redaction source data. |
| **Protect a PDF** | encrypted `.pdf` | AES-256 revision 6 with explicitly chosen passwords and permission flags. |
| **Apply raster redactions** | image-only `.pdf` | Separately rebuilt pages. Loses searchability, forms, links, vectors and original document-level objects. |

**Cropping is not redaction. Field/object deletion is not confidential-data erasure. A drawn signature is not a certificate signature.** XFA, form scripts/calculations, certificate signatures, full tagging/compliance, Office conversion and cloud collaboration remain unimplemented.

## Form data and navigation

**Prepare a form → Import form data** accepts plain field values from XFDF or JSON. The complete batch is validated, unmatched names are reported, and confirmation applies one undoable transaction. Export either interoperable XFDF or versioned JSON. **Check required fields** takes you to the first missing supported value. Read-only changes, invalid choices, unsupported multi-values and overlong input reject the whole import.

The **Bookmarks** panel separates original PDF outlines from editable workspace bookmarks. Supported internal links follow destination page identities through insertion, duplication, reordering, extraction and combination. Links to removed destinations are removed rather than redirected.

Form-data files are unencrypted. Document URLs and scripts are never followed or executed. See the [form-data and navigation guide](docs/form-data-and-navigation.md) for formats, defensive limits and embedding examples.

## Eleven reusable libraries

| Package | Responsibility |
|---|---|
| `PdfSpace.Core` | Workspace, page, annotation, field and geometry models; validated JSON |
| `PdfSpace.Layout` | Crop/rotation transforms and viewport/page layouts |
| `PdfSpace.Editing` | Transactional editing, shared field state and bounded undo/redo |
| `PdfSpace.Documents` | PdfPig import, search, page ranges and XFDF/JSON form data |
| `PdfSpace.Skia` | Cached rendering, overlays, visual export and raster redaction |
| `PdfSpace.Pdf` | PDFsharp native annotation/form writer, source-text editing, inspection and security-provider contract |
| `PdfSpace.Ocr` | Local recognition contracts, raster preparation, bounded TSV decoding, snapshot batches and native process adapter |
| `PdfSpace.Controls` | Custom Uno icons, command buttons, tabs, panels, palettes and dialogs |
| `PdfSpace.Viewer` | Embeddable document viewport, form input and thumbnails |
| `PdfSpace.Storage` | File, recovery, clipboard and print contracts |
| `PdfSpace.Workbench` | Complete multi-document shell and PDF workflows |

The application is a thin browser/desktop host. All eleven libraries are packable; CI produces NuGet artifacts. This does not mean they have been published to nuget.org. See [embedding examples](docs/components.md).

## Toolchain

.NET SDK **10.0.401**, Uno SDK **6.7.30** / WinUI **6.7.135**, matched managed/native SkiaSharp **3.119.4**, PdfPig **0.1.16**, PdfPig.Rendering.Skia **0.1.16.4**, PDFsharp **6.2.4**. Browser security uses hash-pinned `@neslinesli93/qpdf-wasm` **0.3.0** / QPDF **12.2.0**. Rendering is still Uno/Skia, not a third-party web PDF viewer.

## Build and run

Install the .NET SDK, Python 3, Node.js 22, and the Uno desktop prerequisites for your operating system.

```bash
git clone https://github.com/wieslawsoltes/PdfSpace.git
cd PdfSpace
python3 scripts/fetch-assets.py
dotnet workload install wasm-tools

# Native engine and PDF round-trip tests.
dotnet run --project tests/PdfSpace.Tests -c Release

# Desktop host.
dotnet run --project src/PdfSpace.App -f net10.0-desktop -p:PdfSpaceDesktopOnly=true

# Browser publish and static-root collection.
dotnet publish src/PdfSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/PdfSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Open `http://127.0.0.1:4173/PdfSpace/`. The asset scripts verify upstream font/runtime hashes. Static collection includes the self-hosted security worker, WASM and dependency notices. No PDF/password processing service is required.

### Browser acceptance tests

```bash
mkdir -p artifacts/fixtures
cp artifacts/engine/*.pdf artifacts/engine/scanned.png artifacts/fixtures/
npm ci --ignore-scripts
npx playwright install chromium
npm run test:browser
dotnet run --project tests/PdfSpace.Tests -c Release -- \
  --verify-browser-security artifacts/browser-exports
dotnet run --project tests/PdfSpace.Tests -c Release -- \
  --verify-browser-ocr artifacts/browser-exports
```

UI tests use real pointer, keyboard, file-picker and download/reopen interactions. `?test=1` enables read-only diagnostics, not a document-mutation API. The security-provider contract also has dedicated boundary tests. CI retains screenshots, reports and failure traces.

## Delivery and documentation

**Build** tests the native engines, publishes and tests Uno WebAssembly, independently verifies the browser-encrypted PDF, and packages the libraries. **Desktop** compiles on Windows, macOS and Linux. **Pages** deploys a successful main-branch build and validates its commit identity and public behavior. **Release** builds version tags and supports optional NuGet publication through an explicitly configured key.

[Architecture](docs/architecture.md) · [Components](docs/components.md) · [Compatibility](docs/compatibility.md) · [Shortcuts](docs/shortcuts.md) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

PdfSpace source is [MIT licensed](LICENSE). Dependencies and fonts retain their upstream licenses. Adobe and Acrobat are trademarks of their respective owners; Adobe proprietary code, branding and assets are not included.
