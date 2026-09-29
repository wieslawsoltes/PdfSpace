using System.Globalization;

namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private const int ObjectListCapacity = 64;
    private StackPanel? _objectListRoot;
    private ScrollViewer? _objectListHost;
    private TextBlock? _objectListRange;
    private PdfCommandButton? _objectListPrevious, _objectListNext;
    private PdfCommandButton[] _objectListButtons = [];
    private IndexWindow _objectListWindow;
    private int _objectListBoundStart = -1;
    private long _objectListBuilds, _objectListRowsCreated, _objectListGeneration;

    // The cached subtree must not root a discarded inspector or its snapshot closures.
    private void DetachObjectList()
    {
        if (_objectListRoot?.Parent is Panel parent) parent.Children.Remove(_objectListRoot);
    }

    private void ClearObjectList()
    {
        DetachObjectList();
        _objectDiagnostics.Clear();
        if (_objectListHost is not null) _objectListHost.Content = null;
        _objectListRoot?.Children.Clear();
        _objectListRoot = null; _objectListHost = null; _objectListRange = null;
        _objectListPrevious = null; _objectListNext = null;
        _objectListButtons = []; _objectListWindow = default; _objectListBoundStart = -1;
        _objectListGeneration++;
    }

    private void AttachObjectList(StackPanel content)
    {
        if (_objectListRoot is null)
        {
            _objectListRoot = PdfTheme.Column(4);
            _objectListWindow = new(_pageObjects.Length,
                _selectedObjects.Length > 0 ? _selectedObjects[0] : 0, ObjectListCapacity);
            var generation = _objectListGeneration;
            var navigation = PdfTheme.Row(2);
            _objectListPrevious = new PdfCommandButton("Previous object range", action: () => { if (generation == _objectListGeneration) MoveObjectList(-1); }) { Label = "Previous" };
            _objectListNext = new PdfCommandButton("Next object range", action: () => { if (generation == _objectListGeneration) MoveObjectList(1); }) { Label = "Next" };
            navigation.Children.Add(_objectListPrevious); navigation.Children.Add(_objectListNext);
            navigation.Children.Add(new PdfCommandButton("Go to object", action: () => { if (generation == _objectListGeneration && !_disposed) Run(GoToObjectAsync); }) { Label = "Go to…" });
            _objectListRoot.Children.Add(navigation);
            _objectListRange = PdfTheme.Text("", 10, "#686868");
            _objectListRoot.Children.Add(_objectListRange);
            var rows = PdfTheme.Column(2);
            _objectListButtons = new PdfCommandButton[Math.Min(ObjectListCapacity, _pageObjects.Length)];
            for (var slot = 0; slot < _objectListButtons.Length; slot++)
            {
                var button = CreateObjectRow(slot, _objectListGeneration);
                _objectListButtons[slot] = button; rows.Children.Add(button);
            }
            _objectListHost = new ScrollViewer { Content = rows, MaxHeight = 170, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _objectListRoot.Children.Add(_objectListHost);
            _objectListBuilds++; _objectListRowsCreated += _objectListButtons.Length;
        }
        BindObjectList(); DetachObjectList(); content.Children.Add(_objectListRoot);
    }

    private void BindObjectList()
    {
        var changed = _objectListBoundStart != _objectListWindow.Start;
        for (var slot = 0; slot < _objectListButtons.Length; slot++)
        {
            var button = _objectListButtons[slot];
            var valid = _objectListWindow.TryGetIndex(slot, out var index);
            button.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = valid;
            if (!valid) continue;
            if (changed)
            {
                var item = _pageObjects[index];
                var name = $"Select object {index + 1}";
                AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, name);
                ToolTipService.SetToolTip(button, name);
                button.Label = $"{index + 1} · {item.Kind}" + (item.Text.Length > 0 ? " · " + item.Text[..Math.Min(24, item.Text.Length)] : "");
            }
            button.Select(Array.BinarySearch(_selectedObjects, index) >= 0);
        }
        if (_objectListPrevious is not null) _objectListPrevious.IsEnabled = _objectListWindow.HasPrevious;
        if (_objectListNext is not null) _objectListNext.IsEnabled = _objectListWindow.HasNext;
        if (_objectListRange is not null)
            _objectListRange.Text = _objectListWindow.TotalCount == 0 ? "No objects on this page" :
                $"Objects {_objectListWindow.Start + 1}–{_objectListWindow.Start + _objectListWindow.VisibleCount} of {_objectListWindow.TotalCount}";
        _objectListBoundStart = _objectListWindow.Start;
        if (changed) _objectListHost?.ChangeView(null, 0, null, true);
    }

    private void MoveObjectList(int pages)
    {
        if (_disposed || _right != "Objects" || !_objectStamp.Matches(Session.Document)) return;
        _objectListWindow = _objectListWindow.Move(pages); BindObjectList(); StateChanged?.Invoke();
    }

    private void RevealObjectInList(int index)
    {
        if (_objectListRoot is null || (uint)index >= (uint)_objectListWindow.TotalCount) return;
        _objectListWindow = _objectListWindow.Reveal(index);
    }

    private async Task GoToObjectAsync()
    {
        var context = _active; var generation = _objectListGeneration;
        var value = await _dialogs.PromptAsync("Go to object", $"Enter an object number from 1 to {_pageObjects.Length}.",
            (_objectListWindow.Start + 1).ToString(CultureInfo.InvariantCulture), acceptLabel: "Select object");
        if (value is null) return;
        if (_active != context || generation != _objectListGeneration || _right != "Objects")
            throw new InvalidOperationException("The object list changed while choosing an object.");
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1 || number > _pageObjects.Length)
            throw new ArgumentOutOfRangeException(nameof(value), "Enter an object number in the displayed range.");
        Viewport.SelectNativeObjects([number - 1]);
    }

    // Closures capture pool slot/generation only, not native descriptors or PDF snapshots.
    private PdfCommandButton CreateObjectRow(int slot, long generation) => new("Object row", action: () =>
    {
        if (generation == _objectListGeneration && !_disposed && _right == "Objects" &&
            _objectStamp.Matches(Session.Document) && _objectPage == Session.Page.Id &&
            _objectListWindow.TryGetIndex(slot, out var index)) Viewport.SelectNativeObjects([index]);
    }) { Height = 29, HorizontalContentAlignment = HorizontalAlignment.Left };
}
