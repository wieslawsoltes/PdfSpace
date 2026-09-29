# Native page-mark inspection benchmark

This executable compares full-document mark inspection with the range-scoped API used by the current-page inspector. Run on Linux with .NET 10 and DejaVu Sans installed:

```bash
dotnet build benchmarks/PdfSpace.PageMarks -c Release
dotnet run --project benchmarks/PdfSpace.PageMarks -c Release --no-build > inspection.json
```

The fixture has eight remapped source identities, each referencing an eight-page marked document. It is deliberately synthetic. Both paths use the same engine and validate all returned ownership checksums. Three warmups precede nine samples in alternating order; output includes each elapsed time and current-thread managed allocation count.

Do not interpret these values as application frame rate, total process/native/GPU memory, or a comparison between unrelated PDF documents. Fixture construction and PDF writing are outside the measured region. PDF parsing and mark checksums are inside it. There is no timing pass/fail threshold. Run several processes on an otherwise idle machine before generalizing results.

The ordinary native regression suite separately verifies range deduplication, cancellation, unreferenced source handling and strict interpretation of selected malformed/future-version records. An unselected record not read by the scoped API is not certified as valid.
