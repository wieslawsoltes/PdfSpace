using PdfSharp.Pdf;

namespace PdfSpace.Pdf;

public static partial class PdfPageMarks
{
    /// <summary>
    /// Check the exact top-level sequence created by Compose, without decoding or
    /// joining original page streams: behind marks, q, original streams, Q, front marks.
    /// These checks detect stale ownership, not document authenticity or safe content.
    /// </summary>
    private static bool HasIntactEnvelope(PdfPage page, IReadOnlyList<Owned> marks)
    {
        var guards = PdfObjects.Array(page.Elements[IsolationKey]);
        if (guards is null || guards.Elements.Count != 2) return false;
        var begin = PdfObjects.Dictionary(guards.Elements[0]);
        var end = PdfObjects.Dictionary(guards.Elements[1]);
        if (begin is null || end is null || ReferenceEquals(begin, end) ||
            !Exact(begin, "q\n") || !Exact(end, "Q\n")) return false;

        var content = page.Contents.Elements;
        var cursor = 0;
        for (var i = 0; i < marks.Count; i++)
        {
            if (!marks[i].Info.Settings.BehindContent) continue;
            if (cursor >= content.Count || !ReferenceEquals(PdfObjects.Resolve(content[cursor++]), marks[i].Stream))
                return false;
        }
        if (cursor >= content.Count || !ReferenceEquals(PdfObjects.Resolve(content[cursor++]), begin)) return false;

        var closed = false;
        while (cursor < content.Count)
        {
            var current = PdfObjects.Resolve(content[cursor++]);
            if (ReferenceEquals(current, end)) { closed = true; break; }
            if (ReferenceEquals(current, begin)) return false;
            // A managed invocation inside the source envelope is not an original
            // stream. Never absorb it during an update or silently change its order.
            for (var i = 0; i < marks.Count; i++)
                if (ReferenceEquals(current, marks[i].Stream)) return false;
        }
        if (!closed) return false;
        for (var i = 0; i < marks.Count; i++)
        {
            if (marks[i].Info.Settings.BehindContent) continue;
            if (cursor >= content.Count || !ReferenceEquals(PdfObjects.Resolve(content[cursor++]), marks[i].Stream))
                return false;
        }
        return cursor == content.Count;
    }
}
