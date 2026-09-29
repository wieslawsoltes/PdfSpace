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

PdfSpace is an independent, local-first PDF application built in **C# with Uno Platform and SkiaSharp**. The browser runs a real Uno WebAssembly application with the same document, editing and rendering libraries as the desktop host. An Acrobat-inspired shell brings together document tabs, floating quick tools, comments, form preparation, original-text editing, protection, redaction and page organization.

> **0.5.5-alpha.1:** functional PDF workflows, not complete or pixel-identical Adobe Acrobat compatibility. PdfSpace has original branding and icons and is not affiliated with Adobe. Review the [save and security boundaries](docs/compatibility.md) before processing important documents.

## Native layout and batch efficiency in 0.5.5

**Edit → Edit objects → Object layout** adds visible-page alignment, explicit reference-object alignment, width/height/size matching and equal-gap distribution. Repeated exact layout operations retain document identity rather than generating redundant native PDF rewrites. Native selection validation replaces quadratic pair checks with sorted occurrence intervals and ancestor lookup. [Workflow, APIs, limits and validation](docs/object-layout.md).

## Resize and vector-point alignment in 0.5.4

**Edit → Edit objects** now offers independent **Snap resizing objects** and **Snap vector points** controls. Resize guides preserve the fixed edge/corner and Shift proportions; point guides preserve axis locks and handle grab offsets. Alt bypasses snapping. A completed gesture makes one native PDF edit; stationary, returned and cancelled gestures leave the source unchanged.

The object inspector reuses a pool of at most **64 rows**, with Previous/Next ranges and **Go to object** reaching every indexed occurrence. Dense selection and range navigation do not reparse PDF objects or add history. Compacted snap anchors and allocation-free point/resize queries reduce geometry overhead. See [editing behavior](docs/object-editing.md#resize-and-vector-point-alignment-054) and [measurement scope](docs/performance.md#editing-guides-and-inspector-reuse-054).

## Precision object gestures in 0.5.2

Shift constrains movement and aspect ratio; Alt resizes or creates native shapes from their center. Native images and mixed-object selections share the same allocation-free geometry. Repeated selection avoids inspector churn, inline editor handoff retains keyboard focus, and acceptance tooling observes fresh compositor geometry. [Interaction and integration guide](docs/interaction-editing.md).

## Object appearance in 0.5.1

Native opacity/blending, stroke caps/joins/miter and dash editing are available under **Edit → Edit objects → Object appearance**. Dense pages use a reusable ordered spatial index for picking and visible selection outlines. The source-pinned Apache-2.0 renderer fixes image alpha, odd dash patterns and miter rendering. See [appearance, architecture and precise compatibility limits](docs/object-appearance.md).

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

## Download

Every [release](https://github.com/wieslawsoltes/PdfSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `PdfSpace-<version>-win-x64.zip` | `PdfSpace-<version>-win-arm64.zip` |
| macOS | `PdfSpace-<version>-osx-x64.tar.gz` | `PdfSpace-<version>-osx-arm64.tar.gz` |
| Linux | `PdfSpace-<version>-linux-x64.tar.gz` | `PdfSpace-<version>-linux-arm64.tar.gz` |

Extract and run `PdfSpace` (`PdfSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine PdfSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS.txt`.

The libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=PdfSpace), e.g. `dotnet add package PdfSpace.Core --prerelease`.

## NuGet packages

PdfSpace ships as twelve packages, all versioned together. Eleven are MIT-licensed; **`PdfSpace.Rendering.Skia` is Apache-2.0**, a derivative of an upstream renderer (see its subsection). Nine target plain `net10.0` and have no UI dependency: `PdfSpace.Core`, `Layout`, `Editing` and `Storage` are pure .NET, `Documents` adds PdfPig, `Rendering.Skia`, `Skia` and `Ocr` add SkiaSharp, and `Pdf` adds PDFsharp. The three Uno Platform packages (`Controls`, `Viewer`, `Workbench`) target `net10.0-desktop` and `net10.0-browserwasm`. Symbols are published to nuget.org as `.snupkg` with SourceLink. The application itself is a thin browser/desktop host and is not packaged; see also the [embedding guide](docs/components.md).

```sh
dotnet add package PdfSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
| :--- | :--- | :--- | :--- |
| [PdfSpace.Core](https://www.nuget.org/packages/PdfSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Core.svg)](https://www.nuget.org/packages/PdfSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Core.svg)](https://www.nuget.org/packages/PdfSpace.Core) | Immutable workspace, page, annotation, form-field, OCR-layer and geometry models; validated JSON |
| [PdfSpace.Layout](https://www.nuget.org/packages/PdfSpace.Layout) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Layout.svg)](https://www.nuget.org/packages/PdfSpace.Layout) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Layout.svg)](https://www.nuget.org/packages/PdfSpace.Layout) | Crop/rotation transforms, indexed page layouts, hit testing, snapping and spatial indexes |
| [PdfSpace.Editing](https://www.nuget.org/packages/PdfSpace.Editing) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Editing.svg)](https://www.nuget.org/packages/PdfSpace.Editing) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Editing.svg)](https://www.nuget.org/packages/PdfSpace.Editing) | Transactional annotation, page and form editing with bounded immutable undo/redo |
| [PdfSpace.Storage](https://www.nuget.org/packages/PdfSpace.Storage) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Storage.svg)](https://www.nuget.org/packages/PdfSpace.Storage) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Storage.svg)](https://www.nuget.org/packages/PdfSpace.Storage) | Platform-neutral file, recovery, clipboard and print contracts |
| [PdfSpace.Documents](https://www.nuget.org/packages/PdfSpace.Documents) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Documents.svg)](https://www.nuget.org/packages/PdfSpace.Documents) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Documents.svg)](https://www.nuget.org/packages/PdfSpace.Documents) | PdfPig import, text extraction, bounded search, page ranges and XFDF/JSON form data |
| [PdfSpace.Rendering.Skia](https://www.nuget.org/packages/PdfSpace.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/PdfSpace.Rendering.Skia) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/PdfSpace.Rendering.Skia) | Apache-2.0: source-pinned PdfPig Skia renderer with image-alpha, dash and miter fixes |
| [PdfSpace.Skia](https://www.nuget.org/packages/PdfSpace.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Skia.svg)](https://www.nuget.org/packages/PdfSpace.Skia) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Skia.svg)](https://www.nuget.org/packages/PdfSpace.Skia) | Cached page rendering, annotation/form overlays, PNG and flattened PDF export, raster redaction |
| [PdfSpace.Pdf](https://www.nuget.org/packages/PdfSpace.Pdf) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Pdf.svg)](https://www.nuget.org/packages/PdfSpace.Pdf) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Pdf.svg)](https://www.nuget.org/packages/PdfSpace.Pdf) | PDFsharp native annotations/AcroForms, source-text and native object editing, inspection and password protection |
| [PdfSpace.Ocr](https://www.nuget.org/packages/PdfSpace.Ocr) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Ocr.svg)](https://www.nuget.org/packages/PdfSpace.Ocr) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Ocr.svg)](https://www.nuget.org/packages/PdfSpace.Ocr) | Local OCR contracts, raster preparation, bounded Tesseract TSV decoding and cancellable batches |
| [PdfSpace.Controls](https://www.nuget.org/packages/PdfSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Controls.svg)](https://www.nuget.org/packages/PdfSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Controls.svg)](https://www.nuget.org/packages/PdfSpace.Controls) | Custom Uno icons, command buttons, tabs, panels, palettes and dialogs |
| [PdfSpace.Viewer](https://www.nuget.org/packages/PdfSpace.Viewer) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Viewer.svg)](https://www.nuget.org/packages/PdfSpace.Viewer) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Viewer.svg)](https://www.nuget.org/packages/PdfSpace.Viewer) | Embeddable Uno/Skia viewport with direct annotation editing, form input, zoom/pan, page layouts and thumbnails |
| [PdfSpace.Workbench](https://www.nuget.org/packages/PdfSpace.Workbench) | [![NuGet](https://img.shields.io/nuget/vpre/PdfSpace.Workbench.svg)](https://www.nuget.org/packages/PdfSpace.Workbench) | [![Downloads](https://img.shields.io/nuget/dt/PdfSpace.Workbench.svg)](https://www.nuget.org/packages/PdfSpace.Workbench) | Complete multi-document shell: tools, review, forms, organization, search, export, OCR and recovery |

Dependencies (from project references): `Core ← Layout`, `Core ← Editing`, `Core ← Documents`; `Documents + Layout + Rendering.Skia ← Skia ← Pdf`, `Skia ← Ocr`; `Controls + Editing + Skia ← Viewer`; `Pdf + Viewer + Storage + Ocr ← Workbench`. `Storage`, `Controls` and `Rendering.Skia` have no PdfSpace dependencies.

### PdfSpace.Core

The immutable document model shared by every package: a `PdfWorkspace` holds the original source bytes plus per-page state (rotation, crop, bookmarks, annotations with replies, form fields and OCR layers), so edits stay reversible until export. Includes validated `.pdfspace` JSON and page selection/composition helpers. No dependencies and no UI.

```sh
dotnet add package PdfSpace.Core --prerelease
```

**Key types**

- `PdfWorkspace` – `Sources`, `Pages`, `UpdatePage`, `IsSensitive`, `AnnotationCount`, `FieldCount`
- `PdfPageState`, `PdfSource` – page geometry/state and original PDF bytes
- `Annotation` / `AnnotationKind`, `PdfFormFieldState` / `PdfFieldKind` – review and form models
- `WorkspaceJson` – `Save`, `Load` (bounded, validated), `Validate`
- `WorkspacePages.Select`, `WorkspaceComposition.Append`, `PdfFormData.FromWorkspace`
- `RectD`, `PointD` – page-space geometry in PDF points

**Usage**

```csharp
using PdfSpace.Core;

var page = new PdfPageState { Width = 612, Height = 792 };
var note = new Annotation { Kind = AnnotationKind.Note, Bounds = new RectD(72, 72, 24, 24), Text = "Check totals" };
var workspace = new PdfWorkspace { Title = "Review.pdf", Pages = [page with { Annotations = [note] }] };

workspace = workspace.UpdatePage(page.Id, p => p with { Rotation = 90, Bookmark = "Summary" });
var firstPageOnly = WorkspacePages.Select(workspace, [0]);

string json = WorkspaceJson.Save(workspace);   // .pdfspace workspace
var restored = WorkspaceJson.Load(json);       // bounded + validated
Console.WriteLine($"{restored.Pages.Length} page(s), {restored.AnnotationCount} annotation(s)");
```

### PdfSpace.Layout

Renderer-independent page geometry: page ↔ display transforms under crop and rotation, an indexed continuous/single/two-page layout with visible-page queries and hit testing, a spatial bounds index for picking dense pages, object snapping (move, resize, points) and selection transforms. Depends on `PdfSpace.Core`; no UI.

```sh
dotnet add package PdfSpace.Layout --prerelease
```

**Key types**

- `PageLayoutIndex` – `Visible`, `Place`, `HitTest`, `NearestPage`, `TotalHeight` for `PageLayoutMode`
- `PageGeometry` – `ToDisplay`, `ToPage`, `DisplayBounds`; `PagePlacement.ToPage`/`ToScreen`
- `SpatialBoundsIndex` – `Query` and `HitTest` over many rectangles
- `ObjectSnapIndex` – `Snap`, `SnapResize`, `SnapPoint` with guides
- `SelectionTransform`, `SelectionRotation` – constrained move/resize/create and rotation deltas

**Usage**

```csharp
using PdfSpace.Core;
using PdfSpace.Layout;

var pages = Enumerable.Range(0, 50).Select(_ => new PdfPageState()).ToArray();
var index = new PageLayoutIndex(pages, PageLayoutMode.TwoPage);
double width = 1280, zoom = 1.25, scroll = 0, pan = 0;

foreach (PagePlacement placement in index.Visible(width, height: 900, zoom, scroll, pan))
    Console.WriteLine($"page {placement.Index + 1} at {placement.Bounds}");

var screen = new PointD(400, 300);
int hit = index.HitTest(screen, width, zoom, scroll, pan);
if (hit >= 0)
{
    PointD pagePoint = index.Place(hit, width, zoom, scroll, pan).ToPage(pages[hit], screen, zoom);
    Console.WriteLine($"page {hit + 1}, PDF point {pagePoint}");
}
RectD rotatedBox = PageGeometry.DisplayBounds(pages[0] with { Rotation = 90 }, new RectD(72, 72, 200, 50));
```

### PdfSpace.Editing

`EditorSession` applies every change as an immutable workspace transaction with bounded undo/redo (entry count and retained source bytes): annotations and replies, page rotate/crop/insert/duplicate/delete/move/combine, form-field creation, filling and validated XFDF/JSON imports, and OCR corrections. Custom commands go through `Execute`. Depends on `PdfSpace.Core`; no UI.

```sh
dotnet add package PdfSpace.Editing --prerelease
```

**Key types**

- `EditorSession` – `Document`, `CurrentPage`, `Execute(label, command)`, `Undo`/`Redo`, `IsDirty`, `Changed`
- Annotations and pages – `AddAnnotation`, `UpdateAnnotation`, `Reply`, `RotatePage`, `CropPage`, `MovePage`, `Combine`
- Forms – `AddField`, `UpdateField`, `SetFieldValue`, `PrepareFormDataImport` / `ApplyFormDataImport`
- `EditorHistoryOptions` – `MaximumEntries`, `MaximumSourceBytes`; `ClearHistory()`

**Usage**

```csharp
using PdfSpace.Core;
using PdfSpace.Editing;

var session = new EditorSession(new PdfWorkspace(), new EditorHistoryOptions { MaximumEntries = 50 });
session.Changed += (_, _) => Console.WriteLine($"{session.UndoLabel} (dirty: {session.IsDirty})");

session.AddAnnotation(new Annotation
{
    Kind = AnnotationKind.Rectangle,
    Bounds = new RectD(72, 120, 180, 64),
    Color = 0xFF1473E6,
    StrokeWidth = 2
});
session.RotatePage();
session.DuplicatePage();
session.Execute("Bookmark pages", doc => doc with
{
    Pages = doc.Pages.Select((p, i) => p with { Bookmark = $"Page {i + 1}" }).ToArray()
});
session.Undo();
File.WriteAllText("review.pdfspace", WorkspaceJson.Save(session.Document));
```

### PdfSpace.Storage

The host contract the workbench uses for everything platform-specific: opening documents, images and form data, saving/downloading, local recovery, clipboard text and printing. Implement it once per platform (the app ships browser and desktop implementations). No dependencies and no UI.

```sh
dotnet add package PdfSpace.Storage --prerelease
```

**Key types**

- `IWorkspaceStorage` – `OpenAsync`, `SaveAsync`, `Read/Write/ClearRecoveryAsync`, `CopyTextAsync`, `PrintAsync`
- Optional members with defaults – `OpenImageAsync`, `OpenFormDataAsync`
- `WorkspaceFile` – name + bytes

**Usage**

```csharp
using PdfSpace.Storage;

public sealed class FolderStorage(string root) : IWorkspaceStorage
{
    private string Recovery => Path.Combine(root, "recovery.pdfspace");
    public Task<WorkspaceFile?> OpenAsync() => Task.FromResult<WorkspaceFile?>(null); // show a file picker here
    public Task SaveAsync(string name, byte[] bytes, string contentType) => File.WriteAllBytesAsync(Path.Combine(root, name), bytes);
    public async Task<string?> ReadRecoveryAsync() => File.Exists(Recovery) ? await File.ReadAllTextAsync(Recovery) : null;
    public Task WriteRecoveryAsync(string workspace) => File.WriteAllTextAsync(Recovery, workspace);
    public Task ClearRecoveryAsync() { File.Delete(Recovery); return Task.CompletedTask; }
    public Task CopyTextAsync(string text) => Task.CompletedTask;          // host clipboard
    public Task PrintAsync(string name, byte[] pdf) => Task.CompletedTask; // host print dialog
}
```

### PdfSpace.Documents

Reads PDFs into a `PdfWorkspace` with PdfPig: page sizes, words with bounds, full-text extraction and search (including a byte-bounded per-document index), one-based page-range parsing, and plain-field form data as XFDF or versioned JSON. Depends on `PdfSpace.Core` and PdfPig; no UI.

```sh
dotnet add package PdfSpace.Documents --prerelease
```

**Key types**

- `PdfReader` – `Open(bytes, name)`, `Words`, `Find`, `ExtractText`
- `PdfSearchIndex` – bounded cached search with `Find(document, query, matchCase, wholeWord)`
- `PdfTextReader` – reusable per-workspace word reader
- `PageRange.Parse("1-3, 5", pageCount)` – zero-based indices
- `PdfFormDataCodec` – `Read`, `WriteXfdf`, `WriteJson`

**Usage**

```csharp
using PdfSpace.Core;
using PdfSpace.Documents;

var workspace = PdfReader.Open(File.ReadAllBytes("report.pdf"), "report.pdf");
Console.WriteLine(PdfReader.ExtractText(workspace));

var search = new PdfSearchIndex();
foreach (SearchResult hit in search.Find(workspace, "revenue", wholeWord: true).Take(10))
    Console.WriteLine($"p.{hit.PageIndex + 1}: {hit.Text} at {hit.Bounds}");

int[] pages = PageRange.Parse("1-3, 5", workspace.Pages.Length); // zero-based indices
File.WriteAllBytes("form.xfdf", PdfFormDataCodec.WriteXfdf(PdfFormData.FromWorkspace(workspace)));
PdfFormData answers = PdfFormDataCodec.Read(File.ReadAllBytes("answers.json"), "answers.json");
```

### PdfSpace.Rendering.Skia

**License: Apache-2.0 (not MIT).** This package is a derivative work: a source-pinned adaptation of [BobLd/PdfPig.Rendering.Skia](https://github.com/BobLd/PdfPig.Rendering.Skia) 0.1.16.4, moved into the isolated `PdfSpace.Rendering.Skia` namespace, with fixes for image alpha, odd-length dash patterns and miter limits. The package retains the upstream `LICENSE.txt` and `NOTICE.txt` plus `UPSTREAM.json` (original source hashes) and `ADAPTATIONS.md` (every modification); keep those notices when you redistribute it. Use it directly to rasterize PDF pages with PdfPig and SkiaSharp; `PdfSpace.Skia` references it for you. It depends on PdfPig (with its DCT, JBIG2 and JPX filter packages), SkiaSharp and SkiaSharp.HarfBuzz; no UI.

```sh
dotnet add package PdfSpace.Rendering.Skia --prerelease
```

**Key types**

- `PdfPigExtensions` – `AddSkiaPageFactory`, `GetPageSize`, `GetPageAsSKPicture`, `GetPageAsSKBitmap`, `GetPageAsPng`
- `SkiaRenderingParsingOptions.Instance` – lenient PdfPig parsing with the image filter provider
- `PdfPageSize` – `Width`, `Height`, `PageNumber`
- `SkiaPageFactory`, `SkiaRenderingFilterProvider` – the PdfPig factory/filter plumbing

**Usage**

```csharp
using PdfSpace.Rendering.Skia;
using SkiaSharp;
using UglyToad.PdfPig;

using var document = PdfDocument.Open(File.ReadAllBytes("input.pdf"), SkiaRenderingParsingOptions.Instance);
document.AddSkiaPageFactory();

for (var page = 1; page <= document.NumberOfPages; page++)
{
    PdfPageSize size = document.GetPageSize(page);
    using var png = document.GetPageAsPng(page, scale: 2);
    File.WriteAllBytes($"page-{page}.png", png.ToArray());
    Console.WriteLine($"page {page}: {size.Width} x {size.Height} pt");
}
using SKPicture picture = document.GetPageAsSKPicture(1); // replay onto any SKCanvas
```

### PdfSpace.Skia

Draws workspace pages: cached source pictures from the Apache-2.0 renderer, then crop/rotation, annotations and form widgets on top. Exports PNG pages and an explicitly *flattened* visual PDF, rebuilds image-only redacted copies, imports PNG/JPEG scans as pages, and generates the original sample documents. For native annotations and interactive forms use `PdfSpace.Pdf` instead. Depends on `PdfSpace.Documents`, `PdfSpace.Layout`, `PdfSpace.Rendering.Skia` and SkiaSharp; no UI framework.

```sh
dotnet add package PdfSpace.Skia --prerelease
```

**Key types**

- `PdfRenderer` – `DrawPage`, `ExportPng`, `ExportPdf` (flattened), `Typeface`, `CacheCapacity`
- `RasterRedactor.Export` – image-only reconstruction with burned-in redaction marks
- `PdfImageImporter` – `Open` a PNG/JPEG as a one-page workspace
- `AnnotationPainter`, `FormFieldPainter` – overlay drawing on any `SKCanvas`
- `SampleDocument.Create(typeface)` – original synthetic demo workspace

**Usage**

```csharp
using PdfSpace.Documents;
using PdfSpace.Skia;
using SkiaSharp;

var workspace = PdfReader.Open(File.ReadAllBytes("input.pdf"), "input.pdf");
using var typeface = SKTypeface.FromFile("fonts/Inter.ttf");                 // annotation fallback text
using var renderer = new PdfRenderer { CacheCapacity = 8, Typeface = typeface }; // disposed before the typeface

File.WriteAllBytes("first-page.png", renderer.ExportPng(workspace, pageIndex: 0, scale: 2));
File.WriteAllBytes("flattened.pdf", renderer.ExportPdf(workspace));              // visual copy, not native
File.WriteAllBytes("redacted.pdf", RasterRedactor.Export(workspace, renderer));  // image-only rebuild

using var surface = SKSurface.Create(new SKImageInfo(612, 792));
renderer.DrawPage(surface.Canvas, workspace, workspace.Pages[0]);
```

### PdfSpace.Pdf

Structured PDF output and native editing with PDFsharp: save workspaces with real annotation and AcroForm objects, edit supported original text runs, read/move/replace native images, discover and transform native page objects (text, paths, images, shadings, Form groups), read outlines, inspect documents, and encrypt/unlock with AES-256 through `IPdfSecurityProvider`. Depends on `PdfSpace.Skia` and PDFsharp; no UI.

```sh
dotnet add package PdfSpace.Pdf --prerelease
```

**Key types**

- `PdfDocumentEngine` – `Open` (with optional password), `Save` → `PdfWriteResult`, `Inspect`, `CanPreserveCatalog`
- `PdfTextEditor` – `Read`, `Replace`, `ReplaceWithFont`; `PdfImageEditor` – native image occurrences
- `PdfObjectEditor` – `Read`, `Transform`, `Align`, `Distribute`, `Group`, `Copy`/`Paste`, `SetAppearance`
- `IPdfSecurityProvider` / `NativePdfSecurityProvider` – `EncryptAsync`, `UnlockAsync`; `PdfProtectionOptions`
- `PdfNavigation.ReadBookmarks`, `PdfAffineMatrix`

**Usage**

```csharp
using PdfSpace.Pdf;
using SkiaSharp;

using var typeface = SKTypeface.FromFile("fonts/Inter.ttf"); // font for new annotation/form text
var workspace = PdfDocumentEngine.Open(File.ReadAllBytes("input.pdf"), "input.pdf");

PdfTextRun run = PdfTextEditor.Read(workspace, pageIndex: 0).First(r => r.Editable);
workspace = PdfTextEditor.Replace(workspace, run, "Updated heading");

var paths = PdfObjectEditor.Read(workspace, 0).Where(o => o.Kind == PdfPageObjectKind.Path && o.Editable).ToArray();
workspace = PdfObjectEditor.Transform(workspace, paths, PdfAffineMatrix.Translate(12, 6));

PdfWriteResult result = PdfDocumentEngine.Save(workspace, typeface); // native annotations + AcroForms
File.WriteAllBytes("native-copy.pdf", result.Bytes);
foreach (var warning in result.Warnings) Console.WriteLine(warning);

IPdfSecurityProvider security = new NativePdfSecurityProvider(); // desktop; not for AES on browserwasm
byte[] encrypted = await security.EncryptAsync(result.Bytes,
    new PdfProtectionOptions(openingPassword, ownerPassword, AllowCopy: false));
```

### PdfSpace.Ocr

Local OCR orchestration: rasterize selected pages, send them to an `IOcrEngine`, decode bounded Tesseract TSV into word boxes, and apply the result as one undoable OCR layer that `PdfSpace.Pdf` exports as an invisible, searchable text layer. `TesseractProcessEngine` runs an installed Tesseract 5 executable; browser hosts inject their own engine. Nothing is sent to a service. Depends on `PdfSpace.Skia`; no UI.

```sh
dotnet add package PdfSpace.Ocr --prerelease
```

**Key types**

- `OcrBatch` – `RecognizeAsync(snapshot, renderer, engine, pages, language, dpi, progress, token)`, `Apply`, `WordCount`
- `IOcrEngine` – `RecognizeTsvAsync(image, language, token)`; `TesseractProcessEngine`, `UnavailableOcrEngine`
- `TesseractTsv.Decode` – bounded TSV → `PdfOcrLayer`
- `OcrImage`, `OcrProgress`

**Usage**

```csharp
using PdfSpace.Ocr;
using PdfSpace.Skia;

var scan = PdfImageImporter.Open(File.ReadAllBytes("scan.png"), "scan.png"); // PNG/JPEG → one-page workspace
using var renderer = new PdfRenderer();
IOcrEngine engine = new TesseractProcessEngine(); // requires Tesseract 5 on PATH

var progress = new Progress<OcrProgress>(p => Console.WriteLine($"{p.Completed}/{p.Total} {p.Stage}"));
OcrBatch batch = await OcrBatch.RecognizeAsync(scan, renderer, engine, pages: [0], language: "eng", progress: progress);
var searchable = batch.Apply(scan); // adds the recognized OCR layer
Console.WriteLine($"{batch.WordCount} words on {batch.RecognizedPages} page(s)");
```

### PdfSpace.Controls

Original, Skia-drawn Uno building blocks for PDF tools: vector icons, command buttons, document tabs, tool panels, a color palette, an in-window dialog host (prompt/secret/confirm) and styled text fields, plus `PdfTheme` helpers and `PdfResources` styles. Compose them into your own shell without the workbench. Depends only on Uno Platform (Skia renderer).

```sh
dotnet add package PdfSpace.Controls --prerelease
```

**Key types**

- `PdfIcon` / `PdfIconKind`, `PdfCommandButton`, `PdfTextField`
- `PdfToolPanel` – `Heading`, `Description`, `Add(label, icon, action)`, `Items`
- `PdfDocumentTab`, `PdfColorPalette` (`ColorChanged`)
- `PdfDialogHost` – `PromptAsync`, `SecretAsync`, `ConfirmAsync`
- `PdfTheme`, `PdfResources`, `PdfInputFocus.ActivateWhenLoaded`

**Usage**

```csharp
using Microsoft.UI.Xaml.Controls;
using PdfSpace.Controls;

var status = PdfTheme.Text("Ready");
var panel = new PdfToolPanel("Review");
panel.Heading("Annotate");
panel.Add("Highlight", PdfIconKind.Highlight, () => status.Text = "Highlight");
panel.Add("Add note", PdfIconKind.Comment, () => status.Text = "Note");
var palette = new PdfColorPalette();
palette.ColorChanged += argb => status.Text = $"#{argb:X8}";
panel.Items.Children.Add(palette);
panel.Items.Children.Add(status);

var dialogs = new PdfDialogHost();
var root = new Grid { Children = { panel, dialogs } };
window.Content = root;
string? title = await dialogs.PromptAsync("Rename", "New document title", initial: "Report.pdf");
```

### PdfSpace.Viewer

The embeddable document viewport: Skia-rendered pages with continuous/single/two-page layouts, zoom/pan, text selection, direct annotation creation and editing for the session's current tool, in-place form filling, search highlighting and native object/image manipulation surfaces, plus virtualized `PdfThumbnailView`. Navigation and zoom are view operations; document changes belong to `EditorSession`. Depends on `PdfSpace.Controls`, `PdfSpace.Editing`, `PdfSpace.Skia` and Uno Platform.

```sh
dotnet add package PdfSpace.Viewer --prerelease
```

**Key types**

- `PdfViewport` – `PdfViewport(session)`, `Renderer`, `Navigate`, `FitPage`, `ZoomTo`, `SetLayout`, `HighlightSearch`
- `PdfViewport` events – `StatusChanged`, `NoteRequested`, `FieldRequested`, `LinkRequested`, `ContextRequested`
- `PdfThumbnailView` – `PdfThumbnailView(viewport)`, `PageActivated`, `OrganizeMode`
- `NativeObjectTarget`, `NativeImageTarget` – detached geometry the host supplies for object editing

**Usage**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Viewer;

var session = new EditorSession(PdfReader.Open(pdfBytes, "input.pdf"));
var viewport = new PdfViewport(session);
var thumbnails = new PdfThumbnailView(viewport) { Width = 180 };

session.SetTool(PdfTool.Highlight);
viewport.NoteRequested += (pageIndex, point) => session.AddAnnotation(
    new Annotation { Kind = AnnotationKind.Note, Bounds = new RectD(point.X, point.Y, 24, 24), Text = "New note" }, pageIndex);
viewport.Loaded += (_, _) => viewport.FitPage(); // needs a nonzero arranged size

var root = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() } };
Grid.SetColumn(viewport, 1);
root.Children.Add(thumbnails);
root.Children.Add(viewport);
window.Content = root;
window.Closed += (_, _) => viewport.Dispose();
```

### PdfSpace.Workbench

The complete Acrobat-style shell used by the app: document tabs, quick tools, comments, form preparation, original-text and native object editing, page organization, search, export, protection, redaction, OCR and recovery. Platform services come from `IWorkspaceStorage`; security and OCR providers are optional constructor arguments. The host owns the injected typeface; the workbench owns the viewports it creates. Depends on `PdfSpace.Pdf`, `PdfSpace.Viewer`, `PdfSpace.Storage`, `PdfSpace.Ocr` and Uno Platform.

```sh
dotnet add package PdfSpace.Workbench --prerelease
```

**Key types**

- `PdfWorkbench` – `PdfWorkbench(initial, storage, typeface, security?, ocr?)`, `AddDocument`, `OpenFileAsync`, `OfferRecoveryAsync`
- State – `Session`, `Viewport`, `DocumentCount`, `HasUnsavedChanges`, `StateChanged`, `ShowStatus`

**Usage**

```csharp
using PdfSpace.Ocr;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using PdfSpace.Storage;
using PdfSpace.Workbench;
using SkiaSharp;

using var fontData = SKData.CreateCopy(fontBytes);
var typeface = SKTypeface.FromData(fontData);          // host-owned UI/annotation font
IWorkspaceStorage storage = new FolderStorage(appData); // see PdfSpace.Storage above

var workbench = new PdfWorkbench(SampleDocument.Create(typeface), storage, typeface,
    security: new NativePdfSecurityProvider(), ocr: new TesseractProcessEngine()); // desktop providers
window.Content = workbench;
await workbench.OfferRecoveryAsync();
window.Closed += (_, _) => { workbench.Dispose(); typeface.Dispose(); };
```

## Toolchain

.NET SDK **10.0.401**, Uno SDK **6.7.30** / WinUI **6.7.135**, matched managed/native SkiaSharp **3.119.4**, PdfPig **0.1.16**, the source-pinned **PdfSpace.Rendering.Skia** adaptation of PdfPig.Rendering.Skia **0.1.16.4**, PDFsharp **6.2.4**. Browser security uses hash-pinned `@neslinesli93/qpdf-wasm` **0.3.0** / QPDF **12.2.0**. Rendering is still Uno/Skia, not a third-party web PDF viewer.

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
cp artifacts/engine/*.pdf artifacts/engine/*.png artifacts/engine/*.jpg artifacts/fixtures/
npm ci --ignore-scripts
npx playwright install chromium
npm run test:browser
bash scripts/verify-browser-exports.sh artifacts/browser-exports
```

UI tests use real pointer, keyboard, file-picker and download/reopen interactions. `?test=1` enables read-only diagnostics, not a document-mutation API. The security-provider contract also has dedicated boundary tests. CI retains screenshots, reports and failure traces.

## Delivery and documentation

**Build** tests the native engines, publishes and tests Uno WebAssembly, independently verifies the browser-encrypted PDF, and packages the libraries. **Desktop** compiles on Windows, macOS and Linux. **Pages** deploys a successful main-branch build and validates its commit identity and public behavior. **Release** runs for `v*` tags or a supplied manual version. It reruns the engine and browser gates, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), packs the libraries with symbols and emits `SHA256SUMS.txt`. Tags attach all assets to a GitHub Release and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Manual runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing.

[Architecture](docs/architecture.md) · [Components](docs/components.md) · [Compatibility](docs/compatibility.md) · [Shortcuts](docs/shortcuts.md) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

Original PdfSpace source is [MIT licensed](LICENSE). The source-pinned `src/PdfSpace.Rendering.Skia` derivative is [Apache-2.0 licensed](src/PdfSpace.Rendering.Skia/LICENSE.txt), with original copyrights, notices and modification records retained. Dependencies and fonts retain their upstream licenses. Adobe and Acrobat are trademarks of their respective owners; Adobe proprietary code, branding and assets are not included.
