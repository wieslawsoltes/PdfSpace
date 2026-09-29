namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private ScrollViewer? _objectListHost;
    private PdfCommandButton[] _objectListButtons = [];
    private long _objectListBuilds, _objectListRowsCreated, _objectListGeneration;

    // Detach before discarding a panel: a cached child's Parent would otherwise root
    // its old inspector, whose command closures retain the previous PDF snapshot.
    private void DetachObjectList()
    {
        if (_objectListHost?.Parent is Panel parent) parent.Children.Remove(_objectListHost);
    }

    private void ClearObjectList()
    {
        DetachObjectList();
        if (_objectListHost is not null) _objectListHost.Content = null;
        _objectListHost = null;
        _objectListButtons = [];
        _objectListGeneration++;
    }

    private void AttachObjectList(StackPanel content)
    {
        if (_objectListHost is null)
        {
            var rows = PdfTheme.Column(2);
            _objectListButtons = new PdfCommandButton[Math.Min(200, _pageObjects.Length)];
            for (var i = 0; i < _objectListButtons.Length; i++)
            {
                var item = _pageObjects[i];
                var label = $"{i + 1} · {item.Kind}" + (item.Text.Length > 0 ? " · " + item.Text[..Math.Min(24, item.Text.Length)] : "");
                var button = CreateObjectRow(i, label, _objectListGeneration);
                _objectListButtons[i] = button;
                rows.Children.Add(button);
            }
            _objectListHost = new ScrollViewer { Content = rows, MaxHeight = 170, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _objectListBuilds++;
            _objectListRowsCreated += _objectListButtons.Length;
        }
        // Selection is sorted; repeated rows skip brush updates in Select().
        for (var i = 0; i < _objectListButtons.Length; i++)
            _objectListButtons[i].Select(Array.BinarySearch(_selectedObjects, i) >= 0);
        DetachObjectList();
        content.Children.Add(_objectListHost);
    }

    // Separate closure scope deliberately avoids capturing PdfPageObject or PdfWorkspace.
    private PdfCommandButton CreateObjectRow(int index, string label, long generation)
    {
        return new PdfCommandButton($"Select object {index + 1}", action: () =>
        {
            if (generation == _objectListGeneration && !_disposed && _right == "Objects")
                Viewport.SelectNativeObjects([index]);
        }) { Label = label, Height = 29, HorizontalContentAlignment = HorizontalAlignment.Left };
    }
}
