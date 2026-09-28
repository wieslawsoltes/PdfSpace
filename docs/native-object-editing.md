# Editing existing PDF objects

PdfSpace 0.4 adds native image placement editing and text editing inside nested Form XObjects. The editor rewrites supported source content; it does not paint a white rectangle over the old object or rasterize the page to simulate an edit.

## Image workflow

Open **Edit → Edit original images**. Click an image on the page, drag to move it, or drag a selection handle to resize. The gesture shows a geometry preview; release commits one undoable native edit. The inspector provides exact X/Y/width/height in PDF points, rotation, horizontal/vertical reflection, replacement and deletion. **Add image** selects a local file and then lets you drag its destination rectangle. **Open native object example** loads an original two-page example with repeated, nested shared objects.

Selection identifies a drawing occurrence, not merely an image object number. The same image may be painted twice on one page, through shared forms, and on another page. Editing one placement clones only the selected Form-XObject path and its resource dictionaries, redirects that occurrence, and retains unrelated resource references. Page aliases created by duplicating a workspace page are isolated as well.

Existing transformation matrices, form matrices and effective inherited resource dictionaries participate in hit geometry. Native image bounds represent the full transformed quadrilateral; existing clipping stays in force. A clip can therefore hide part or all of an otherwise selected image. Rotation and resize use the selected placement's axis-aligned logical bounding rectangle; arbitrary polygon editing is not provided.

Replacement preserves the drawing transform and writes a new independent RGB image, with a grayscale soft mask when alpha is present. It accepts images decoded by the installed Skia build, limited to 32 MiB, 16 million pixels and 8192 pixels per dimension. Orientation must already be normalized for replacement/insertion; the separate Scan & OCR image importer supports EXIF orientation. Color is converted to RGB; this is not a print-production CMYK/ICC-preserving image editor.

**Deleting or replacing an image is not redaction.** The original bytes remain in undo/workspace sources, and unused objects/resources can remain in a rewritten PDF. Only the separate explicit redaction export is designed to produce a fresh image-only document without the original object graph.

## Text and replacement fonts

**Edit original text** now enumerates supported text runs inside nested forms as well as page content. Normal mode retains the original font encoding and replaces only encodable characters. The selected form invocation is isolated before its text changes, leaving shared siblings and other pages unchanged.

Enable **Use replacement font** to embed the bundled Unicode TrueType font for an independent, horizontal text-showing run. Select the run, enter replacement text, and confirm the font size. The writer emits native text with a Type0/CIDFont, glyph map and ToUnicode mapping; supported Unicode characters remain searchable in other readers. The original text position, graphics transformation and color remain in effect, and the previous font is restored after the replacement.

This is single-run replacement, not a paragraph layout engine. Replacement-font mode rejects dependent successive text positioning and text clipping. It does not provide general complex-script shaping, bidirectional layout, vertical writing, arbitrary font repair, line breaking or paragraph reflow. Original mode retains positioned TJ adjustments, so longer glyph-by-glyph replacements are restricted. Newlines and control characters are rejected rather than silently laid out incorrectly. Outlined glyphs and text with unknown decoding remain unavailable.

## Safety, transactions and reuse

Every target carries source identity, source-page identity, page identity and source hash. Editing validates the target against the immutable current snapshot and confirms the original operator/resource/transform. Stale or conflicting edits fail without changing the document. Signed/certified and XFA source changes remain blocked. Cyclic/deep form graphs and unsupported inline-image content fail explicitly.

Public APIs are in `PdfSpace.Pdf`:

```csharp
var images = PdfImageEditor.Read(workspace, pageIndex);
var edited = PdfImageEditor.SetBounds(workspace, images[0], new RectD(72, 120, 180, 90));
// Re-read after each edit: occurrence identities belong to the source snapshot.
var next = PdfImageEditor.Read(edited, pageIndex)[0];
edited = PdfImageEditor.Replace(edited, next, File.ReadAllBytes("replacement.png"));

var text = PdfTextEditor.Read(edited, pageIndex).First(run => run.CanReplaceFont);
edited = PdfTextEditor.ReplaceWithFont(edited, text, "Żółć café — reviewed", hostTypeface, 16);
```

Wrap each operation in `EditorSession.Execute` for named undo/redo. The host owns the typeface. Native saves are rewrites, not incremental or signature-preserving updates. Validate the exported file for your document corpus before relying on preservation of specialized PDF structures.
