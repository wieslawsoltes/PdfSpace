# Native object layout

## Workflow

Open **Edit → Edit objects**, select supported native page objects, and expand **Object layout**. Operations are native source edits with one undo transaction, not annotation overlays or raster substitutes.

**Align page** aligns each selected bounding box to the visible page crop. Left/center/right and top/middle/bottom honor workspace page rotation. Single selections are supported. Existing PDF object clipping stays in effect and can still hide moved content.

**Layout reference object** accepts the displayed number of a member of the current selection. **Align reference** aligns source-space edges or centers to that member without moving the reference. The reference number persists while it remains selected. Selection/snapshot guards reject obsolete inspector callbacks.

**Match object width / height / both** scales the selected source-space bounding boxes to the reference's dimensions while retaining each object's center. The reference does not change. This geometrically scales text; it does not reflow paragraphs. A zero dimension on a matched axis is rejected before a transaction is committed.

**Equal horizontal / vertical gaps** distributes source-space bounding boxes by their leading edge, preserving both outer objects. It differs from the existing equal-center distribution when sizes differ. Ties retain selection order. Insufficient room for nonnegative gaps is rejected rather than silently producing overlap.

## Reusable API

```csharp
var objects = PdfObjectEditor.Read(workspace, pageIndex);
var selection = new[] { objects[6], objects[7], objects[8] };
var next = PdfObjectEditor.AlignToObject(workspace, selection, 1, PdfObjectAlignment.Left);

// Descriptors belong to the original snapshot: reacquire after any committed edit.
var refreshed = PdfObjectEditor.Read(next, pageIndex);
next = PdfObjectEditor.MatchSize(next, new[] { refreshed[6], refreshed[7], refreshed[8] },
    1, PdfObjectSizeMatch.Both);
```

`referenceIndex` is zero-based within the supplied selection; the UI instead asks for the one-based inventory number. `AlignToPage` uses displayed page coordinates; reference alignment, size matching and gap distribution use source-space bounds, consistent with the native geometry inspector.

## Performance and correctness

Native batch preflight sorts occurrence intervals per scope and checks complete ancestor paths using a hash set instead of comparing every selected pair. Its structural work is O(n log n + total unique-scope path-prefix work), not O(n²). It allocates bounded working collections for at most 1,000 selected objects and does not retain PDF snapshots. Native source hashes, fingerprints, geometry, editability, and the one-page restriction are still validated.

Exact identity transforms are validated but neither cloned into content streams nor serialized. A wholly unchanged batch returns the original workspace object, preserving undo identity, source buffers, preview and object-index caches. Mixed batches rewrite only effective changes; identity reference/outer objects retain their original operators. Validation and PDF parsing still occur for a no-op: this is not a zero-work or parse-free operation. Near-identity matrices are not silently discarded with an arbitrary epsilon.

Content range copying uses direct indexed access instead of walking a prefix for each selected object. This does not remove every rewriting cost: PDF parsing/serialization, text metrics, preview rehydration and content-sequence insertion remain separate stages.

## Validation

```bash
dotnet run --project tests/PdfSpace.Tests -c Release -- --test-object-layout
npx playwright test tests/browser/object-layout.spec.mjs
dotnet run --project tests/PdfSpace.Tests -c Release -- --verify-browser-layout artifacts/browser-exports
```

Native tests cover reference invariance, exact no-ops with stale/forged descriptor rejection, unequal-size gaps, size matching, all six page alignments with crop at all four rotations, native save/reopen and structural-preflight equivalence against exhaustive legacy comparisons. A bounded benchmark records allocation and timing observations separately from correctness assertions. Browser tests issue actual pointer/keyboard actions and independently reopen downloads.

This is not full Acrobat parity, general paragraph layout, an arbitrary layer hierarchy or secure content erasure. Cropping/deleting/moving an object is not redaction. Native clipping, font/shaping, group and signature-preserving-update limitations remain in [compatibility](compatibility.md).
