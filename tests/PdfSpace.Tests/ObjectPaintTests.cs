using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class ObjectPaintTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var source = ObjectEditingSample.Create();
        var objects = PdfObjectEditor.Read(source, 0);
        var path = objects.First(o => o.Kind == PdfPageObjectKind.Path);
        var sibling = objects.Where(o => o.Kind == PdfPageObjectKind.Path).Skip(1).First();
        var styled = PdfObjectEditor.SetAppearance(source, [path], new(StrokeEnabled: true, Stroke: 0xFF000000,
            StrokeWidth: 8, LineCap: PdfLineCap.Round, LineJoin: PdfLineJoin.Bevel, MiterLimit: 4, Dash: new([9, 3, 1], 2)));
        var paint = PdfObjectEditor.Read(styled, 0).First(o => o.Kind == PdfPageObjectKind.Path).Paint;
        check(paint.StrokeWidth == 8 && paint.LineCap == PdfLineCap.Round && paint.LineJoin == PdfLineJoin.Bevel && paint.MiterLimit == 4,
            "native stroke width/cap/join/miter survive object rescan");
        check(paint.Dash?.Lengths.SequenceEqual([9d, 3, 1]) == true && paint.Dash.Phase == 2,
            "odd-length native dash pattern and phase survive serialization");
        check(PdfObjectEditor.Read(styled, 0).First(o => o.Bounds == sibling.Bounds).Paint.LineCap == sibling.Paint.LineCap,
            "stroke overrides do not leak into sibling state");
        check(Render(source, 1).SequenceEqual(Render(styled, 1)), "stroke editing preserves shared-resource page pixels");
        var reopened = PdfDocumentEngine.Open(PdfDocumentEngine.Save(styled, SKTypeface.Default).Bytes, "styled.pdf");
        check(PdfObjectEditor.Read(reopened, 0).First(o => o.Kind == PdfPageObjectKind.Path).Paint.Dash?.Lengths.Count == 3,
            "native dash pattern survives PDF save/reopen");
        var cleared = PdfObjectEditor.SetAppearance(styled, [PdfObjectEditor.Read(styled, 0).First(o => o.Kind == PdfPageObjectKind.Path)], new(Dash: PdfDashPattern.Solid));
        check(PdfObjectEditor.Read(cleared, 0).First(o => o.Kind == PdfPageObjectKind.Path).Paint.Dash?.Lengths.Count == 0,
            "empty dash array explicitly restores a solid stroke");
        var lengths = new[] { 8d, 4d };
        var immutable = new PdfDashPattern(lengths, 1); lengths[0] = 99;
        check(immutable.Lengths[0] == 8, "dash pattern defensively owns its immutable input");
        reject(() => PdfObjectEditor.SetAppearance(source, [path], new(Dash: new([1, 2], .5))), "fractional dash phase authoring rejects preview quantization");
        reject(() => new PdfDashPattern([0, 0]), "all-zero dash pattern rejected");
        reject(() => new PdfDashPattern([-1, 2]), "negative dash length rejected");
        reject(() => new PdfDashPattern([double.NaN]), "nonfinite dash length rejected");
        reject(() => new PdfDashPattern(Enumerable.Repeat(1d, 65)), "dash pattern length is bounded");
        reject(() => new PdfDashPattern([1], -1), "negative dash phase rejected");
        reject(() => PdfObjectEditor.SetAppearance(source, [path], new(LineCap: (PdfLineCap)9)), "unknown cap rejected atomically");
        reject(() => PdfObjectEditor.SetAppearance(source, [path], new(LineJoin: (PdfLineJoin)9)), "unknown join rejected atomically");
        reject(() => PdfObjectEditor.SetAppearance(source, [path], new(MiterLimit: .5)), "miter limit below one rejected");
        reject(() => PdfObjectEditor.SetAppearance(source, [path], new(MiterLimit: double.NaN)), "nonfinite miter limit rejected");

        var translucent = PdfObjectEditor.SetCompositing(source, [path], new(.5, .6, PdfBlendMode.Multiply));
        var translucentPath = PdfObjectEditor.Read(translucent, 0).First(o => o.Kind == PdfPageObjectKind.Path);
        check(translucentPath.Paint.FillOpacity == .5 && translucentPath.Paint.StrokeOpacity == .6 && translucentPath.Paint.BlendMode == PdfBlendMode.Multiply,
            "per-occurrence alpha and blend values survive native roundtrip");
        check(Pixel(translucent, new(70, 380)).Red > Pixel(source, new(70, 380)).Red + 60,
            "native renderer shows translucent path over white");
        check(Render(translucent, 1).SequenceEqual(Render(source, 1)), "alpha override leaves another shared page pixel-identical");
        check(ReferenceEquals(PdfObjectEditor.SetCompositing(translucent, [translucentPath], new(.5, .6, PdfBlendMode.Multiply)), translucent),
            "repeated equivalent compositing does not rewrite source or add history");
        check(ReferenceEquals(PdfObjectEditor.SetCompositing(source, [path], new()), source), "empty compositing patch preserves snapshot identity");
        foreach (var mode in Enum.GetValues<PdfBlendMode>())
        {
            var edited = PdfObjectEditor.SetCompositing(source, [path], new(BlendMode: mode));
            check(PdfObjectEditor.Read(edited, 0).First(o => o.Kind == PdfPageObjectKind.Path).Paint.BlendMode == mode,
                "native blend-name roundtrip " + mode);
        }
        foreach (var invalid in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
            reject(() => PdfObjectEditor.SetCompositing(source, [path], new(invalid)), "invalid opacity rejected " + invalid);
        reject(() => PdfObjectEditor.SetCompositing(source, [path], new(BlendMode: (PdfBlendMode)90)), "unrecognized blend mode rejected");
        var form = PdfObjectEditor.Read(source, 0, true).First(o => o.Kind == PdfPageObjectKind.Form);
        reject(() => PdfObjectEditor.SetCompositing(source, [form], new(.5)), "Form contents are not mislabeled as uniform group opacity");
        reject(() => PdfObjectEditor.SetCompositing(translucent, [path], new(.2)), "stale paint descriptor rejected");
        var session = new EditorSession(source);
        var chosen = objects.Where(o => o.Kind is PdfPageObjectKind.Image or PdfPageObjectKind.Text).Take(3).ToArray();
        session.Execute("Opacity", d => PdfObjectEditor.SetCompositing(d, chosen, new(.7, .8)));
        check(session.UndoCount == 1, "mixed object opacity is one history transaction");
        session.Undo(); check(ReferenceEquals(session.Document, source), "opacity undo restores original source identity");
        session.Redo(); check(PdfObjectEditor.Read(session.Document, 0).Count(o => o.Paint.FillOpacity == .7) == chosen.Length,
            "opacity redo preserves the selected occurrence set");

        var fixture = StateFixture();
        var initial = PdfObjectEditor.Read(fixture, 0);
        var nativePaths = initial.Where(o => o.Kind == PdfPageObjectKind.Path).ToArray();
        check(nativePaths[0].Paint.LineCap == PdfLineCap.Square && nativePaths[0].Paint.Dash?.Phase == 3 && nativePaths[0].Paint.Dash?.Lengths.Count == 3,
            "scanner resolves dash and cap inherited through ExtGState");
        check(nativePaths[1].Paint.LineCap == PdfLineCap.Round && nativePaths[2].Paint.LineCap == PdfLineCap.Square,
            "graphics save/restore restores effective stroke state");
        var text = initial.First(o => o.Kind == PdfPageObjectKind.Text);
        check(text.Paint.FillOpacity is null && text.Paint.StrokeOpacity is null,
            "mixed text-paint alpha is not falsely reported as the initial state");
        var afterText = PdfObjectEditor.SetCompositing(fixture, [text], new(.45, .55, PdfBlendMode.Screen));
        var after = PdfObjectEditor.Read(afterText, 0).Where(o => o.Kind == PdfPageObjectKind.Text).ToArray();
        check(after[0].Paint.FillOpacity == .45 && after[0].Paint.StrokeOpacity == .55 && after[0].Paint.BlendMode == PdfBlendMode.Screen,
            "text alpha override survives internal gs changes before all show operators");
        check(after[1].Paint.FillOpacity == .8 && after[1].Paint.StrokeOpacity == .9 && after[1].Paint.BlendMode == PdfBlendMode.Normal,
            "text appearance edit restores original state side effects for following text");
        check(RegionEqual(Render(fixture, 0), Render(afterText, 0), new(0, 165, 400, 45)),
            "following inherited-state text retains exact pixels after alpha edit");
        check(PdfSpace.Documents.PdfReader.ExtractText(afterText).Contains("Neighbor"), "text transparency retains searchable native content");

        using var rgba = new SKBitmap(8, 8); rgba.Erase(new SKColor(220, 30, 20, 120));
        using var png = rgba.Encode(SKEncodedImageFormat.Png, 100);
        var masked = PdfImageEditor.OpenImage(png.ToArray(), "alpha.png");
        var maskedImage = PdfObjectEditor.Read(masked, 0).Single(o => o.Kind == PdfPageObjectKind.Image);
        var faded = PdfObjectEditor.SetCompositing(masked, [maskedImage], new(.5));
        using (var pdf = PdfReader.Open(new MemoryStream(PdfDocumentEngine.Save(faded, SKTypeface.Default).Bytes), PdfDocumentOpenMode.Modify))
            check(pdf.Internals.GetAllObjects().OfType<PdfDictionary>().Any(d => d.Elements.GetName("/Subtype") == "/Image" && d.Elements.ContainsKey("/SMask")),
                "image opacity retains the original native alpha soft mask");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/paint-mask-before.pdf", PdfDocumentEngine.Save(masked, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-mask-after.pdf", PdfDocumentEngine.Save(faded, SKTypeface.Default).Bytes);
        check(Pixel(faded, new(1, 1)).Green > Pixel(masked, new(1, 1)).Green, "image soft mask combines with nonstroking opacity");

        Directory.CreateDirectory("artifacts/structured"); Directory.CreateDirectory("artifacts/engine");
        File.WriteAllBytes("artifacts/structured/paint-original.pdf", PdfDocumentEngine.Save(source, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-styled.pdf", PdfDocumentEngine.Save(styled, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-translucent.pdf", PdfDocumentEngine.Save(translucent, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-text-before.pdf", PdfDocumentEngine.Save(fixture, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-text-after.pdf", PdfDocumentEngine.Save(afterText, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/paint-softmask.pdf", PdfDocumentEngine.Save(faded, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/engine/paint-image.pdf", PdfDocumentEngine.Save(masked, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/engine/paint-state.pdf", PdfDocumentEngine.Save(fixture, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/engine/paint-dense.pdf", DenseFixture());
    }
    private static PdfWorkspace StateFixture()
    {
        using var pdf = new PdfDocument(); var page = pdf.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(400); page.Height = PdfSharp.Drawing.XUnit.FromPoint(300);
        var font = new PdfDictionary(pdf); font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica");
        var fonts = new PdfDictionary(pdf); fonts.Elements["/F1"] = font;
        var gs1 = new PdfDictionary(pdf); gs1.Elements.SetReal("/ca", .25); gs1.Elements.SetReal("/CA", .5);
        gs1.Elements.SetInteger("/LC", 2); gs1.Elements.SetInteger("/LJ", 2); gs1.Elements.SetReal("/LW", 3); gs1.Elements.SetReal("/ML", 4);
        gs1.Elements.SetName("/BM", "/Multiply"); gs1.Elements["/D"] = new PdfArray(pdf, new PdfArray(pdf, new PdfInteger(6), new PdfInteger(2), new PdfInteger(1)), new PdfInteger(3));
        var gs2 = new PdfDictionary(pdf); gs2.Elements.SetReal("/ca", .8); gs2.Elements.SetReal("/CA", .9); gs2.Elements.SetName("/BM", "/Normal");
        var gs = new PdfDictionary(pdf); gs.Elements["/First"] = gs1; gs.Elements["/Second"] = gs2;
        var resources = new PdfDictionary(pdf); resources.Elements["/Font"] = fonts; resources.Elements["/ExtGState"] = gs; page.Elements["/Resources"] = resources;
        SetContent(pdf, page, "/First gs 0 0 1 RG 20 260 m 100 260 l S\nq 1 J [3] 2 d /Second gs 20 230 80 10 re f Q\n20 200 80 10 re f\nBT /F1 20 Tf 1 0 0 1 20 150 Tm (A) Tj /Second gs 25 0 Td (B) Tj ET\nBT 1 0 0 1 20 110 Tm (Neighbor) Tj ET\n");
        using var stream = new MemoryStream(); pdf.Save(stream, false); return PdfDocumentEngine.Open(stream.ToArray(), "state.pdf");
    }
    private static byte[] DenseFixture()
    {
        using var pdf = new PdfDocument(); var page = pdf.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(2000); page.Height = PdfSharp.Drawing.XUnit.FromPoint(2000);
        var content = new StringBuilder("0.2 0.4 0.7 rg\n");
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            content.Append(x * 30 + 10).Append(' ').Append(y * 30 + 10).Append(" 12 12 re f\n");
        SetContent(pdf, page, content.ToString()); using var stream = new MemoryStream(); pdf.Save(stream, false); return stream.ToArray();
    }
    private static void SetContent(PdfDocument pdf, PdfPage page, string text)
    {
        var stream = new PdfDictionary(pdf); stream.CreateStream(Encoding.ASCII.GetBytes(text)); pdf.Internals.AddObject(stream); page.Elements["/Contents"] = stream.Reference!;
    }
    private static byte[] Render(PdfWorkspace d, int page) { using var r = new PdfRenderer(); return r.ExportPng(d, page, 1); }
    private static SKColor Pixel(PdfWorkspace d, PointD p) { using var bitmap = SKBitmap.Decode(Render(d, 0)); return bitmap.GetPixel((int)p.X, (int)p.Y); }
    private static bool RegionEqual(byte[] a, byte[] b, RectD r)
    {
        using var x = SKBitmap.Decode(a); using var y = SKBitmap.Decode(b);
        for (var j = (int)r.Y; j < r.Bottom; j++) for (var i = (int)r.X; i < r.Right; i++) if (x.GetPixel(i, j) != y.GetPixel(i, j)) return false;
        return true;
    }
}
