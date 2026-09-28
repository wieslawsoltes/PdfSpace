using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
namespace PdfSpace.Pdf;

/// <summary>Bounded occurrence traversal and copy-on-write editing of shared Form XObjects.</summary>
internal static class PdfContentGraph
{
    internal sealed record Scope(string Path, CSequence Content, PdfDictionary Resources, PdfAffineMatrix Transform);
    private static readonly ConditionalWeakTable<byte[], string> Hashes = new();
    internal static string Hash(byte[] bytes) => Hashes.GetValue(bytes, static value => Convert.ToHexString(SHA256.HashData(value)));
    internal static PdfDictionary Resources(PdfPage page) => PdfObjects.Dictionary(PdfObjects.Inherited(page, "/Resources")) ?? new PdfDictionary(page.Owner);
    internal static IEnumerable<(int Index, COperator Operation, PdfAffineMatrix Matrix)> Operations(Scope scope)
    {
        var matrix = scope.Transform; var stack = new Stack<PdfAffineMatrix>();
        for (var i = 0; i < scope.Content.Count; i++)
        {
            if (scope.Content[i] is not COperator operation) continue;
            switch (operation.Name)
            {
                case "q": if (stack.Count >= 256) throw new InvalidDataException("Excessive PDF graphics-state nesting."); stack.Push(matrix); break;
                case "Q": if (stack.Count == 0) throw new InvalidDataException("Unbalanced PDF graphics state."); matrix = stack.Pop(); break;
                case "cm": matrix *= Matrix(operation); if (!matrix.IsFinite) throw new InvalidDataException("Nonfinite PDF transform."); break;
            }
            yield return (i, operation, matrix);
        }
        if (stack.Count != 0) throw new InvalidDataException("Unbalanced PDF graphics state.");
    }
    internal static Scope[] Read(PdfPage page)
    {
        var result = new List<Scope>(); var active = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var cache = new Dictionary<PdfDictionary, CSequence>(ReferenceEqualityComparer.Instance);
        var operators = 0; long bytes = 0;
        void Visit(Scope scope, int depth)
        {
            if (depth > 24 || result.Count >= 4096) throw new InvalidDataException("Excessive or cyclic Form XObject nesting.");
            operators += scope.Content.Count;
            if (operators > 500000) throw new InvalidDataException("PDF content traversal exceeds the operation budget.");
            result.Add(scope);
            foreach (var (index, operation, matrix) in Operations(scope))
            {
                if (operation.Name != "Do" || operation.Operands.FirstOrDefault() is not CName name) continue;
                var form = PdfObjects.Dictionary(PdfObjects.Dictionary(scope.Resources.Elements["/XObject"])?.Elements[name.Name]);
                if (form is null || PdfObjects.Text(form.Elements["/Subtype"]) != "Form") continue;
                if (!active.Add(form)) throw new InvalidDataException("Cyclic Form XObject graph.");
                if (!cache.TryGetValue(form, out var content))
                {
                    var data = form.Stream?.UnfilteredValue ?? [];
                    bytes += data.Length;
                    if (bytes > 64 * 1024 * 1024) throw new InvalidDataException("Expanded form content exceeds 64 MB.");
                    content = Parse(data); cache.Add(form, content);
                }
                var resources = PdfObjects.Dictionary(form.Elements["/Resources"]) ?? scope.Resources;
                var path = scope.Path.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : scope.Path + "/" + index.ToString(CultureInfo.InvariantCulture);
                Visit(new(path, content, resources, matrix * Matrix(form.Elements["/Matrix"])), depth + 1);
                active.Remove(form);
            }
        }
        Visit(new("", Parse(page), Resources(page), PdfAffineMatrix.Identity), 0);
        return result.ToArray();
    }
    internal static CSequence Parse(PdfPage page)
    {
        // Concatenation is performed by the parser, preserving PDF stream-array semantics.
        var content = ContentReader.ReadContent(page); Validate(content); return content;
    }
    internal static CSequence Parse(byte[] bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Expanded PDF content exceeds 64 MB.");
        var content = ContentReader.ReadContent(bytes); Validate(content); return content;
    }
    private static void Validate(CSequence content)
    {
        if (content.Count > 500000) throw new InvalidDataException("Too many PDF content operators.");
        // PDFsharp's content AST cannot round-trip all inline-image encodings.
        // Refuse a stream containing these rather than silently losing raster data.
        if (content.OfType<COperator>().Any(operation => operation.Name is "BI" or "ID" or "EI"))
            throw new NotSupportedException("Inline-image content streams are not editable yet. XObject images are supported.");
    }
    internal static PdfDictionary CopyDictionary(PdfDocument document, PdfDictionary? source)
    {
        var result = new PdfDictionary(document);
        if (source is not null) foreach (var pair in source.Elements) result.Elements[pair.Key] = pair.Value;
        return result;
    }
    internal static string AddResource(PdfDocument document, PdfDictionary resources, string category, PdfDictionary resource)
    {
        var entries = CopyDictionary(document, PdfObjects.Dictionary(resources.Elements[category]));
        resources.Elements[category] = entries;
        var index = 1; while (entries.Elements.ContainsKey("/PdfSpace" + index)) index++;
        var name = "/PdfSpace" + index;
        if (resource.Reference is null) document.Internals.AddObject(resource);
        entries.Elements[name] = resource.Reference!; return name;
    }
    internal static void Edit(PdfDocument document, PdfPage page, string path, Action<CSequence, PdfDictionary> edit)
    {
        var indices = path.Length == 0 ? [] : path.Split('/').Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : throw new InvalidDataException("Invalid content occurrence path.")).ToArray();
        if (indices.Length > 24) throw new InvalidDataException("Excessive content path depth.");
        CSequence Visit(CSequence content, PdfDictionary resources, int depth)
        {
            if (depth == indices.Length) { edit(content, resources); return content; }
            var index = indices[depth];
            if (index < 0 || index >= content.Count || content[index] is not COperator { Name: "Do" } operation || operation.Operands.FirstOrDefault() is not CName name)
                throw new InvalidOperationException("The selected form occurrence has changed.");
            var form = PdfObjects.Dictionary(PdfObjects.Dictionary(resources.Elements["/XObject"])?.Elements[name.Name]);
            if (form is null || PdfObjects.Text(form.Elements["/Subtype"]) != "Form" || form.Stream is null) throw new InvalidOperationException("The selected form no longer exists.");
            var owned = CopyDictionary(document, form);
            var childResources = CopyDictionary(document, PdfObjects.Dictionary(form.Elements["/Resources"]) ?? resources);
            owned.Elements["/Resources"] = childResources;
            var rewritten = Visit(Parse(form.Stream.UnfilteredValue), childResources, depth + 1);
            owned.Elements.Remove("/Filter"); owned.Elements.Remove("/DecodeParms"); owned.Elements.Remove("/Length");
            owned.CreateStream(Serialize(rewritten));
            operation.Operands[0] = new CName(AddResource(document, resources, "/XObject", owned));
            return content;
        }
        var rootResources = CopyDictionary(document, Resources(page));
        var root = Visit(Parse(page), rootResources, 0);
        page.Elements["/Resources"] = rootResources;
        // Assign a fresh stream, not a shared Contents array or its original stream object.
        var stream = new PdfDictionary(document); stream.CreateStream(Serialize(root)); document.Internals.AddObject(stream);
        page.Elements["/Contents"] = stream.Reference!;
    }
    private sealed class HexOperand : CString
    { public override string ToString() => "<" + Convert.ToHexString(Encoding.Latin1.GetBytes(Value)) + ">"; }
    internal static byte[] Serialize(CSequence sequence)
    {
        void Normalize(CSequence items)
        {
            foreach (var (item, index) in items.Select((value, index) => (value, index)).ToArray())
            {
                if (item is CString text && text.CStringType != CStringType.Dictionary)
                {
                    if (text.Value.Any(c => c > 255)) throw new NotSupportedException("A non-byte PDF string cannot be safely rewritten.");
                    items[index] = new HexOperand { Value = text.Value, CStringType = CStringType.HexString };
                }
                else if (item is COperator operation) Normalize(operation.Operands);
                else if (item is CSequence nested) Normalize(nested);
            }
        }
        Normalize(sequence); return sequence.ToContent();
    }
    internal static void Insert(CSequence content, int index, string operators)
    {
        var parsed = ContentReader.ReadContent(Encoding.ASCII.GetBytes(operators));
        for (var i = 0; i < parsed.Count; i++) content.Insert(index + i, parsed[i]);
    }
    private static PdfAffineMatrix Matrix(COperator operation)
    {
        if (operation.Operands.Count != 6) throw new InvalidDataException("Invalid PDF transform.");
        double Number(int index) => operation.Operands[index] switch { CInteger v => v.Value, CReal v => v.Value, _ => throw new InvalidDataException("Invalid matrix operand.") };
        return new(Number(0), Number(1), Number(2), Number(3), Number(4), Number(5));
    }
    private static PdfAffineMatrix Matrix(PdfItem? value)
    {
        if (value is null) return PdfAffineMatrix.Identity;
        if (PdfObjects.Array(value) is not { Elements.Count: 6 } array) throw new InvalidDataException("Invalid Form XObject matrix.");
        var m = array.Elements.Select(PdfObjects.Number).ToArray(); return new(m[0], m[1], m[2], m[3], m[4], m[5]);
    }
    internal static PdfSource ValidateTarget(PdfWorkspace workspace, Guid sourceId, int sourcePage, Guid pageId, string hash)
    {
        WorkspaceJson.Validate(workspace);
        var source = workspace.Sources.FirstOrDefault(source => source.Id == sourceId) ?? throw new InvalidOperationException("The selected source belongs to an older revision.");
        if (Hash(source.Bytes) != hash || !workspace.Pages.Any(page => page.Id == pageId && page.SourceId == sourceId && page.SourcePage == sourcePage))
            throw new InvalidOperationException("The source selection is stale. Select the object again.");
        var inspection = PdfDocumentEngine.Inspect(source.Bytes);
        if (inspection.SignatureFields > 0 || inspection.HasXfa) throw new NotSupportedException("Signed/certified and XFA sources cannot be edited.");
        return source;
    }
    internal static PdfWorkspace Commit(PdfWorkspace workspace, PdfSource source, Guid pageId, int sourcePage, PdfDocument native)
    {
        var next = source with { Id = Guid.NewGuid(), Bytes = PdfDocumentEngine.Bytes(native), PreviewBytes = null };
        var pages = workspace.Pages.Select(page => page.SourceId == source.Id && (page.SourcePage != sourcePage || page.Id == pageId) ? page with { SourceId = next.Id } : page).ToArray();
        var ids = pages.Where(page => page.SourceId is not null).Select(page => page.SourceId!.Value).ToHashSet();
        var sources = workspace.Sources.Append(next).Where(item => ids.Contains(item.Id)).ToArray();
        return PdfDocumentEngine.PrepareEditedSource(workspace with { Sources = sources, Pages = pages }, next.Id);
    }
}
