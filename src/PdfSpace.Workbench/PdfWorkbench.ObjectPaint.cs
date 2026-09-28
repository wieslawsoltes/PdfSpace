using System.Globalization;

namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private bool _objectPaintExpanded;

    private void BuildObjectPaintControls(StackPanel content, PdfPageObject[] selected)
    {
        var owner = _active;
        var snapshot = Session.Document;
        var enabled = selected.All(o => o.Editable);
        var toggle = new PdfCommandButton("Object appearance", action: () =>
        {
            _objectPaintExpanded = !_objectPaintExpanded;
            RefreshRight();
        }) { HorizontalContentAlignment = HorizontalAlignment.Left };
        toggle.Label = (_objectPaintExpanded ? "− " : "+ ") + "Object appearance";
        content.Children.Add(PdfTheme.Divider());
        content.Children.Add(toggle);
        if (!_objectPaintExpanded) return;

        void Guard(Action action) => Safe(() =>
        {
            if (_active != owner || !ReferenceEquals(snapshot, Session.Document))
                throw new InvalidOperationException("The appearance inspector belongs to an older document.");
            action();
        });
        void Button(string name, Action action) => content.Children.Add(new PdfCommandButton(name, action: () => Guard(action))
            { IsEnabled = enabled, HorizontalContentAlignment = HorizontalAlignment.Left });
        double? Common(Func<PdfObjectPaintState, double?> get)
        {
            var value = get(selected[0].Paint);
            return selected.All(o => get(o.Paint) == value) ? value : null;
        }
        PdfTextField Field(string name, double? value) => new(name)
        { Text = value?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "", PlaceholderText = "Mixed / unchanged" };
        static double? Number(PdfTextField input) => string.IsNullOrWhiteSpace(input.Text) ? null :
            double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v :
            throw new ArgumentException("Enter a finite number or leave a mixed value empty.");
        void Pair(string title, PdfTextField left, PdfTextField right)
        {
            content.Children.Add(Paragraph(title, 10));
            var row = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new(), new() } };
            PdfTheme.Place(row, left); PdfTheme.Place(row, right, column: 1); content.Children.Add(row);
        }
        void Choice<T>(string name, T? current, Action<T> changed) where T : struct, Enum
        {
            var button = new PdfCommandButton(name) { IsEnabled = enabled, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Label = name + " · " + (current?.ToString() ?? "Mixed");
            button.Click += (_, _) => Guard(() =>
            {
                var options = PdfTheme.Column(2);
                var popup = new Flyout { Content = new ScrollViewer { Content = options, MaxHeight = 320 } };
                foreach (var value in Enum.GetValues<T>())
                {
                    var option = new PdfCommandButton(name + " " + value, action: () =>
                    { popup.Hide(); Guard(() => changed(value)); }) { HorizontalContentAlignment = HorizontalAlignment.Left };
                    option.Select(EqualityComparer<T?>.Default.Equals(current, value));
                    options.Children.Add(option);
                }
                popup.ShowAt(button);
            });
            content.Children.Add(button);
        }

        if (selected.All(o => o.Kind != PdfPageObjectKind.Form))
        {
            var fill = Field("Object fill opacity", Common(p => p.FillOpacity) * 100);
            var stroke = Field("Object stroke opacity", Common(p => p.StrokeOpacity) * 100);
            Pair("FILL / STROKE OPACITY · %", fill, stroke);
            Button("Apply object opacity", () => ApplyObjects("Change object opacity", (d, o) =>
                PdfObjectEditor.SetCompositing(d, o, new(Number(fill) / 100, Number(stroke) / 100))));
            var blend = selected[0].Paint.BlendMode;
            if (!selected.All(o => o.Paint.BlendMode == blend)) blend = null;
            Choice("Blend", blend, value => ApplyObjects("Change object blend", (d, o) =>
                PdfObjectEditor.SetCompositing(d, o, new(BlendMode: value))));
            content.Children.Add(Paragraph("Per-paint opacity, not group opacity. Native masks remain effective.", 10));
        }
        else content.Children.Add(Paragraph("Select individual objects to edit opacity. A Form group can contain its own transparency states.", 10));

        if (!selected.All(o => o.Kind == PdfPageObjectKind.Path)) return;
        var cap = selected[0].Paint.LineCap;
        if (!selected.All(o => o.Paint.LineCap == cap)) cap = null;
        Choice("Line cap", cap, value => ApplyObjects("Change line cap", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(LineCap: value))));
        var join = selected[0].Paint.LineJoin;
        if (!selected.All(o => o.Paint.LineJoin == join)) join = null;
        Choice("Line join", join, value => ApplyObjects("Change line join", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(LineJoin: value))));
        var miter = Field("Path miter limit", Common(p => p.MiterLimit));
        content.Children.Add(miter);
        Button("Apply miter limit", () => ApplyObjects("Change miter limit", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(MiterLimit: Number(miter)))));
        var dash = selected[0].Paint.Dash;
        if (!selected.All(o => o.Paint.Dash is { } v && dash is not null && v.Phase == dash.Phase && v.Lengths.SequenceEqual(dash.Lengths))) dash = null;
        var pattern = new PdfTextField("Path dash lengths")
        { Text = dash is null ? "" : string.Join(" ", dash.Lengths.Select(v => v.ToString("0.#####", CultureInfo.InvariantCulture))), PlaceholderText = "Empty = solid; e.g. 8 4" };
        var phase = Field("Path dash phase", dash?.Phase ?? 0);
        Pair("DASH LENGTHS / INTEGER PHASE · LOCAL UNITS", pattern, phase);
        Button("Apply path dash", () => ApplyObjects("Change path dash", (d, o) =>
        {
            var values = pattern.Text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length > 64) throw new ArgumentException("Use at most 64 dash lengths.");
            var lengths = values.Select(v => double.Parse(v, CultureInfo.InvariantCulture));
            return PdfObjectEditor.SetAppearance(d, o, new(Dash: new PdfDashPattern(lengths, Number(phase) ?? 0)));
        }));
    }
}
