# Third-party notices

PdfSpace has original branding, UI composition, icon paths and demonstration document content. Dependencies remain subject to their upstream licenses and notices.

- **Uno Platform** — Apache-2.0. https://github.com/unoplatform/uno
- **SkiaSharp** — MIT. Native Skia and its bundled dependencies have their own notices. https://github.com/mono/SkiaSharp
- **PdfPig** — Apache-2.0. https://github.com/UglyToad/PdfPig
- **PdfPig.Rendering.Skia** and its filters — consult the licenses bundled with each NuGet package, including native codec dependencies. https://github.com/BobLd/PdfPig.Rendering.Skia
- **Noto Sans** — SIL Open Font License 1.1. The build retrieves `OFL.txt` alongside the font into `src/PdfSpace.App/Assets/Fonts`. https://github.com/google/fonts/tree/main/ofl/notosans
- **Playwright** — Apache-2.0; development/test dependency. https://github.com/microsoft/playwright

This is a direct-dependency overview, not a replacement for transitive package license notices. Distributors must retain the license and notice files required by the packages and native binaries they ship. No Adobe code, logos, icons, fonts or sample documents are included.

## Structured PDF and browser security

- **PDFsharp 6.2.4** — MIT. https://github.com/empira/PDFsharp
- **QPDF 12.2.0** — Apache-2.0. Runtime built from commit `856d32c610334855d30e96d25eb5f9636fb62f08`; LICENSE.txt and NOTICE.md are retained beside the published WASM.
- **@neslinesli93/qpdf-wasm 0.3.0** — package metadata declares ISC. https://github.com/neslinesli93/qpdf-wasm. The exact npm tarball and JS/WASM members are verified by pinned integrity hashes; its package manifest is included in published assets.
- Bundled **zlib** and **libjpeg-turbo** retain their upstream license/notice documents, fetched from the exact commits used by the WASM build. See `scripts/fetch-security-assets.py` and the published `security/runtime-manifest.json`.

Security assets are self-hosted. Original input PDFs and passwords are not sent to package registries; registry access happens at build time only.

## OCR (0.3)

Tesseract.js 7.0.0, Tesseract.js-core 7.0.0 and `tessdata_fast` 4.1.0 language models are Apache-2.0-licensed upstream components. Runtime/native subcomponents retain their own notices. Build-time acquisition is pinned by `scripts/ocr-inputs.json` and `scripts/ocr-assets.json`. License files are copied into the self-hosted OCR asset folders. Tesseract.js source: https://github.com/naptha/tesseract.js; engine: https://github.com/tesseract-ocr/tesseract; models: https://github.com/tesseract-ocr/tessdata_fast. Native distributors remain responsible for packaging the CLI and transitive license notices.

## Source-pinned renderer adaptation (0.5.1)

`src/PdfSpace.Rendering.Skia/Upstream` contains modified Apache-2.0 source from BobLd/PdfPig.Rendering.Skia at `e4476d80f98bf1a5a7cd6f7d45fb7c2afe10ec11`. This derivative is separately packaged as `PdfSpace.Rendering.Skia`, not relicensed as MIT. Its `LICENSE.txt`, `NOTICE.txt`, original copyright comments, `UPSTREAM.json` source hashes and `ADAPTATIONS.md` are retained. No upstream private signing key, font or native binary is copied.
