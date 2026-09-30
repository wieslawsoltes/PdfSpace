# Page-label formatting benchmark

Compare the previous formatter (retained from `c1de18c3bf8c045682cc11944f88bdebf72aee84`, isolated to this non-packable benchmark) with direct result-string formatting:

```bash
dotnet build benchmarks/PdfSpace.PageLabels -c Release
dotnet run --project benchmarks/PdfSpace.PageLabels -c Release --no-build > label-formatting.json
```

The fixture formats 4,096 lowercase Roman values with the same Unicode prefix. Exact string equality is checked before timing. Three warmups precede nine alternating-order samples. Inputs and delegates are prebuilt; each measurement includes validation, formatting, and result strings. Current-thread managed allocations are not native/GPU allocations or peak process memory. PDF parsing, writing, index construction, UI and renderer work are excluded. No machine-dependent timing threshold controls test success. Run multiple processes on an otherwise idle host before generalizing elapsed-time results.

The native test suite separately compares all six styles against an independent reference over 4,096 values, validates exact length limits and malformed Unicode, and records 100,000 literal-label lookups without allocating query substrings.
