using System.Globalization;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private string _labelRange = "all";
    private PdfPageLabel _labelDraft = new();

    private void ShowPageLabels()
    {
        Viewport.FinishText(true); Viewport.CancelGesture();
        UseTool(PdfTool.Hand);
        _labelRange = "all"; _labelDraft = Session.Page.Label ?? new PdfPageLabel();
        _right = "Page labels"; RefreshRight(); AdaptLayout();
    }

    private void NavigatePageLabel()
    {
        var result = Viewport.PageLabels.Resolve(_pageField.Text, out var index);
        if (result == PageLabelMatch.Found) Viewport.Navigate(index);
        else ShowStatus(result == PageLabelMatch.Ambiguous
            ? "This label occurs on several pages. Enter # followed by its physical page number."
            : "Page label not found. Use its exact case, or # followed by a physical page number.", true);
    }

    private void BuildPageLabels(StackPanel content)
    {
        var owner = _active; var snapshot = Session.Document;
        var initial = _labelDraft;
        var style = initial.Style;
        content.Children.Add(Paragraph("NAVIGATION LABELS", 11));
        content.Children.Add(Paragraph("Label thumbnails and navigation without printing over the PDF. Printed page numbers are a separate Header and footer tool.", 11));
        var range = new PdfTextField("Label page range") { Text = _labelRange };
        var prefix = new PdfTextField("Label prefix") { Text = initial.Prefix };
        var start = new PdfTextField("Label starting number") { Text = initial.Number.ToString(CultureInfo.InvariantCulture) };
        content.Children.Add(Paragraph("Physical page range · all or one contiguous range", 10)); content.Children.Add(range);
        content.Children.Add(Paragraph("Prefix", 10)); content.Children.Add(prefix);
        content.Children.Add(Paragraph("Starting number (at least 1)", 10)); content.Children.Add(start);
        var feedback = Paragraph("", 11); content.Children.Add(feedback);
        (int First, int Count) Range()
        {
            var indices = PageRange.Parse(range.Text, snapshot.Pages.Length).Order().ToArray();
            if (indices.Length == 0 || indices[^1] - indices[0] + 1 != indices.Length)
                throw new ArgumentException("Choose a contiguous physical page range.");
            return (indices[0], indices.Length);
        }
        PdfPageLabel Value()
        {
            if (!int.TryParse(start.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new ArgumentException("Enter a positive integer starting number.");
            var value = new PdfPageLabel { Style = style, Prefix = prefix.Text, Number = number };
            value.Validate(); return value;
        }
        void Preview()
        {
            try
            {
                var (first, count) = Range(); var value = Value();
                var last = value with { Number = style == PdfPageLabelStyle.PrefixOnly ? value.Number : checked(value.Number + (count - 1)) };
                feedback.Text = $"Physical {first + 1}–{first + count}: {value.Format()} … {last.Format()}";
                _labelRange = range.Text; _labelDraft = value;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException)
            { feedback.Text = ex.Message; }
        }
        var buttons = new List<(PdfPageLabelStyle Style, PdfCommandButton Button)>();
        foreach (var entry in new[]
        {
            (PdfPageLabelStyle.Decimal, "Decimal labels"), (PdfPageLabelStyle.RomanLower, "Lowercase Roman labels"),
            (PdfPageLabelStyle.RomanUpper, "Uppercase Roman labels"), (PdfPageLabelStyle.LettersLower, "Lowercase letter labels"),
            (PdfPageLabelStyle.LettersUpper, "Uppercase letter labels"), (PdfPageLabelStyle.PrefixOnly, "Prefix only labels")
        })
        {
            var button = new PdfCommandButton(entry.Item2, action: () =>
            { style = entry.Item1; foreach (var pair in buttons) pair.Button.Select(pair.Style == style); Preview(); })
            { HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Select(style == entry.Item1); buttons.Add((entry.Item1, button)); content.Children.Add(button);
        }
        foreach (var field in new[] { range, prefix, start }) field.TextChanged += (_, _) => Preview();
        void Commit(string description, Func<PdfWorkspace, int, int, PdfWorkspace> operation) => Safe(() =>
        {
            if (_active != owner || !ReferenceEquals(snapshot, Session.Document))
                throw new InvalidOperationException("The document changed. Reopen its Page labels panel.");
            var (first, count) = Range(); var next = operation(snapshot, first, count);
            Session.Execute(description, _ => next);
            ShowStatus(ReferenceEquals(next, snapshot) ? "Page labels already match; no PDF or history change." : description + ". Source content is unchanged.");
        });
        content.Children.Add(PdfTheme.Divider());
        content.Children.Add(new PdfCommandButton("Apply page labels", action: () => Commit("Set page labels", (d, first, count) => PdfPageLabels.Apply(d, first, count, Value()))));
        content.Children.Add(new PdfCommandButton("Extend previous labels", action: () => Commit("Extend page labels", (d, first, count) => PdfPageLabels.Extend(d, first, count))));
        content.Children.Add(new PdfCommandButton("Reset page labels", action: () => Commit("Reset page labels", (d, first, count) => PdfPageLabels.Reset(d, first, count))));
        content.Children.Add(Paragraph("Labels follow the page when moved or extracted. Duplicate labels are allowed; #N always navigates to physical page N. Only the selected range changes. Alphabetic sequences are A…Z, AA…ZZ, AAA… per PDF rules.", 10));
        Preview();
    }
}
