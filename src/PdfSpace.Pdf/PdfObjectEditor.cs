using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using SkiaSharp;
using static PdfSpace.Pdf.PdfObjectScanner;

namespace PdfSpace.Pdf;
/// <summary>
/// Transactional native editing for painted text objects, paths, image placements and Form XObjects.
/// One batch opens/rewrites a source once and clones each changed Form invocation prefix once.
/// Descriptors are revalidated against native content; callers must obtain fresh selections after each edit.
/// </summary>
public static partial class PdfObjectEditor
{
    public static PdfPageObject[] Read(PdfWorkspace workspace, int pageIndex, bool includeContainers = false)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)pageIndex >= workspace.Pages.Length)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var page = workspace.Pages[pageIndex];
        if (page.SourceId is not { } id)
            return[];
        using var native = PdfDocumentEngine.OpenNative(workspace.Sources.First(s => s.Id == id).Bytes);
        return Scan(workspace, pageIndex, native).Items.Select(i => i.Object).Where(o => includeContainers || !o.IsContainer).ToArray();
    }

    public static PdfWorkspace Transform(PdfWorkspace workspace, IReadOnlyList<PdfObjectChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            if (!change.Transform.IsFinite)
                throw new ArgumentException("Transform must be finite.");
            _ = change.Transform.Inverse();
            ValidateBounds(Bounds(Corners(change.Target.Bounds, change.Transform)));
        }

        return Edit(workspace, changes.Select(c => c.Target).ToArray(), (native, items) =>
        {
            var matrices = changes.ToDictionary(c => Key(c.Target), c => c.Transform);
            ChangeScopes(native, items, (content, resources, item) =>
            {
                var delta = item.Object.LocalToPage.Inverse() * matrices[Key(item.Object)] * item.Object.LocalToPage;
                Replace(content, item, "q\n" + Matrix(delta), null, "Q\n", preserveState: true);
            });
        });
    }

    public static PdfWorkspace Transform(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfAffineMatrix transform) => Transform(workspace, objects.Select(o => new PdfObjectChange(o, transform)).ToArray());
    public static RectD SelectionBounds(IReadOnlyList<PdfPageObject> objects)
    {
        if (objects.Count == 0)
            throw new ArgumentException("Select at least one object.");
        return objects.Select(o => o.Bounds).Aggregate(RectD.Union);
    }

    public static PdfWorkspace SetBounds(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, RectD bounds)
    {
        ValidateBounds(bounds);
        var old = SelectionBounds(objects);
        if (old.Width <= 1e-8 || old.Height <= 1e-8)
            throw new NotSupportedException("Resize requires a two-dimensional selection. Use Move for a straight line.");
        return Transform(workspace, objects, PdfAffineMatrix.Translate(bounds.X, bounds.Y) * PdfAffineMatrix.Scale(bounds.Width / old.Width, bounds.Height / old.Height) * PdfAffineMatrix.Translate(-old.X, -old.Y));
    }

    public static PdfWorkspace Align(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfObjectAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (objects.Count < 2)
            throw new ArgumentException("Alignment needs at least two objects.");
        var all = SelectionBounds(objects);
        return Transform(workspace, objects.Select(o => new PdfObjectChange(o, alignment switch
        {
            PdfObjectAlignment.Left => PdfAffineMatrix.Translate(all.X - o.Bounds.X, 0),
            PdfObjectAlignment.Center => PdfAffineMatrix.Translate(all.Center.X - o.Bounds.Center.X, 0),
            PdfObjectAlignment.Right => PdfAffineMatrix.Translate(all.Right - o.Bounds.Right, 0),
            PdfObjectAlignment.Top => PdfAffineMatrix.Translate(0, all.Y - o.Bounds.Y),
            PdfObjectAlignment.Middle => PdfAffineMatrix.Translate(0, all.Center.Y - o.Bounds.Center.Y),
            _ => PdfAffineMatrix.Translate(0, all.Bottom - o.Bounds.Bottom)})).ToArray());
    }

    public static PdfWorkspace Distribute(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, bool horizontal)
    {
        if (objects.Count < 3)
            throw new ArgumentException("Distribution needs at least three objects.");
        var ordered = objects.OrderBy(o => horizontal ? o.Bounds.Center.X : o.Bounds.Center.Y).ToArray();
        var first = horizontal ? ordered[0].Bounds.Center.X : ordered[0].Bounds.Center.Y;
        var last = horizontal ? ordered[^1].Bounds.Center.X : ordered[^1].Bounds.Center.Y;
        return Transform(workspace, ordered.Select((o, i) => new PdfObjectChange(o, horizontal ? PdfAffineMatrix.Translate(first + (last - first) * i / (ordered.Length - 1) - o.Bounds.Center.X, 0) : PdfAffineMatrix.Translate(0, first + (last - first) * i / (ordered.Length - 1) - o.Bounds.Center.Y))).ToArray());
    }

    public static PdfWorkspace Delete(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects) => Edit(workspace, objects, (native, items) => ChangeScopes(native, items, (content, resources, item) => Replace(content, item, "", "", "", preserveState: true)));
    public static PdfWorkspace Crop(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, RectD pageCrop)
    {
        ValidateBounds(pageCrop);
        return Edit(workspace, objects, (native, items) => ChangeScopes(native, items, (content, resources, item) =>
        {
            var points = Corners(pageCrop, item.Object.LocalToPage.Inverse());
            var path = $"{F(points[0].X)} {F(points[0].Y)} m\n" + string.Concat(points.Skip(1).Select(p => $"{F(p.X)} {F(p.Y)} l\n")) + "h W n\n";
            Replace(content, item, "q\n" + path, null, "Q\n", preserveState: true);
        }));
    }

    public static PdfWorkspace Duplicate(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PointD offset) => Edit(workspace, objects, (native, items) => ChangeScopes(native, items, (content, resources, item) =>
    {
        var delta = PdfAffineMatrix.Translate(offset.X, offset.Y);
        ValidateBounds(Bounds(Corners(item.Object.Bounds, delta)));
        var form = Capture(native, item);
        var name = PdfContentGraph.AddResource(native, resources, "/XObject", form);
        var matrix = item.EndMatrix.Inverse() * delta * item.Object.LocalToPage;
        PdfContentGraph.Insert(content, item.Object.End + 1, "q\n" + Matrix(matrix) + name + " Do\nQ\n");
    }));
    /// <summary>Reorders within the same native scope and clipping context, preserving original graphics state.</summary>
    public static PdfWorkspace Arrange(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfObjectOrder order)
    {
        var changed = false;
        return Edit(workspace, objects, (native, selected, scanner) =>
    {
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));
        var scope = selected[0].Object.ScopePath;
        if (selected.Any(i => i.Object.ScopePath != scope || !ReferenceEquals(i.Clip, selected[0].Clip)))
            throw new NotSupportedException("Arrange objects within one native group and clipping context. Enter or select a whole Form group for nested content.");
        var siblings = scanner.Items.Where(i => i.Object.ScopePath == scope).OrderBy(i => i.Object.Start).ToArray();
        var chosen = selected.Select(i => i.Object.Start).ToHashSet();
        var backward = order is PdfObjectOrder.Back or PdfObjectOrder.Backward;
        var candidates = siblings.Where(i => !chosen.Contains(i.Object.Start) && (backward ? i.Object.End < selected.Min(s => s.Object.Start) : i.Object.Start > selected.Max(s => s.Object.End))).ToArray();
        if (candidates.Length == 0)
            return;
        // Do not silently jump across a different clip or unknown painting operator.
        var anchor = order switch
        {
            PdfObjectOrder.Back => candidates[0],
            PdfObjectOrder.Backward => candidates[^1],
            PdfObjectOrder.Forward => candidates[0],
            _ => candidates[^1]
        };
        var destinationClip = order == PdfObjectOrder.Front ? scanner.ScopeEnds[scope].Clip : order == PdfObjectOrder.Back ? scanner.ScopeStarts[scope].Clip : anchor.Clip;
        if (!ReferenceEquals(destinationClip, selected[0].Clip))
            throw new NotSupportedException("The destination has a different clipping context; moving there could hide content.");
        var at = order == PdfObjectOrder.Front ? selected[0].Content.Count : order == PdfObjectOrder.Back ? 0 : backward ? anchor.Object.Start : anchor.Object.End + 1;
        var matrixAt = order == PdfObjectOrder.Front ? scanner.ScopeEnds[scope].Matrix : order == PdfObjectOrder.Back ? scanner.ScopeStarts[scope].Matrix : backward ? anchor.Object.LocalToPage : anchor.EndMatrix;
        changed = true;
        var drawings = new StringBuilder();
        var resources = PdfContentGraph.Resources(native.Pages[selected[0].Object.SourcePage - 1]);
        PdfContentGraph.Edit(native, native.Pages[selected[0].Object.SourcePage - 1], scope, (content, local) =>
        {
            foreach (var item in selected.OrderBy(i => i.Object.Start))
            {
                var name = PdfContentGraph.AddResource(native, local, "/XObject", Capture(native, item));
                drawings.Append("q\n").Append(Matrix(matrixAt.Inverse() * item.Object.LocalToPage)).Append(name).Append(" Do\nQ\n");
            }

            // Rebuild by original indices: state-only remnants stay exactly where they were.
            var output = new CSequence();
            var starts = selected.ToDictionary(i => i.Object.Start);
            for (var i = 0; i <= content.Count; i++)
            {
                if (i == at)
                    Append(output, drawings.ToString());
                if (i == content.Count)
                    break;
                if (starts.TryGetValue(i, out var item))
                {
                    Append(output, StateOnly(item));
                    i = item.Object.End;
                }
                else
                    output.Add(content[i]);
            }

            content.Clear();
            foreach (var op in output)
                content.Add(op);
        });
    }, hasChanges: () => changed);
    }
    public static PdfWorkspace SetAppearance(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfObjectAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (appearance.StrokeWidth is { } w && (!double.IsFinite(w) || w is < 0 or > 1000))
            throw new ArgumentOutOfRangeException(nameof(appearance));
        return Edit(workspace, objects, (native, items) => ChangeScopes(native, items, (content, resources, item) =>
        {
            if (item.Object.Kind != PdfPageObjectKind.Path)
                throw new NotSupportedException("Fill/stroke editing applies to vector paths. Text and image edits retain their native appearance.");
            var original = (COperator)content[item.Object.End];
            var op = original.Name;
            var fill = appearance.FillEnabled ?? op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
            var stroke = appearance.StrokeEnabled ?? op is "S" or "s" or "B" or "B*" or "b" or "b*";
            var even = appearance.EvenOdd ?? op.EndsWith('*');
            var paint = fill ? (stroke ? "B" : "f") + (even ? "*" : "") : stroke ? "S" : "n";
            var prefix = new StringBuilder("q\n");
            if (appearance.Fill is { } fc)
                prefix.Append(Color(fc, false));
            if (appearance.Stroke is { } sc)
                prefix.Append(Color(sc, true));
            if (appearance.StrokeWidth is { } width)
                prefix.Append(F(width)).Append(" w\n");
            var body = new CSequence();
            foreach (var o in content.Skip(item.Object.Start).Take(item.Object.End - item.Object.Start))
                body.Add(o);
            if (op is "s" or "b" or "b*")
                Append(body, "h\n");
            Append(body, paint + "\n");
            Replace(content, item, prefix.ToString(), Encoding.Latin1.GetString(PdfContentGraph.Serialize(body)), "Q\n", false);
        }));
    }

    public static PdfWorkspace SetPath(PdfWorkspace workspace, PdfPageObject target, IReadOnlyList<PdfPathNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count is < 1 or > 10000 || nodes[0].Operator is not ("m" or "re"))
            throw new ArgumentException("A path starts with Move or Rectangle and contains at most 10,000 commands.");
        using var path = new SKPath();
        var commands = new StringBuilder();
        foreach (var node in nodes)
        {
            PdfObjectScanner.Append(path, node.Operator, node.Values, PdfAffineMatrix.Identity);
            commands.Append(node.Operator == "h" ? "h\n" : string.Join(" ", node.Values.Select(F)) + " " + node.Operator + "\n");
        }

        return Edit(workspace, [target], (native, items) => ChangeScopes(native, items, (content, resources, item) =>
        {
            if (item.Object.Kind != PdfPageObjectKind.Path)
                throw new ArgumentException("Select a vector path.");
            var end = ((COperator)content[item.Object.End]).Name;
            Replace(content, item, "", commands + end + "\n", "", false);
        }));
    }

    // Internal edit callbacks receive re-read objects, not client-supplied geometry/state.
    private static PdfWorkspace Edit(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, Action<PdfDocument, Item[]> action) => Edit(workspace, objects, (native, items, _) => action(native, items));
    private static PdfWorkspace Edit(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, Action<PdfDocument, Item[], PdfObjectScanner> action, bool commit = true, Func<bool>? hasChanges = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.Count is < 1 or > 1000)
            throw new ArgumentException("Select between 1 and 1,000 objects.");
        var first = objects[0];
        if (objects.Any(o => o.PageId != first.PageId || o.SourceId != first.SourceId || o.SourceHash != first.SourceHash || o.SourcePage != first.SourcePage))
            throw new ArgumentException("One edit batch belongs to one source page and snapshot.");
        if (objects.Select(Key).Distinct().Count() != objects.Count)
            throw new ArgumentException("Duplicate object selection.");
        foreach (var outer in objects)
            foreach (var inner in objects)
                if (!ReferenceEquals(outer, inner) && ((outer.ScopePath == inner.ScopePath && outer.Start <= inner.End && inner.Start <= outer.End) || inner.ScopePath == ChildKey(outer) || inner.ScopePath.StartsWith(ChildKey(outer) + "/", StringComparison.Ordinal)))
                    throw new ArgumentException("Select either a Form group or its contents, not overlapping parent and child occurrences.");
        var source = PdfContentGraph.ValidateTarget(workspace, first.SourceId, first.SourcePage, first.PageId, first.SourceHash);
        using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        var pageIndex = Array.FindIndex(workspace.Pages, p => p.Id == first.PageId);
        var scanner = Scan(workspace, pageIndex, native);
        var map = scanner.Items.ToDictionary(i => Key(i.Object));
        var actual = objects.Select(o =>
        {
            if (!map.TryGetValue(Key(o), out var item) || item.Object.Fingerprint != o.Fingerprint || item.Object.LocalToPage != o.LocalToPage || item.Object.Bounds != o.Bounds || item.Object.Kind != o.Kind)
                throw new InvalidOperationException("The object descriptor does not match its native occurrence.");
            if (!item.Object.Editable)
                throw new NotSupportedException(item.Object.Limitation);
            return item;
        }).ToArray();
        action(native, actual, scanner);
        return commit && (hasChanges?.Invoke() ?? true) ? PdfContentGraph.Commit(workspace, source, first.PageId, first.SourcePage, native) : workspace;
    }

    private static string Key(PdfPageObject o) => o.ScopePath + ":" + o.Start + ":" + o.End;
    private static string ChildKey(PdfPageObject o) => o.ScopePath.Length == 0 ? o.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) : o.ScopePath + "/" + o.Start;
    private static void ChangeScopes(PdfDocument native, Item[] items, Action<CSequence, PdfDictionary, Item> edit)
    {
        var edits = items.GroupBy(i => i.Object.ScopePath).ToDictionary(g => g.Key, g => (Action<CSequence, PdfDictionary>)((content, resources) =>
        {
            foreach (var item in g.OrderByDescending(i => i.Object.Start))
                edit(content, resources, item);
        }));
        PdfContentGraph.EditMany(native, native.Pages[items[0].Object.SourcePage - 1], edits);
    }

    private static void Replace(CSequence content, Item item, string before, string? body, string after, bool preserveState)
    {
        var original = new CSequence();
        foreach (var op in content.Skip(item.Object.Start).Take(item.Object.End - item.Object.Start + 1))
            original.Add(op);
        var result = new CSequence();
        Append(result, before);
        if (body is null)
            foreach (var op in original)
                result.Add(op);
        else
            Append(result, body);
        Append(result, after);
        if (preserveState && item.HasSideEffects)
            Append(result, StateOnly(item));
        for (var i = item.Object.End; i >= item.Object.Start; i--)
            content.RemoveAt(i);
        for (var i = 0; i < result.Count; i++)
            content.Insert(item.Object.Start + i, result[i]);
    }

    private static string StateOnly(Item item)
    {
        if (item.Object.Kind != PdfPageObjectKind.Text || !item.HasSideEffects)
            return "";
        var content = new CSequence();
        foreach (var op in item.Content.Skip(item.Object.Start).Take(item.Object.End - item.Object.Start + 1).OfType<COperator>())
        {
            if (op.Name is "Tj" or "TJ" or "'")
                continue;
            if (op.Name == "\"")
            {
                Append(content, $"{F(N(op.Operands[0]))} Tw {F(N(op.Operands[1]))} Tc\n");
                continue;
            }

            content.Add(op);
        }

        return Encoding.Latin1.GetString(PdfContentGraph.Serialize(content));
    }

    private static PdfDictionary Capture(PdfDocument native, Item item, bool includeClips = false)
    {
        var resources = PdfContentGraph.CopyDictionary(native, item.Resources);
        var body = new CSequence();
        var commands = new Stack<StateCommand>();
        var defaults = new PdfDictionary(native);
        defaults.Elements.SetReal("/CA", 1);
        defaults.Elements.SetReal("/ca", 1);
        defaults.Elements.SetName("/BM", "/Normal");
        defaults.Elements.SetName("/SMask", "/None");
        defaults.Elements.SetBoolean("/AIS", false);
        defaults.Elements.SetBoolean("/OP", false);
        defaults.Elements.SetBoolean("/op", false);
        defaults.Elements.SetInteger("/OPM", 0);
        defaults.Elements.SetBoolean("/TK", true);
        defaults.Elements.SetBoolean("/SA", false);
        foreach (var key in new[]
        {
            "/BG2",
            "/UCR2",
            "/TR2",
            "/HT"
        }

        )
            defaults.Elements.SetName(key, "/Default");
        var defaultName = PdfContentGraph.AddResource(native, resources, "/ExtGState", defaults);
        Append(body, "0 g 0 G 1 w 0 J 0 j 10 M [] 0 d /RelativeColorimetric ri 1 i 0 Tc 0 Tw 100 Tz 0 TL 0 Tr 0 Ts\n" + defaultName + " gs\n");
        for (var node = item.State; node is not null; node = node.Previous)
        {
            if (commands.Count >= 20000)
                throw new InvalidDataException("Excessive graphics-state replay.");
            commands.Push(node);
        }

        if (includeClips)
        {
            var clips = new Stack<Clip>();
            for (var clip = item.Clip; clip is not null; clip = clip.Previous)
                clips.Push(clip);
            foreach (var clip in clips)
            {
                if (!clip.Reproducible)
                    throw new NotSupportedException("This object inherits text or interleaved clipping that cannot be copied independently. Its original clipping remains preserved when moved in place.");
                var transform = item.Object.LocalToPage.Inverse() * clip.Matrix;
                Append(body, Matrix(transform) + clip.Operators + Matrix(transform.Inverse()));
            }
        }

        while (commands.TryPop(out var node))
        {
            // COperator.Clone is shallow: rewriting its operands would corrupt
            // the shared scanner state used to capture subsequent objects.
            var originalState = new CSequence { node.Operation };
            var op = (COperator)PdfContentGraph.Parse(originalState.ToContent())[0];
            var category = op.Name switch
            {
                "Tf" => "/Font",
                "gs" => "/ExtGState",
                "CS" or "cs" => "/ColorSpace",
                "SCN" or "scn" => "/Pattern",
                _ => null
            };
            if (category is not null)
                for (var i = 0; i < op.Operands.Count; i++)
                    if (op.Operands[i] is CName name && PdfObjects.Dictionary(node.Resources.Elements[category])?.Elements[name.Name] is { } value)
                    {
                        var entries = PdfContentGraph.CopyDictionary(native, PdfObjects.Dictionary(resources.Elements[category]));
                        resources.Elements[category] = entries;
                        var n = 1;
                        while (entries.Elements.ContainsKey("/ObjectState" + n))
                            n++;
                        var renamed = "/ObjectState" + n;
                        entries.Elements[renamed] = value;
                        op.Operands[i] = new CName(renamed);
                    }

            var atState = item.Object.LocalToPage.Inverse() * node.Matrix;
            if (op.Name == "gs")
                Append(body, Matrix(atState));
            body.Add(op);
            if (op.Name == "gs")
                Append(body, Matrix(atState.Inverse()));
        }

        foreach (var op in item.Content.Skip(item.Object.Start).Take(item.Object.End - item.Object.Start + 1))
            body.Add(op);
        var form = new PdfDictionary(native);
        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        // A deliberately generous local BBox prevents introducing new clipping on stroked glyphs/miter joins.
        form.Elements["/BBox"] = PdfObjects.Numbers(native, -1000000, -1000000, 1000000, 1000000);
        form.Elements.SetBoolean("/PdfSpaceWrapper", true);
        form.Elements["/Resources"] = resources;
        form.CreateStream(PdfContentGraph.Serialize(body));
        return form;
    }

    private static string Color(uint rgba, bool stroke)
    {
        if ((rgba >> 24) != 255)
            throw new ArgumentException("Solid path colors must be opaque; existing alpha remains in the graphics state.");
        return $"{F(((rgba >> 16) & 255) / 255d)} {F(((rgba >> 8) & 255) / 255d)} {F((rgba & 255) / 255d)} {(stroke ? "RG" : "rg")}\n";
    }

    private static void Append(CSequence sequence, string operators)
    {
        if (operators.Length > 0)
            PdfContentGraph.Insert(sequence, sequence.Count, operators);
    }

    private static string Matrix(PdfAffineMatrix m) => $"{F(m.A)} {F(m.B)} {F(m.C)} {F(m.D)} {F(m.E)} {F(m.F)} cm\n";
    private static void ValidateBounds(RectD b)
    {
        if (!b.IsFinite || b.Width < 0 || b.Height < 0 || b.Width > 100000 || b.Height > 100000 || Math.Abs(b.X) > 100000 || Math.Abs(b.Y) > 100000)
            throw new ArgumentOutOfRangeException(nameof(b), "Object coordinates must be finite and within 100,000 points.");
    }
}
