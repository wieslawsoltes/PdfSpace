using System.Globalization;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private void BuildFieldProperties(StackPanel content)
    {
        var context = _active;
        if (context.Session.SelectedField is not { } field)
        {
            content.Children.Add(Paragraph("Select a field from Prepare form, then open its properties."));
            content.Children.Add(new PdfCommandButton("Show form fields", PdfIconKind.Grid, ShowFormFields));
            return;
        }
        if (field.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported)
        { content.Children.Add(Paragraph("This field type is preserved but cannot be edited.")); return; }
        content.Children.Add(PdfTheme.Text(field.Name, 15, bold: true));
        content.Children.Add(Paragraph(field.Kind + " · " + (field.SourceKey is null ? "New field" : "Imported PDF widget"), 11));
        content.Children.Add(Paragraph("Shared values and field properties apply to every widget in this logical field. Position applies only to this widget.", 11));
        PdfTextField Text(string name, string value)
        {
            content.Children.Add(Paragraph(name, 11));
            var input = new PdfTextField(name) { Text = value };
            content.Children.Add(input); return input;
        }
        var label = Text("Field tooltip", field.Label);
        var defaults = Text("Field default value", field.DefaultValue);
        defaults.AcceptsReturn = field.Multiline;
        defaults.TextWrapping = TextWrapping.Wrap;
        var required = field.Required; var readOnly = field.ReadOnly; var multiline = field.Multiline;
        var flags = PdfTheme.Column(4);
        PdfCommandButton Toggle(string name, bool initial, Action<bool> apply)
        {
            var selected = initial;
            var button = new PdfCommandButton(name, PdfIconKind.Check) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Select(selected); button.Click += (_, _) => { selected = !selected; button.Select(selected); apply(selected); };
            flags.Children.Add(button); return button;
        }
        Toggle("Required field", required, value => required = value);
        Toggle("Read-only field", readOnly, value => readOnly = value);
        if (field.Kind == PdfFieldKind.Text)
            Toggle("Multiline field", multiline, value => { multiline = value; defaults.AcceptsReturn = value; });
        content.Children.Add(flags);
        content.Children.Add(PdfTheme.Divider());
        var position = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(1, GridUnitType.Star) } }, RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } }, ColumnSpacing = 8, RowSpacing = 8 };
        PdfTextField Number(string name, double value, int row, int column)
        {
            var group = PdfTheme.Column(4); group.Children.Add(Paragraph(name, 11));
            var input = new PdfTextField(name) { Text = value.ToString("0.###", CultureInfo.InvariantCulture) };
            group.Children.Add(input); PdfTheme.Place(position, group, row, column); return input;
        }
        var x = Number("Field X", field.Bounds.X, 0, 0); var y = Number("Field Y", field.Bounds.Y, 0, 1);
        var width = Number("Field width", field.Bounds.Width, 1, 0); var height = Number("Field height", field.Bounds.Height, 1, 1);
        content.Children.Add(position);
        var font = Text("Field font size", field.FontSize.ToString(CultureInfo.InvariantCulture));
        var maxLength = field.Kind == PdfFieldKind.Text ? Text("Maximum characters", field.MaxLength.ToString(CultureInfo.InvariantCulture)) : null;
        PdfTextField? options = null;
        if (field.Kind is PdfFieldKind.ComboBox or PdfFieldKind.ListBox)
        {
            options = Text("Field choices", string.Join('\n', field.Options.Select(option => option.Value + "|" + option.Label)));
            options.AcceptsReturn = true; options.TextWrapping = TextWrapping.Wrap; options.MinHeight = 90;
            content.Children.Add(Paragraph("One export value|display label per line. Existing and default selections must remain in the list.", 11));
        }
        if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
            content.Children.Add(Paragraph("Default: Off or a button export value. This widget's export value is " + field.ExportValue + ".", 11));
        var apply = new PdfCommandButton("Apply field properties", PdfIconKind.Check, () => Safe(() =>
        {
            static double Read(PdfTextField input)
            {
                if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ArgumentException("Enter a finite number for " + AutomationProperties.GetName(input) + ".");
                return value;
            }
            var pageIndex = Array.FindIndex(context.Session.Document.Pages, page => page.Fields.Any(item => item.Id == field.Id));
            if (pageIndex < 0) throw new InvalidOperationException("The field has been removed.");
            var page = context.Session.Document.Pages[pageIndex];
            var bounds = new RectD(Read(x), Read(y), Read(width), Read(height));
            if (bounds.X < 0 || bounds.Y < 0 || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Right > page.Width || bounds.Bottom > page.Height) throw new ArgumentException("The field must fit inside its source page.");
            var fontSize = Read(font); if (fontSize is < 1 or > 1000) throw new ArgumentException("Font size must be between 1 and 1000 points.");
            var maximum = field.MaxLength;
            if (maxLength is not null && (!int.TryParse(maxLength.Text, NumberStyles.None, CultureInfo.InvariantCulture, out maximum) || maximum > 1000000)) throw new ArgumentException("Maximum characters must be between 0 and 1000000. Zero means no limit.");
            var choices = options is null ? field.Options : options.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line =>
            {
                var separator = line.IndexOf('|');
                return separator < 0 ? new PdfFieldOption(line, line) : new PdfFieldOption(line[..separator].Trim(), line[(separator + 1)..].Trim());
            }).ToArray();
            if (options is not null && (choices.Length == 0 || choices.Any(choice => choice.Value.Length == 0))) throw new ArgumentException("Enter at least one nonempty choice value.");
            context.Session.Navigate(pageIndex);
            context.Session.UpdateField(field.Id, current => current with { Bounds = bounds, Label = label.Text.Trim(), DefaultValue = defaults.Text, Required = required, ReadOnly = readOnly, Multiline = multiline, MaxLength = maximum, FontSize = fontSize, Options = choices });
            context.Session.SelectField(field.Id);
            RefreshRight(); ShowStatus("Field properties applied. Save PDF retains the field, defaults and appearance.");
        })) { HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Primary(); content.Children.Add(apply);
        content.Children.Add(new PdfCommandButton("Delete this field", PdfIconKind.Trash, () => Safe(() =>
        {
            var index = Array.FindIndex(context.Session.Document.Pages, page => page.Fields.Any(item => item.Id == field.Id));
            if (index < 0) return;
            context.Session.Navigate(index); context.Session.SelectField(field.Id); context.Session.DeleteField(); ShowFormFields();
        })));
        content.Children.Add(new PdfCommandButton("Back to form fields", PdfIconKind.Left, ShowFormFields));
    }
}
