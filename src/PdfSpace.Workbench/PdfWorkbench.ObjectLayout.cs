using System.Globalization;

namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private bool _objectLayoutExpanded;
    private int _objectLayoutReference = -1;

    private void BuildObjectLayoutControls(StackPanel content, PdfPageObject[] selected)
    {
        var owner = _active;
        var snapshot = Session.Document;
        var indices = _selectedObjects.ToArray();
        void Guard(Action action) => Safe(() =>
        {
            if (_active != owner || !ReferenceEquals(snapshot, Session.Document) || !indices.SequenceEqual(_selectedObjects))
                throw new InvalidOperationException("The layout selection changed. Use the current inspector.");
            action();
        });
        var toggle = new PdfCommandButton("Object layout", action: () => Guard(() =>
        {
            _objectLayoutExpanded = !_objectLayoutExpanded;
            RefreshRight();
        })) { HorizontalContentAlignment = HorizontalAlignment.Left };
        toggle.Label = (_objectLayoutExpanded ? "− " : "+ ") + "Object layout";
        content.Children.Add(PdfTheme.Divider());
        content.Children.Add(toggle);
        if (!_objectLayoutExpanded) return;
        var editable = selected.Length > 0 && selected.All(o => o.Editable);
        void Button(string name, Action action, bool available = true) => content.Children.Add(
            new PdfCommandButton(name, action: () => Guard(action))
            { IsEnabled = editable && available, HorizontalContentAlignment = HorizontalAlignment.Left });
        content.Children.Add(Paragraph("ALIGN TO VISIBLE PAGE", 10));
        foreach (var alignment in Enum.GetValues<PdfObjectAlignment>())
            Button("Align page " + alignment.ToString().ToLowerInvariant(), () => ApplyObjects("Align to page",
                (d, o) => PdfObjectEditor.AlignToPage(d, o, alignment)));
        if (selected.Length < 2) return;
        content.Children.Add(Paragraph("REFERENCE · SELECTED OBJECT NUMBER", 10));
        if (!indices.Contains(_objectLayoutReference)) _objectLayoutReference = indices[0];
        var reference = new PdfTextField("Layout reference object")
        { Text = (_objectLayoutReference + 1).ToString(CultureInfo.InvariantCulture) };
        content.Children.Add(reference);
        int ReferenceIndex()
        {
            if (int.TryParse(reference.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
            {
                var position = Array.IndexOf(indices, number - 1);
                if (position >= 0) { _objectLayoutReference = number - 1; return position; }
            }
            throw new ArgumentException("Enter the number of an object in the current selection. The reference remains fixed.");
        }
        foreach (var alignment in Enum.GetValues<PdfObjectAlignment>())
            Button("Align reference " + alignment.ToString().ToLowerInvariant(), () =>
            {
                var index = ReferenceIndex();
                ApplyObjects("Align to reference", (d, o) => PdfObjectEditor.AlignToObject(d, o, index, alignment));
            });
        foreach (var dimensions in Enum.GetValues<PdfObjectSizeMatch>())
            Button("Match object " + dimensions.ToString().ToLowerInvariant(), () =>
            {
                var index = ReferenceIndex();
                ApplyObjects("Match object size", (d, o) => PdfObjectEditor.MatchSize(d, o, index, dimensions));
            });
        Button("Equal horizontal gaps", () => ApplyObjects("Distribute object gaps", (d, o) => PdfObjectEditor.DistributeSpacing(d, o, true)), selected.Length > 2);
        Button("Equal vertical gaps", () => ApplyObjects("Distribute object gaps", (d, o) => PdfObjectEditor.DistributeSpacing(d, o, false)), selected.Length > 2);
        content.Children.Add(Paragraph("Page alignment honors crop/rotation. Reference alignment, gaps and size use source-space bounds. Size matching fixes each center; text scales, not reflows. Equal gaps retain outer objects and reject insufficient room.", 10));
    }
}
