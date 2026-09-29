# Native headers, footers and watermarks

**Edit → Header and footer**, **Bates numbering**, **Add page numbers** and **Add watermark** open the Page marks inspector. These commands now create searchable native text instead of ordinary review annotations. Existing legacy annotation watermarks are not converted or removed automatically.

## Workflow

Choose `all` or a one-based range such as `1, 3-5`. Header/footer marks have six independent left/center/right slots. Set margins in PDF points, a font size and text color, and optional opacity. Watermarks have centered text, angle and front/behind-content placement. The miniature is explicitly a **placement-only preview**: it shows the page dimensions and proposed text, not the source drawing, text or form widgets. Typing settings performs no PDF parsing or writing.

Use **Apply native page marks**, review the confirmation and choose **Apply marks**. One effective batch is one undo transaction. Identical retained settings, numbering context, font and unmodified source geometry return the original workspace; a no-op adds no history. Export PDF writes a durable copy. Settings also survive an editable workspace save.

**Reload saved mark settings** reads the current page's intact PdfSpace mark of that kind. **Remove native page marks** removes only verified PdfSpace-owned streams of the chosen kind in the chosen range, retaining the other kind and unrelated native content. A header/footer and a watermark can coexist on each page. There is one managed instance of each kind per page, not an unlimited preset/layer stack.

## Number templates

| Token | Meaning |
|---|---|
| `{page}` | Starting number plus the zero-based ordinal in the ascending, distinct selected range; decimal or upper/lower Roman. |
| `{pages}` | Physical page count of the current document, not selected-range length or PDF page labels. |
| `{bates}` | Prefix + zero-padded decimal starting number/ordinal + suffix. Independent of Roman page style. |
| `{date}` | Explicit date text supplied by the caller/user. No clock, locale or time-zone dependency. |

`{{` and `}}` escape braces. Unknown or unbalanced tokens reject the operation. Roman values are 1–3999. Bates width is 3–15 digits; it is a minimum width, not truncation. Range numbering restarts at the supplied starting number for each batch; cross-file sequencing/file renaming are not provided. Page insertion/reordering does not automatically renumber existing native marks: apply the intended range again. The numbering is visible text, **not a `/PageLabels` editor**.

## Reusable API

```csharp
using PdfSpace.Pdf;
using PdfSpace.Editing;
using SkiaSharp;

// The host owns and disposes an embeddable TrueType typeface.
using var typeface = SKTypeface.FromFile("approved-font.ttf");
var session = new EditorSession(PdfDocumentEngine.Open(
    File.ReadAllBytes("input.pdf"), "input.pdf"));
var before = session.Document;
var settings = new PdfPageMarkSettings
{
    HeaderLeft = "Review {date}",
    FooterCenter = "{bates}",
    BatesPrefix = "CASE-",
    BatesDigits = 6,
    StartNumber = 42,
    DateText = "2026-09-29",
    FontSize = 10
};

var result = PdfPageMarks.Apply(before, [0, 1], settings, typeface);
session.Execute("Add review numbering", current =>
    ReferenceEquals(current, before) ? result.Workspace :
    throw new InvalidOperationException("Document changed."));
foreach (var warning in result.Warnings) Console.WriteLine(warning);
File.WriteAllBytes("numbered-copy.pdf",
    PdfDocumentEngine.Save(session.Document, typeface).Bytes);

var records = PdfPageMarks.Read(session.Document);
var removed = PdfPageMarks.Remove(session.Document, [0, 1],
    PdfPageMarkKind.HeaderFooter, typeface);
```

Indices in the API are zero-based. `PdfPageMarks.Layout` is a pure layout/glyph-validation API for host previews. `PdfPageMarkPreview` is the reusable Uno/Skia placement component; its host owns the supplied typeface. `PdfPageMarkResult` reports changed page count, newly embedded font count, serialized byte count and structured-save warnings. No-op results report zero output bytes because no output was written.

## Native representation and update safety

One Type0/CID TrueType font with explicit Unicode and glyph maps is shared across a batch. Each selected page references its own small Form XObject and invocation stream. Display-space layout is transformed back through native page rotation/crop geometry. Original page content bytes are retained, isolated between balanced graphics-state guards rather than being concatenated through a string editor.

A page-level `/PdfSpacePageMarks` manifest stores the settings, numbering context, requested font identity, stream reference and a canonical resource-graph checksum. Stream identity and exact invocation bytes, metadata version, and the graph/settings checksum are checked before updating/removing. Shared resource graphs are hashed once per operation. Compression container bytes and persisted numeric representation are normalized for checks across saving. The checksum is an **accidental/stale-edit guard**, not authentication, a digital signature, proof of ownership or tamper resistance against someone who can rewrite the manifest.

Native object edits can change or detach managed mark streams; the panel then refuses managed update/removal. Review such objects in the native editor instead. Unknown versions, malformed/duplicate records, unexpected resource reuse, unbalanced source state and unsupported inherited-resource contexts are rejected rather than guessed. Ordinary Adobe/third-party marks are not detected by visual similarity, so Remove never indiscriminately deletes matching-looking text.

The engine materializes the current workspace through the existing structured writer before marking. This normalizes crops and rotations, preserves stable workspace page IDs/bookmarks and supported annotation/form/OCR state, and propagates sensitive-source flags. This is not a general lossless rewrite: the [structured-save contracts](compatibility.md) still apply. Signed/XFA changes and unsafe existing-form reassembly are blocked. Warnings are surfaced in the workbench status. Decrypted sensitive copies stay excluded from automatic recovery.

## Boundaries

Batches are bounded to 500 selected pages. Fonts must be embeddable TrueType sfnt fonts; independent left-to-right glyphs are supported, while complex-script shaping, bidi, multiline marks and font collections/CFF are rejected. Latin input is normalized to NFC. Text that exceeds visible dimensions or makes adjacent slots overlap is rejected before a native edit. Source content is **not shrunk or moved away from the marks**. Behind-content marks can be occluded by opaque source content.

Image/PDF watermarks, background images/colors, print-only/screen-only visibility, reusable saved presets, automatic date updates, cross-document Bates jobs and Adobe private metadata compatibility are not implemented. Cropping, editing, removing a mark, or clearing undo history is **not secure redaction**. Original source files/workspaces may retain previous content. Native parsing/writing is synchronous; cancellation is checked between stages/pages, not inside the PDF parser or font encoder.

Reviewed OCR words replace the invisible OCR layer in search while later visible native additions remain searchable. Oblique native text uses baseline-aware word grouping; axis-aligned text retains the original fast extractor. This does not introduce mixed-page regional OCR or guarantee arbitrary rotated-script reading order.

## Validation

`dotnet run --project tests/PdfSpace.Tests -c Release -- --test-page-marks` exercises native persistence, font reuse/change, repeated updates, malformed/stale records, Unicode, aliases, crop/rotation, form values, OCR integration and atomicity. `--benchmark-page-marks` records three alternating-order samples after warmup for eight blank pages: one batch versus repeated per-page Apply calls, including actual native font counts and file sizes. Timing is observational, not a pass threshold or whole-app speed claim.

`tests/browser/page-marks.spec.mjs` drives the actual Uno inspector, confirmation, history and file downloads. `--verify-browser-page-marks <directory>` independently reopens those downloads with the native backend. The full CI verifier includes this mode alongside the previous ten categories.

## Range-scoped inspection

`PdfPageMarks.Read(workspace, pageIndices, cancellationToken)` inspects an ascending distinct range of at most 500 pages and opens each referenced source once. The inspector uses the current page, and Apply/Remove inspect only their requested range before materialization. Unreferenced source buffers are not parsed and pages without ownership metadata do not normalize their content arrays. Full-document `Read(workspace)` still inspects every referenced page. This is not certification of unselected content; all structured-save checks remain in force when an effective edit materializes the document.
