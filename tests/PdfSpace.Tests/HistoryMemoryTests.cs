using System.Runtime.CompilerServices;
using System.Text;
using PdfSpace.Core;
using PdfSpace.Editing;

internal static class HistoryMemoryTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var document = Source(512); var session = new EditorSession(document, new EditorHistoryOptions { MaximumSourceBytes = 1536 });
        check(session.RetainedSourceBytes == 512, "history accounting includes current source bytes once");
        for (var i = 0; i < 10; i++) session.BookmarkPage("Label " + i);
        check(session.RetainedSourceBytes == 512 && session.UndoCount == 10, "metadata-only history shares rather than recounts immutable source arrays");
        var withPreview = document with { Sources = [document.Sources[0] with { PreviewBytes = new byte[256] }] };
        session.Execute("Preview", _ => withPreview);
        check(session.RetainedSourceBytes == 768, "history budget includes reconstructed preview buffers");
        session.Execute("New source", _ => Source(512));
        check(session.RetainedSourceBytes == 1280, "source revision retention counts only distinct buffers");
        session.MarkSaved(); var saved = session.Document;
        session.Execute("Third source", _ => Source(512));
        check(session.RetainedSourceBytes <= 1536 && session.PrunedHistoryEntries > 0, "source byte pressure prunes oldest undo entries");
        session.Undo(); check(!session.IsDirty && ReferenceEquals(session.Document, saved), "saved-state identity survives undo after memory pruning");
        var retained = session.RetainedSourceBytes;
        session.Redo(); check(session.IsDirty && session.RetainedSourceBytes == retained, "undo/redo transfers do not duplicate retained-buffer accounting");
        session.Undo(); session.BookmarkPage("New branch");
        check(!session.CanRedo && session.RetainedSourceBytes < retained, "branching history releases discarded redo source buffers");
        var current = session.Document;
        session.ClearHistory();
        check(!session.CanUndo && !session.CanRedo && ReferenceEquals(current, session.Document) && session.IsDirty && session.RetainedSourceBytes == 512, "clear history frees old sources without changing document or dirty state");
        session.MarkSaved(); session.ClearHistory(); check(!session.IsDirty, "clear history on a saved document preserves clean state");
        var tiny = new EditorSession(Source(512), new EditorHistoryOptions { MaximumSourceBytes = 128 });
        tiny.Execute("Replace oversized source", _ => Source(600));
        check(!tiny.CanUndo && tiny.RetainedSourceBytes == 600, "current source is never discarded when it exceeds the history budget");
        var none = new EditorSession(Source(512), new EditorHistoryOptions { MaximumEntries = 0 });
        none.BookmarkPage("No undo"); check(!none.CanUndo && none.Document.Pages[0].Bookmark == "No undo", "zero-capacity history still applies edits");
        var aliases = document with { Sources = [document.Sources[0], document.Sources[0] with { Id = Guid.NewGuid(), PreviewBytes = document.Sources[0].Bytes }] };
        check(new EditorSession(aliases).RetainedSourceBytes == 512, "source and preview aliases are deduplicated by buffer identity");
        reject(() => new EditorSession(document, new EditorHistoryOptions { MaximumEntries = -1 }), "history rejects negative capacity");
        reject(() => new EditorSession(document, new EditorHistoryOptions { MaximumSourceBytes = -1 }), "history rejects negative source budget");
        var revision = session.Revision; var bytes = session.RetainedSourceBytes;
        reject(() => session.Execute("Invalid", d => d with { Pages = [] }), "invalid command is rejected before history accounting");
        check(session.Revision == revision && session.RetainedSourceBytes == bytes, "failed edit leaves history accounting unchanged");
        var (collectible, weak) = CollectibleSaved();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!weak.IsAlive && collectible.IsDirty, "saved-state marker does not pin discarded source snapshots");
        GC.KeepAlive(collectible);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (EditorSession Session, WeakReference Previous) CollectibleSaved()
    {
        var initial = Source(512); var previous = new WeakReference(initial);
        var session = new EditorSession(initial, new EditorHistoryOptions { MaximumEntries = 0 });
        session.Execute("Replace", _ => Source(512));
        return (session, previous);
    }
    private static PdfWorkspace Source(int count)
    {
        var bytes = new byte[count]; Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
        var source = new PdfSource(Guid.NewGuid(), "synthetic.pdf", bytes);
        return new PdfWorkspace { Sources = [source], Pages = [new PdfPageState { SourceId = source.Id }] };
    }
}
