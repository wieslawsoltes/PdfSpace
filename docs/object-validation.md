# Object editing regression contracts

The native object suite validates the serialized PDF, not only the in-memory selection state. Run it independently with:

```bash
dotnet run --project tests/PdfSpace.Tests -c Release -- --test-unified-objects
```

The full engine run includes this suite and the previous form, security, OCR, image, history and navigation regressions:

```bash
PDFSPACE_TEST_NATIVE_OCR=1 dotnet run --project tests/PdfSpace.Tests -c Release
```

The optional environment setting requires a locally installed Tesseract executable. It does not send a document to a remote recognition service.

## Native invariants

The synthetic object fixture deliberately shares nested image and font resources between occurrences and pages. Checks cover one-transaction mixed transformations, unchanged siblings, exact undo identity, stale/forged descriptor rejection, source path operators and appearance, alignment/distribution, captured-state stacking, grouping/ungrouping, clipboard resources/clips, Unicode text-block replacement, overflow rejection and source save/reopen.

Clipping tests include real numeric rectangle clips, text clipping and clips constructed under interleaved matrices. Unsupported clipboard contexts must reject the transfer rather than silently exposing more content. A generated-group marker is not authority to remove its actual native BBox. Inherited graphics-state operations must be detached before rebinding resources.

## Browser interaction

`tests/browser/unified-objects.spec.mjs` drives real Uno pointer, keyboard, modal input and downloads. It verifies native object-index reuse across zoom/fitting, additive and marquee selection, drag/resize commit boundaries, path nodes and painting, native grouping, cleared selection after stacking, cross-document clipboard and searchable Unicode insertion. Diagnostics are read-only geometry/state observations, not a document-mutation API.

After serving the published Uno application, run:

```bash
npm run test:browser
bash scripts/verify-browser-exports.sh artifacts/browser-exports
```

The verifier independently reopens actual browser downloads using the native backend. `--verify-browser-mixed` specifically checks mixed geometry, path operands, grouping render identity, shared-page isolation, clipboard text and native Unicode insertion.

## Independent rendering

For the committed synthetic fixtures, MuPDF comparison is useful in addition to the application's own Skia renderer. Compare `unified-original.pdf` with `unified-grouped.pdf` and `unified-ungrouped.pdf`; the grouping operations must preserve rendered pixels. Compare the untouched second page of `unified-moved.pdf` with the original. Also compare `unified-clipped-group-before.pdf` with `unified-clipped-group-after.pdf` to ensure ungrouping retains the real clipping bounds.

Passing these fixtures does not establish arbitrary-PDF equivalence, accessibility conformance, secure sanitization or cryptographic signature preservation. See the [object editing boundary](object-editing.md) and [general compatibility guide](compatibility.md).
