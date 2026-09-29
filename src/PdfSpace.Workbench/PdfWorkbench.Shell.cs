namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private Grid _root = null!, _body = null!, _documentHost = null!;
    private readonly StackPanel _tabs = PdfTheme.Row(3);
    private PdfToolPanel _leftPanel = null!;
    private readonly ContentControl _rightHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl _organizerHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl _homeHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, Border> _modeTabs = [];
    private readonly List<(PdfTool Tool, PdfCommandButton Button)> _toolButtons = [];
    private Border _quickTools = null!, _selectionBar = null!;
    private PdfTextField _find = null!, _pageField = null!;
    private TextBlock _pageTotal = null!, _zoomLabel = null!, _status = null!, _documentInfo = null!;
    private PdfCommandButton _undo = null!, _redo = null!, _menuButton = null!;
    private void BuildShell()
    {
        _root = new Grid { Background = PdfTheme.Brush("#FFFFFF"), RowDefinitions = { new() { Height = new GridLength(46) }, new() { Height = new GridLength(52) }, new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = new GridLength(26) } } };
        Content = _root;
        var title = new Grid { Background = PdfTheme.Brush("#EFEFEF"), ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        var brand = PdfTheme.Row(4); brand.Margin = new Thickness(13, 0, 10, 0);
        brand.Children.Add(new PdfIcon { Kind = PdfIconKind.File, Color = 0xFFD93830, Width = 25, Height = 25 });
        brand.Children.Add(PdfTheme.Text("PdfSpace", 16, "#383838", true));
        _menuButton = new("Menu", PdfIconKind.Menu, ShowFileMenu) { Margin = new Thickness(10, 0, 0, 0) }; brand.Children.Add(_menuButton);
        brand.Children.Add(new PdfCommandButton("Home", PdfIconKind.Home, ShowHome, true));
        PdfTheme.Place(title, brand);
        _tabs.Margin = new Thickness(0, 8, 0, 0); _tabs.VerticalAlignment = VerticalAlignment.Bottom;
        PdfTheme.Place(title, new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, column: 1);
        var titleActions = PdfTheme.Row(7); titleActions.Margin = new Thickness(7, 0, 14, 0);
        titleActions.Children.Add(new PdfCommandButton("Open PDF", PdfIconKind.Plus, () => Run(() => OpenAsync()), true));
        titleActions.Children.Add(new PdfCommandButton("Help and shortcuts", PdfIconKind.Help, () => Run(ShowHelpAsync), true));
        titleActions.Children.Add(new Border { Width = 29, Height = 29, CornerRadius = new CornerRadius(15), Background = PdfTheme.Brush("#DCD9F0"), Child = new TextBlock { Text = "P", FontFamily = PdfTheme.Font, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = PdfTheme.Brush("#62518B") } });
        PdfTheme.Place(title, titleActions, column: 2); PdfTheme.Place(_root, title);
        BuildGlobalBar();
        _body = new Grid { ColumnDefinitions = { new() { Width = new GridLength(256) }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(0) }, new() { Width = new GridLength(54) } } };
        _leftPanel = new("All tools"); _leftPanel.CloseRequested += () => { _leftOpen = false; AdaptLayout(); }; PdfTheme.Place(_body, _leftPanel);
        var center = new Grid(); _documentHost = new Grid(); center.Children.Add(_documentHost); center.Children.Add(_organizerHost);
        BuildQuickTools(); center.Children.Add(_quickTools); BuildSelectionBar(); center.Children.Add(_selectionBar);
        PdfTheme.Place(_body, center, column: 1); PdfTheme.Place(_body, _rightHost, column: 2); PdfTheme.Place(_body, BuildNavigationRail(), column: 3); PdfTheme.Place(_root, _body, row: 2);
        PdfTheme.Place(_root, _homeHost, row: 2);
        var footer = new Grid { Background = PdfTheme.Brush("#F7F7F7"), Padding = new Thickness(14, 0, 14, 0), ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _status = PdfTheme.Text("All files stay on your device.", 11, "#686868"); _documentInfo = PdfTheme.Text("", 10, "#858585");
        PdfTheme.Place(footer, _status); PdfTheme.Place(footer, _documentInfo, column: 1); PdfTheme.Place(_root, new Border { Child = footer, BorderBrush = PdfTheme.Brush("#DDDDDD"), BorderThickness = new Thickness(0, 1, 0, 0) }, row: 3);
        PdfTheme.Place(_root, _dialogs, rowSpan: 4);
    }
    private void BuildGlobalBar()
    {
        var bar = new Grid { BorderBrush = PdfTheme.Brush("#DDDDDD"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 0, 14, 0), ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        var modes = PdfTheme.Row(5);
        foreach (var (name, icon) in new[] { ("All tools", PdfIconKind.Grid), ("Edit", PdfIconKind.Edit), ("Convert", PdfIconKind.Export), ("E-Sign", PdfIconKind.Sign) })
        {
            var button = new PdfCommandButton(name, action: () => { if (name == "All tools" && _mode == name && _leftOpen) { _leftOpen = false; AdaptLayout(); } else SetMode(name); }) { Height = 49, Padding = new Thickness(14, 4, 14, 4), CornerRadius = new CornerRadius(0) };
            var border = new Border { Child = button, BorderThickness = new Thickness(0, 0, 0, 3), BorderBrush = PdfTheme.Brush(name == "All tools" ? "#1473E6" : "#00FFFFFF") }; _modeTabs.Add(name, border); modes.Children.Add(border);
        }
        PdfTheme.Place(bar, modes);
        var actions = PdfTheme.Row(4); _find = new("Find in document", "Find text in document") { Width = 180, Height = 32, Margin = new Thickness(10, 0, 10, 0) };
        _find.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Run(() => SearchAsync(_find.Text)); e.Handled = true; } };
        actions.Children.Add(_find);
        _undo = new("Undo", PdfIconKind.Undo, () => Safe(() => Session.Undo()), true); _redo = new("Redo", PdfIconKind.Redo, () => Safe(() => Session.Redo()), true);
        actions.Children.Add(_undo); actions.Children.Add(_redo); actions.Children.Add(PdfTheme.Divider(true));
        actions.Children.Add(new PdfCommandButton("Save editable workspace", PdfIconKind.Save, () => Run(SaveWorkspaceAsync), true));
        actions.Children.Add(new PdfCommandButton("Print PDF", PdfIconKind.Print, () => Run(PrintAsync), true));
        var export = new PdfCommandButton("Export PDF", PdfIconKind.Share, () => Run(() => ExportPdfAsync())) { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(15, 6, 15, 6) }; export.Primary(); actions.Children.Add(export);
        PdfTheme.Place(bar, actions, column: 2); PdfTheme.Place(_root, bar, row: 1);
    }
    private UIElement BuildNavigationRail()
    {
        var grid = new Grid { Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#D8D8D8"), BorderThickness = new Thickness(1, 0, 0, 0), RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } } };
        var top = PdfTheme.Column(5); top.Margin = new Thickness(8, 13, 8, 0);
        foreach (var (name, icon) in new[] { ("Comments", PdfIconKind.Comment), ("Bookmarks", PdfIconKind.Bookmark), ("Page thumbnails", PdfIconKind.Pages), ("Properties", PdfIconKind.Info) })
            top.Children.Add(new PdfCommandButton(name, icon, () => OpenRight(name), true) { Height = 35 });
        PdfTheme.Place(grid, top);
        var bottom = PdfTheme.Column(3); bottom.Margin = new Thickness(7, 0, 7, 10);
        _pageField = new("Page number") { Width = 37, TextAlignment = TextAlignment.Center, Padding = new Thickness(2), Height = 30, MinHeight = 30, FontSize = 12 };
        _pageField.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Safe(NavigatePageLabel); e.Handled = true; } };
        ToolTipService.SetToolTip(_pageField, "Exact page label or #physical-page. Duplicate labels require #N.");
        bottom.Children.Add(_pageField); _pageTotal = PdfTheme.Text("/ 6", 11, "#686868"); _pageTotal.HorizontalAlignment = HorizontalAlignment.Center; bottom.Children.Add(_pageTotal);
        bottom.Children.Add(new PdfCommandButton("Previous page", PdfIconKind.Up, () => Viewport.Navigate(Session.CurrentPage - 1), true));
        bottom.Children.Add(new PdfCommandButton("Next page", PdfIconKind.Down, () => Viewport.Navigate(Session.CurrentPage + 1), true));
        bottom.Children.Add(PdfTheme.Divider());
        bottom.Children.Add(new PdfCommandButton("Rotate clockwise", PdfIconKind.Rotate, () => Safe(() => Session.RotatePage()), true));
        bottom.Children.Add(new PdfCommandButton("Fit page", PdfIconKind.FitPage, () => Viewport.FitPage(), true));
        bottom.Children.Add(new PdfCommandButton("Zoom in", PdfIconKind.Plus, () => Viewport.ZoomTo(Viewport.Zoom * 1.2), true));
        bottom.Children.Add(new PdfCommandButton("Zoom out", PdfIconKind.ZoomOut, () => Viewport.ZoomTo(Viewport.Zoom / 1.2), true));
        _zoomLabel = PdfTheme.Text("88%", 10); _zoomLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var zoom = new PdfCommandButton("Set zoom", action: () => Run(SetZoomAsync)) { Content = _zoomLabel, Width = 38, MinWidth = 0, Height = 24, Padding = new Thickness(0) }; bottom.Children.Add(zoom);
        PdfTheme.Place(grid, bottom, row: 2); return grid;
    }
    private void BuildQuickTools()
    {
        var column = PdfTheme.Column(3); column.Margin = new Thickness(5);
        foreach (var (tool, name, icon) in new[] { (PdfTool.Select, "Select tool", PdfIconKind.Select), (PdfTool.Hand, "Hand tool", PdfIconKind.Hand), (PdfTool.Highlight, "Highlight text", PdfIconKind.Highlight), (PdfTool.Note, "Add a comment", PdfIconKind.Comment), (PdfTool.Ink, "Draw freehand", PdfIconKind.Pen), (PdfTool.Text, "Add text", PdfIconKind.Text), (PdfTool.Signature, "Draw signature", PdfIconKind.Sign) })
        { var button = new PdfCommandButton(name, icon, () => UseTool(tool), true) { Width = 35, Height = 36 }; column.Children.Add(button); _toolButtons.Add((tool, button)); }
        column.Children.Add(PdfTheme.Divider()); column.Children.Add(new PdfCommandButton("More drawing tools", PdfIconKind.More, () => SetMode("Edit"), true));
        _quickTools = new Border { Child = column, Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#D4D4D4"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(15, 22, 0, 0) };
    }
    private void BuildSelectionBar()
    {
        var row = PdfTheme.Row(6); row.Margin = new Thickness(8, 5, 8, 5);
        var palette = new PdfColorPalette(); palette.ColorChanged += color => { Session.Color = color; if (Session.SelectedAnnotation is { } annotation) Safe(() => Session.UpdateAnnotation(annotation.Id, a => a with { Color = color }, "Change annotation color")); };
        row.Children.Add(palette); row.Children.Add(PdfTheme.Divider(true));
        row.Children.Add(new PdfCommandButton("Edit selected annotation", PdfIconKind.Edit, () => Run(EditSelectedAsync), true));
        row.Children.Add(new PdfCommandButton("Annotation properties", PdfIconKind.Settings, () => OpenRight("Properties"), true));
        row.Children.Add(new PdfCommandButton("Delete annotation", PdfIconKind.Trash, () => Safe(() => Session.DeleteSelection()), true));
        _selectionBar = new Border { Child = row, Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#CCCCCC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 18), Visibility = Visibility.Collapsed };
    }
    private void AdaptLayout()
    {
        if (_body is null) return;
        var width = ActualWidth;
        _body.ColumnDefinitions[0].Width = new GridLength(_leftOpen && width >= 900 ? 256 : _leftOpen && width >= 650 ? 215 : 0);
        _body.ColumnDefinitions[2].Width = new GridLength(_right.Length > 0 ? (width >= 900 ? 278 : width >= 650 ? 235 : Math.Max(0, width - 200)) : 0);
        _find.Visibility = width >= 1100 ? Visibility.Visible : Visibility.Collapsed;
        _documentInfo.Visibility = width >= 850 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var context in _documents) context.Tab.Width = width >= 1000 ? 222 : 155;
    }
}
