namespace PdfSpace.Core;

/// <summary>Combines workspaces without colliding identifiers, even when importing the same workspace twice.</summary>
public static class WorkspaceComposition
{
    public static PdfWorkspace Append(PdfWorkspace current, PdfWorkspace incoming)
    {
        WorkspaceJson.Validate(current);
        WorkspaceJson.Validate(incoming);
        if (current.FieldCount > 0 || incoming.FieldCount > 0) throw new InvalidOperationException("Combining interactive forms is not supported; export flattened copies first.");
        var sourceMap = incoming.Sources.ToDictionary(source => source.Id, _ => Guid.NewGuid());
        var sources = incoming.Sources.Select(source => source with { Id = sourceMap[source.Id] }).ToArray();
        var pages = incoming.Pages.Select(page => page with
        {
            Id = Guid.NewGuid(),
            SourceId = page.SourceId is { } sourceId ? sourceMap[sourceId] : null,
            Annotations = page.Annotations.Select(annotation => annotation with
            {
                Id = Guid.NewGuid(),
                TargetPage = annotation.TargetPage is { } target ? current.Pages.Length + target : null,
                Replies = annotation.Replies.Select(reply => reply with { Id = Guid.NewGuid() }).ToArray()
            }).ToArray()
        }).ToArray();
        var result = current with { Sources = [..current.Sources, ..sources], Pages = [..current.Pages, ..pages] };
        WorkspaceJson.Validate(result);
        return result;
    }
}
