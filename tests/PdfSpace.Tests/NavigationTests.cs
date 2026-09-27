using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using NativeReader = PdfSharp.Pdf.IO.PdfReader;

internal static class NavigationTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var link = new Annotation { Kind = AnnotationKind.Link, TargetPage = 2, Bounds = new(20, 20, 100, 25), Text = "Go to target" };
        var original = new PdfWorkspace { Pages = [new() { Annotations = [link] }, new(), new() { Bookmark = "Target" }] };
        var session = new EditorSession(original); session.InsertBlank();
        check(session.Document.Pages[0].Annotations[0].TargetPage == 3, "inserted pages do not change internal link destination identity");
        session.Undo(); session.Navigate(0); session.DuplicatePage();
        check(session.Document.Pages[0].Annotations[0].TargetPage == 3 && session.Document.Pages[1].Annotations[0].TargetPage == 3, "duplicated-page links and existing links follow the original destination");
        session.Undo(); session.Navigate(2); session.MovePage(0);
        check(session.Document.Pages[1].Annotations[0].TargetPage == 0, "page reordering remaps internal links by stable page identity");
        session.DeletePage(); check(session.Document.AnnotationCount == 0, "links to a deleted destination are removed instead of retargeted");
        session.Undo(); check(session.Document.Pages[1].Annotations[0].TargetPage == 0, "undo restores removed destination links atomically");
        var selected = WorkspacePages.Select(original, [2, 0]);
        check(selected.Pages[1].Annotations[0].TargetPage == 0, "page extraction remaps links in the requested order");
        check(WorkspacePages.Select(original, [0, 1]).AnnotationCount == 0, "links to excluded extraction pages are removed");
        reject(() => WorkspacePages.Select(original, [0, 0]), "extraction rejects duplicate page identities");
        reject(() => WorkspacePages.Select(original, [-1]), "extraction rejects negative page indices");
        check(ReferenceEquals(WorkspacePages.Select(original, [0, 1, 2]), original), "unchanged page selection preserves snapshot identity");
        var combined = WorkspaceComposition.Append(original, original);
        check(combined.Pages[0].Annotations[0].TargetPage == 2 && combined.Pages[3].Annotations[0].TargetPage == 5, "combining workspaces offsets only incoming internal links");
        reject(() => WorkspaceJson.Validate(original.UpdatePage(original.Pages[0].Id, page => page with { Annotations = [link with { TargetPage = 10 }] })), "invalid internal destinations are rejected during workspace validation");
        reject(() => WorkspaceJson.Validate(original.UpdatePage(original.Pages[0].Id, page => page with { Annotations = [link with { TargetPage = null, Uri = "javascript:alert(1)" }] })), "unsafe workspace link protocols are rejected");

        // Reassembly must not leave the imported original link behind alongside its replacement.
        var pdf = PdfDocumentEngine.Open(PdfDocumentEngine.Save(original, SKTypeface.Default).Bytes, "navigation.pdf");
        var edit = new EditorSession(pdf); edit.Navigate(2); edit.MovePage(0);
        var moved = PdfDocumentEngine.Open(PdfDocumentEngine.Save(edit.Document, SKTypeface.Default).Bytes, "moved.pdf");
        check(moved.AnnotationCount == 1 && moved.Pages[1].Annotations.Single().TargetPage == 0, "native reassembly replaces imported links without stale duplicates");
        edit.DeletePage(); var removedBytes = PdfDocumentEngine.Save(edit.Document, SKTypeface.Default).Bytes;
        using (var removedNative = NativeReader.Open(new MemoryStream(removedBytes), PdfDocumentOpenMode.Modify))
            check(removedNative.Pages.Cast<PdfPage>().Sum(page => page.Annotations.Count) == 0, "deleted-target links cannot reappear from native source annotations");

        var namedBytes = CreateNamedNavigation();
        var named = PdfDocumentEngine.Open(namedBytes, "named-navigation.pdf");
        check(named.Pages[0].Annotations.Count(item => item.Kind == AnnotationKind.Link && item.TargetPage == 2) == 2, "legacy and name-tree destinations import as local editable links");
        var bookmarks = PdfNavigation.ReadBookmarks(named);
        check(bookmarks.Count == 2 && bookmarks[0].Title == "Overview" && bookmarks[0].PageIndex == 0 && bookmarks[1].Title == "Details" && bookmarks[1].Depth == 1 && bookmarks[1].PageIndex == 2, "source outline hierarchy exposes resolved local destinations");
        var reordered = WorkspacePages.Select(named, [2, 0, 1]);
        check(PdfNavigation.ReadBookmarks(reordered).Single(entry => entry.Title == "Details").PageIndex == 0, "source bookmarks follow page reordering in the viewer");
        var saved = PdfDocumentEngine.Save(named, SKTypeface.Default).Bytes;
        var roundtrip = PdfDocumentEngine.Open(saved, "named-roundtrip.pdf");
        check(roundtrip.Pages[0].Annotations.Length == 2 && roundtrip.Pages[0].Annotations.All(item => item.TargetPage == 2), "named links stay functional after native rewriting");

        var bookmarkSession = new EditorSession(named); bookmarkSession.Navigate(2); bookmarkSession.BookmarkPage("My review");
        var savedWithBookmark = PdfDocumentEngine.Save(bookmarkSession.Document, SKTypeface.Default).Bytes;
        var reopened = PdfDocumentEngine.Open(savedWithBookmark, "bookmarked.pdf");
        check(reopened.Pages[2].Bookmark == "My review" && PdfNavigation.ReadBookmarks(reopened).Count == 2, "managed PDF bookmarks reopen as editable workspace bookmarks without duplicate source entries");
        var clear = new EditorSession(reopened); clear.Navigate(2); clear.BookmarkPage("");
        var cleared = PdfDocumentEngine.Open(PdfDocumentEngine.Save(clear.Document, SKTypeface.Default).Bytes, "cleared.pdf");
        check(cleared.Pages.All(page => page.Bookmark.Length == 0) && PdfNavigation.ReadBookmarks(cleared).Count == 2, "removing the final workspace bookmark preserves unrelated source outlines");
        using (var authored = NativeReader.Open(new MemoryStream(namedBytes), PdfDocumentOpenMode.Modify))
        {
            authored.Outlines.Add("PdfSpace bookmarks", authored.Pages[1]);
            using var stream = new MemoryStream(); authored.Save(stream, false);
            var matchingTitle = PdfDocumentEngine.Open(stream.ToArray(), "unrelated-title.pdf");
            check(PdfNavigation.ReadBookmarks(PdfDocumentEngine.Open(PdfDocumentEngine.Save(matchingTitle, SKTypeface.Default).Bytes, "kept.pdf")).Any(entry => entry.Title == "PdfSpace bookmarks"), "a third-party outline is never deleted merely because its title matches PdfSpace");
        }
        Directory.CreateDirectory("artifacts/engine"); File.WriteAllBytes("artifacts/engine/navigation.pdf", namedBytes);
        File.WriteAllBytes("artifacts/structured/navigation-reordered.pdf", PdfDocumentEngine.Save(reordered, SKTypeface.Default).Bytes);
    }

    private static byte[] CreateNamedNavigation()
    {
        var sample = SampleDocument.Create();
        using var document = NativeReader.Open(new MemoryStream(sample.Sources[0].Bytes), PdfDocumentOpenMode.Modify);
        PdfArray Destination(int page)
        { var array = new PdfArray(document); array.Elements.Add(document.Pages[page].Reference!); array.Elements.Add(new PdfName("/Fit")); return array; }
        var wrapped = new PdfDictionary(document); wrapped.Elements["/D"] = Destination(2);
        var leaf = new PdfDictionary(document); var names = new PdfArray(document);
        names.Elements.Add(new PdfString("chapter.details")); names.Elements.Add(wrapped); leaf.Elements["/Names"] = names;
        document.Internals.AddObject(leaf);
        var nameRoot = new PdfDictionary(document); var kids = new PdfArray(document); kids.Elements.Add(leaf.Reference!); nameRoot.Elements["/Kids"] = kids;
        var catalogNames = new PdfDictionary(document); catalogNames.Elements["/Dests"] = nameRoot; document.Internals.Catalog.Elements["/Names"] = catalogNames;
        var legacy = new PdfDictionary(document); legacy.Elements["/legacy"] = Destination(2); document.Internals.Catalog.Elements["/Dests"] = legacy;
        var annotations = new PdfArray(document); document.Pages[0].Elements["/Annots"] = annotations;
        foreach (var destination in new PdfItem[] { new PdfString("chapter.details"), new PdfName("/legacy") })
        {
            var link = new PdfDictionary(document); link.Elements.SetName("/Type", "/Annot"); link.Elements.SetName("/Subtype", "/Link");
            link.Elements.SetRectangle("/Rect", new PdfRectangle(new PdfSharp.Drawing.XPoint(50, 500), new PdfSharp.Drawing.XPoint(200, 530)));
            link.Elements["/Dest"] = destination; document.Internals.AddObject(link); annotations.Elements.Add(link.Reference!);
        }
        var root = document.Outlines.Add("Overview", document.Pages[0], true); root.Outlines.Add("Details", document.Pages[2]);
        using var output = new MemoryStream(); document.Save(output, false); return output.ToArray();
    }
}
