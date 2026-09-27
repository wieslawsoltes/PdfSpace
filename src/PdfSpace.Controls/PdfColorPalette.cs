namespace PdfSpace.Controls;
public sealed class PdfColorPalette : UserControl
{
    public event Action<uint>? ColorChanged;
    public PdfColorPalette(uint selected = 0xFF1473E6)
    {
        var row = PdfTheme.Row(5); var colors = new[] { ("Blue", 0xFF1473E6u), ("Yellow", 0xFFFFCA28u), ("Red", 0xFFD93830u), ("Green", 0xFF29834Bu), ("Purple", 0xFF9254CCu), ("Black", 0xFF242424u) };
        foreach (var (name, color) in colors)
        {
            var button = new PdfCommandButton(name + " annotation color", iconOnly: true) { Width = 28, Height = 28, Padding = new Thickness(4) };
            button.Content = new Border { Width = 19, Height = 19, CornerRadius = new CornerRadius(10), Background = PdfTheme.Brush("#" + color.ToString("X8")), BorderThickness = new Thickness(1), BorderBrush = PdfTheme.Brush("#22000000") };
            button.Click += (_, _) => { foreach (var child in row.Children.OfType<PdfCommandButton>()) child.Select(child == button); ColorChanged?.Invoke(color); };
            button.Select(selected == color); row.Children.Add(button);
        }
        Content = row;
    }
}
