using System.Text;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class PageMarkEnvelopeTests
{
    public static void Run(PdfWorkspace marked, PdfPageMarkSettings settings, SKTypeface font,
        Action<bool, string> check)
    {
        PdfDictionary Metadata(PdfPage page) => PdfObjects.Dictionary(
            PdfObjects.Array(page.Elements["/PdfSpacePageMarks"])!.Elements[0])!;
        PdfArray Guards(PdfPage page) => PdfObjects.Array(page.Elements["/PdfSpaceMarkIsolation"])!;
        void Refused(Action action, string name)
        {
            try { action(); }
            catch (InvalidOperationException) { check(true, name); return; }
            throw new Exception("FAILED: " + name);
        }
        var mutations = new (string Name, Action<PdfPage> Mutate)[]
        {
            ("missing isolation", page => page.Elements.Remove("/PdfSpaceMarkIsolation")),
            ("reversed isolation metadata", page => {
                var guards = Guards(page); (guards.Elements[0], guards.Elements[1]) = (guards.Elements[1], guards.Elements[0]);
            }),
            ("altered isolation operator", page => PdfObjects.Dictionary(Guards(page).Elements[0])!.Stream.Value = Encoding.ASCII.GetBytes("q\n0 0 10 10 re W n\n")),
            ("duplicate isolation invocation", page => page.Contents.Elements.Insert(0, Guards(page).Elements[0])),
            ("mark inside source isolation", page => {
                var stream = Metadata(page).Elements["/Stream"]!;
                page.Contents.Elements.RemoveAt(page.Contents.Elements.Count - 1);
                page.Contents.Elements.Insert(1, stream);
            }),
            ("front mark moved behind source", page => {
                var stream = Metadata(page).Elements["/Stream"]!;
                page.Contents.Elements.RemoveAt(page.Contents.Elements.Count - 1);
                page.Contents.Elements.Insert(0, stream);
            }),
            ("untracked prefix content", page => {
                var extra = new PdfDictionary(page.Owner); page.Owner.Internals.AddObject(extra);
                extra.CreateStream("q Q\n"u8.ToArray()); page.Contents.Elements.Insert(0, extra.Reference!);
            }),
            ("untracked suffix content", page => {
                var extra = new PdfDictionary(page.Owner); page.Owner.Internals.AddObject(extra);
                extra.CreateStream("q Q\n"u8.ToArray()); page.Contents.Elements.Add(extra.Reference!);
            })
        };
        foreach (var (name, mutate) in mutations)
        {
            using var native = PdfDocumentEngine.OpenNative(marked.Sources[0].Bytes);
            _ = native.Pages[0].Contents;
            mutate(native.Pages[0]);
            var changed = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), name + ".pdf");
            check(!PdfPageMarks.Read(changed, [0]).Single().Intact, "mark envelope detects " + name);
            check(PdfPageMarks.Read(changed, [1]).Single().Intact, "mark envelope preserves untouched page: " + name);
            Refused(() => PdfPageMarks.Apply(changed, [0, 1], settings, font), "unchanged apply rejects " + name);
            Refused(() => PdfPageMarks.Remove(changed, [0], settings.Kind, font), "mark removal rejects " + name);
        }
        using (var native = PdfDocumentEngine.OpenNative(marked.Sources[0].Bytes))
        {
            native.Pages[0].Elements.Remove("/PdfSpacePageMarks");
            var orphan = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "orphan-isolation.pdf");
            Refused(() => PdfPageMarks.Read(orphan, [0]), "orphan isolation is not mistaken for unmarked source content");
            Refused(() => PdfPageMarks.Apply(orphan, [0], settings, font), "new marks cannot silently consume orphan isolation guards");
        }
        var watermark = new PdfPageMarkSettings { Kind = PdfPageMarkKind.Watermark, BehindContent = true, FontSize = 24 };
        var combined = PdfPageMarks.Apply(marked, [0], watermark, font).Workspace;
        check(PdfPageMarks.Read(combined, [0]).Count == 2 && PdfPageMarks.Read(combined, [0]).All(m => m.Intact),
            "front headers and behind-content watermarks share a valid envelope");
        var removed = PdfPageMarks.Remove(combined, [0], PdfPageMarkKind.HeaderFooter, font).Workspace;
        check(PdfPageMarks.Read(removed, [0]).Single().Intact, "removing a front mark retains the valid behind-content envelope");
        var restored = PdfPageMarks.Remove(removed, [0], PdfPageMarkKind.Watermark, font).Workspace;
        check(PdfPageMarks.Read(restored, [0]).Count == 0, "removing final marks removes their isolation metadata");
    }
}
