# Contributing

Use the SDK and dependency versions pinned by the repository. Run the engine suite and the relevant Uno build before submitting a change; UI work should include browser interaction coverage and screenshots.

Keep document state immutable, source-PDF bytes shared across history entries, and geometry in PDF points. Route mutations through `EditorSession` so undo and recovery stay consistent. Dispose native Skia objects deterministically. Keep browser/OS file APIs in host adapters, not Core, Layout or Editing.

New tools must perform the operation advertised by their label. Do not add a simulated redaction, signature, encryption or OCR button. Document import/export preservation boundaries explicitly and add regression fixtures for specialized PDF content. Use synthetic or permissively licensed samples; never commit private documents or Adobe proprietary assets.

Follow `.editorconfig`, keep types focused, and add public API documentation when extending an independently packable component. A release is not considered complete solely because it compiles: inspect rendering and exercise the actual interaction path.
