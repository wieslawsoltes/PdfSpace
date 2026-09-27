using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>Resolves local destinations only, with bounded name-tree and alias traversal.</summary>
internal sealed class PdfDestinationResolver
{
    private readonly Dictionary<string, PdfItem> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pages = new(StringComparer.Ordinal);
    public PdfDestinationResolver(PdfDocument document)
    {
        for (var i = 0; i < document.PageCount; i++)
            if (document.Pages[i].Reference is { } reference) _pages[reference.ToString()] = i;
        var catalog = document.Internals.Catalog;
        if (PdfObjects.Dictionary(catalog.Elements["/Dests"]) is { } legacy)
            foreach (var key in legacy.Elements.Keys) Add(key.TrimStart('/'), legacy.Elements[key]);
        var root = PdfObjects.Dictionary(PdfObjects.Dictionary(catalog.Elements["/Names"])?.Elements["/Dests"]);
        if (root is null) return;
        var pending = new Stack<(PdfDictionary Node, int Depth)>(); pending.Push((root, 0));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            if (!visited.Add(item.Node) || item.Depth > 64 || visited.Count > 10000) throw new InvalidDataException("Cyclic or excessive PDF destination name tree.");
            if (PdfObjects.Array(item.Node.Elements["/Names"]) is { } names)
            {
                if (names.Elements.Count % 2 != 0) throw new InvalidDataException("Invalid destination name/value pairs.");
                for (var i = 0; i < names.Elements.Count; i += 2) Add(PdfObjects.Text(names.Elements[i]), names.Elements[i + 1]);
            }
            if (PdfObjects.Array(item.Node.Elements["/Kids"]) is { } children)
                foreach (var child in children.Elements)
                    if (PdfObjects.Dictionary(child) is { } dictionary) pending.Push((dictionary, item.Depth + 1));
        }
    }
    private void Add(string name, PdfItem? item)
    {
        if (name.Length > 4096 || _names.Count >= 10000) throw new InvalidDataException("Excessive named destinations.");
        if (item is not null) _names.TryAdd(name, item);
    }
    public int? Resolve(PdfItem? destination)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; destination is not null && depth < 64; depth++)
        {
            destination = PdfObjects.Resolve(destination);
            if (destination is PdfString or PdfName)
            {
                var name = PdfObjects.Text(destination);
                if (!aliases.Add(name) || !_names.TryGetValue(name, out destination)) return null;
            }
            else if (destination is PdfDictionary dictionary) destination = dictionary.Elements["/D"];
            else if (destination is PdfArray { Elements.Count: > 0 } array)
            {
                var page = PdfObjects.Dictionary(array.Elements[0]);
                return page?.Reference is { } reference && _pages.TryGetValue(reference.ToString(), out var index) ? index : null;
            }
            else return null;
        }
        return null;
    }
    public int? FromAction(PdfDictionary item)
    {
        if (item.Elements["/Dest"] is { } direct) return Resolve(direct);
        var action = PdfObjects.Dictionary(item.Elements["/A"]);
        return PdfObjects.Text(action?.Elements["/S"]) == "GoTo" ? Resolve(action?.Elements["/D"]) : null;
    }
}
