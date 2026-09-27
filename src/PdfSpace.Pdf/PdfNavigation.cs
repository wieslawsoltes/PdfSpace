using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

public static class PdfNavigation
{
    internal const string ManagedOutlineKey = "/PdfSpaceManaged";
    internal sealed record OutlineNode(string Title, int? PageIndex, int Depth, bool Managed, bool ManagedRoot);

    public static IReadOnlyList<PdfBookmark> ReadBookmarks(PdfWorkspace workspace)
    {
        WorkspaceJson.Validate(workspace);
        var result = new List<PdfBookmark>();
        foreach (var source in workspace.Sources)
        {
            using var document = PdfDocumentEngine.OpenNative(source.Bytes);
            var map = workspace.Pages.Select((page, index) => (page, index)).Where(item => item.page.SourceId == source.Id)
                .GroupBy(item => item.page.SourcePage).ToDictionary(group => group.Key, group => group.First().index);
            foreach (var entry in ReadOutlines(document))
            {
                if (entry.Managed) continue; // Editable workspace bookmarks have a separate section.
                var target = entry.PageIndex is { } page && map.TryGetValue(page + 1, out var index) ? (int?)index : null;
                result.Add(new(entry.Title, target, entry.Depth, source.Name));
                if (result.Count >= 10000) throw new InvalidDataException("Too many document bookmarks.");
            }
        }
        return result.AsReadOnly();
    }

    internal static void RepairOutlineLinks(PdfDocument document)
    {
        // PDFsharp 6.2 regenerates links that exist, but does not clear stale
        // /Next or /Prev keys at the ends after collection removal. Clear only
        // structural links; destinations and other source properties stay intact.
        if (document.Outlines.Count == 0) { document.Internals.Catalog.Elements.Remove("/Outlines"); return; }
        var pending = new Stack<PdfOutline>(document.Outlines);
        var visited = new HashSet<PdfOutline>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var outline))
        {
            if (!visited.Add(outline) || visited.Count > 10000) throw new InvalidDataException("Invalid outline collection.");
            outline.Elements.Remove("/Prev"); outline.Elements.Remove("/Next");
            if (outline.Outlines.Count == 0)
            { outline.Elements.Remove("/First"); outline.Elements.Remove("/Last"); outline.Elements.Remove("/Count"); }
            foreach (var child in outline.Outlines) pending.Push(child);
        }
    }

    internal static IReadOnlyList<OutlineNode> ReadOutlines(PdfDocument document)
    {
        var first = PdfObjects.Dictionary(PdfObjects.Dictionary(document.Internals.Catalog.Elements["/Outlines"])?.Elements["/First"]);
        var result = new List<OutlineNode>(); if (first is null) return result;
        var resolver = new PdfDestinationResolver(document);
        var pending = new Stack<(PdfDictionary Node, int Depth, bool Managed)>(); pending.Push((first, 0, false));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            if (!visited.Add(item.Node) || item.Depth > 64 || visited.Count > 10000) throw new InvalidDataException("Cyclic or excessive PDF outline hierarchy.");
            var root = item.Depth == 0 && item.Node.Elements.GetBoolean(ManagedOutlineKey);
            var managed = item.Managed || root;
            var title = PdfObjects.Text(item.Node.Elements["/Title"]);
            if (title.Length > 4096) title = title[..4096];
            result.Add(new(title, resolver.FromAction(item.Node), item.Depth, managed, root));
            if (PdfObjects.Dictionary(item.Node.Elements["/Next"]) is { } next) pending.Push((next, item.Depth, item.Managed));
            if (PdfObjects.Dictionary(item.Node.Elements["/First"]) is { } child) pending.Push((child, item.Depth + 1, managed));
        }
        return result;
    }
}
