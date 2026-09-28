# PdfSpace renderer adaptation

Upstream: BobLd/PdfPig.Rendering.Skia, Apache-2.0, package 0.1.16.4, commit
`e4476d80f98bf1a5a7cd6f7d45fb7c2afe10ec11`.
`UPSTREAM.json` records the original source paths and SHA-256 hashes before changes.
Original LICENSE.txt and NOTICE.txt are retained. No font files or native binaries
are vendored. Codec/native package dependencies retain their own licenses.

Changes relative to those original sources:

- All source namespaces use `PdfSpace.Rendering.Skia` to coexist without assembly/type identity collisions. Global aliases restore references previously resolved through the original parent namespace.
- Ordinary image paints apply `AlphaConstantNonStroking`, including the inner paint of a graphics-state soft mask. The cache includes the byte opacity actually consumed by Skia.
- Stroke paints forward `MiterLimit` for paths, glyphs and stroked shading patterns; ten is the PDF default.
- Odd-length dash arrays are repeated in full to form an even Skia array. Empty sequences are solid regardless of phase; valid zero intervals are preserved. Invalid or excessive cycles fail rather than allocate without bounds.
- Full structural paint keys replace hash-only identities. Dash arrays are compared structurally and copied only for a new retained key. Miter limits and raw Skia blend overrides remain distinct.
- Nullable/type guards, current image BoundingBox access, and explicit parent-namespace references make the retained source compile cleanly with this host's nullable settings. Pattern type mismatches produce explicit errors; the canvas is assigned before processing operations.

The original PdfPig dash-phase operation remains integer-valued; fractional PDF phases are not made exact by this renderer adaptation. This is not a replacement implementation of the complete PDF rendering standard.
