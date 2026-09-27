namespace PdfSpace.Controls;

public sealed class PdfToolPanel : UserControl
{
    private readonly TextBlock _title;
    public StackPanel Items { get; } = new() { Spacing = 3, Margin = new Thickness(14, 5, 14, 20) };
    public event Action? CloseRequested;
    public string Title { get => _title.Text; set => _title.Text = value; }
    public PdfToolPanel(string title)
    {
        Background = PdfTheme.Brush("#FFFFFF"); HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid { RowDefinitions = { new() { Height = new GridLength(57) }, new() { Height = new GridLength(1, GridUnitType.Star) } } };
        var header = new Grid { Margin = new Thickness(21, 0, 12, 0), ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _title = PdfTheme.Text(title, 17, bold: true); PdfTheme.Place(header, _title);
        PdfTheme.Place(header, new PdfCommandButton("Close panel", PdfIconKind.Close, () => CloseRequested?.Invoke(), true), column: 1);
        PdfTheme.Place(root, header); PdfTheme.Place(root, new ScrollViewer { Content = Items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, row: 1);
        Content = new Border { Child = root, BorderBrush = PdfTheme.Brush("#DADADA"), BorderThickness = new Thickness(0, 0, 1, 0) };
    }
    public PdfCommandButton Add(string label, PdfIconKind icon, Action action, uint color = 0xFF363636)
    {
        var button = new PdfCommandButton(label, icon, action) { Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 6, 9, 6) }; button.SetIconColor(color); Items.Children.Add(button); return button;
    }
    public void Description(string text)
    { var label = PdfTheme.Text(text, 12, "#757575"); label.TextWrapping = TextWrapping.Wrap; label.Margin = new Thickness(8, 9, 8, 14); Items.Children.Add(label); }
    public void Heading(string text)
    { var label = PdfTheme.Text(text, 11, "#696969", true); label.Margin = new Thickness(8, 17, 8, 7); Items.Children.Add(label); }
}
