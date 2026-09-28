# Native photographs, duplication and memory

## User workflow

Choose **All tools → Scan & OCR → Create PDF from image** to create an image-only PDF at 150 DPI. Recognition is a separate action: importing an image does not invent a text layer. The same PNG/JPEG importer is used by **Edit → Edit original images → Add image / Replace image**.

Phone photographs with any of the eight EXIF orientations, including mirrored variants, are normalized before native sample embedding. Replacement retains the existing image placement while honoring the incoming file's orientation. It does not reapply orientation on a later replacement. Supported unrotated JPEGs retain their original compressed data, avoiding an unnecessary quality-loss cycle. Profiled/oriented JPEGs take the decoded sRGB path; transparency uses an unpremultiplied RGB stream and a separate native soft mask.

**Duplicate image** creates an offset drawing occurrence 24 points right and down, sharing the encoded resource. Later occurrence-isolated changes still leave siblings unchanged. Existing enclosing clipping remains effective; duplicating inside a tightly clipped form may leave part of the copy outside that form's visible region.

**Restore image proportions** preserves the center, horizontal axis size and affine orientation while restoring the raster aspect ratio. It works for rotated placements as well as axis-aligned rectangles. It does not remove existing shears or clipping.

**Properties → Clear undo history** explicitly releases retained history after confirmation. Current edits and dirty state remain; this is neither Save nor sanitization. Automatic history trimming may reduce available undo depth before 100 operations when distinct source buffers reach the default 128 MiB budget.

## Library example

```csharp
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;

var document = PdfImageEditor.OpenImage(File.ReadAllBytes("photo.jpg"), "photo.jpg", dpi: 150);
var session = new EditorSession(document, new EditorHistoryOptions
{
    MaximumEntries = 100,
    MaximumSourceBytes = 128L * 1024 * 1024
});
var image = PdfImageEditor.Read(session.Document, 0).Single();
session.Execute("Duplicate image", current =>
    PdfImageEditor.Duplicate(current, image, new PointD(24, 24)));
Console.WriteLine($"{session.RetainedSourceBytes} bytes of distinct retained source buffers");
```

Source descriptors are snapshot-specific. Re-read the image after an edit or undo before applying a subsequent mutation. `PdfImageImporter` remains available in the renderer-only package for hosts without the structured PDF library; the workbench uses `PdfImageEditor.OpenImage` for native JPEG preservation.

## Limits and validation

Inputs must be single-frame PNG/JPEG, at most 32 MiB encoded, 16 million pixels, and 8192 pixels on either axis. Import validates dimensions before allocating a pixel buffer. JPEG passthrough validates marker/frame/scan structure, not every entropy-coded coefficient; displaying an invalid JPEG can still fail in its decoder. Lossless original compression is retained only on the documented eligible path. The color-managed fallback may change color values or precision and is not archival byte preservation.

Native regression tests cover all eight orientations, transparency samples, native duplication without extra image resources, rotated proportion correction, invalid inputs, history pruning and saved-state lifetime. Real browser tests cover file pickers, downloads, EXIF replacement, duplicate/proportion commands and confirmed history release. A native verifier reopens browser exports. This is not full Acrobat parity, a universal color-fidelity claim, or security certification.

## UI cache lifetime

Image, bookmark and shell cache stamps use `WorkspaceSnapshotStamp`, a weak identity marker. They can reuse results for the active immutable snapshot without keeping an obsolete source PDF alive after history trimming. Hidden home cards are detached when returning to a document, and the home list is rebuilt after closing a tab. These changes remove application-owned references; garbage collection and Skia cache eviction still control when physical memory returns to the runtime.
