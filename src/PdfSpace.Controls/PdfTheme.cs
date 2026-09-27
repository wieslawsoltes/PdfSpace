namespace PdfSpace.Controls;

public static class PdfTheme
{
    public static FontFamily Font { get; set; } = new("Arial");
    public static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#'); var color = Convert.ToUInt32(hex, 16);
        return new(Windows.UI.Color.FromArgb(hex.Length == 8 ? (byte)(color >> 24) : (byte)255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
    }
    public static TextBlock Text(string text, double size = 13, string color = "#2B2B2B", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontFamily = Font, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public static Border Divider(bool vertical = false) => new() { Background = Brush("#E4E4E4"), Width = vertical ? 1 : double.NaN, Height = vertical ? double.NaN : 1, Margin = vertical ? new Thickness(7, 7, 7, 7) : new Thickness(0, 8, 0, 8) };
    public static StackPanel Row(double spacing = 4) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel Column(double spacing = 8) => new() { Spacing = spacing };
    public static void Place(Grid grid, UIElement element, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    { Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetRowSpan(element, rowSpan); Grid.SetColumnSpan(element, columnSpan); grid.Children.Add(element); }
}
