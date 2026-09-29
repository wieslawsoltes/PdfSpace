# Native page-mark ownership and source isolation

Page marks are native searchable PDF content. Updating or removing them must not guess at the page state that surrounds them. In addition to resource and settings checksums, PdfSpace validates the complete top-level content envelope produced by its composer:

```text
behind-content mark invocations
q                      # uniquely referenced owned guard
original page streams  # retained bytes; no owned invocations inside this region
Q                      # uniquely referenced owned guard
front-content mark invocations
```

Each guard must have its original decoded operator, and all invocation positions must match their saved front/behind settings. Missing, duplicated, reordered or modified guards; marks moved into the source region; and extra streams outside that region invalidate every mark on that page. An orphan isolation record with no ownership container is rejected rather than treated as an unmarked page.

These checks run during inspection, before an unchanged Apply can return the original workspace. Invalid marks cannot bypass validation by retaining identical settings. Removal and effective updates reject the same states. Other selected or unselected pages retain their independent validity.

The envelope scan is linear in the number of top-level content references with at most two managed marks per page. It does not concatenate, decode or reparse the original page streams. Effective editing retains the existing bounded source-state validation. Checksums and guards detect stale edits, not authenticity, arbitrary-PDF correctness, secure redaction or cryptographic tamper resistance.

## Regression coverage

`PageMarkEnvelopeTests` changes serialized native PDFs, reopens them, and covers eight invalid envelope configurations, untouched sibling pages, update/remove rejection, orphan metadata and valid front/behind coexistence and removal. Run it as part of:

```sh
dotnet run --project tests/PdfSpace.Tests -c Release -- --test-page-marks
```

The missing-isolation fixture fails before this fix even though the mark's resource checksum is unchanged. With the fix, all cases pass without relaxing original PDF editing checks.
