# Embedding the reusable components

All nine libraries are packable with `dotnet pack`; the application itself is not. The Uno UI libraries contain browser and desktop assets. Use the same Uno/Skia dependency versions as the host.

## Headless editing

```csharp
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;

var workspace = PdfReader.Open(File.ReadAllBytes("input.pdf"), "input.pdf");
var editor = new EditorSession(workspace);
editor.AddAnnotation(new Annotation
{
    Kind = AnnotationKind.Rectangle,
    Bounds = new RectD(72, 120, 180, 64),
    Color = 0xFF1473E6,
    StrokeWidth = 2
});
editor.RotatePage();
editor.Undo();
File.WriteAllText("review.pdfspace", WorkspaceJson.Save(editor.Document));
```

## Render or export without the workbench

```csharp
using PdfSpace.Skia;

using var renderer = new PdfRenderer { CacheCapacity = 8 };
File.WriteAllBytes("review.pdf", renderer.ExportPdf(editor.Document));
File.WriteAllBytes("first-page.png", renderer.ExportPng(editor.Document, 0, 2));
```

Provide the native SkiaSharp runtime for your target. Set `renderer.Typeface` to a host-owned `SKTypeface` for annotation fallback text. Dispose the renderer before disposing the typeface. Export creates a new flattened visual PDF, not a lossless source-file save.

## Embed the viewport in Uno

```csharp
using PdfSpace.Viewer;

var viewport = new PdfViewport(editor);
Content = viewport;
viewport.FitPage(); // Call after the control has a nonzero arranged size.
viewport.StatusChanged += message => statusText.Text = message;
viewport.NoteRequested += (pageIndex, point) =>
{
    // Collect comment text in the host, then add an AnnotationKind.Note.
};
```

Dispose the viewport when its document is permanently closed. Navigation, fit and zoom APIs are view operations; page editing belongs to `EditorSession`. `PdfThumbnailView` can share the viewport renderer and enter `OrganizeMode` for a page grid.

## Embed the complete workspace

Implement `PdfSpace.Storage.IWorkspaceStorage` for open/save, recovery, clipboard and printing. The repository includes browser and desktop implementations in the app host.

```csharp
var workbench = new PdfSpace.Workbench.PdfWorkbench(workspace, storage, typeface);
Content = workbench;
await workbench.OfferRecoveryAsync();
```

The host owns the injected typeface. The workbench owns the viewports it creates. A `.pdfspace` file contains the complete original PDF sources and should be handled as sensitive document data.

## Customize the UI

`PdfResources.xaml` holds common command-button, text-field and flyout styles. `PdfTheme` supplies fonts, brushes and layout helpers. `PdfIconKind` identifies the original vector icons. Compose these independently of `PdfWorkbench`, or host only the viewer with an application-specific toolbar.

This release intentionally uses a styled native Uno text field for text/IME input. It does not claim a complete custom implementation of every primitive, full screen-reader document semantics or binary/API compatibility with Adobe Acrobat.
