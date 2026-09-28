# Embedding the reusable components

All twelve libraries are packable with `dotnet pack`; the application itself is not. The Uno UI libraries contain browser and desktop assets. Use the same Uno/Skia dependency versions as the host.

## Headless editing

```csharp
using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Editing;

var workspace = PdfDocumentEngine.Open(File.ReadAllBytes("input.pdf"), "input.pdf");
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

## Render or explicitly flatten without the workbench

```csharp
using PdfSpace.Skia;

using var renderer = new PdfRenderer { CacheCapacity = 8 };
File.WriteAllBytes("review-flattened.pdf", renderer.ExportPdf(editor.Document));
File.WriteAllBytes("first-page.png", renderer.ExportPng(editor.Document, 0, 2));
```

Provide the native SkiaSharp runtime for your target. Set `renderer.Typeface` to a host-owned `SKTypeface` for annotation fallback text. Dispose the renderer before disposing the typeface. `PdfRenderer.ExportPdf` explicitly creates a flattened visual PDF. Use `PdfDocumentEngine.Save`, shown below, for native annotations and interactive forms; do not interchange the two contracts.

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

## Structured PDF editing and security

```csharp
using PdfSpace.Pdf;

var document = PdfDocumentEngine.Open(File.ReadAllBytes("input.pdf"), "input.pdf");
var result = PdfDocumentEngine.Save(document, typeface);
File.WriteAllBytes("native-copy.pdf", result.Bytes);
foreach (var warning in result.Warnings) Console.WriteLine(warning);

IPdfSecurityProvider security = new NativePdfSecurityProvider();
var protectedPdf = await security.EncryptAsync(result.Bytes,
    new PdfProtectionOptions(openingPassword, ownerPassword, AllowCopy: false));
```

Inject a platform provider as the optional fourth `PdfWorkbench` constructor argument. The App project supplies `BrowserPdfSecurityProvider` on WebAssembly; a custom browser host must also ship its worker/runtime assets. The default provider is native .NET and must not be used for AES on browserwasm. `PdfUnlockResult.WasEncrypted` must propagate into source sensitivity so recovery cannot silently persist decrypted bytes.

`EditorSession.UpdateField` applies logical properties across all widgets of a field while changing bounds only on the selected widget. `SetFieldValue` enforces type, choice and read-only rules. `DeleteField` supports imported widgets; native save prunes the corresponding field hierarchy. Neither ordinary deletion nor workspace serialization sanitizes confidential source data.

## Form interchange and PDF navigation

`PdfFormDataCodec` in Documents reads/writes plain-field XFDF and JSON. `EditorSession.PrepareFormDataImport` validates a batch against a snapshot; `ApplyFormDataImport` applies one undoable transaction and refuses stale plans. `PdfNavigation.ReadBookmarks` exposes read-only source outlines with resolved local page indices. `WorkspacePages.Select` performs page extraction/reordering while remapping internal destinations. Host picker integration and full examples are in [form data and navigation](form-data-and-navigation.md).

`PdfInputFocus.ActivateWhenLoaded` provides one-shot initial focus/selection for newly attached Uno editors and dialogs. Its current-owner predicate cancels an obsolete activation; it never reselects text on a later Loaded notification.

## OCR engine injection

Pass an `IOcrEngine` as the optional fifth `PdfWorkbench` constructor argument. The browser host supplies `BrowserOcrEngine` and its self-hosted assets; desktop uses `TesseractProcessEngine`. A reusable host without an injected engine reports OCR unavailable rather than pretending to recognize text. See [OCR integration](ocr.md).

## Native object editing and indexed viewing (0.4)

`PdfImageEditor`, `PdfTextEditor.ReplaceWithFont` and `PdfAffineMatrix` expose native occurrence editing without the workbench. `PdfViewport` receives immutable `NativeImageTarget` geometry from the host and raises edit requests; it does not parse native objects itself. `PageLayoutIndex` is independently usable without Uno or Skia. `PdfSearchIndex` provides bounded per-document text caching. See [native object examples](native-object-editing.md) and [performance ownership](performance.md).

## Photographs and history

`PdfImageEditor.OpenImage`, `Duplicate` and `RestoreAspectRatio` support native photo workflows. `EditorHistoryOptions` configures count/source-byte retention; `EditorSession.ClearHistory` releases undo/redo while retaining current edits and saved-state identity. See [the photograph embedding example](photo-import.md) and [memory accounting](performance.md).

## Unified object editing

`PdfObjectEditor` discovers snapshot-specific `PdfPageObject` descriptors and applies native operations without depending on Uno. `Transform`, `SetBounds`, `Align`, `Distribute`, `Crop`, `Arrange`, `Duplicate`, `Group`, `Ungroup`, `Copy` and `Paste` support the guarded mixed-object workflows documented in [object editing](object-editing.md). `SetPath`/`SetAppearance` edit vector commands/painting; `ReplaceTextBlock` and `InsertText` explicitly lay out independent left-to-right glyphs with overflow validation.

```csharp
var objects = PdfObjectEditor.Read(session.Document, session.CurrentPage);
var selected = objects.Where(item => item.Kind == PdfPageObjectKind.Path && item.Editable).ToArray();
session.Execute("Move native paths", snapshot =>
    PdfObjectEditor.Transform(snapshot, selected, PdfAffineMatrix.Translate(12, 6)));
```

Refresh descriptors after any document mutation or undo. Never reuse a descriptor from a different page or snapshot. A clipboard payload owns serialized source/resource data until the host releases it; propagate its sensitivity flag. The viewer's object-target records contain detached geometry and no parsed PDF handles. Selection membership and union bounds are cached independently from PDF object discovery.

## PDF rendering adaptation

`PdfSpace.Rendering.Skia` is an independently packable Apache-2.0 source adaptation with an isolated namespace; other original PdfSpace packages remain MIT. `PdfSpace.Skia` references it transitively. A custom consumer must retain its included upstream license/notice. See [native appearance editing](object-appearance.md) for APIs and renderer limitations.
