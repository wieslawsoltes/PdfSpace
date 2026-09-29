# Native object editing

Open **All tools → Edit objects** or **Edit → Edit objects**. The **Open object editing example** command opens an original two-page document with selectable native text, vector rectangles and Béziers, clipped graphics, and repeated shared image/Form resources.

## Selection and transformations

Click an object or drag an empty-page marquee; Shift/Ctrl-click toggles selection membership. Drag a selected object to move the complete selection. Eight bounding-box handles resize; arrows nudge by one point and Shift+arrow by ten. Escape cancels a gesture and clears selection. Changes commit once on pointer release; a stationary click does not rewrite the PDF. Selection outlines are geometry previews, not a live rerasterization of dragged content.

The inspector supports exact X/Y/width/height, rotation, reflections, crop, duplicate, delete, six alignments and horizontal/vertical center distribution. Crop adds a native clipping path and does not sanitize source data. Clip boundaries inherited from the original content remain effective.

The default mode selects leaf painting occurrences. **Select whole Form groups** selects top-level Form invocations as units. Native groups created by PdfSpace remain single selectable objects until **Ungroup objects**. Selecting an ancestor and its descendant together is rejected rather than applying a transform twice.

## Paths and text

Vector path editing changes real `m`, `l`, `c`, `v`, `y`, `re`, `h` and painting operations. **Edit path points** exposes original control/anchor handles on the canvas. The numeric command editor uses source-local coordinates; transformations are applied separately. Solid RGB fill/stroke, stroke width, enabled painting and even-odd fill changes preserve other native graphics-state properties. The inspector exposes the first 64 path commands; the API accepts up to 10,000. The object list uses a reusable pool of 64 rows, with Previous/Next ranges and Go to object reaching every indexed occurrence.

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

## Validation and conservative boundaries

Regression fixtures verify native mixed transforms, shared-resource isolation, path nodes and appearance, state-preserving deletion/reordering, grouping, clipboard resources and clipping, explicit Unicode block layout, and exact save/reopen behavior. A copied path records its real numeric clipping operands. Shared inherited graphics-state operations are deep-cloned before resource rebinding. Copies inheriting text clipping or a clip built under interleaved transforms fail explicitly rather than broadening the visible region. Ungroup keeps the actual Form BBox and rejects unexpected non-placement operators.

After a stacking-order change, selection is cleared instead of silently retaining indices that now refer to different objects. Already-front/back arrangements preserve source identity and do not add an undo entry. Object-index fingerprints access operator ranges directly; they do not repeatedly enumerate the prefix of the complete content stream.

## Rotation and alignment guides (0.5.3)

Drag the round handle outside the selection to rotate native content around the selection center. Hold Shift for 15-degree increments. Ctrl-click bypasses rotation/resize handles to select an underlying object when a handle overlaps it. The original content remains in place during the geometry-only preview; pointer release commits one native transform, and Escape cancels without rewriting. **Object rotation degrees → Apply object rotation** performs an exact relative rotation; multiples of 360 are a no-op. The existing 90-degree rotation command remains available. Handles are UI only and can extend beyond the crop boundary without exposing additional source-page pixels.

**Snap moving objects** enables alignment of the moving selection's edges/center to the visible page box and unselected indexed object bounds. It is initially off and is view state, not a document edit. Purple lines identify the active alignment. Tolerance is six screen pixels at every zoom. Shift constrains movement to the dominant logical-page axis; snapping cannot change the locked coordinate. Hold Alt to temporarily bypass snapping. In 0.5.3 this option applies to moves; 0.5.4 adds separate resize and vector-point options below. Rotation and text layout do not use these guides; it uses object bounding boxes, not painted-contour or spacing analysis.

The immutable `PdfSpace.Layout.ObjectSnapIndex` accepts value bounds without retaining source documents. Sorted axis anchors are built only when needed for a new selection/index/page box; pointer updates use binary search and reuse the index. Equal-distance ties prefer the earlier target, earlier moving anchor, then lower target coordinate. A visible page box placed first has deterministic priority over coincident object anchors. This is deliberately independent of PDF rewriting and Uno event handling.

Dragging out and returning to the pointer-down dead zone restores the original preview. A Shift-drag does not become a Shift-click merely because it ends where it started. Keyboard and exact-geometry commands remain available where pointer handles are outside the current viewport.


## Resize and vector-point alignment (0.5.4)

**Snap resizing objects** aligns only the edges controlled by the active handle to page and unselected-object edges/centers. The opposite corner/edge remains fixed. Shift preserves the original aspect ratio: the solver chooses one consistent scale rather than snapping the two axes independently. A candidate is accepted only when every grabbed-edge correction remains within six screen pixels. A guide is drawn only when the final constrained edge actually satisfies it. Snapping cannot cross the fixed anchor or reduce the result below the minimum extent. Alt retains centered resizing and temporarily disables snapping.

**Snap vector points** aligns a dragged Bézier anchor/control point to page and unselected-object edges/centers. Shift locks the dominant page-space axis. Alt bypasses snapping. Grab offsets are retained, so pressing slightly away from a point does not relocate it. Stationary gestures and drags returned to the pointer-down dead zone preserve the original operands; Escape cancels. Only release invokes the existing native point-edit transaction. These guides do not yet include other control points on the selected path, tangent constraints, contours or equal-gap spacing.

All three snapping options are initially off, independently configurable view state. They share the immutable value-only anchor index. Selection/document/page changes invalidate it; changing zoom only changes the query tolerance. Public `ObjectSnapIndex.SnapPoint` and `SnapResize` can be used without Uno or the PDF writer. `SnapResize` also supports centered snapping programmatically; the UI intentionally reserves Alt for centered, unsnapped resizing.

The object inspector retains at most 64 row controls across selection changes, range navigation and panel reopening. **Previous object range**, **Next object range**, and **Go to object** reach all indexed occurrences, not only the first 200. Selecting an off-range object on canvas reveals its range. Navigation is view state and never adds undo or reparses PDF objects. Rows are rebound by pool slot and generation rather than retaining descriptors in their command closures; inactive slots are disabled and hidden. Rows are discarded after a new object index or active-document switch. The cached subtree is detached from its old inspector parent to avoid retaining document-capturing command closures. Other inspector fields are still rebuilt when their values change; this is bounded row pooling, not complete inspector virtualization.
