<div align="center">

# PdfSpace
### Read, edit and review PDFs — on your device.

[![Build](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/build.yml)
[![Desktop](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/desktop.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/desktop.yml)
[![Pages](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PdfSpace/actions/workflows/pages.yml)
[![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/PdfSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Core.svg)](https://www.nuget.org/packages/PdfSpace.Core)

**[Open the browser app](https://wieslawsoltes.github.io/PdfSpace/)** · **[Components](docs/components.md)** · **[Architecture](docs/architecture.md)** · **[Compatibility](docs/compatibility.md)**

</div>

PdfSpace is an independent, local-first PDF application built in **C# with Uno Platform and SkiaSharp**. Its browser host is a real Uno WebAssembly application, sharing document, editing and rendering libraries with the desktop host. An Acrobat-inspired shell brings together document tabs, floating tools, original-content editing, review, forms and page organization.

> **0.6.4-alpha.1** provides functional PDF workflows within explicit compatibility limits. It is not complete or pixel-identical Adobe Acrobat parity. PdfSpace has original branding and icons and is not affiliated with Adobe. Check [save and security boundaries](docs/compatibility.md) before processing important documents.

## New in 0.6.4

**Native attachment authoring.** Choose **Convert → Browse attachments → Add attachment**, or **Edit → Attach file**, to embed a file and Unicode description. Existing entries support description edits, payload replacement and removal from the catalog. Effective edits are undoable and persist through ordinary PDF export/reopen. The engine accepts blank workspaces or one original PDF in its complete original page order. Signed, XFA, Portfolio and assembled-catalog inputs are guarded. [Workflow, APIs and limits](docs/attachments.md#native-authoring-064).

**Less repeated PDF work.** Catalog-only attachment changes retain page-state arrays, source identity and renderer-preview identity. `PdfAttachmentEditor.AddRange` applies up to 32 files in one native parse/write rather than repeating those stages for every file. Shared file specifications are copy-on-write; compression is retained only when it makes the payload smaller. Desktop file input uses pooled, bounded reads, including non-seekable or growing files. [Performance contracts](docs/performance.md#catalog-attachment-authoring-064).

**Separate recovery feedback.** A per-tab footer badge reports recovery progress and failures without overwriting foreground command results. A late save completion cannot mark a newly opened tab as saved. Recovery still keeps the last saved workspace, not a multi-document archive. [Recovery and file ownership](docs/attachments.md#recovery-feedback-and-file-ownership).

Embedded files are never executed automatically. **Removing a catalog entry is not secure erasure**: old objects, shared references, previews and undo workspaces may retain its payload.

## Workflows

| Area | Available behavior | Details |
|---|---|---|
| **Read and navigate** | Multiple documents, search/copy, zoom/pan, continuous/single/two-page layouts, virtualized thumbnails, outlines, bookmarks, local destinations and exact page-label navigation. | [Labels and sections](docs/page-labels.md) |
| **Edit native content** | Supported original text operands and Unicode replacement; native image placements; vector paths and Bézier points; mixed transforms, appearance, grouping, stacking and internal cross-document clipboard. | [Object editing](docs/object-editing.md) |
| **Position precisely** | Rotation handles and numeric angles, axis/aspect modifiers, move/resize/point snapping, page/reference alignment, size matching and equal-gap distribution. | [Layout](docs/object-layout.md) · [Gestures](docs/interaction-editing.md) |
| **Review** | Native notes/replies, highlights, underlines, strikethrough, ink, shapes, links and stamps; annotation movement, properties, resolution and undo. | [Compatibility](docs/compatibility.md) |
| **Prepare and fill forms** | Supported AcroForm text, checkboxes, radio groups and single-select choices; field authoring/properties, shared widgets, required checks and XFDF/JSON values. | [Forms and navigation](docs/form-data-and-navigation.md) |
| **Scan and recognize** | Local English, Polish and German OCR, page ranges, cancellation, confidence review/correction and searchable invisible Unicode export. | [OCR](docs/ocr.md) |
| **Organize pages** | Crop, rotate, reorder, duplicate, delete, insert, combine, extract and split; labels follow page identity. Existing-form/catalog assembly is guarded. | [Compatibility](docs/compatibility.md) |
| **Mark documents** | Searchable headers/footers, page/date/Bates templates, text watermarks, selected ranges, crop/rotation-aware placement, validated update/remove and no-op identity. | [Page marks](docs/page-marks.md) |
| **Inspect source space** | Encoded-stream accounting, seven categories, bounded largest-stream details and JSON export with weak source caching. This is not a whole-file byte partition or optimizer. | [Space audit](docs/space-audit.md) |
| **Manage attachments** | Catalog browsing, confirmed downloads, add/batch-add, description edits, replacement and removal, with bounded input and source-scoped paging. | [Attachments](docs/attachments.md) |
| **Protect or redact a copy** | Local AES-256 opening/owner-password workflows and separately confirmed image-only reconstruction with burned-in redaction marks. | [Security](SECURITY.md) |

The bundled **Circular futures** report and review form are original synthetic documents. They exercise the tools without uploading a private document. See the [21-area Acrobat coverage inventory](docs/acrobat-coverage.md) for implemented, partial and absent workflows, and the [changelog](CHANGELOG.md) for previous increments.

## Choose the correct save operation

| Operation | Output | Important boundary |
|---|---|---|
| **Export PDF** | Native `.pdf` | Writes supported annotations/forms and source content. Original catalog preservation requires one source in its complete original page sequence. Not an incremental or universally lossless update. |
| **Export flattened visual PDF** | New visual `.pdf` | Discards interactive and structural features. |
| **Save editable workspace** | `.pdfspace` | Retains original source bytes and reversible editing state, including cropped/deleted and pending-redaction data. Unencrypted. |
| **Protect a PDF** | Encrypted `.pdf` | AES-256 revision 6 with explicitly chosen passwords and permission flags. |
| **Apply raster redactions** | Image-only `.pdf` | Reconstructs every page, losing searchable text, forms, links, vectors and original catalog objects. |

**Cropping, ordinary object/field deletion, attachment removal and history clearing are not secure redaction. A drawn signature is not a certificate signature.** General paragraph reconstruction, complex shaping, XFA/scripts/calculations, cryptographic signatures, complete tagging/conformance, Office conversion and cloud collaboration remain separate gaps. No unknown retained source dictionary is counted as a supported authoring feature.

## Twelve reusable libraries

| Package | Responsibility |
|---|---|
| `PdfSpace.Core` | Workspace, page, annotation, field and geometry models; validated JSON |
| `PdfSpace.Layout` | Crop/rotation transforms, page geometry, spatial indexes and manipulation/snapping |
| `PdfSpace.Editing` | Transactional editing, shared field state and bounded undo/redo |
| `PdfSpace.Documents` | PdfPig import, search and XFDF/JSON form data |
| `PdfSpace.Rendering.Skia` | Source-pinned Apache-2.0 renderer adaptation with image-alpha, dash and miter fixes |
| `PdfSpace.Skia` | Cached rendering, overlays, visual export and raster redaction |
| `PdfSpace.Pdf` | Native PDF writing/editing, forms, page marks, labels, audits, attachments and security contracts |
| `PdfSpace.Ocr` | Local recognition contracts, bounded TSV decoding, snapshot batches and native process adapter |
| `PdfSpace.Controls` | Custom Uno icons, commands, tabs, panels, palettes and dialogs |
| `PdfSpace.Viewer` | Embeddable document viewport, form input and thumbnails |
| `PdfSpace.Storage` | File, recovery, clipboard, print and bounded input contracts |
| `PdfSpace.Workbench` | Multi-document shell and complete application workflows |

The application is a thin browser/desktop host. All twelve libraries are packable. CI package artifacts are not evidence that a particular version is published to nuget.org. [Embedding examples](docs/components.md).

## Build and run

The repository pins **.NET SDK 10.0.401**, **Uno SDK 6.7.30 / WinUI 6.7.135**, matched managed/native **SkiaSharp 3.119.4**, **PdfPig 0.1.16** and **PDFsharp 6.2.4**. The renderer adaptation is based on PdfPig.Rendering.Skia 0.1.16.4. Browser security uses hash-pinned QPDF WASM; browser OCR self-hosts its pinned Tesseract assets. Rendering remains Uno/Skia rather than a third-party web PDF viewer.

Install the pinned .NET SDK, Python 3, Node.js 22 and your platform's Uno desktop prerequisites.

```bash
git clone https://github.com/wieslawsoltes/PdfSpace.git
cd PdfSpace
python3 scripts/fetch-assets.py
dotnet workload install wasm-tools

# Native engine and PDF round-trip checks.
dotnet run --project tests/PdfSpace.Tests -c Release

# Desktop host.
dotnet run --project src/PdfSpace.App -f net10.0-desktop -p:PdfSpaceDesktopOnly=true

# Browser publish and static-root collection.
dotnet publish src/PdfSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/PdfSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
```

Open `http://127.0.0.1:4173/PdfSpace/`. Asset scripts verify upstream hashes; static collection includes self-hosted runtime assets and dependency notices. No PDF/password/OCR processing service is required. Native OCR requires a separately installed Tesseract executable and models; set `PDFSPACE_TEST_NATIVE_OCR=1` to include its installed-engine regression checks.

### Browser acceptance and native export verification

```bash
mkdir -p artifacts/fixtures
cp artifacts/engine/*.pdf artifacts/engine/*.png artifacts/engine/*.jpg artifacts/engine/*.txt artifacts/fixtures/
npm ci --ignore-scripts
npx playwright install chromium
npm run test:browser
bash scripts/verify-browser-exports.sh artifacts/browser-exports
```

Tests drive real pointer, keyboard, file-picker and download/reopen interactions. `?test=1` enables read-only diagnostics, not document mutation. CI uses two isolated browser workers and zero retries. Screenshots, reports, downloaded files and failure traces are retained. [Independent attachment checks](docs/attachments.md#reproduce-authoring-verification).

## Delivery

**Build** runs native checks, publishes/tests Uno WebAssembly, independently reopens browser exports and packages reusable libraries. **Desktop** compiles on Windows, macOS and Linux. **Pages** deploys a successful main-branch Build and verifies its public commit identity and behavior.

**Release** produces browser/source packages and six self-contained desktop targets. Tagged releases use the repository's configured NuGet Trusted Publishing environment; manual runs are dry-run artifact builds. Publication requires that configuration and a successful release workflow, not merely a version change in source.

[Architecture](docs/architecture.md) · [Components](docs/components.md) · [Compatibility](docs/compatibility.md) · [Shortcuts](docs/shortcuts.md) · [Contributing](CONTRIBUTING.md) · [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

Original PdfSpace source is [MIT licensed](LICENSE). The source-pinned `src/PdfSpace.Rendering.Skia` derivative is [Apache-2.0 licensed](src/PdfSpace.Rendering.Skia/LICENSE.txt), with original copyrights, notices and modification records retained. Dependencies and fonts retain their upstream licenses. Adobe and Acrobat are trademarks of their respective owners; Adobe proprietary code, branding and assets are not included.
