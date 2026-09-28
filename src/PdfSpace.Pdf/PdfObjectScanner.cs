using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using SkiaSharp;
using Letter = UglyToad.PdfPig.Content.Letter;

namespace PdfSpace.Pdf;
// Native state is owned by one operation; public descriptors never retain parser objects or source buffers.
internal sealed class PdfObjectScanner
{
    internal sealed record StateCommand(COperator Operation, PdfDictionary Resources, PdfAffineMatrix Matrix, StateCommand? Previous);
    internal sealed record Clip(string Operators, PdfAffineMatrix Matrix, Clip? Previous);
    internal sealed record Item(PdfPageObject Object, CSequence Content, PdfDictionary Resources, StateCommand? State, Clip? Clip, PdfAffineMatrix EndMatrix, bool HasSideEffects);
    internal readonly List<Item> Items = [];
    internal readonly Dictionary<string, (Clip? Clip, PdfAffineMatrix Matrix)> ScopeEnds = [];
    internal readonly Dictionary<string, (Clip? Clip, PdfAffineMatrix Matrix)> ScopeStarts = [];
    private readonly Dictionary<string, PdfContentGraph.Scope> _scopes;
    private readonly Dictionary<int, Letter[]> _letters;
    private readonly Guid _pageId, _sourceId;
    private readonly int _sourcePage;
    private readonly string _hash;
    private readonly PdfAffineMatrix _logical;
    private readonly bool _locked;
    private int _sequence;
    private static readonly HashSet<string> Paint = new(["S", "s", "f", "F", "f*", "B", "B*", "b", "b*"], StringComparer.Ordinal);
    internal static readonly HashSet<string> Path = new(["m", "l", "c", "v", "y", "h", "re"], StringComparer.Ordinal);
    private static readonly HashSet<string> StateOps = new(["w", "J", "j", "M", "d", "ri", "i", "gs", "CS", "cs", "SC", "sc", "SCN", "scn", "G", "g", "RG", "rg", "K", "k", "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts"], StringComparer.Ordinal);
    internal PdfObjectScanner(PdfWorkspace workspace, int pageIndex, PdfDocument native)
    {
        var state = workspace.Pages[pageIndex];
        SourceHeight = state.Height;
        SourceWidth = state.Width;
        _pageId = state.Id;
        _sourceId = state.SourceId!.Value;
        _sourcePage = state.SourcePage;
        var source = workspace.Sources.First(s => s.Id == _sourceId);
        _hash = PdfContentGraph.Hash(source.Bytes);
        var page = native.Pages[_sourcePage - 1];
        _logical = PdfImageEditor.LogicalMatrix(new SourceGeometry(page));
        _scopes = PdfContentGraph.Read(page).ToDictionary(s => s.Path, StringComparer.Ordinal);
        var info = PdfDocumentEngine.Inspect(native);
        _locked = info.SignatureFields > 0 || info.HasXfa;
        using var text = UglyToad.PdfPig.PdfDocument.Open(source.Bytes);
        _letters = text.GetPage(_sourcePage).Letters.GroupBy(l => l.TextSequence).ToDictionary(g => g.Key, g => g.ToArray());
        Visit("", null, null, 0, 0);
    }

    private void Visit(string scopePath, StateCommand? inherited, Clip? inheritedClip, int inheritedMode, int depth)
    {
        var scope = _scopes[scopePath];
        var lastMatrix = _logical * scope.Transform;
        ScopeStarts[scopePath] = (inheritedClip, lastMatrix);
        var current = inherited;
        var clip = inheritedClip;
        var mode = inheritedMode;
        var stack = new Stack<(StateCommand? , Clip? , int)>();
        var start = -1;
        var startMatrix = PdfAffineMatrix.Identity;
        StateCommand? startState = null;
        Clip? startClip = null;
        var textStart = -1;
        var textMatrix = PdfAffineMatrix.Identity;
        StateCommand? textState = null;
        Clip? textClip = null;
        var textPoints = new List<PointD>();
        var textValue = new StringBuilder();
        var textSafe = true;
        var textSides = false;
        var pathNodes = new List<PdfPathNode>();
        var pathText = new StringBuilder();
        var pathSafe = true;
        var clips = false;
        using var shape = new SKPath();
        foreach (var(index, op, transform)in PdfContentGraph.Operations(scope))
        {
            var matrix = _logical * transform;
            lastMatrix = matrix;
            if (op.Name == "q")
                stack.Push((current, clip, mode));
            else if (op.Name == "Q")
            {
                if (stack.Count == 0)
                    throw new InvalidDataException("Unbalanced object graphics state.");
                (current, clip, mode) = stack.Pop();
            }

            if (op.Name == "BT")
            {
                ScopeEnds[scopePath] = (clip, lastMatrix);
                if (textStart >= 0)
                    throw new InvalidDataException("Nested PDF text objects are invalid.");
                textStart = index;
                textMatrix = matrix;
                textState = current;
                textClip = clip;
                textPoints.Clear();
                textValue.Clear();
                textSafe = mode < 4;
                textSides = false;
            }

            if (StateOps.Contains(op.Name))
            {
                current = new(op, scope.Resources, matrix, current);
                if (op.Name == "Tr")
                    mode = (int)N(op.Operands[0]);
                if (textStart >= 0)
                {
                    textSides = true;
                    textSafe &= mode < 4;
                }
            }

            if (textStart >= 0 && op.Name is "Do" or "sh" or "W" or "W*" or "q" or "Q" or "cm")
                textSafe = false;
            if (op.Name is "Tj" or "TJ" or "'" or "\"")
            {
                _sequence++;
                if (_letters.TryGetValue(_sequence, out var letters))
                {
                    foreach (var letter in letters.Where(l => (int)l.RenderingMode != 3))
                    {
                        // PdfPig has already normalized the source page rotation/crop.
                        var box = letter.BoundingBox;
                        var scopePageHeight = SourceHeight;
                        textPoints.Add(new(box.Left, scopePageHeight - box.Top));
                        textPoints.Add(new(box.Right, scopePageHeight - box.Bottom));
                        textValue.Append(letter.Value);
                    }
                }

                if (op.Name == "\"")
                {
                    var w = ParseOperation($"{F(N(op.Operands[0]))} Tw");
                    var c = ParseOperation($"{F(N(op.Operands[1]))} Tc");
                    current = new(w, scope.Resources, matrix, current);
                    current = new(c, scope.Resources, matrix, current);
                    textSides = true;
                }
            }

            if (op.Name == "ET" && textStart >= 0)
            {
                if (textPoints.Count > 0)
                    Add(textStart, index, PdfPageObjectKind.Text, Bounds(textPoints), textMatrix, matrix, textState, textClip, textSafe, "Clipping or interleaved graphics inside this text object require specialized editing.", textValue.ToString(), [], textSides);
                textStart = -1;
            }

            if (Path.Contains(op.Name))
            {
                if (start < 0)
                {
                    start = index;
                    startMatrix = matrix;
                    startState = current;
                    startClip = clip;
                    pathNodes.Clear();
                    pathText.Clear();
                    shape.Reset();
                    pathSafe = true;
                    clips = false;
                }

                pathSafe &= matrix == startMatrix && textStart < 0;
                var values = op.Operands.Select(N).ToArray();
                pathNodes.Add(new(op.Name, values));
                pathText.Append(op).Append('\n');
                Append(shape, op.Name, values, matrix);
            }
            else if (start >= 0 && op.Name is "W" or "W*")
            {
                clips = true;
            }
            else if (start >= 0 && (Paint.Contains(op.Name) || op.Name == "n"))
            {
                if (clips)
                    clip = new(pathText + (scope.Content.Skip(start).Take(index - start + 1).OfType<COperator>().Any(o => o.Name == "W*") ? "W* n\n" : "W n\n"), startMatrix, clip);
                if (Paint.Contains(op.Name))
                {
                    var b = shape.TightBounds;
                    Add(start, index, PdfPageObjectKind.Path, new(b.Left, b.Top, b.Width, b.Height), startMatrix, matrix, startState, startClip, pathSafe && !clips, "This path changes clipping or contains interleaved state; edits are blocked to preserve other content.", "", pathNodes.ToArray(), false);
                }

                start = -1;
            }
            else if (start >= 0)
                pathSafe = false;
            if (op.Name == "sh" && op.Operands.FirstOrDefault()is CName shadingName)
            {
                var shading = PdfObjects.Dictionary(PdfObjects.Dictionary(scope.Resources.Elements["/Shading"])?.Elements[shadingName.Name]);
                if (shading is not null)
                {
                    var shadingBounds = new RectD(0, 0, SourceWidth, SourceHeight);
                    if (shading.Elements["/BBox"] is { } box)
                    {
                        var r = PdfObjects.Rectangle(box);
                        shadingBounds = Bounds(Corners(new(r.X1, r.Y1, r.Width, r.Height), matrix));
                    }

                    Add(index, index, PdfPageObjectKind.Shading, shadingBounds, matrix, matrix, current, clip, textStart < 0, "Shading inside a text object is unsupported.", "", [], false);
                }
            }

            if (op.Name == "Do" && op.Operands.FirstOrDefault()is CName resource)
            {
                var obj = PdfObjects.Dictionary(PdfObjects.Dictionary(scope.Resources.Elements["/XObject"])?.Elements[resource.Name]);
                var subtype = PdfObjects.Text(obj?.Elements["/Subtype"]);
                if (subtype == "Image")
                {
                    Add(index, index, PdfPageObjectKind.Image, Bounds(Corners(new(0, 0, 1, 1), matrix)), matrix, matrix, current, clip, textStart < 0, "Image painting inside text is unsupported.", "", [], false, obj!.Elements.GetInteger("/Width"), obj.Elements.GetInteger("/Height"));
                }
                else if (subtype == "Form")
                {
                    var child = scopePath.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : scopePath + "/" + index.ToString(CultureInfo.InvariantCulture);
                    var childMatrix = _logical * _scopes[child].Transform;
                    var bbox = PdfObjects.Rectangle(obj!.Elements["/PdfSpaceBounds"] ?? obj.Elements["/BBox"]);
                    var local = new RectD(bbox.X1, bbox.Y1, bbox.Width, bbox.Height);
                    if (!obj.Elements.GetBoolean("/PdfSpaceWrapper"))
                        Add(index, index, PdfPageObjectKind.Form, Bounds(Corners(local, childMatrix)), matrix, matrix, current, clip, textStart < 0, "Form painting inside text is unsupported.", "", [], false);
                    var isGroup = obj.Elements.GetBoolean("/PdfSpaceGroup");
                    var retained = Items.Count;
                    if (isGroup && retained > 0)
                        Items[^1] = Items[^1] with
                        {
                            Object = Items[^1].Object with
                            {
                                IsContainer = false
                            }
                        };
                    var actualBox = PdfObjects.Rectangle(obj.Elements["/BBox"]);
                    Visit(child, current, new($"{F(actualBox.X1)} {F(actualBox.Y1)} {F(actualBox.Width)} {F(actualBox.Height)} re W n\n", childMatrix, clip), mode, depth + 1);
                    if (isGroup && Items.Count > retained)
                        Items.RemoveRange(retained, Items.Count - retained);
                }
            }
        }

        ScopeEnds[scopePath] = (clip, lastMatrix);
        if (textStart >= 0)
            throw new InvalidDataException("Unterminated text object.");
        void Add(int first, int last, PdfPageObjectKind kind, RectD bounds, PdfAffineMatrix matrix, PdfAffineMatrix endMatrix, StateCommand? state, Clip? clipping, bool editable, string limitation, string text, PdfPathNode[] nodes, bool sides, int pw = 0, int ph = 0)
        {
            if (Items.Count >= 20000)
                throw new InvalidDataException("Object editing is limited to 20,000 painted occurrences per page.");
            if (!bounds.IsFinite || !matrix.IsFinite)
                throw new InvalidDataException("Nonfinite object geometry.");
            try
            {
                _ = matrix.Inverse();
            }
            catch (NotSupportedException)
            {
                editable = false;
                limitation = "Singular source transform.";
            }

            if (_locked)
            {
                editable = false;
                limitation = "Signed/certified or XFA source editing is blocked.";
            }

            var sequence = new CSequence();
            for (var index = first; index <= last; index++)
                sequence.Add(scope.Content[index]);
            var fingerprint = Convert.ToHexString(SHA256.HashData(sequence.ToContent()));
            Items.Add(new(new(_pageId, _sourceId, _sourcePage, _hash, scopePath, first, last, kind, bounds, matrix, fingerprint, editable, editable ? "Native occurrence editing. Existing clipping remains effective. This is not redaction." : limitation) { Text = text, IsContainer = kind == PdfPageObjectKind.Form, Nodes = nodes, PixelWidth = pw, PixelHeight = ph }, scope.Content, scope.Resources, state, clipping, endMatrix, sides));
        }
    }

    private double SourceHeight { get; set; }
    private double SourceWidth { get; set; }

    internal static PdfObjectScanner Scan(PdfWorkspace workspace, int pageIndex, PdfDocument native) => new(workspace, pageIndex, native);
    internal static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    internal static double N(CObject value) => value switch
    {
        CInteger i => i.Value,
        CReal r when double.IsFinite(r.Value) => r.Value,
        _ => throw new InvalidDataException("Invalid numeric path operand.")};
    internal static COperator ParseOperation(string value) => (COperator)PdfContentGraph.Parse(Encoding.ASCII.GetBytes(value + "\n"))[0];
    internal static PointD[] Corners(RectD r, PdfAffineMatrix m) => [m.Transform(new(r.X, r.Y)), m.Transform(new(r.Right, r.Y)), m.Transform(new(r.Right, r.Bottom)), m.Transform(new(r.X, r.Bottom))];
    internal static RectD Bounds(IEnumerable<PointD> p)
    {
        var a = p.ToArray();
        return new(a.Min(v => v.X), a.Min(v => v.Y), a.Max(v => v.X) - a.Min(v => v.X), a.Max(v => v.Y) - a.Min(v => v.Y));
    }

    internal static void Append(SKPath path, string op, double[] v, PdfAffineMatrix matrix)
    {
        SKPoint P(int i)
        {
            var p = matrix.Transform(new(v[i], v[i + 1]));
            return new((float)p.X, (float)p.Y);
        }

        var count = op switch
        {
            "m" or "l" => 2,
            "c" => 6,
            "v" or "y" or "re" => 4,
            "h" => 0,
            _ => -1
        };
        if (v.Length != count || v.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e7))
            throw new InvalidDataException("Invalid path command.");
        switch (op)
        {
            case "m":
                path.MoveTo(P(0));
                break;
            case "l":
                path.LineTo(P(0));
                break;
            case "c":
                path.CubicTo(P(0), P(2), P(4));
                break;
            case "v":
                path.CubicTo(path.LastPoint, P(0), P(2));
                break;
            case "y":
                path.CubicTo(P(0), P(2), P(2));
                break;
            case "h":
                path.Close();
                break;
            case "re":
                var c = Corners(new(v[0], v[1], v[2], v[3]), matrix);
                path.MoveTo((float)c[0].X, (float)c[0].Y);
                foreach (var p in c.Skip(1))
                    path.LineTo((float)p.X, (float)p.Y);
                path.Close();
                break;
        }
    }
}
