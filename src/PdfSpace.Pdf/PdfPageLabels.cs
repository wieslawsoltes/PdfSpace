using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>Native /PageLabels number-tree import/export and snapshot-only page label editing.</summary>
public static class PdfPageLabels
{
    /// <summary>Initialize labels from older workspaces or the low-level reader. Each required source opens once.</summary>
    public static PdfWorkspace Initialize(PdfWorkspace workspace, CancellationToken cancellationToken = default)
    {
        WorkspaceJson.Validate(workspace); cancellationToken.ThrowIfCancellationRequested();
        var pending = workspace.Pages.Where(p => !p.LabelInitialized && p.SourceId.HasValue).ToArray();
        if (pending.Length == 0) return workspace;
        var sources = workspace.Sources.ToDictionary(s => s.Id);
        var labels = new Dictionary<Guid, PdfPageLabel?[]>();
        foreach (var id in pending.Where(p => p.SourceId.HasValue).Select(p => p.SourceId!.Value).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var native = PdfDocumentEngine.OpenNative(sources[id].Bytes);
            labels.Add(id, Read(native, cancellationToken));
        }
        var pages = workspace.Pages.Select(p => p.LabelInitialized ? p : p with
        {
            Label = p.Label ?? (p.SourceId is { } id ? SourceLabel(labels[id], p.SourcePage) : null),
            LabelInitialized = true
        }).ToArray();
        var result = workspace with { Pages = pages }; WorkspaceJson.Validate(result); return result;
    }

    /// <summary>Set a contiguous section; unselected labels remain attached to their existing page identities.</summary>
    public static PdfWorkspace Apply(PdfWorkspace workspace, int firstPage, int pageCount, PdfPageLabel firstLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstLabel); firstLabel.Validate();
        ValidateRange(workspace, firstPage, pageCount); cancellationToken.ThrowIfCancellationRequested();
        // Validate the complete sequence before native parsing or constructing a new snapshot.
        var values = new PdfPageLabel[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var number = firstLabel.Style == PdfPageLabelStyle.PrefixOnly ? firstLabel.Number : checked(firstLabel.Number + i);
            values[i] = firstLabel with { Number = number }; values[i].Validate();
        }
        return Change(workspace, firstPage, pageCount, i => values[i], cancellationToken);
    }

    public static PdfWorkspace Extend(PdfWorkspace workspace, int firstPage, int pageCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(workspace, firstPage, pageCount);
        if (firstPage == 0) throw new ArgumentException("The first page has no preceding section.", nameof(firstPage));
        var ready = Initialize(workspace, cancellationToken);
        var previous = ready.Pages[firstPage - 1].Label ?? new PdfPageLabel { Number = firstPage };
        return Apply(ready, firstPage, pageCount, previous with
        { Number = previous.Style == PdfPageLabelStyle.PrefixOnly ? previous.Number : checked(previous.Number + 1) }, cancellationToken);
    }

    /// <summary>Restore physical numbering only in the requested range. Does not touch printed numbers.</summary>
    public static PdfWorkspace Reset(PdfWorkspace workspace, int firstPage, int pageCount,
        CancellationToken cancellationToken = default) => Change(workspace, firstPage, pageCount, _ => null, cancellationToken);

    private static PdfWorkspace Change(PdfWorkspace workspace, int firstPage, int count, Func<int, PdfPageLabel?> label,
        CancellationToken cancellationToken)
    {
        ValidateRange(workspace, firstPage, count); var ready = Initialize(workspace, cancellationToken);
        PdfPageState[]? pages = null;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var value = label(i);
            if (ready.Pages[firstPage + i].Label == value) continue;
            pages ??= ready.Pages.ToArray();
            pages[firstPage + i] = pages[firstPage + i] with { Label = value, LabelInitialized = true };
        }
        return pages is null ? ready : ready with { Pages = pages };
    }

    private static void ValidateRange(PdfWorkspace workspace, int first, int count)
    {
        WorkspaceJson.Validate(workspace);
        if (first < 0 || count < 1 || first >= workspace.Pages.Length || count > workspace.Pages.Length - first)
            throw new ArgumentOutOfRangeException(nameof(first), "Choose a contiguous range of existing physical pages.");
    }

    private static PdfPageLabel? SourceLabel(PdfPageLabel?[] labels, int number) => number > 0 && number <= labels.Length
        ? labels[number - 1] : throw new InvalidDataException("The page-label source page does not exist.");

    internal static PdfPageLabel?[] Read(PdfDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document.PageCount is < 1 or > 4096) throw new InvalidDataException("Page labels support 1 to 4096 source pages.");
        var result = new PdfPageLabel?[document.PageCount];
        var item = document.Internals.Catalog.Elements["/PageLabels"];
        if (item is null) return result;
        var root = PdfObjects.Dictionary(item) ?? throw new InvalidDataException("PageLabels must be a number-tree dictionary.");
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var entries = new List<(int Index, PdfPageLabel Label)>();
        (int First, int Last) Visit(PdfDictionary node, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 16 || !seen.Add(node) || seen.Count > 8192 || node.Stream is not null)
                throw new InvalidDataException("Cyclic, shared, streamed or excessive page-label number tree.");
            var firstEntry = entries.Count;
            var numsItem = node.Elements["/Nums"]; var kidsItem = node.Elements["/Kids"];
            if ((numsItem is null) == (kidsItem is null)) throw new InvalidDataException("Page-label nodes require either Nums or Kids.");
            if (numsItem is not null)
            {
                var nums = PdfObjects.Array(numsItem) ?? throw new InvalidDataException("Invalid page-label Nums array.");
                if (nums.Elements.Count == 0 || nums.Elements.Count % 2 != 0 || nums.Elements.Count > 8192)
                    throw new InvalidDataException("Invalid page-label number-tree pairs.");
                for (var i = 0; i < nums.Elements.Count; i += 2)
                {
                    var key = Integer(nums.Elements[i]);
                    if (key < 0 || key >= result.Length || entries.Count >= result.Length || (entries.Count > 0 && key <= entries[^1].Index))
                        throw new InvalidDataException("Page-label keys must be unique, ordered and in the document.");
                    var value = PdfObjects.Dictionary(nums.Elements[i + 1]) ?? throw new InvalidDataException("Invalid page-label rule.");
                    if (value.Stream is not null) throw new InvalidDataException("Page-label rules cannot be streams.");
                    var style = PdfObjects.Resolve(value.Elements["/S"]);
                    var kind = style switch
                    {
                        null => PdfPageLabelStyle.PrefixOnly,
                        PdfName n => n.Value switch
                        {
                            "/D" => PdfPageLabelStyle.Decimal, "/r" => PdfPageLabelStyle.RomanLower,
                            "/R" => PdfPageLabelStyle.RomanUpper, "/a" => PdfPageLabelStyle.LettersLower,
                            "/A" => PdfPageLabelStyle.LettersUpper,
                            _ => throw new NotSupportedException("Unsupported native page-label numbering style.")
                        },
                        _ => throw new InvalidDataException("Page-label S must be a name.")
                    };
                    var prefix = PdfObjects.Resolve(value.Elements["/P"]) switch
                    {
                        null => "", PdfString p => p.Value,
                        _ => throw new InvalidDataException("Page-label P must be a string.")
                    };
                    var number = value.Elements["/St"] is { } start ? Integer(start) : 1;
                    var label = new PdfPageLabel { Style = kind, Prefix = prefix, Number = number }; label.Validate();
                    entries.Add((key, label));
                }
            }
            else
            {
                var kids = PdfObjects.Array(kidsItem) ?? throw new InvalidDataException("Invalid page-label Kids array.");
                if (kids.Elements.Count is < 1 or > 4096) throw new InvalidDataException("Page-label tree fanout exceeds its bound.");
                foreach (var kid in kids.Elements)
                    Visit(PdfObjects.Dictionary(kid) ?? throw new InvalidDataException("Invalid page-label child."), depth + 1);
            }
            var first = entries[firstEntry].Index; var last = entries[^1].Index;
            if (node.Elements["/Limits"] is { } limitsItem)
            {
                var limits = PdfObjects.Array(limitsItem);
                if (limits is null || limits.Elements.Count != 2 || Integer(limits.Elements[0]) != first || Integer(limits.Elements[1]) != last)
                    throw new InvalidDataException("Page-label number-tree Limits do not match its contents.");
            }
            return (first, last);
        }
        Visit(root, 0);
        if (entries.Count == 0 || entries[0].Index != 0) throw new InvalidDataException("The first page-label range must begin at zero.");
        for (var i = 0; i < entries.Count; i++)
        {
            var (first, rule) = entries[i]; var end = i + 1 < entries.Count ? entries[i + 1].Index : result.Length;
            for (var p = first; p < end; p++)
            {
                var value = rule with { Number = rule.Style == PdfPageLabelStyle.PrefixOnly ? rule.Number : checked(rule.Number + (p - first)) };
                value.Validate(); result[p] = value;
            }
        }
        return result;
    }

    private static int Integer(PdfItem item) => PdfObjects.Resolve(item) is PdfInteger integer
        ? integer.Value : throw new InvalidDataException("Page-label indices and start values must be integers.");

    /// <summary>Write maximal compatible runs. The old tree is replaced, not edited through aliased children.</summary>
    internal static void Write(PdfDocument document, PdfWorkspace workspace, IReadOnlyDictionary<Guid, PdfDocument> sources)
    {
        // The common unlabelled/reset case needs neither temporary label records
        // nor number-tree allocation. Uninitialized native sources still take the
        // resolution path below, so older workspaces cannot silently lose labels.
        var allImplicit = true;
        foreach (var page in workspace.Pages)
            if (page.Label is not null || (!page.LabelInitialized && page.SourceId.HasValue))
            { allImplicit = false; break; }
        if (allImplicit) { document.Internals.Catalog.Elements.Remove("/PageLabels"); return; }
        var sourceLabels = new Dictionary<Guid, PdfPageLabel?[]>();
        var hasExplicitLabels = false;
        PdfPageLabel Effective(int i)
        {
            var page = workspace.Pages[i]; var label = page.Label;
            if (!page.LabelInitialized && label is null && page.SourceId is { } id)
            {
                if (!sourceLabels.TryGetValue(id, out var labels)) { labels = Read(sources[id]); sourceLabels.Add(id, labels); }
                label = SourceLabel(labels, page.SourcePage);
            }
            hasExplicitLabels |= label is not null;
            return label ?? new PdfPageLabel { Number = i + 1 };
        }
        var runs = new List<(int Index, PdfPageLabel Label)>(); PdfPageLabel? previous = null;
        for (var i = 0; i < workspace.Pages.Length; i++)
        {
            var current = Effective(i); current.Validate();
            if (previous is null || previous.Style != current.Style || previous.Prefix != current.Prefix ||
                (current.Style == PdfPageLabelStyle.PrefixOnly ? previous.Number != current.Number :
                    (long)previous.Number + 1 != current.Number)) runs.Add((i, current));
            previous = current;
        }
        // Explicit physical-looking labels still belong to their page identity after
        // reopening/reordering. Only an entirely implicit/reset sequence drops the tree.
        if (!hasExplicitLabels) { document.Internals.Catalog.Elements.Remove("/PageLabels"); return; }
        var nums = new PdfArray(document);
        foreach (var (index, label) in runs)
        {
            var rule = new PdfDictionary(document);
            var style = label.Style switch
            {
                PdfPageLabelStyle.Decimal => "/D", PdfPageLabelStyle.RomanLower => "/r", PdfPageLabelStyle.RomanUpper => "/R",
                PdfPageLabelStyle.LettersLower => "/a", PdfPageLabelStyle.LettersUpper => "/A", _ => null
            };
            if (style is not null) rule.Elements.SetName("/S", style);
            if (label.Number != 1) rule.Elements.SetInteger("/St", label.Number);
            if (label.Prefix.Length > 0) rule.Elements["/P"] = new PdfString(label.Prefix, PdfStringEncoding.Unicode);
            nums.Elements.Add(new PdfInteger(index)); nums.Elements.Add(rule);
        }
        var root = new PdfDictionary(document); root.Elements["/Nums"] = nums;
        document.Internals.Catalog.Elements["/PageLabels"] = root;
    }
}
