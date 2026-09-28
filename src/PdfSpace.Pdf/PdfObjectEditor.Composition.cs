using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using static PdfSpace.Pdf.PdfObjectScanner;

namespace PdfSpace.Pdf;
/// <summary>Internal application clipboard data. Original PDF resources may remain; this is not sanitization.</summary>
public sealed record PdfObjectClipboard(byte[] Pdf, RectD Bounds, bool Sensitive);
public static partial class PdfObjectEditor
{
    public static PdfObjectClipboard Copy(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects)
    {
        PdfObjectClipboard? clipboard = null;
        _ = Edit(workspace, objects, (native, items, scanner) =>
        {
            var state = workspace.Pages.Single(p => p.Id == objects[0].PageId);
            var page = native.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(state.Width);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(state.Height);
            var resources = new PdfDictionary(native);
            page.Elements["/Resources"] = resources;
            var body = new StringBuilder();
            var logical = PdfImageEditor.LogicalMatrix(new SourceGeometry(page));
            foreach (var item in items.OrderBy(i => scanner.Items.IndexOf(i)))
            {
                var name = PdfContentGraph.AddResource(native, resources, "/XObject", Capture(native, item, includeClips: true));
                body.Append("q\n").Append(Matrix(logical.Inverse() * item.Object.LocalToPage)).Append(name).Append(" Do\nQ\n");
            }

            var stream = new PdfDictionary(native);
            stream.CreateStream(Encoding.ASCII.GetBytes(body.ToString()));
            native.Internals.AddObject(stream);
            page.Elements["/Contents"] = stream.Reference!;
            using var import = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(PdfDocumentEngine.Bytes(native)), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            using var export = new PdfDocument();
            export.AddPage(import.Pages[^1]);
            clipboard = new(PdfDocumentEngine.Bytes(export), SelectionBounds(objects), workspace.IsSensitive);
        }, commit: false);
        return clipboard!;
    }

    public static PdfWorkspace Paste(PdfWorkspace workspace, int pageIndex, PdfObjectClipboard clipboard, PointD topLeft)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (clipboard.Pdf.Length > WorkspaceJson.MaximumSourceBytes)
            throw new InvalidDataException("Object clipboard exceeds 64 MiB.");
        var delta = PdfAffineMatrix.Translate(topLeft.X - clipboard.Bounds.X, topLeft.Y - clipboard.Bounds.Y);
        ValidateBounds(Bounds(Corners(clipboard.Bounds, delta)));
        return InsertNative(workspace, pageIndex, (native, page, resources, logical) =>
        {
            using var import = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(clipboard.Pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            if (import.PageCount != 1)
                throw new InvalidDataException("Object clipboard must contain one page.");
            var imported = native.AddPage(import.Pages[0]);
            var form = new PdfDictionary(native);
            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements.SetBoolean("/PdfSpaceWrapper", true);
            form.Elements["/BBox"] = PdfObjects.Numbers(native, -1000000, -1000000, 1000000, 1000000);
            form.Elements["/Resources"] = PdfContentGraph.CopyDictionary(native, PdfContentGraph.Resources(imported));
            form.CreateStream(PdfContentGraph.Serialize(PdfContentGraph.Parse(imported)));
            var transform = logical.Inverse() * delta * PdfImageEditor.LogicalMatrix(new SourceGeometry(imported));
            native.Pages.Remove(imported);
            var name = PdfContentGraph.AddResource(native, resources, "/XObject", form);
            return Matrix(transform) + name + " Do\n";
        }, clipboard.Sensitive);
    }

    public static PdfWorkspace InsertPath(PdfWorkspace workspace, int pageIndex, IReadOnlyList<PdfPathNode> nodes, PdfObjectAppearance? appearance = null)
    {
        if (nodes.Count is < 1 or > 10000 || nodes[0].Operator is not ("m" or "re"))
            throw new ArgumentException("Path must begin with Move or Rectangle.");
        using var shape = new SkiaSharp.SKPath();
        var path = new StringBuilder();
        foreach (var node in nodes)
        {
            PdfObjectScanner.Append(shape, node.Operator, node.Values, PdfAffineMatrix.Identity);
            path.Append(string.Join(" ", node.Values.Select(F))).Append(' ').Append(node.Operator).Append('\n');
        }

        appearance ??= new(Fill: 0xFF1473E6, Stroke: 0xFF333333, StrokeWidth: 1, FillEnabled: true, StrokeEnabled: true);
        var fill = appearance.FillEnabled ?? true;
        var stroke = appearance.StrokeEnabled ?? true;
        var width = appearance.StrokeWidth ?? 1;
        if (!double.IsFinite(width) || width is < 0 or > 1000)
            throw new ArgumentException("Invalid stroke width.");
        return InsertNative(workspace, pageIndex, (_, _, _, logical) => Matrix(logical.Inverse()) + Color(appearance.Fill ?? 0xFF1473E6, false) + Color(appearance.Stroke ?? 0xFF333333, true) + F(width) + " w\n" + path + (fill ? (stroke ? "B" : "f") + (appearance.EvenOdd == true ? "*" : "") : stroke ? "S" : "n") + "\n");
    }

    public static PdfWorkspace Group(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects) => Edit(workspace, objects, (native, items, scanner) =>
    {
        if (items.Length < 2)
            throw new ArgumentException("Select at least two objects to group.");
        var scope = items[0].Object.ScopePath;
        if (items.Any(i => i.Object.ScopePath != scope || !ReferenceEquals(i.Clip, items[0].Clip)))
            throw new NotSupportedException("Group objects within the same native scope and clipping context.");
        var min = items.Min(i => i.Object.Start);
        var max = items.Max(i => i.Object.End);
        var keys = items.Select(i => Key(i.Object)).ToHashSet();
        if (scanner.Items.Any(i => i.Object.ScopePath == scope && i.Object.Start >= min && i.Object.End <= max && !keys.Contains(Key(i.Object))))
            throw new NotSupportedException("Grouping requires consecutive painted objects; include intervening objects to preserve stacking order.");
        var first = items.OrderBy(i => i.Object.Start).First();
        PdfContentGraph.Edit(native, native.Pages[first.Object.SourcePage - 1], scope, (content, resources) =>
        {
            var group = new PdfDictionary(native);
            group.Elements.SetName("/Type", "/XObject");
            group.Elements.SetName("/Subtype", "/Form");
            group.Elements.SetBoolean("/PdfSpaceGroup", true);
            group.Elements["/BBox"] = PdfObjects.Numbers(native, -1000000, -1000000, 1000000, 1000000);
            var bounds = Bounds(Corners(SelectionBounds(objects), first.Object.LocalToPage.Inverse()));
            group.Elements["/PdfSpaceBounds"] = PdfObjects.Numbers(native, bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
            var childResources = new PdfDictionary(native);
            group.Elements["/Resources"] = childResources;
            var body = new StringBuilder();
            foreach (var item in items.OrderBy(i => i.Object.Start))
            {
                var name = PdfContentGraph.AddResource(native, childResources, "/XObject", Capture(native, item));
                body.Append("q\n").Append(Matrix(first.Object.LocalToPage.Inverse() * item.Object.LocalToPage)).Append(name).Append(" Do\nQ\n");
            }

            group.CreateStream(Encoding.ASCII.GetBytes(body.ToString()));
            var groupName = PdfContentGraph.AddResource(native, resources, "/XObject", group);
            foreach (var item in items.OrderByDescending(i => i.Object.Start))
                Replace(content, item, item.Object.Start == min ? "q\n" + groupName + " Do\nQ\n" : "", "", "", true);
        });
    });
    public static PdfWorkspace Ungroup(PdfWorkspace workspace, PdfPageObject target) => Edit(workspace, [target], (native, items) => ChangeScopes(native, items, (content, resources, item) =>
    {
        if (item.Object.Kind != PdfPageObjectKind.Form || content[item.Object.Start] is not COperator op || op.Operands[0] is not CName name)
            throw new ArgumentException("Select a generated object group.");
        var group = PdfObjects.Dictionary(PdfObjects.Dictionary(resources.Elements["/XObject"])?.Elements[name.Name]);
        if (group?.Elements.GetBoolean("/PdfSpaceGroup") != true || group.Stream is null || group.Elements.ContainsKey("/Group") || group.Elements.ContainsKey("/Matrix"))
            throw new NotSupportedException("Only PdfSpace groups can be ungrouped without changing transparency or clipping semantics. Original Form contents remain individually editable.");
        var body = PdfContentGraph.Parse(group.Stream.UnfilteredValue);
        if (body.OfType<COperator>().Any(operation => operation.Name is not ("q" or "Q" or "cm" or "Do")))
            throw new NotSupportedException("This group contains non-placement operators and cannot be safely ungrouped.");
        var box = PdfObjects.Rectangle(group.Elements["/BBox"]);
        var groupResources = PdfObjects.Dictionary(group.Elements["/Resources"])!;
        foreach (var drawing in body.OfType<COperator>().Where(o => o.Name == "Do"))
        {
            var old = (CName)drawing.Operands[0];
            var child = PdfObjects.Dictionary(PdfObjects.Dictionary(groupResources.Elements["/XObject"])?.Elements[old.Name]) ?? throw new InvalidDataException("Invalid group resource.");
            drawing.Operands[0] = new CName(PdfContentGraph.AddResource(native, resources, "/XObject", child));
        }

        Replace(content, item, $"q\n{F(box.X1)} {F(box.Y1)} {F(box.Width)} {F(box.Height)} re W n\n", Encoding.Latin1.GetString(PdfContentGraph.Serialize(body)), "Q\n", false);
    }));
    private static PdfWorkspace InsertNative(PdfWorkspace workspace, int pageIndex, Func<PdfDocument, PdfPage, PdfDictionary, PdfAffineMatrix, string> draw, bool sensitive = false)
    {
        WorkspaceJson.Validate(workspace);
        if ((uint)pageIndex >= workspace.Pages.Length)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var state = workspace.Pages[pageIndex];
        var source = workspace.Sources.FirstOrDefault(s => s.Id == state.SourceId);
        if (source is not null)
            PdfContentGraph.ValidateTarget(workspace, source.Id, state.SourcePage, state.Id, PdfContentGraph.Hash(source.Bytes));
        using var native = source is null ? new PdfDocument() : PdfDocumentEngine.OpenNative(source.Bytes);
        var page = source is null ? native.AddPage() : native.Pages[state.SourcePage - 1];
        if (source is null)
        {
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(state.Width);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(state.Height);
        }

        PdfContentGraph.Edit(native, page, "", (content, resources) =>
        {
            var drawing = draw(native, page, resources, PdfImageEditor.LogicalMatrix(new SourceGeometry(page)));
            PdfContentGraph.Insert(content, 0, "q\n");
            PdfContentGraph.Insert(content, content.Count, "Q\nq\n" + drawing + "Q\n");
        });
        if (source is not null)
        {
            var result = PdfContentGraph.Commit(workspace, source, state.Id, state.SourcePage, native);
            return sensitive ? result with
            {
                Sources = result.Sources.Select(s => s with { Sensitive = true }).ToArray()
            }

            : result;
        }

        var added = new PdfSource(Guid.NewGuid(), "Pasted objects.pdf", PdfDocumentEngine.Bytes(native))
        {
            Sensitive = sensitive
        };
        return PdfDocumentEngine.PrepareWorkspace(workspace with { Sources = [..workspace.Sources, added], Pages = workspace.Pages.Select(p => p.Id == state.Id ? p with { SourceId = added.Id, SourcePage = 1 } : p).ToArray() });
    }
}
