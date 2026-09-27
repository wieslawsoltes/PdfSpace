using PdfSharp.Pdf;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

internal static class NativeForms
{
    public static PdfFormFieldState? Read(PdfDictionary widget, SourceGeometry geometry, string key)
    {
        if (PdfObjects.Text(widget.Elements["/Subtype"]) != "Widget") return null;
        var type = PdfObjects.Text(PdfObjects.Inherited(widget, "/FT"));
        var flags = (int)PdfObjects.Number(PdfObjects.Inherited(widget, "/Ff"));
        var kind = type switch
        {
            "Tx" => PdfFieldKind.Text,
            "Btn" when (flags & (1 << 16)) != 0 => PdfFieldKind.Unsupported,
            "Btn" when (flags & (1 << 15)) != 0 => PdfFieldKind.RadioButton,
            "Btn" => PdfFieldKind.CheckBox,
            "Ch" when (flags & (1 << 21)) != 0 => PdfFieldKind.Unsupported,
            "Ch" when (flags & (1 << 17)) != 0 => PdfFieldKind.ComboBox,
            "Ch" => PdfFieldKind.ListBox,
            "Sig" => PdfFieldKind.Signature,
            _ => PdfFieldKind.Unsupported
        };
        var options = new List<PdfFieldOption>();
        if (PdfObjects.Array(PdfObjects.Inherited(widget, "/Opt")) is { } entries)
            foreach (var entry in entries.Elements)
            {
                var pair = PdfObjects.Array(entry);
                var value = pair is { Elements.Count: > 0 } ? PdfObjects.Text(pair.Elements[0]) : PdfObjects.Text(entry);
                var label = pair is { Elements.Count: > 1 } ? PdfObjects.Text(pair.Elements[1]) : value;
                options.Add(new(value, label));
            }
        var normal = PdfObjects.Dictionary(PdfObjects.Dictionary(widget.Elements["/AP"])?.Elements["/N"]);
        var export = normal?.Elements.Keys.FirstOrDefault(k => k != "/Off")?.TrimStart('/') ?? "Yes";
        var group = PdfObjects.FullName(widget); if (group.Length == 0) group = key;
        var valueText = PdfObjects.Text(PdfObjects.Inherited(widget, "/V"));
        if (kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && valueText.Length == 0) valueText = "Off";
        var defaultValue = PdfObjects.Text(PdfObjects.Inherited(widget, "/DV"));
        if (kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && defaultValue.Length == 0) defaultValue = "Off";
        return new()
        {
            SourceKey = key, Name = group, GroupName = group, Label = PdfObjects.Text(PdfObjects.Inherited(widget, "/TU")), Kind = kind,
            Bounds = geometry.ToLogical(PdfObjects.Rectangle(widget.Elements["/Rect"])), Value = valueText,
            DefaultValue = defaultValue, ExportValue = export,
            Options = options.ToArray(), ReadOnly = (flags & 1) != 0 || kind is PdfFieldKind.Unsupported or PdfFieldKind.Signature,
            Required = (flags & 2) != 0, Multiline = (flags & (1 << 12)) != 0,
            MaxLength = (int)PdfObjects.Number(PdfObjects.Inherited(widget, "/MaxLen"))
        };
    }
    public static void Write(PdfDocument document, PdfPage page, PdfFormFieldState state, SourceGeometry geometry, SKTypeface typeface, PdfDictionary? original)
    {
        if (!state.Modified && original is not null) return;
        if (!state.CanFill && original is not null) throw new InvalidOperationException($"Field {state.Name} is not editable.");
        if (state.Kind is PdfFieldKind.Unsupported or PdfFieldKind.Signature) throw new NotSupportedException("Unsupported form-field type.");
        var widget = original ?? new PdfDictionary(document);
        var terminal = original is null ? widget : PdfObjects.Terminal(widget);
        if (original is null)
        {
            document.Internals.AddObject(widget);
            widget.Elements.SetName("/Type", "/Annot"); widget.Elements.SetName("/Subtype", "/Widget");
            widget.Elements.SetString("/T", state.Name); widget.Elements.SetString("/TU", state.Label.Length > 0 ? state.Label : state.Name);
            widget.Elements.SetName("/FT", state.Kind switch { PdfFieldKind.Text => "/Tx", PdfFieldKind.CheckBox or PdfFieldKind.RadioButton => "/Btn", _ => "/Ch" });
            widget.Elements.SetInteger("/Ff", (state.ReadOnly ? 1 : 0) | (state.Required ? 2 : 0) | (state.Multiline ? 1 << 12 : 0) | (state.Kind == PdfFieldKind.RadioButton ? 1 << 15 : 0) | (state.Kind == PdfFieldKind.ComboBox ? 1 << 17 : 0));
            widget.Elements.SetInteger("/F", 4); widget.Elements.SetString("/NM", state.Id.ToString());
            widget.Elements["/P"] = page.Reference!;
            PdfObjects.ArrayValue(page, "/Annots").Elements.Add(widget.Reference!);
            var form = PdfObjects.DictionaryValue(document.Internals.Catalog, "/AcroForm");
            PdfObjects.ArrayValue(form, "/Fields").Elements.Add(widget.Reference!);
            var fonts = PdfObjects.DictionaryValue(PdfObjects.DictionaryValue(form, "/DR"), "/Font");
            if (!fonts.Elements.ContainsKey("/PdfSpaceHelv"))
            {
                var font = new PdfDictionary(document);
                font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica"); font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
                fonts.Elements["/PdfSpaceHelv"] = font;
            }
            widget.Elements.SetString("/DA", $"/PdfSpaceHelv {PdfObjects.F(state.FontSize)} Tf 0 g");
            form.Elements.SetBoolean("/NeedAppearances", false);
            if (state.MaxLength > 0) widget.Elements.SetInteger("/MaxLen", state.MaxLength);
            if (state.Options.Length > 0)
            {
                var options = new PdfArray(document);
                foreach (var option in state.Options)
                {
                    var pair = new PdfArray(document); pair.Elements.Add(new PdfString(option.Value)); pair.Elements.Add(new PdfString(option.Label)); options.Elements.Add(pair);
                }
                widget.Elements["/Opt"] = options;
            }
        }
        widget.Elements.SetRectangle("/Rect", geometry.ToPdf(state.Bounds));
        if (state.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
        {
            terminal.Elements.SetName("/V", "/" + state.Value);
            widget.Elements.SetName("/AS", state.IsChecked ? "/" + state.ExportValue : "/Off");
            var appearances = new PdfDictionary(document);
            appearances.Elements["/Off"] = NativeAppearance.ForField(document, state with { Value = "Off" }, geometry, typeface).Reference!;
            appearances.Elements["/" + state.ExportValue] = NativeAppearance.ForField(document, state with { Value = state.ExportValue }, geometry, typeface).Reference!;
            PdfObjects.DictionaryValue(widget, "/AP").Elements["/N"] = appearances;
        }
        else
        {
            terminal.Elements.SetString("/V", state.Value);
            if (state.Kind is PdfFieldKind.ListBox or PdfFieldKind.ComboBox)
            {
                var index = System.Array.FindIndex(state.Options, option => option.Value == state.Value);
                if (index >= 0) terminal.Elements["/I"] = PdfObjects.Numbers(document, index); else terminal.Elements.Remove("/I");
            }
            PdfObjects.DictionaryValue(widget, "/AP").Elements["/N"] = NativeAppearance.ForField(document, state, geometry, typeface).Reference!;
        }
    }
}
