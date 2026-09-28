# Native object editing

Open **All tools → Edit objects** or **Edit → Edit objects**. The **Open object editing example** command opens an original two-page document with selectable native text, vector rectangles and Béziers, clipped graphics, and repeated shared image/Form resources.

## Selection and transformations

Click an object or drag an empty-page marquee; Shift/Ctrl-click toggles selection membership. Drag a selected object to move the complete selection. Eight bounding-box handles resize; arrows nudge by one point and Shift+arrow by ten. Escape cancels a gesture and clears selection. Changes commit once on pointer release; a stationary click does not rewrite the PDF. Selection outlines are geometry previews, not a live rerasterization of dragged content.

The inspector supports exact X/Y/width/height, rotation, reflections, crop, duplicate, delete, six alignments and horizontal/vertical center distribution. Crop adds a native clipping path and does not sanitize source data. Clip boundaries inherited from the original content remain effective.

The default mode selects leaf painting occurrences. **Select whole Form groups** selects top-level Form invocations as units. Native groups created by PdfSpace remain single selectable objects until **Ungroup objects**. Selecting an ancestor and its descendant together is rejected rather than applying a transform twice.

## Paths and text

Vector path editing changes real `m`, `l`, `c`, `v`, `y`, `re`, `h` and painting operations. **Edit path points** exposes original control/anchor handles on the canvas. The numeric command editor uses source-local coordinates; transformations are applied separately. Solid RGB fill/stroke, stroke width, enabled painting and even-odd fill changes preserve other native graphics-state properties. The inspector exposes the first 64 path commands; the API accepts up to 10,000. The object list shows 200 entries, with all indexed objects still selectable on canvas.

**Edit selected text** opens the existing source-run editor (original font encoding or explicit replacement-font mode). **Replace text block** explicitly lays out new text in a page-aligned bounding box with the bundled embedded font, word wrapping and overflow rejection. This is not reconstruction of the original paragraph, font runs, text rotation, or word-processing layout model. It supports left-to-right independently positioned glyphs; complex-script/combining replacements are rejected rather than silently reshaped incorrectly. Moving/resizing/duplicating an existing shaped text object preserves its original native glyph operations.

**Draw native rectangle**, **Draw native ellipse** and **Add native text** create editable page content, not review annotations. The original annotation and form tools remain separate.

## Grouping, arrangement and clipboard

Arrange uses captured native graphics state so moving an object does not acquire the destination's fill, font or transparency. Front/back operations target the containing scope; one-step forward/backward operations target a sibling paint occurrence. Operations crossing different clipping contexts are rejected. Grouping requires consecutive painted objects in one native scope and clipping context, preserving the stacking order of other content. Generated groups can be ungrouped; arbitrary transparency Form groups cannot be flattened safely and are not silently ungrouped.

Ctrl/Cmd+C, X, V and D copy, cut, paste and duplicate when the document canvas is focused. The clipboard is internal to the application, not the system clipboard. Copy captures native resources and source clipping. Paste works across open documents and propagates sensitivity to disable implicit unencrypted recovery. Clipboard data lives in memory until replaced or the workbench is disposed.

**None of copy, cut, deletion, cropping or history release is redaction.** Source resources, images, metadata or private data can remain in the original PDF, workspace, clipboard and rewritten PDF resources. Use the separate confirmed reconstruction/redaction workflow.

## Reusable API

```csharp
var objects = PdfObjectEditor.Read(session.Document, pageIndex);
var selection = objects.Where(o => o.Kind is PdfPageObjectKind.Image or PdfPageObjectKind.Path).ToArray();
session.Execute("Move objects", document =>
    PdfObjectEditor.Transform(document, selection, PdfAffineMatrix.Translate(12, 9)));

// Source descriptors are snapshot-specific: refresh after edits or undo.
selection = PdfObjectEditor.Read(session.Document, pageIndex).Take(2).ToArray();
session.Execute("Align left", document =>
    PdfObjectEditor.Align(document, selection, PdfObjectAlignment.Left));
```

The source hash, page identity, scope path, operator range, native fingerprint and geometry are revalidated before mutation. A batch opens one native writer and clones each edited shared invocation prefix once using an edit trie. All selected occurrences must belong to the same workspace page/snapshot. At most 1,000 objects may participate in one batch; a page may expose at most 20,000 occurrences. The workbench caches its immutable object index by weak snapshot identity and page ID; pan/zoom and drag previews do not reparse PDF content.

## Compatibility boundary

Native text objects, paths, XObject images, Form invocations and shading placements are supported. This is not full Adobe Acrobat or arbitrary PDF compatibility. Inline-image streams remain blocked by the underlying round-trip parser. Clipping text, paths that themselves mutate clipping, interleaved text/graphics, singular transforms and signed/XFA source editing are guarded. Original complex transparency, pattern and clipping data are preserved, not exposed as complete specialist authoring tools. Shading bounds without an explicit BBox conservatively use the page extent. Image-mask/soft-mask authoring, arbitrary cross-scope z-order, full complex-script paragraph shaping and incremental signature-preserving writes remain outside this release.
