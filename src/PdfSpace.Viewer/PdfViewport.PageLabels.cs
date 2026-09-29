namespace PdfSpace.Viewer;

public sealed partial class PdfViewport
{
    private readonly WorkspaceSnapshotStamp _pageLabelStamp = new();
    private PageLabelIndex? _pageLabelIndex;
    public int PageLabelIndexBuilds { get; private set; }

    /// <summary>Shared immutable strings for navigation and visible thumbnails; no source parsing during pointer updates.</summary>
    public PageLabelIndex PageLabels
    {
        get
        {
            if (_pageLabelIndex is null || !_pageLabelStamp.Matches(Session.Document))
            {
                if (_pageLabelIndex is null || !_pageLabelIndex.Matches(Session.Document.Pages))
                {
                    _pageLabelIndex = new PageLabelIndex(Session.Document.Pages); PageLabelIndexBuilds++;
                }
                _pageLabelStamp.Remember(Session.Document);
            }
            return _pageLabelIndex;
        }
    }
}
