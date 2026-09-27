using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using SkiaSharp;
using NativeReader = PdfSharp.Pdf.IO.PdfReader;

internal static class FormPersistenceTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var document = PdfDocumentEngine.CreateFormSample(SKTypeface.Default);
        var editor = new EditorSession(document); editor.Navigate(5);
        var field = editor.Page.Fields.Single(item => item.Name == "FullName");
        editor.UpdateField(field.Id, state => state with { Label = "Reviewer name", DefaultValue = "Review team", ReadOnly = true, Required = false, Multiline = true, FontSize = 17, MaxLength = 32, Bounds = new(55, 345, 460, 48) });
        var bytes = PdfDocumentEngine.Save(editor.Document, SKTypeface.Default).Bytes;
        var reopened = PdfDocumentEngine.Open(bytes, "properties.pdf");
        var saved = reopened.Pages[5].Fields.Single(item => item.Name == "FullName");
        check(saved.Label == "Reviewer name" && saved.DefaultValue == "Review team", "imported field tooltip and default value persist");
        check(saved.ReadOnly && !saved.Required && saved.Multiline, "imported field flags persist including read-only changes");
        check(saved.FontSize == 17 && saved.MaxLength == 32 && saved.Bounds == new RectD(55, 345, 460, 48), "font size, maximum length and widget geometry persist");
        var fill = new EditorSession(reopened); fill.Navigate(5);
        reject(() => fill.SetFieldValue(saved.Id, "Blocked"), "read-only field cannot be filled");
        fill.UpdateField(saved.Id, state => state with { ReadOnly = false }); fill.ResetForm();
        check(fill.Page.Fields.Single(item => item.Id == saved.Id).Value == "Review team", "reset restores the persisted text default");
        check(fill.Page.Fields.Single(item => item.Name == "Role").Value == "Design", "choice default survives original form generation");
        fill.MarkSaved(); fill.ResetForm(); check(!fill.IsDirty, "reset with unchanged defaults creates no history entry");
        reject(() => fill.UpdateField(saved.Id, state => state with { MaxLength = 3 }), "properties cannot truncate current or default values silently");
        reject(() => fill.UpdateField(saved.Id, state => state with { Name = "Different identity" }), "field properties preserve imported names and identity");

        var choice = fill.Page.Fields.Single(item => item.Name == "Role");
        fill.UpdateField(choice.Id, state => state with { DefaultValue = "Engineering", Options = [new("Design", "Design team"), new("Engineering", "Engineering team")], FontSize = 13 });
        var choiceCopy = PdfDocumentEngine.Open(PdfDocumentEngine.Save(fill.Document, SKTypeface.Default).Bytes, "choices.pdf");
        var choiceResult = choiceCopy.Pages[5].Fields.Single(item => item.Name == "Role");
        check(choiceResult.DefaultValue == "Engineering" && choiceResult.Options[1].Label == "Engineering team" && choiceResult.FontSize == 13, "choice labels, defaults and appearance size persist");
        fill.SelectField(saved.Id); fill.DeleteField();
        var deleted = PdfDocumentEngine.Open(PdfDocumentEngine.Save(fill.Document, SKTypeface.Default).Bytes, "deleted-field.pdf");
        check(deleted.FieldCount == 3 && deleted.Pages[5].Fields.All(item => item.Name != "FullName"), "imported widget is removed from native page annotations");
        using (var native = NativeReader.Open(new MemoryStream(PdfDocumentEngine.Save(fill.Document, SKTypeface.Default).Bytes), PdfDocumentOpenMode.Modify))
            check(GetArray(GetDictionary(native.Internals.Catalog.Elements["/AcroForm"]).Elements["/Fields"]).Elements.Count == 3, "imported widget is removed from AcroForm field tree");
        fill.Undo(); check(fill.Document.FieldCount == 4, "imported field deletion is one undo transaction");

        var blank = new EditorSession(new PdfWorkspace());
        blank.AddField(new() { Name = "BlankName", GroupName = "BlankName", Bounds = new(50, 60, 200, 30), DefaultValue = "Draft" });
        check(PdfDocumentEngine.Open(PdfDocumentEngine.Save(blank.Document, SKTypeface.Default).Bytes, "blank-form.pdf").FieldCount == 1, "a new blank PDF can export a real AcroForm widget");

        var radio = new EditorSession(PdfDocumentEngine.Open(CreateRadioDocument(), "radio.pdf"));
        check(radio.Document.FieldCount == 2 && radio.Page.Fields.Count(item => item.IsChecked) == 1, "shared radio widgets import as one logical field");
        var second = radio.Page.Fields.Single(item => item.ExportValue == "Two");
        radio.SetFieldValue(second.Id, "Two");
        radio.UpdateField(second.Id, state => state with { DefaultValue = "Two", Required = true, Label = "Choose an option" });
        check(radio.Page.Fields.All(item => item.Value == "Two" && item.DefaultValue == "Two" && item.Required), "radio properties and values update the full group atomically");
        radio.SelectField(second.Id); radio.DeleteField();
        check(radio.Page.Fields.Length == 1 && radio.Page.Fields[0].Value == "Off" && radio.Page.Fields[0].DefaultValue == "Off", "deleting selected radio option clears its group value and default");
        var oneRadio = PdfDocumentEngine.Open(PdfDocumentEngine.Save(radio.Document, SKTypeface.Default).Bytes, "one-radio.pdf");
        check(oneRadio.FieldCount == 1 && oneRadio.Pages[0].Fields[0].GroupName == "Choice", "radio deletion retains its parent and sibling widget");
        radio.SelectField(radio.Page.Fields[0].Id); radio.DeleteField();
        using (var native = NativeReader.Open(new MemoryStream(PdfDocumentEngine.Save(radio.Document, SKTypeface.Default).Bytes), PdfDocumentOpenMode.Modify))
        {
            var form = GetDictionary(native.Internals.Catalog.Elements["/AcroForm"]);
            check(GetArray(form.Elements["/Fields"]).Elements.Count == 0 && GetArray(form.Elements["/CO"]).Elements.Count == 0, "last-widget deletion prunes empty parents and calculation references");
        }
        Directory.CreateDirectory("artifacts/structured"); File.WriteAllBytes("artifacts/structured/field-properties.pdf", bytes);
        File.WriteAllBytes("artifacts/structured/deleted-field.pdf", PdfDocumentEngine.Save(new EditorSession(deleted).Document, SKTypeface.Default).Bytes);
    }
    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;
    private static PdfDictionary GetDictionary(PdfItem? item) => (PdfDictionary)Resolve(item)!;
    private static PdfArray GetArray(PdfItem? item) => (PdfArray)Resolve(item)!;
    private static byte[] CreateRadioDocument()
    {
        using var pdf = new PdfDocument(); var page = pdf.AddPage();
        var parent = new PdfDictionary(pdf); pdf.Internals.AddObject(parent);
        parent.Elements.SetName("/FT", "/Btn"); parent.Elements.SetInteger("/Ff", 1 << 15); parent.Elements.SetString("/T", "Choice");
        parent.Elements.SetName("/V", "/One"); parent.Elements.SetName("/DV", "/One");
        var kids = new PdfArray(pdf); parent.Elements["/Kids"] = kids;
        var fields = new PdfArray(pdf); fields.Elements.Add(parent.Reference!);
        var order = new PdfArray(pdf); order.Elements.Add(parent.Reference!);
        var form = new PdfDictionary(pdf); form.Elements["/Fields"] = fields; form.Elements["/CO"] = order; pdf.Internals.Catalog.Elements["/AcroForm"] = form;
        var annotations = new PdfArray(pdf); page.Elements["/Annots"] = annotations;
        foreach (var (name, position) in new[] { ("One", 60), ("Two", 110) })
        {
            var widget = new PdfDictionary(pdf); pdf.Internals.AddObject(widget);
            widget.Elements.SetName("/Type", "/Annot"); widget.Elements.SetName("/Subtype", "/Widget"); widget.Elements["/Parent"] = parent.Reference!;
            var rect = new PdfArray(pdf); foreach (var number in new[] { position, 600, position + 20, 620 }) rect.Elements.Add(new PdfInteger(number)); widget.Elements["/Rect"] = rect;
            widget.Elements.SetName("/AS", name == "One" ? "/One" : "/Off"); widget.Elements["/P"] = page.Reference!;
            var appearance = new PdfDictionary(pdf); var normal = new PdfDictionary(pdf);
            foreach (var state in new[] { "Off", name })
            {
                var stream = new PdfDictionary(pdf); pdf.Internals.AddObject(stream);
                stream.Elements.SetName("/Type", "/XObject"); stream.Elements.SetName("/Subtype", "/Form");
                var box = new PdfArray(pdf); foreach (var number in new[] { 0, 0, 20, 20 }) box.Elements.Add(new PdfInteger(number)); stream.Elements["/BBox"] = box;
                stream.CreateStream(System.Text.Encoding.ASCII.GetBytes("q Q")); normal.Elements["/" + state] = stream.Reference!;
            }
            appearance.Elements["/N"] = normal; widget.Elements["/AP"] = appearance;
            kids.Elements.Add(widget.Reference!); annotations.Elements.Add(widget.Reference!);
        }
        using var output = new MemoryStream(); pdf.Save(output, false); return output.ToArray();
    }
}
