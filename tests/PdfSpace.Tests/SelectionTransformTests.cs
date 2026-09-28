using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class SelectionTransformTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var bounds = new RectD(50, 100, 160, 80);
        check(SelectionTransform.ConstrainMove(new(20, 8), true) == new PointD(20, 0), "Shift locks horizontal native movement");
        check(SelectionTransform.ConstrainMove(new(8, -20), true) == new PointD(0, -20), "Shift locks vertical native movement");
        check(SelectionTransform.ConstrainMove(new(-10, 10), true) == new PointD(-10, 0), "equal axis displacements are deterministic");
        check(SelectionTransform.ConstrainMove(new(7, 9), false) == new PointD(7, 9), "free movement is unchanged");
        check(SelectionTransform.Resize(bounds, 4, new(40, 4), true) == new RectD(50, 100, 200, 100), "corner resize keeps original aspect and opposite corner");
        check(SelectionTransform.Resize(bounds, 0, new(-40, -4), true) == new RectD(10, 80, 200, 100), "top-left proportional resize keeps bottom-right anchor");
        check(SelectionTransform.Resize(bounds, 3, new(40, 9), true) == new RectD(50, 90, 200, 100), "side proportional resize centers the perpendicular axis");
        check(SelectionTransform.Resize(bounds, 4, new(20, 5), false, true) == new RectD(30, 95, 200, 90), "Alt resize expands both sides around original center");
        check(SelectionTransform.Resize(bounds, 4, new(20, 5), true, true) == new RectD(30, 90, 200, 100), "Shift+Alt keeps both center and proportions");
        check(SelectionTransform.Resize(bounds, 4, default, true, true) == bounds, "stationary handle produces exact identity geometry");
        check(SelectionTransform.Create(new(10,20),new(-20,10),true) == new RectD(-20,-10,30,30), "Shift shape creation works in negative quadrants");
        check(SelectionTransform.Create(new(100,100),new(130,120),true,true) == new RectD(70,70,60,60), "Shift+Alt ellipse creation produces a centered circle");
        for (var handle = 0; handle < 8; handle++)
        {
            var centered = SelectionTransform.Resize(bounds, handle, new(11, -17), true, true);
            check(centered.Center.Distance(bounds.Center) < 1e-10 && Math.Abs(centered.Width/centered.Height-2) < 1e-10, $"handle {handle} preserves center and aspect");
            var unchanged = SelectionTransform.Resize(bounds, handle, default);
            check(unchanged == bounds, $"handle {handle} no-motion does not jump by hit slop");
        }
        var clipped = SelectionTransform.Resize(bounds,4,new(-500,-500),true);
        check(clipped.Width >= .01 && clipped.Height >= .01 && clipped.X==bounds.X && clipped.Y==bounds.Y, "crossing the resize anchor clamps without reflecting native content");
        var line = new RectD(2,3,0,10);
        check(SelectionTransform.Resize(line,5,new(0,10),true) == new RectD(2,3,0,20), "zero-width native lines have a defined resize path");
        reject(()=>SelectionTransform.Resize(bounds,-1,default),"negative handle rejected");
        reject(()=>SelectionTransform.Resize(bounds,8,default),"unknown handle rejected");
        reject(()=>SelectionTransform.Resize(bounds,4,new(double.NaN,1)),"nonfinite gesture displacement rejected");
        reject(()=>SelectionTransform.Resize(bounds,4,new(1,1),minimumExtent:0),"zero clamp extent rejected");
        reject(()=>SelectionTransform.Resize(bounds,4,new(1,1),minimumExtent:double.PositiveInfinity),"nonfinite clamp extent rejected");
        reject(()=>SelectionTransform.Create(new(double.MaxValue,0),new(-double.MaxValue,0)),"overflowing shape bounds rejected");
        var random = new Random(529);
        var valid = true;
        for (var i=0;i<2000;i++)
        {
            var b = new RectD(random.Next(-1000,1000),random.Next(-1000,1000),random.Next(1,600),random.Next(1,900));
            var r = SelectionTransform.Resize(b,i%8,new(random.Next(-1200,1200),random.Next(-1200,1200)),true,true);
            valid &= r.IsFinite && r.Width>=.0099999 && r.Height>=.0099999 && r.Center.Distance(b.Center)<1e-8 && Math.Abs(r.Width/r.Height-b.Width/b.Height)<1e-9;
        }
        check(valid,"2000 deterministic resize cases preserve invariants through all quadrants");

        var original = ObjectEditingSample.Create();
        var objects = PdfObjectEditor.Read(original,0);
        var item = objects.First(o=>o.Kind==PdfPageObjectKind.Path);
        var target = SelectionTransform.Resize(item.Bounds,4,new(32,9),true,true);
        var session = new EditorSession(original);
        session.Execute("Constrained native resize", d=>PdfObjectEditor.SetBounds(d,[item],target));
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        var saved = PdfDocumentEngine.Save(session.Document, font);
        var reloaded = PdfDocumentEngine.Open(saved.Bytes,"constrained.pdf");
        var actual = PdfObjectEditor.Read(reloaded,0).First(o=>o.Kind==PdfPageObjectKind.Path).Bounds;
        check(Math.Abs(actual.X-target.X)<.01 && Math.Abs(actual.Y-target.Y)<.01 && Math.Abs(actual.Width-target.Width)<.01 && Math.Abs(actual.Height-target.Height)<.01,"constrained resize survives native PDF save and reopen");
        check(session.UndoCount==1,"entire constrained gesture is one undo transaction");
        session.Undo();check(ReferenceEquals(session.Document,original),"constrained undo restores exact source snapshot");
        session.Redo();check(session.UndoCount==1,"constrained redo reuses the edited snapshot");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/constrained-native-original.pdf",PdfDocumentEngine.Save(original, font).Bytes);
        File.WriteAllBytes("artifacts/structured/constrained-native-resized.pdf",saved.Bytes);
    }
}
