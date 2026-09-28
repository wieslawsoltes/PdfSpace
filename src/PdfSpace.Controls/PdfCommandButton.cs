namespace PdfSpace.Controls;

public sealed class PdfCommandButton : Button
{
    private readonly PdfIcon _icon;
    private readonly TextBlock _label;
    public string Label { get => _label.Text; set { _label.Text = value; _label.Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible; } }
    public bool IsSelected { get; private set; }
    public PdfCommandButton(string name, PdfIconKind icon = PdfIconKind.None, Action? action = null, bool iconOnly = false)
    {
        Style = (Style)PdfResources.Shared["CommandButtonStyle"];
        AutomationProperties.SetName(this, name); AutomationProperties.SetAutomationId(this, name);
        ToolTipService.SetToolTip(this, name);
        var row = PdfTheme.Row(9); _icon = new() { Kind = icon, Visibility = icon == PdfIconKind.None ? Visibility.Collapsed : Visibility.Visible };
        _label = PdfTheme.Text(iconOnly ? "" : name); _label.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        row.Children.Add(_icon); row.Children.Add(_label); Content = row;
        if (iconOnly) { Width = 34; Padding = new Thickness(7); }
        if (action is not null) Click += (_, _) => action();
    }
    public void Select(bool selected)
    {
        if (IsSelected == selected) return;
        IsSelected = selected; Background = PdfTheme.Brush(selected ? "#E7F0FF" : "#00FFFFFF");
        _icon.Color = selected ? 0xFF0865CB : 0xFF363636; _label.Foreground = PdfTheme.Brush(selected ? "#0865CB" : "#292929");
    }
    public void Primary()
    {
        Background = PdfTheme.Brush("#1473E6"); _label.Foreground = PdfTheme.Brush("#FFFFFF"); _icon.Color = 0xFFFFFFFF; CornerRadius = new CornerRadius(17);
    }
    public void SetIconColor(uint color) => _icon.Color = color;
}

public sealed class PdfTextField : TextBox
{
    public PdfTextField(string name, string placeholder = "")
    {
        Style = (Style)PdfResources.Shared["TextFieldStyle"];
        MinWidth = 0;
        FontFamily = PdfTheme.Font; PlaceholderText = placeholder;
        AutomationProperties.SetName(this, name); AutomationProperties.SetAutomationId(this, name);
    }
}
