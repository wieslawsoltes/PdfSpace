# Native page labels and navigation

Page labels are `/PageLabels` catalog metadata, not text painted on the page. Use **Organize pages → Page labels** for Roman front matter, decimal chapters, alphabetic appendices or literal prefix-only labels. Use **Header and footer** for printed numbers. This follows the distinction described in [Adobe's page-label workflow](https://helpx.adobe.com/acrobat/desktop/edit-documents/organize-pages/renumber-pages.html) and [PDF Association's page-label guidance](https://pdfa.org/pdf-ux-page-labels/), reviewed 2026-09-29.

## Editing

Enter a contiguous physical page range, numbering style, prefix and a positive start. Apply starts a section in that range; Extend previous labels continues the preceding page's sequence. Reset restores physical numbering in the range. Pages outside the range retain their existing resolved labels. There is one undo entry per effective command; repeated identical assignments and resetting an already implicit range are no-ops in an initialized workspace.

PDF alphabetic numbering repeats the same letter: A…Z, AA…ZZ, AAA…ZZZ. It is not spreadsheet column lettering. Prefix-only labels can be empty or identical on multiple pages. The prefix limit is 256 UTF-16 code units and expanded labels are bounded to 1024; invalid Unicode, controls, nonpositive starts, integer overflow, unsupported styles and excessively long expansion are rejected before committing changes.

## Navigation and page identity

The page field accepts an exact, case-sensitive label. `#N` always selects physical page N, even if numeric labels conflict with physical positions. Nonnumeric hash-prefixed labels such as `#Appendix` resolve normally. `=text` requests literal label lookup without command-prefix or physical fallback: `=#2` selects the native label `#2`, `==Cover` selects `=Cover`, and `=` selects a unique empty label. Duplicate literal labels are still ambiguous. These are PdfSpace navigation shortcuts, not modifications to the native label string. A duplicate label is explicitly ambiguous rather than selecting an arbitrary occurrence. A number that is not an existing label can resolve to a physical position. The physical position and total remain displayed separately. Long labels are clipped to their thumbnail cell, and the full value is editable in the navigation field and inspector.

Resolved labels follow stable page identities through extraction, reordering and combination. Duplicating a page duplicates its label; it does not silently renumber other sections. An implicit label remains the page's current physical number. Explicit decimal labels retain their resolved number when moved. Reapply or extend a range to intentionally renumber it.

## Reusable APIs

```csharp
var document = PdfDocumentEngine.Open(bytes, "manual.pdf");
var labels = PdfPageLabels.Apply(document, 0, 3,
    new PdfPageLabel { Style = PdfPageLabelStyle.RomanLower });
labels = PdfPageLabels.Apply(labels, 3, labels.Pages.Length - 3,
    new PdfPageLabel { Prefix = "Chapter-", Number = 1 });
var index = new PageLabelIndex(labels.Pages);
if (index.Resolve("Chapter-4", out var page) == PageLabelMatch.Found)
    viewport.Navigate(page);
var output = PdfDocumentEngine.Save(labels, typeface);
```

`Apply`, `Extend` and `Reset` take a zero-based first page and count, not inclusive endpoints. Initialize upgrades labels in older workspaces and objects created through the low-level reader. Each required source is parsed once during initialization. Normal `PdfDocumentEngine.Open`/`PrepareWorkspace` initialize labels during their existing source pass. Explicit per-page initialization state prevents an intentional reset from being overwritten with the original source's labels. Blank pages need no native initialization.

## Native format and safety

The reader validates a bounded number tree, including nested Kids/Nums, global ascending keys, a page-zero start, positive integer start values, optional Limits, cycles/reused nodes, depth and expansion limits. Unsupported or malformed native labels fail explicitly rather than being guessed. This means some malformed PDFs previously readable without interpreting labels will now be rejected by the structured engine.

Export coalesces consecutive compatible labels into maximal runs and replaces the catalog tree. A fully reset, implicit physical sequence removes the tree; explicitly assigned numeric labels remain explicit. It does not claim byte-for-byte preservation of private label dictionary keys or the original tree topology. Page streams are not rewritten by the label operation. Structured export still runs its existing annotation/form/OCR and source-safety logic; it is not a signature-preserving incremental writer. Signed/XFA and unsafe form-reassembly restrictions remain in effect.

Older PdfSpace binaries do not understand these new workspace fields; use current versions for label-preserving round trips. General flattened visual exports intentionally discard document structure and are not label-preserving.

## Performance and verification

Label assignment changes metadata snapshots and shares original/preview buffers; it does not embed a font, rerender source content or write a PDF until export. `PageLabelIndex` stores bounded strings and a dictionary, not sources or workspace objects. The viewport shares one weak-snapshot-keyed index with visible thumbnails. Pan/zoom and navigation do not rebuild it. Lookup is expected constant time and avoids query-time allocations for unchanged input strings.

The native suite emits `artifacts/structured/labels-performance.json`: 100,000 warm exact-label queries across 4,096 pages. It records elapsed time and current-thread allocations; fixture/index construction, PDF parsing/writing, UI and GPU work are excluded. No universal speedup or timing pass threshold is claimed. Native number-tree, Unicode, invalid-input, identity/history, reassembly, exact content/pixel and browser download checks accompany the feature.

## Identity and cache details

Explicitly assigned decimal labels that happen to equal physical numbers remain in `/PageLabels`, so reopening and then reordering does not silently change them into implicit numbering. Resetting every page to implicit numbering removes the tree. Within a PDF that contains a label tree, every page necessarily belongs to a native range; importing resolves those ranges into per-page definitions.

The navigation/thumbnail index retains strings and immutable label definitions, not page states or PDF sources. A new snapshot with unchanged label definitions reuses the index after a bounded sequence comparison; pan/zoom/navigation on the same snapshot skips even that comparison. Only changed label definitions rebuild the lookup.

Adjacent prefix-only sections retain distinct saved starting-number settings even when their displayed strings match. Export only coalesces records whose relevant settings are equivalent. The native writer also fast-paths an entirely implicit/reset label sequence without temporary per-page label records; uninitialized source-backed pages still resolve their native tree before that decision. The exact-label dictionary is sized once for its page count.

## Direct formatting and literal lookup

`PdfPageLabel.Format` measures the exact expansion before allocation and writes the prefix and suffix directly into a single result string. Roman symbol tables are shared, and casing affects only the Roman suffix. Prefix-only labels reuse the existing immutable prefix. Exact 1,024-code-unit Roman/alphabetic expansions are accepted; larger ones are rejected before allocating the expanded string.

`PageLabelIndex.ResolveLabel` is the command-free lookup for embedders. `Resolve` accepts the UI shortcuts described above and uses an alternate span lookup, so `=text` does not allocate a substring. The immutable index does not retain source buffers or workspace objects. A literal query never silently falls back to a physical page.

The reproducible `benchmarks/PdfSpace.PageLabels` harness compares the previous formatter with the direct implementation over identical valid inputs. It includes all result-string allocations, unlike the warmed lookup benchmark, and excludes index construction, native PDF/UI/rendering and GPU work. The fixture is not an application-wide performance claim.
