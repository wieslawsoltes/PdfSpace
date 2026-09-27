using System.Text;
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class FormDataTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var original = PdfDocumentEngine.CreateFormSample(SKTypeface.Default);
        var session = new EditorSession(original);
        var data = new PdfFormData("source.pdf", [new("FullName", ["Marie Curie"], PdfFieldKind.Text), new("Approved", ["Yes"]), new("Role", ["Engineering"]), new("Unknown", ["ignored"])]);
        var plan = session.PrepareFormDataImport(data);
        check(plan.CanApply && plan.ChangedFieldCount == 3 && plan.UnknownFields.SequenceEqual(["Unknown"]) && !session.IsDirty, "form import preview reports matches without mutating the document");
        session.ApplyFormDataImport(plan);
        check(Value(session, "FullName") == "Marie Curie" && Value(session, "Approved") == "Yes" && Value(session, "Role") == "Engineering", "atomic form import applies text, button and export-choice values");
        session.Undo(); check(ReferenceEquals(session.Document, original) && !session.CanUndo, "whole form-data batch uses one undo transaction");
        session.Redo(); check(Value(session, "FullName") == "Marie Curie", "form-data import redo restores the complete batch");
        var unchanged = session.PrepareFormDataImport(data);
        var revision = session.Revision; session.ApplyFormDataImport(unchanged);
        check(unchanged.ChangedFieldCount == 0 && unchanged.UnchangedFieldCount == 3 && revision == session.Revision, "no-op form import adds no history or revision");
        var stale = session.PrepareFormDataImport(new("", [new("FullName", ["New value"])]));
        session.RotatePage(); reject(() => session.ApplyFormDataImport(stale), "form import rejects a stale document snapshot");
        var before = session.Document;
        var invalid = session.PrepareFormDataImport(new("", [new("FullName", ["Must not apply"]), new("Role", ["Not an option"])]));
        reject(() => session.ApplyFormDataImport(invalid), "invalid choice prevents the entire form import");
        check(ReferenceEquals(session.Document, before), "failed form import leaves source, values and history untouched");
        check(!session.PrepareFormDataImport(new("", [new("FullName", [new string('x', 81)])])).CanApply, "import enforces native maximum text length");
        check(!session.PrepareFormDataImport(new("", [new("FullName", ["Wrong type"], PdfFieldKind.CheckBox)])).CanApply, "JSON type mismatches fail closed");
        check(!session.PrepareFormDataImport(new("", [new("Role", ["Design", "Review"])])).CanApply, "multi-value import is rejected rather than silently truncated");
        session.Navigate(5);
        var name = session.Page.Fields.Single(field => field.Name == "FullName");
        session.UpdateField(name.Id, field => field with { ReadOnly = true });
        check(!session.PrepareFormDataImport(new("", [new("FullName", ["Cannot write"])])).CanApply, "import respects read-only logical fields");
        check(session.PrepareFormDataImport(new("", [new("FullName", [name.Value])])).CanApply, "unchanged read-only values can round-trip without being overwritten");
        var clear = session.PrepareFormDataImport(new("", [new("Approved", [])])); session.ApplyFormDataImport(clear);
        check(Value(session, "Approved") == "Off", "an empty button import selects Off, not an invalid empty state");
        var exported = PdfFormData.FromWorkspace(session.Document);
        check(exported.Fields.Length == 4, "form-data export emits one entry per logical field");
        foreach (var format in new[] { "xfdf", "json" })
        {
            var source = new PdfFormData("Unicode.pdf", [new("Applicant.Name", ["  Łódź & <test> \"quoted\"\r\n東京 😀  "], PdfFieldKind.Text), new("Values", ["A", "B"])]);
            var bytes = format == "xfdf" ? PdfFormDataCodec.WriteXfdf(source) : PdfFormDataCodec.WriteJson(source);
            var read = PdfFormDataCodec.Read(bytes, "values." + format);
            check(read.Fields[0].Name == source.Fields[0].Name && read.Fields[0].Values.SequenceEqual(source.Fields[0].Values) && read.Fields[1].Values.SequenceEqual(["A", "B"]), format + " preserves Unicode, whitespace, escaped text and multiple values in its data model");
        }
        PdfFormData Xfdf(string xml) => PdfFormDataCodec.Read(Encoding.UTF8.GetBytes(xml), "test.xfdf");
        const string start = "<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields>";
        const string end = "</fields></xfdf>";
        var nested = Xfdf(start + "<field name=\"Applicant\"><field name=\"Name\"><value>Curie</value></field></field>" + end);
        check(nested.Fields.Single().Name == "Applicant.Name", "nested XFDF names resolve to qualified AcroForm names");
        var link = Xfdf("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><f href=\"https://example.invalid/private.pdf\"/><fields><field name=\"FullName\"><value>Local</value></field></fields><annots/></xfdf>");
        check(link.Document == "" && link.Fields.Single().Values[0] == "Local", "XFDF document references and annotations are never followed by field-data import");
        reject(() => Xfdf("<!DOCTYPE xfdf [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>" + start + "<field name=\"A\"><value>&x;</value></field>" + end), "XFDF rejects DTDs and external entities");
        reject(() => Xfdf(start + "<field name=\"A\"><value-richtext>Unsupported</value-richtext></field>" + end), "rich-text XFDF is rejected rather than flattened silently");
        reject(() => Xfdf(start + "<field name=\"A\"><value>one</value><field name=\"B\"/></field>" + end), "XFDF rejects mixed terminal and hierarchical field values");
        reject(() => Xfdf(start + "<field name=\"A\"/><field name=\"A\"/>" + end), "duplicate XFDF field identities are rejected");
        reject(() => Xfdf("<xfdf><fields/></xfdf>"), "XFDF namespace is validated");
        reject(() => Xfdf(start + string.Concat(Enumerable.Repeat("<field name=\"A\">", 80)) + string.Concat(Enumerable.Repeat("</field>", 80)) + end), "XML depth is checked before building its object tree");
        reject(() => PdfFormDataCodec.Read(new byte[PdfFormData.MaximumBytes + 1], "large.json"), "form-data input has a 4 MB byte limit");
        reject(() => PdfFormDataCodec.Read(Encoding.UTF8.GetBytes("{}"), "wrong.pdf"), "form import does not accept a PDF as data");
        var legacy = PdfFormDataCodec.Read(Encoding.UTF8.GetBytes("{\"document\":\"old.pdf\",\"fields\":[{\"name\":\"FullName\",\"value\":\"Legacy\",\"type\":\"Text\"}]}"), "old.json");
        check(legacy.Fields.Single().Values.Single() == "Legacy", "0.2 JSON exports remain import-compatible");
        reject(() => PdfFormDataCodec.Read(Encoding.UTF8.GetBytes("{\"fields\":[],\"fields\":[]}"), "duplicate.json"), "duplicate JSON properties cannot shadow form values");
        reject(() => PdfFormDataCodec.Read(Encoding.UTF8.GetBytes("{\"fields\":[{\"name\":\"A\",\"value\":\"one\",\"values\":[\"two\"]}]}"), "ambiguous.json"), "ambiguous singular and plural JSON values are rejected");
        var finalSession = new EditorSession(original); finalSession.ApplyFormDataImport(finalSession.PrepareFormDataImport(PdfFormDataCodec.Read(PdfFormDataCodec.WriteXfdf(data), "import.xfdf")));
        var native = PdfDocumentEngine.Save(finalSession.Document, SKTypeface.Default).Bytes;
        var reopened = new EditorSession(PdfDocumentEngine.Open(native, "imported.pdf"));
        check(Value(reopened, "FullName") == "Marie Curie" && Value(reopened, "Role") == "Engineering" && Value(reopened, "Approved") == "Yes", "imported XFDF values survive native PDF save and reopen");
        Directory.CreateDirectory("artifacts/structured"); File.WriteAllBytes("artifacts/structured/form-data-imported.pdf", native);
        File.WriteAllBytes("artifacts/structured/form-data.xfdf", PdfFormDataCodec.WriteXfdf(PdfFormData.FromWorkspace(finalSession.Document)));
        File.WriteAllBytes("artifacts/structured/form-data.json", PdfFormDataCodec.WriteJson(PdfFormData.FromWorkspace(finalSession.Document)));

        var radio1 = new PdfFormFieldState { Name = "Choice", GroupName = "Choice", Kind = PdfFieldKind.RadioButton, Bounds = new(10, 10, 20, 20), Value = "Off", DefaultValue = "Off", ExportValue = "One" };
        var radio2 = radio1 with { Id = Guid.NewGuid(), ExportValue = "Two", Bounds = new(40, 10, 20, 20) };
        var radios = new EditorSession(new PdfWorkspace { Pages = [new() { Fields = [radio1] }, new() { Fields = [radio2] }] });
        radios.ApplyFormDataImport(radios.PrepareFormDataImport(new("", [new("Choice", ["Two"])])));
        check(radios.Document.Pages.All(page => page.Fields[0].Value == "Two") && PdfFormData.FromWorkspace(radios.Document).Fields.Length == 1, "form import synchronizes radio widgets across pages and exports one logical value");
        var guarded = radios.Document with { Pages = [radios.Document.Pages[0] with { Fields = [radios.Document.Pages[0].Fields[0] with { ReadOnly = true }] }, radios.Document.Pages[1]] };
        check(!new EditorSession(guarded).PrepareFormDataImport(new("", [new("Choice", ["One"])])).CanApply, "one read-only sibling prevents inconsistent radio-group import");
    }
    private static string Value(EditorSession session, string name) => session.Document.Pages.SelectMany(page => page.Fields).First(field => field.GroupName == name).Value;
}
