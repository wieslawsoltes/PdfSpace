# Scan & OCR

PdfSpace 0.3 adds on-device recognition, review and interoperable searchable PDF output. OCR changes the text layer, not the scanned pixels. Recognition is probabilistic: review important words, numbers and names before sharing a document.

## Workflow

Open **All tools → Scan & OCR**. Open an image-only PDF, use **Create PDF from image** for a PNG/JPEG, or try **Open scanned example**. Image import uses 150 DPI and honors EXIF orientation; it does not recognize text by itself.

Choose English, Polish or German with **OCR language**. Choose 150, 200 or 300 DPI. Rotate the page upright and crop it before recognition when appropriate. Run **Recognize current page** or select a range with **Recognize pages…**. A batch supports up to 100 pages. Pages that already have non-PdfSpace selectable text are skipped to avoid a duplicate text layer. PdfSpace-recognized pages can be recognized again.

**Cancel recognition** discards the entire pending batch. A provider error also applies nothing. Recognition captures an immutable workspace snapshot; editing the document while OCR runs makes that result stale and prevents its application. A successful batch is one undo transaction. Changing tools or opening another tab does not redirect results into that tab.

Use **Correct recognized text** to inspect a highlighted word against the original image, change **Recognized as**, and **Accept correction**. The low-confidence view filters unreviewed words below 85 percent. High confidence is not proof of correctness. Corrections are undoable; the original recognition score is retained alongside the reviewed flag.

Normal selection, copy, search and text export use the recognized layer. **Export searchable PDF** uses the normal structured writer. It embeds a TrueType CID font, an explicit Unicode map and invisible text operators. Other readers can extract and search the text without understanding PdfSpace metadata. PdfSpace also records bounded per-word metadata for correction after reopening. Repeated saves replace the previous owned layer rather than appending duplicates. New crop/rotation geometry is reflected in both the PDF text and the correction metadata.

A `.pdfspace` workspace also retains OCR. A flattened visual export is a different operation and is not the promised searchable-OCR export. Raster-redaction reconstruction intentionally does not transfer the OCR text or metadata.

## Platform engines

The browser uses **Tesseract.js 7.0.0**, with its Tesseract WebAssembly runtime and pinned `tessdata_fast` 4.1.0 English/Polish/German models. All runtime scripts, WASM variants and model files are served from the application origin. `scripts/fetch-ocr-assets.py` verifies input archives and every extracted asset against SHA-256 manifests. Files are obtained at build time, not from a third-party CDN during recognition. A worker is reused within a batch and terminated afterward; model IndexedDB caching is disabled. Documents and results are never submitted to an OCR server.

Native hosts use `TesseractProcessEngine`. Install **Tesseract 5** and the desired language packs and ensure `tesseract` is on PATH. The provider sends PNG bytes to standard input and reads TSV from standard output; it creates no document temporary files. Cancellation kills the process tree. A custom host can inject another `IOcrEngine` without changing the viewer, document model or PDF writer.

Decrypted protected documents retain the existing sensitive-source rules: automatic recovery is disabled, and unencrypted exports require confirmation. Signed/certified PDFs and XFA documents are rejected by the OCR editing workflow. OCR is not redaction, permission enforcement or accessibility certification.

## Integration

```csharp
using PdfSpace.Ocr;

var snapshot = editor.Document;
var batch = await OcrBatch.RecognizeAsync(
    snapshot, renderer, ocrEngine, new[] { 0, 1 },
    language: "eng", dpi: 200,
    progress: progress, cancellationToken: cancellationToken);

editor.Execute("Recognize scanned text", current => batch.Apply(current));
```

`PdfSpace.Ocr` is the eleventh packable library. It contains the provider contract, bounded TSV parser, raster preparation, snapshot transaction and native process adapter. `PdfSpace.Core` owns the serializable word/layer records, `PdfSpace.Editing` owns correction transactions, `PdfSpace.Pdf` writes native text, and `PdfSpace.Workbench` provides the tools and review UI. Browser interop remains in the thin application host.

## Limits and deliberate boundaries

Recognition is limited to 16 million pixels and 32 MB of PNG data per page, 20,000 words per page, 250,000 words per workspace, 512 UTF-16 code units per corrected word and a 90-second provider deadline per page. TSV input is bounded to eight million characters. Native PDF correction metadata is bounded separately. These are resource guardrails, not a security audit of native decoders.

This release does not provide camera/scanner acquisition, automated deskew or orientation detection, image cleanup, handwriting guarantees, text-versus-image region classification, arbitrary languages, mathematical OCR, a layout/reflow engine or PDF/UA tagging. Existing text pages are skipped as a whole, so recognition of only the scanned region of a mixed-content page remains unsupported. The searchable layer currently requires an embeddable TrueType font; CFF fonts, font collections and unavailable glyphs fail explicitly. Supported Latin-script text is positioned word by word; complex shaping and exact original-font reconstruction are not claimed.

## Verification

The native suite checks raster geometry at every quarter rotation, Unicode correction, source-byte identity, batch cancellation/failure/staleness, existing-text skipping, workspace/native round trips, duplicate-layer prevention and unchanged rendered pixels. `PDFSPACE_TEST_NATIVE_OCR=1` enables a real Tesseract recognition of the synthetic image-only scan.

Browser acceptance tests perform actual recognition with self-hosted assets, correction, undo/redo, PDF download and reopen, image picking, existing-text skipping and cancellation while a language model is loading. The CI then reads the browser-produced PDF with the independent native PdfPig/Skia backend to verify real text and pixel equality. No recognition result is injected into the app by the tests.

Upstream references: [Tesseract.js API](https://github.com/naptha/tesseract.js/blob/v7.0.0/docs/api.md), [Tesseract CLI](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html), [language models](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0).
