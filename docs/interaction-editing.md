# Native object gesture editing

## Constrained transformations (0.5.2)

In **Edit → Edit objects**, hold **Shift** while dragging a selected object to lock movement to its dominant page axis. Shift-click still toggles selection; a drag of an already-selected object is distinguished from the click at pointer release. Hold Shift while dragging any resize handle to retain the selection's bounding-box aspect ratio. On a side handle the perpendicular axis expands about its center.

Hold **Alt** to resize symmetrically about the selection center, or **Shift+Alt** to combine center and aspect constraints. These modifiers also apply to the native-image editing tool. Resize calculation uses displacement from pointer-down, not an absolute pointer coordinate, preserving the grab offset within the handle hit area. Crossing the opposite anchor clamps to a positive extent instead of implicitly mirroring the PDF. Use the explicit Flip commands for reflection.

When drawing a native rectangle or ellipse, Shift gives a square/circle and Alt places the center at pointer-down. Combining them creates a centered square/circle. Escape abandons the preview with no source rewrite. Pointer release commits the complete gesture as one undo operation, including multi-object selections. The same source-identity and native occurrence isolation checks as other object edits apply.

`PdfSpace.Layout.SelectionTransform` exposes these pure geometry operations without Uno or PDFsharp dependencies. All helpers validate finite coordinates and extents. Geometry-only pointer previews do not parse or serialize the PDF; the native writer runs on a successful gesture commit. Re-selecting an unchanged native selection no longer rebuilds the inspector or its selection lookup sets.

## Input handoff

Closing an active inline editor first transfers managed focus to the persistent document viewport, and only then detaches the editor. This retains a keyboard target while its replacement is loading. Focus that has already moved to a toolbar or another editor is not reclaimed. The browser bridge may focus the document canvas only when browser focus is on the body/root; it never steals focus from a native text editor or the accessibility entry point.

## Acceptance geometry

`?test=1` diagnostics are read-only. A test-only 80 ms sampler observes render-transform changes during scroll animation; layout events alone do not observe those transforms. The native host stops this sampler on window closure. Normal sessions do not enable it. Each published snapshot has a revision; the pointer helper requires multiple fresh snapshots as well as stable geometry before clicking once. It never invokes a document-mutation API or retries a click blindly.

## Verification and boundaries

The native tests cover all eight handles, zero-motion identity, anchor crossing, 2,000 deterministic randomized cases, a zero-width line, invalid/nonfinite inputs, and actual native PDF save/reopen with undo/redo. Browser tests cover real modifier keys and pointer gestures, cancellation, original-path preservation, consecutive form Tab transitions, and stale-geometry rejection. The native verifier independently reopens the browser's constrained PDF download.

These are axis-aligned selection-bounds constraints, not arbitrary oriented-frame or mesh manipulation. Object pixels still render after native commit; dragging previews outlines. Native transforms retain the existing clipping, font, transparency-group and structured-save limitations. Editing, source replacement, and history clearing are not secure redaction. See [object editing](object-editing.md) and [compatibility](compatibility.md).
