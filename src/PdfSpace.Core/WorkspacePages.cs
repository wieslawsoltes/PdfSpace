namespace PdfSpace.Core;

/// <summary>Page-sequence operations that preserve internal link destination identity.</summary>
public static class WorkspacePages
{
    public static PdfWorkspace Select(PdfWorkspace workspace, IEnumerable<int> pageIndices)
    {
        WorkspaceJson.Validate(workspace); ArgumentNullException.ThrowIfNull(pageIndices);
        var indices = pageIndices.ToArray();
        if (indices.Length is < 1 or > 4096 || indices.Distinct().Count() != indices.Length || indices.Any(index => index < 0 || index >= workspace.Pages.Length))
            throw new ArgumentException("Select unique, existing pages in the desired order.", nameof(pageIndices));
        return WithPages(workspace, indices.Select(index => workspace.Pages[index]).ToArray());
    }

    /// <summary>Links to retained pages follow their IDs. Links to removed pages are removed, never redirected to an unrelated page.</summary>
    public static PdfWorkspace WithPages(PdfWorkspace workspace, PdfPageState[] pages)
    {
        WorkspaceJson.Validate(workspace); ArgumentNullException.ThrowIfNull(pages);
        if (pages.Length is < 1 or > 4096 || pages.Any(page => page is null) || pages.Select(page => page.Id).Distinct().Count() != pages.Length)
            throw new ArgumentException("A page sequence must contain 1–4096 distinct page identities.", nameof(pages));
        if (pages.SequenceEqual(workspace.Pages)) return workspace;
        var indices = pages.Select((page, index) => (page.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var mapped = pages.Select(page =>
        {
            var changed = false; var annotations = new List<Annotation>();
            foreach (var annotation in page.Annotations)
            {
                if (annotation.Kind != AnnotationKind.Link || annotation.TargetPage is not { } previous) { annotations.Add(annotation); continue; }
                if (previous < 0 || previous >= workspace.Pages.Length) throw new InvalidDataException("Invalid internal link destination.");
                if (!indices.TryGetValue(workspace.Pages[previous].Id, out var next)) { changed = true; continue; }
                annotations.Add(next == previous ? annotation : annotation with { TargetPage = next });
                changed |= next != previous;
            }
            return changed ? page with { Annotations = annotations.ToArray() } : page;
        }).ToArray();
        var result = workspace with { Pages = mapped }; WorkspaceJson.Validate(result); return result;
    }
}
