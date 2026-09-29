using System.Globalization;

namespace PdfSpace.Workbench;
public sealed partial class PdfWorkbench
{
    private readonly WorkspaceSnapshotStamp _objectStamp = new();
    private Guid _objectPage;
    private PdfPageObject[] _pageObjects = [];
    private int[] _selectedObjects = [];
    private bool _refreshingObjects, _objectGroups, _applyingObjects;
    private long _objectIndexBuilds;
    private PdfObjectClipboard? _objectClipboard;
    private void ShowObjects()
    {
        _right = "Objects";
        UseTool(PdfTool.EditObject);
        RefreshObjects();
        RefreshRight();
        AdaptLayout();
        ShowStatus("Click or marquee-select native objects. Shift-click adds; drag moves; handles resize; arrows nudge. Ctrl/Cmd+C/V uses the private object clipboard.");
    }

    private void RefreshObjects()
    {
        if (_refreshingObjects || _active is null || Session.Tool is not (PdfTool.EditObject or PdfTool.ObjectCrop) || _objectStamp.Matches(Session.Document) && _objectPage == Session.Page.Id)
            return;
        ClearObjectList();
        _refreshingObjects = true;
        try
        {
            _objectStamp.Remember(Session.Document);
            _objectPage = Session.Page.Id;
            _selectedObjects = [];
            _pageObjects = PdfObjectEditor.Read(Session.Document, Session.CurrentPage, _objectGroups);
            if (_objectGroups)
                _pageObjects = _pageObjects.Where(o => o.ScopePath.Length == 0).ToArray();
            _objectIndexBuilds++;
            var targets = _pageObjects.Select((o, index) => new NativeObjectTarget(index, o.Kind.ToString(), o.Bounds, o.Editable, Points(o))).ToArray();
            Viewport.SetNativeObjects(targets);
        }
        catch (Exception ex)
        {
            _pageObjects = [];
            Viewport.SetNativeObjects([]);
            ShowStatus(ex.Message, true);
        }
        finally
        {
            _refreshingObjects = false;
        }

        static NativeObjectPoint[] Points(PdfPageObject item)
        {
            var points = new List<NativeObjectPoint>();
            for (var i = 0; i < item.Nodes.Length; i++)
                for (var j = 0; j + 1 < item.Nodes[i].Values.Length; j += 2)
                {
                    var values = item.Nodes[i].Values;
                    var p = new PointD(values[j], values[j + 1]);
                    if (item.Nodes[i].Operator == "re" && j == 2)
                        p += new PointD(values[0], values[1]);
                    points.Add(new(i, j, item.LocalToPage.Transform(p)));
                }

            return points.ToArray();
        }
    }

    private void HookObjects(DocumentContext context)
    {
        context.Viewport.NativeObjectsSelectionChanged += indices =>
        {
            if (_active != context)
                return;
            _selectedObjects = indices;
            if (indices.Length > 0) RevealObjectInList(indices[0]);
            _right = "Objects";
            RefreshRight();
            AdaptLayout();
        };
        context.Viewport.NativeObjectsRotated += (indices, center, degrees) => Safe(() =>
        {
            if (_active != context) return;
            _selectedObjects = indices;
            ApplyObjects("Rotate objects", (document, objects) => PdfObjectEditor.Transform(document, objects,
                PdfAffineMatrix.Around(center, PdfAffineMatrix.Rotate(degrees))));
        });
        context.Viewport.NativeObjectsTransformed += (indices, old, bounds) => Safe(() =>
        {
            if (_active != context)
                return;
            _selectedObjects = indices;
            ApplyObjects("Transform objects", (document, objects) => old.Width < 1e-8 || old.Height < 1e-8 ? PdfObjectEditor.Transform(document, objects, PdfAffineMatrix.Translate(bounds.X - old.X, bounds.Y - old.Y)) : PdfObjectEditor.SetBounds(document, objects, bounds));
        });
        context.Viewport.NativeObjectPointChanged += (index, node, ordinate, point) => Safe(() =>
        {
            if (_active != context)
                return;
            _selectedObjects = [index];
            ApplyObjects("Edit vector point", (document, objects) =>
            {
                var target = objects.Single();
                var local = target.LocalToPage.Inverse().Transform(point);
                var nodes = target.Nodes.Select(n => new PdfPathNode(n.Operator, n.Values.ToArray())).ToArray();
                if (nodes[node].Operator == "re" && ordinate == 2)
                    local -= new PointD(nodes[node].Values[0], nodes[node].Values[1]);
                nodes[node].Values[ordinate] = local.X;
                nodes[node].Values[ordinate + 1] = local.Y;
                return PdfObjectEditor.SetPath(document, target, nodes);
            });
        });
        context.Viewport.NativeObjectsCropped += bounds => Safe(() =>
        {
            if (_active != context)
                return;
            ApplyObjects("Crop objects", (d, o) => PdfObjectEditor.Crop(d, o, bounds));
            UseTool(PdfTool.EditObject);
        });
        context.Viewport.NativeVectorCreated += (bounds, ellipse) => Safe(() =>
        {
            if (_active != context)
                return;
            PdfPathNode[] nodes;
            if (!ellipse)
                nodes = [new("re", [bounds.X, bounds.Y, bounds.Width, bounds.Height])];
            else
            {
                var x = bounds.Center.X;
                var y = bounds.Center.Y;
                var rx = bounds.Width / 2;
                var ry = bounds.Height / 2;
                const double k = .5522847498307936;
                nodes = [new("m", [x + rx, y]), new("c", [x + rx, y + k * ry, x + k * rx, y + ry, x, y + ry]), new("c", [x - k * rx, y + ry, x - rx, y + k * ry, x - rx, y]), new("c", [x - rx, y - k * ry, x - k * rx, y - ry, x, y - ry]), new("c", [x + k * rx, y - ry, x + rx, y - k * ry, x + rx, y]), new("h", [])];
            }

            context.Session.Execute("Insert native vector", d => PdfObjectEditor.InsertPath(d, context.Session.CurrentPage, nodes));
            ShowObjects();
        });
        context.Viewport.NativeObjectCommand += command =>
        {
            if (_active == context)
                Run(() => ObjectCommandAsync(command));
        };
    }

    private PdfPageObject[] ObjectSelection()
    {
        if (!_objectStamp.Matches(Session.Document) || _objectPage != Session.Page.Id)
            throw new InvalidOperationException("Object selection belongs to an older document revision.");
        return _selectedObjects.Where(i => (uint)i < _pageObjects.Length).Select(i => _pageObjects[i]).ToArray();
    }

    private void ApplyObjects(string label, Func<PdfWorkspace, PdfPageObject[], PdfWorkspace> action)
    {
        var context = _active;
        context.Viewport.FinishText(true);
        var selected = ObjectSelection();
        var indices = _selectedObjects.ToArray();
        var count = _pageObjects.Length;
        if (selected.Length == 0)
            throw new InvalidOperationException("Select one or more native objects first.");
        var before = context.Session.Document;
        _applyingObjects = true;
        try { context.Session.Execute(label, d => action(d, selected)); }
        finally { _applyingObjects = false; }
        if (ReferenceEquals(before, context.Session.Document))
        {
            ShowStatus(label + ": no document change.");
            return;
        }
        RefreshObjects();
        if (_pageObjects.Length == count && label != "Arrange objects")
        {
            _selectedObjects = indices.Where(i => (uint)i < _pageObjects.Length).ToArray();
            Viewport.SelectNativeObjects(_selectedObjects, false);
        }

        RefreshRight();
        ShowStatus(label + " applied as one native PDF transaction. Undo restores the source; this is not redaction.");
    }

    private async Task ObjectCommandAsync(string command)
    {
        switch (command)
        {
            case "Select all objects":
                if (_pageObjects.Length > 1000)
                    throw new InvalidOperationException("Select at most 1,000 objects per edit.");
                Viewport.SelectNativeObjects(_pageObjects.Select((o, i) => (o, i)).Where(v => v.o.Editable).Select(v => v.i));
                return;
            case "Copy objects":
            case "Cut objects":
                _objectClipboard = PdfObjectEditor.Copy(Session.Document, ObjectSelection());
                if (command == "Cut objects")
                    ApplyObjects(command, PdfObjectEditor.Delete);
                ShowStatus("Native objects copied to the private application clipboard. Original PDF resources can be retained; this is not sanitization.");
                RefreshRight();
                return;
            case "Paste objects":
                if (_objectClipboard is null)
                    throw new InvalidOperationException("Copy native objects first.");
                var data = _objectClipboard;
                Session.Execute(command, d => PdfObjectEditor.Paste(d, Session.CurrentPage, data, new(data.Bounds.X + 24, data.Bounds.Y + 24)));
                ShowObjects();
                return;
            case "Duplicate objects":
                ApplyObjects(command, (d, o) => PdfObjectEditor.Duplicate(d, o, new(24, 24)));
                return;
            case "Delete objects":
                ApplyObjects(command, PdfObjectEditor.Delete);
                return;
            case "Group objects":
                ApplyObjects(command, PdfObjectEditor.Group);
                return;
            case "Ungroup objects":
                ApplyObjects(command, (d, o) => PdfObjectEditor.Ungroup(d, o.Single()));
                return;
            case "Edit selected text":
                var target = ObjectSelection().Single();
                var runs = PdfTextEditor.Read(Session.Document, Session.CurrentPage).Where(r => r.ScopePath == target.ScopePath && r.OperatorStart >= target.Start && r.OperatorEnd <= target.End).ToArray();
                if (runs.Length == 1)
                {
                    await ReplaceOriginalTextAsync(runs[0]);
                    ShowObjects();
                }
                else
                {
                    _originalRuns = runs;
                    _right = "Original text";
                    RefreshRight();
                    AdaptLayout();
                }

                return;
            case "Replace text block":
            case "Add native text":
                var context = _active;
                var snapshot = Session.Document;
                var page = Session.CurrentPage;
                var selectedText = command == "Replace text block" ? ObjectSelection().Single() : null;
                var text = await _dialogs.PromptAsync(command, "Explicit page-aligned text layout with the bundled font. This replaces the selected block, not a hidden overlay. Left-to-right glyphs; overflow is rejected.", selectedText?.Text ?? "New native text", true);
                if (text is null)
                    return;
                var sizeText = await _dialogs.PromptAsync("Object text size", "Enter a font size in PDF points.", "14");
                if (sizeText is null)
                    return;
                var size = double.Parse(sizeText, CultureInfo.InvariantCulture);
                var box = selectedText?.Bounds ?? new RectD(72, 120, 350, 160);
                var height = await _dialogs.PromptAsync("Object text box height", "Increase this height to allow wrapped replacement lines.", Math.Max(box.Height, 80).ToString("0.###", CultureInfo.InvariantCulture));
                if (height is null)
                    return;
                box = box with
                {
                    Height = double.Parse(height, CultureInfo.InvariantCulture)
                };
                if (_active != context || !ReferenceEquals(snapshot, Session.Document))
                    throw new InvalidOperationException("Document changed during text editing.");
                context.Session.Execute(command, d => selectedText is null ? PdfObjectEditor.InsertText(d, page, text, _typeface, size, box) : PdfObjectEditor.ReplaceTextBlock(d, selectedText, text, _typeface, size, box));
                ShowObjects();
                return;
            case "Replace selected image":
                var owner = _active;
                var before = Session.Document;
                var file = await _storage.OpenImageAsync();
                if (file is null)
                    return;
                if (_active != owner || !ReferenceEquals(before, Session.Document))
                    throw new InvalidOperationException("Document changed while choosing an image.");
                ApplyObjects(command, (d, o) =>
                {
                    var selected = o.Single();
                    var image = PdfImageEditor.Read(d, Session.CurrentPage).Single(i => i.ScopePath == selected.ScopePath && i.OperatorIndex == selected.Start);
                    return PdfImageEditor.Replace(d, image, file.Bytes);
                });
                return;
        }
    }

    private void BuildObjects(StackPanel content)
    {
        RefreshObjects();
        var context = _active;
        var snapshot = Session.Document;
        void Button(string name, Action action, PdfIconKind icon = PdfIconKind.None, bool enabled = true)
        {
            content.Children.Add(new PdfCommandButton(name, icon, () => Safe(() =>
            {
                if (_active != context || !ReferenceEquals(snapshot, Session.Document))
                    throw new InvalidOperationException("The inspector is stale.");
                action();
            })) { IsEnabled = enabled, HorizontalContentAlignment = HorizontalAlignment.Left });
        }

        void Command(string name, bool enabled = true) => Button(name, () => Run(() => ObjectCommandAsync(name)), enabled: enabled);
        Button("Open object editing example", () =>
        {
            AddDocument(ObjectEditingSample.Create());
            ShowObjects();
        }, PdfIconKind.File);
        Button(_objectGroups ? "Select individual objects" : "Select whole Form groups", () =>
        {
            _objectGroups = !_objectGroups;
            _objectStamp.Clear();
            ShowObjects();
        });
        content.Children.Add(Paragraph($"{_pageObjects.Length} native objects · {_selectedObjects.Length} selected", 11));
        AttachObjectList(content);
        var snap = new PdfCommandButton("Snap moving objects", action: () =>
        {
            Viewport.SnapNativeObjectMovement = !Viewport.SnapNativeObjectMovement;
            RefreshRight();
            ShowStatus(Viewport.SnapNativeObjectMovement
                ? "Moving objects snap to page/object edges and centers. Alt bypasses snapping; Shift preserves the movement axis."
                : "Object snapping disabled.");
        });
        snap.Select(Viewport.SnapNativeObjectMovement); content.Children.Add(snap);
        var resizeSnap = new PdfCommandButton("Snap resizing objects", action: () =>
        {
            Viewport.SnapNativeObjectResize = !Viewport.SnapNativeObjectResize;
            RefreshRight();
            ShowStatus("Resize snapping " + (Viewport.SnapNativeObjectResize ? "enabled. Shift retains proportions; Alt centers and bypasses snapping." : "disabled."));
        });
        resizeSnap.Select(Viewport.SnapNativeObjectResize); content.Children.Add(resizeSnap);
        var pointSnap = new PdfCommandButton("Snap vector points", action: () =>
        {
            Viewport.SnapNativeObjectPoints = !Viewport.SnapNativeObjectPoints;
            RefreshRight();
            ShowStatus("Vector-point snapping " + (Viewport.SnapNativeObjectPoints ? "enabled. Shift locks an axis; Alt bypasses snapping." : "disabled."));
        });
        pointSnap.Select(Viewport.SnapNativeObjectPoints); content.Children.Add(pointSnap);
        Command("Select all objects");
        Command("Paste objects", _objectClipboard is not null);
        Button("Draw native rectangle", () => UseTool(PdfTool.ObjectRectangle), PdfIconKind.Rectangle);
        Button("Draw native ellipse", () => UseTool(PdfTool.ObjectEllipse), PdfIconKind.Ellipse);
        Command("Add native text");
        content.Children.Add(Paragraph("Drag the round handle to rotate; Shift snaps to 15°. Enable move, resize or point snapping separately; Alt bypasses it. Shift constrains movement/proportions; Alt resizes/draws from center. Escape cancels.", 10));
        var selected = _selectedObjects.Where(i => (uint)i < _pageObjects.Length).Select(i => _pageObjects[i]).ToArray();
        if (selected.Length == 0)
            return;
        var enabled = selected.All(o => o.Editable);
        var bounds = PdfObjectEditor.SelectionBounds(selected);
        content.Children.Add(PdfTheme.Divider());
        content.Children.Add(Paragraph("SELECTION · PDF POINTS", 10));
        var grid = new Grid
        {
            ColumnSpacing = 6,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new(),
                new()
            },
            RowDefinitions =
            {
                new()
                {
                    Height = GridLength.Auto
                },
                new()
                {
                    Height = GridLength.Auto
                }
            }
        };
        PdfTextField Field(string name, double v) => new(name)
        {
            Text = v.ToString("0.###", CultureInfo.InvariantCulture)
        };
        var x = Field("Object X", bounds.X);
        var y = Field("Object Y", bounds.Y);
        var w = Field("Object width", bounds.Width);
        var h = Field("Object height", bounds.Height);
        PdfTheme.Place(grid, x);
        PdfTheme.Place(grid, y, column: 1);
        PdfTheme.Place(grid, w, row: 1);
        PdfTheme.Place(grid, h, row: 1, column: 1);
        content.Children.Add(grid);
        static double Number(PdfTextField f) => double.TryParse(f.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new ArgumentException("Enter a finite number.");
        Button("Apply object geometry", () => ApplyObjects("Resize objects", (d, o) => PdfObjectEditor.SetBounds(d, o, new(Number(x), Number(y), Number(w), Number(h)))), PdfIconKind.Check, enabled);
        var angle = Field("Object rotation degrees", 0);
        content.Children.Add(angle);
        Button("Apply object rotation", () =>
        {
            var degrees = Number(angle);
            if (Math.Abs(degrees) > 360000) throw new ArgumentOutOfRangeException(nameof(degrees));
            degrees = Math.IEEERemainder(degrees, 360);
            ApplyObjects("Rotate objects", (d, o) => Math.Abs(degrees) < 1e-7 ? d : PdfObjectEditor.Transform(d, o,
                PdfAffineMatrix.Around(PdfObjectEditor.SelectionBounds(o).Center, PdfAffineMatrix.Rotate(degrees))));
        }, PdfIconKind.Rotate, enabled);
        Button("Rotate objects right", () => ApplyObjects("Rotate objects", (d, o) => PdfObjectEditor.Transform(d, o, PdfAffineMatrix.Around(PdfObjectEditor.SelectionBounds(o).Center, PdfAffineMatrix.Rotate(90)))), PdfIconKind.Rotate, enabled);
        Button("Flip objects horizontally", () => ApplyObjects("Flip objects", (d, o) => PdfObjectEditor.Transform(d, o, PdfAffineMatrix.Around(PdfObjectEditor.SelectionBounds(o).Center, PdfAffineMatrix.Scale(-1, 1)))), PdfIconKind.Left, enabled);
        Button("Flip objects vertically", () => ApplyObjects("Flip objects", (d, o) => PdfObjectEditor.Transform(d, o, PdfAffineMatrix.Around(PdfObjectEditor.SelectionBounds(o).Center, PdfAffineMatrix.Scale(1, -1)))), PdfIconKind.Up, enabled);
        Button("Crop selected objects", () =>
        {
            UseTool(PdfTool.ObjectCrop);
            ShowStatus("Drag a crop rectangle. Clipping hides pixels; it is not secure redaction.");
        }, PdfIconKind.Crop, enabled);
        foreach (var command in new[]
        {
            "Copy objects",
            "Cut objects",
            "Duplicate objects",
            "Delete objects"
        }

        )
            Command(command, enabled);
        foreach (var order in Enum.GetValues<PdfObjectOrder>())
            Button("Send objects " + order.ToString().ToLowerInvariant(), () => ApplyObjects("Arrange objects", (d, o) => PdfObjectEditor.Arrange(d, o, order)), enabled: enabled);
        if (selected.Length > 1)
        {
            foreach (var alignment in Enum.GetValues<PdfObjectAlignment>())
                Button("Align objects " + alignment.ToString().ToLowerInvariant(), () => ApplyObjects("Align objects", (d, o) => PdfObjectEditor.Align(d, o, alignment)), enabled: enabled);
            if (selected.Length > 2)
            {
                Button("Distribute horizontally", () => ApplyObjects("Distribute objects", (d, o) => PdfObjectEditor.Distribute(d, o, true)));
                Button("Distribute vertically", () => ApplyObjects("Distribute objects", (d, o) => PdfObjectEditor.Distribute(d, o, false)));
            }

            Command("Group objects", enabled);
        }

        if (selected.Length == 1)
        {
            var item = selected[0];
            if (item.Kind == PdfPageObjectKind.Form)
                Command("Ungroup objects", enabled);
            if (item.Kind == PdfPageObjectKind.Image)
                Command("Replace selected image", enabled);
            if (item.Kind == PdfPageObjectKind.Text)
            {
                Command("Edit selected text", enabled);
                Command("Replace text block", enabled);
            }

            if (item.Kind == PdfPageObjectKind.Path)
            {
                content.Children.Add(PdfTheme.Divider());
                Button("Edit path points", () =>
                {
                    Viewport.EditObjectPoints = !Viewport.EditObjectPoints;
                    Viewport.Invalidate();
                });
                Button("Path fill blue", () => ApplyObjects("Change path fill", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(Fill: 0xFF1473E6, FillEnabled: true))));
                Button("Path fill red", () => ApplyObjects("Change path fill", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(Fill: 0xFFD93830, FillEnabled: true))));
                Button("Path stroke black", () => ApplyObjects("Change path stroke", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(Stroke: 0xFF242424, StrokeEnabled: true))));
                Button("Remove path fill", () => ApplyObjects("Remove path fill", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(FillEnabled: false))));
                Button("Use even-odd fill", () => ApplyObjects("Change fill rule", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(EvenOdd: true))));
                var stroke = Field("Path stroke width", 2);
                content.Children.Add(stroke);
                Button("Apply path stroke width", () => ApplyObjects("Change stroke width", (d, o) => PdfObjectEditor.SetAppearance(d, o, new(StrokeWidth: Number(stroke), StrokeEnabled: true))));
                content.Children.Add(Paragraph("PATH COMMANDS · SOURCE LOCAL COORDINATES", 10));
                foreach (var(node, index)in item.Nodes.Select((n, i) => (n, i)).Take(64))
                {
                    content.Children.Add(Paragraph($"{index + 1} · {node.Operator}", 11));
                    var fields = PdfTheme.Column(4);
                    var values = node.Values.Select((v, i) => Field($"Path node {index + 1} value {i + 1}", v)).ToArray();
                    foreach (var f in values)
                        fields.Children.Add(f);
                    content.Children.Add(fields);
                    if (values.Length > 0)
                        Button($"Apply path node {index + 1}", () => ApplyObjects("Edit path node", (d, o) =>
                        {
                            var nodes = o[0].Nodes.ToArray();
                            nodes[index] = new(node.Operator, values.Select(Number).ToArray());
                            return PdfObjectEditor.SetPath(d, o[0], nodes);
                        }));
                }
            }

            content.Children.Add(Paragraph(item.Limitation, 11));
        }
        BuildObjectPaintControls(content, selected);
    }
}
