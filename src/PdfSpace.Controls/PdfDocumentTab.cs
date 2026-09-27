namespace PdfSpace.Controls;

public sealed class PdfDocumentTab : UserControl
{
    private readonly Border _border;
    private readonly PdfCommandButton _title;
    public event Action? Activated;
    public event Action? CloseRequested;
    public PdfDocumentTab(string name)
    {
        Height = 38; Width = 222;
        var row = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _title = new(name, PdfIconKind.File, () => Activated?.Invoke()) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 0 };
        _title.SetIconColor(0xFFD93830);
        var close = new PdfCommandButton("Close " + name, PdfIconKind.Close, () => CloseRequested?.Invoke(), true) { Width = 26, Height = 28, Margin = new Thickness(0, 3, 5, 3) };
        PdfTheme.Place(row, _title); PdfTheme.Place(row, close, column: 1);
        _border = new() { Child = row, BorderThickness = new Thickness(1, 1, 1, 0), BorderBrush = PdfTheme.Brush("#D9D9D9"), CornerRadius = new CornerRadius(7, 7, 0, 0), Background = PdfTheme.Brush("#F4F4F4") }; Content = _border;
    }
    public void Update(string name, bool active, bool dirty) { _title.Label = (dirty ? "• " : "") + name; _border.Background = PdfTheme.Brush(active ? "#FFFFFF" : "#EDEDED"); }
}
