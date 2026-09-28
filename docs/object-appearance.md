# Object appearance and dense-page selection

## Edit native painting state

Open **Edit → Edit objects**, select one or more native objects, then expand **Object appearance**. The inspector reads effective graphics state, including inherited ExtGState dictionaries and changes within a text object. Mixed or unrecognized values are blank rather than being presented as a default.

Fill and stroke opacity accept percentages from 0 to 100. Blank opacity fields leave that parameter unchanged. The blend menu provides the sixteen standard PDF blend names. These are **per-paint** overrides, not the opacity of a composited group. Path, image, shading and supported text occurrences can be edited; whole Form/group targets require selecting their contents instead. Existing soft masks remain in effect. Alpha-is-shape is explicitly disabled when setting opacity.

For paths, the inspector also provides butt/round/square caps, miter/round/bevel joins, the miter ratio and an immutable dash sequence. An empty dash sequence explicitly restores a solid stroke. Zero-length intervals are allowed when the complete sequence is not zero; round caps can therefore produce dots. Odd-length sequences retain the complete pattern rather than dropping their last interval. Dash lengths are local user-space distances, so a transformed path can have a different apparent size.

Dash **phase authoring currently requires integer local units**. The pinned PdfPig operation model quantizes a phase to an integer before rendering. Fractional imported phases are retained in native content and exposed by the inspector, but their preview can differ. Attempting to author a fractional phase fails explicitly rather than silently promising preview fidelity.

A batch validates all source identities and uses one editing transaction. Safe text objects can contain their own `gs` operations: the override is applied immediately before every text-showing operator, then the original object's state side effects are restored for following content. Ordinary source edits are not secure redaction. Existing signed/XFA editing guards remain applicable.

## Reusable API

```csharp
using PdfSpace.Pdf;

var objects = PdfObjectEditor.Read(workspace, pageIndex);
var selected = objects.Where(o => o.Kind == PdfPageObjectKind.Path).Take(2).ToArray();

var styled = PdfObjectEditor.SetAppearance(workspace, selected, new PdfObjectAppearance(
    StrokeEnabled: true,
    StrokeWidth: 4,
    LineCap: PdfLineCap.Round,
    LineJoin: PdfLineJoin.Bevel,
    MiterLimit: 8,
    Dash: new PdfDashPattern(new double[] { 9, 3, 1 }, phase: 2)));

// Rescan after a source change: descriptors are revision-specific.
var updated = PdfObjectEditor.Read(styled, pageIndex)
    .Where(o => o.Kind == PdfPageObjectKind.Path).Take(2).ToArray();
var transparent = PdfObjectEditor.SetCompositing(styled, updated,
    new PdfObjectCompositing(FillOpacity: 0.5, StrokeOpacity: 0.75,
        BlendMode: PdfBlendMode.Multiply));
```

Wrap a mutation in `EditorSession.Execute` for undo. A repeated equivalent compositing override preserves the original snapshot identity. `PdfObjectPaintState` contains effective stroke, dash, alpha, blend and mask-presence metadata. Its nullable members represent mixed or unsupported input. `PdfDashPattern` owns a copy of its input intervals; changing the caller's array cannot change an accepted request.

## Renderer fixes and provenance

`PdfSpace.Rendering.Skia` is a separate packable **Apache-2.0 derivative**, not original MIT PdfSpace code. It retains source from PdfPig.Rendering.Skia 0.1.16.4 at commit `e4476d80f98bf1a5a7cd6f7d45fb7c2afe10ec11`, isolated under the PdfSpace.Rendering.Skia namespace. The original license, notices, copyright comments and per-file upstream hashes are checked in; see its `UPSTREAM.json` and `ADAPTATIONS.md`.

The application now references that source project rather than the previous renderer package. The fixes apply nonstroking alpha to ordinary and soft-masked images, repeat odd-length dash sequences correctly, and forward native miter limits to Skia for paths and glyph strokes. Paint caches compare complete state keys rather than treating a hash code as a unique identity. Image cache keys include opacity; stroke cache keys include dash contents and miter limits.

This does not make rendering universally equivalent to Acrobat. PDF transparency groups, knockout/overprint, unusual color spaces, complex fonts and platform font substitution still require representative document testing. No source PDF is rasterized merely to apply these appearance edits.

## Spatial index

`PdfSpace.Layout.SpatialBoundsIndex` is an immutable ordered bounding-volume hierarchy. A query returns original input ordinals, preserving PDF paint order despite the tree's internal partitioning. Point queries choose the last-painted candidate; area queries return sorted intersecting or contained bounds. Zero-size and boundary hits are supported. Callers provide a reusable result buffer.

The viewport builds the tree when its native-object targets change, not during pan/zoom. It uses it for hit testing, marquee queries and visible outline candidates. Selection membership and union bounds remain cached. Selected geometry undergoing a preview stays in the candidate set even when its original position is outside the clip. This is still **bounding-box hit testing**, not exact path-fill, alpha-mask or clipping-shape picking.

`SpatialBoundsIndexTests` compares 400 queries over 12,000 heterogeneous bounds with linear references. A separate 16,384-object grid measurement records tested bounds, time and current-thread managed allocations for 10,000 warmed queries. It excludes tree construction, native memory, source scanning, document rewriting and raster rendering. Overlapping or page-sized bounds can still approach linear query behavior; the result is not a general frame-rate guarantee.

## Validation

Run the focused checks with `dotnet run --project tests/PdfSpace.Tests -c Release -- --test-object-paint`. The full native suite includes the existing PDF editing, security, OCR and memory tests. Browser scenarios exercise actual controls, save/reopen, text-state isolation, masked image opacity and a 4,096-object page. `scripts/verify-browser-exports.sh` independently reopens their downloaded PDFs with the native backend.
