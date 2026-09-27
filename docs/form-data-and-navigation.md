# Form data and document navigation

## Import and export form values

Open a PDF with supported AcroForm fields, then choose **Prepare a form → Import form data**. Select an `.xfdf` or `.json` file. PdfSpace validates the complete batch before offering confirmation. The confirmation reports changed, unchanged and unmatched logical field names. Cancel leaves the document untouched. Accept applies all supported changes as one undoable transaction.

Matching is case-sensitive and uses the fully qualified logical field name, not the visible label or tooltip. Nested XFDF fields are flattened into names such as `Applicant.Name`. A radio group or another shared field is updated across all its widgets. Unknown names are reported and skipped only after confirmation. No-op imports do not add history entries. If the document changes while the file picker or confirmation is open, the plan is rejected as stale.

A type mismatch, modification of a read-only value, invalid choice/radio export value, maximum-length violation, inconsistent shared field or unsupported multi-value rejects the entire batch. PdfSpace does not truncate values or import the valid half of an invalid batch. Empty text clears a text field; an empty button value becomes `Off`. Reset form uses the PDF field defaults, not the last imported values.

**Export XFDF form data** produces a field-value-only XFDF file. **Export form data** produces JSON. Both export one entry per logical field; signature and unsupported field types are excluded. Both files are plaintext, even when the source PDF was encrypted. An unlocked sensitive document requires a plaintext-export confirmation. Treat the exports as confidential where appropriate.

**Check required fields** navigates to the first missing supported logical field. Empty or whitespace-only text is missing; a required check box/radio group with value `Off` is missing. This is not JavaScript calculation/validation, XFA validation, certificate verification or a submission service.

## Formats

```xml
<?xml version="1.0" encoding="utf-8"?>
<xfdf xmlns="http://ns.adobe.com/xfdf/">
  <fields>
    <field name="FullName"><value>Ada Lovelace</value></field>
    <field name="Approved"><value>Yes</value></field>
  </fields>
</xfdf>
```

The required namespace identifies the XFDF vocabulary. Plain XML text retains Unicode, whitespace and line endings. DTDs, entity expansion, duplicate field names, rich-text values and ambiguous mixed field/value hierarchies are rejected. Document references such as `<f href="..."/>`, annotation payloads and scripts are not imported, fetched or executed. The export intentionally omits document references.

```json
{
  "format": "PdfSpace.FormData/1",
  "document": "review.pdf",
  "fields": [
    { "name": "FullName", "type": "Text", "value": "Ada Lovelace" },
    { "name": "Approved", "type": "CheckBox", "value": "Yes" }
  ]
}
```

The `document` member is descriptive, not an instruction to open another file. JSON imports also accept the earlier PdfSpace 0.2 shape with singular `value` and no format identifier. Duplicate JSON properties and unknown format identifiers fail. `type` may be omitted for field-name matching; when present it must match the target field. The codec can represent multiple values, but current single-value editing rejects them rather than dropping extra values.

Input is bounded to 4 MiB, 10,000 logical fields, 100,000 characters per value and bounded aggregate character count. XML nesting is checked before materializing a tree; logical field nesting is bounded to 32 levels. These are defensive parser limits, not support for every XFDF extension.

## Embedding

```csharp
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;

var data = PdfFormDataCodec.Read(File.ReadAllBytes("review.xfdf"), "review.xfdf");
var plan = editor.PrepareFormDataImport(data);
if (!plan.CanApply)
    throw new InvalidDataException(string.Join(Environment.NewLine, plan.Errors));

// Surface plan.UnknownFields and obtain host/user consent before applying.
editor.ApplyFormDataImport(plan);
var exported = PdfFormDataCodec.WriteXfdf(PdfFormData.FromWorkspace(editor.Document));
File.WriteAllBytes("review-values.xfdf", exported);
```

`FormDataImportPlan` binds to a specific immutable workspace snapshot. Applying it to a changed or unrelated session fails. `IWorkspaceStorage.OpenFormDataAsync` is the host picker contract; the default interface implementation throws an explicit unsupported-operation exception so existing hosts can opt into interchange without changing their document picker.

## Navigation and preservation

The Bookmarks panel has separate workspace and source-document sections. Source outlines are read-only hierarchical navigation entries. Supported local destinations resolve direct page arrays, `/D` wrappers, legacy `/Dests` dictionaries, and `/Names` destination trees. Only local `/GoTo` actions are used. Unsupported/external outline destinations are not activated. The panel shows at most 1,000 source entries; engine traversal is bounded to 10,000 items and 64 levels and rejects corrupt cycles.

Page operations resolve supported internal links by stable workspace page ID, not by their old ordinal. An extracted/combined/reordered PDF receives the new destination index. Links to excluded/deleted pages are removed. A duplicated page copies its outgoing links; these continue to point at the original destination identity. When a source page has several workspace copies, source-outline navigation selects the first retained copy.

Workspace bookmarks export under a PdfSpace-marked outline root and reimport as editable page bookmarks. The marker—not the visible title—identifies that root, preventing an unrelated source outline named “PdfSpace bookmarks” from being deleted. Removing the last workspace bookmark removes the managed root and stale outline sibling pointers.

Structured save is still a rewrite, not an incremental or universally lossless operation. Original single-source page order can retain its source catalog/outlines. Page reassembly produces a new catalog, so source-only outlines and arbitrary named-destination metadata are not promised to survive as editable structures. Supported local links are rewritten as page-fit destinations; original XYZ/zoom parameters are not retained. This feature does not add external-file actions, script execution, source-outline authoring, OCR or accessibility tagging.

## References

- Adobe Acrobat SDK JavaScript API: https://opensource.adobe.com/dc-acrobat-sdk-docs/library/jsapiref/doc.html
- Adobe Acrobat SDK form workflows: https://opensource.adobe.com/dc-acrobat-sdk-docs/library/jsdevguide/JS_Dev_AcrobatForms.html

These references document interoperability concepts. PdfSpace does not implement the Acrobat JavaScript runtime or claim comprehensive XFDF compatibility.
