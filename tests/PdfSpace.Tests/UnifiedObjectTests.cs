using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using TextReader = PdfSpace.Documents.PdfReader;

internal static class UnifiedObjectTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var original = ObjectEditingSample.Create();
        var all = PdfObjectEditor.Read(original, 0);
        var images = all.Where(o => o.Kind == PdfPageObjectKind.Image).ToArray();
        var paths = all.Where(o => o.Kind == PdfPageObjectKind.Path).ToArray();
        var text = all.Where(o => o.Kind == PdfPageObjectKind.Text).ToArray();
        check(images.Length == 2 && paths.Length == 4 && text.Length >= 4, "unified inventory discovers native images, paths and text");
        check(all.All(o => o.Editable) && all.All(o => o.Bounds.IsFinite), "sample descriptors have editable finite geometry");
        check(PdfObjectEditor.Read(new PdfWorkspace(), 0).Length == 0, "blank page has no fabricated native objects");
        check(PdfObjectEditor.Read(original, 0, true).Any(o => o.Kind == PdfPageObjectKind.Form && o.IsContainer), "whole-Form selection exposes container invocations");

        var nativeText = text.First(o => o.ScopePath == images[0].ScopePath);
        var selection = new[] { images[0], nativeText };
        var beforePage1 = Render(original, 1);
        var moved = PdfObjectEditor.Transform(original, selection, PdfAffineMatrix.Translate(18, 12));
        var movedAll = PdfObjectEditor.Read(moved, 0);
        check(Near(movedAll.First(o => o.Kind == PdfPageObjectKind.Image).Bounds, images[0].Bounds.Translate(new(18,12))), "mixed native transform moves selected nested image");
        check(Near(movedAll.First(o => o.Kind == PdfPageObjectKind.Text && o.Text == nativeText.Text).Bounds, nativeText.Bounds.Translate(new(18,12))), "mixed native transform moves nested text in the same transaction");
        check(movedAll.Where(o => o.Kind == PdfPageObjectKind.Image).Last().Bounds == images[1].Bounds, "mixed edit preserves shared sibling image geometry");
        check(Render(moved,1).SequenceEqual(beforePage1), "mixed nested edit preserves the other page pixels exactly");
        var session = new EditorSession(original);
        session.Execute("Move mixed selection", _ => moved);
        check(session.UndoCount == 1, "mixed edit produces a single history transaction");
        session.Undo(); check(ReferenceEquals(session.Document, original), "undo restores original object source identity");
        session.Redo(); check(ReferenceEquals(session.Document, moved), "redo restores the edited native snapshot");
        reject(() => PdfObjectEditor.Delete(moved, selection), "stale mixed-object selection is rejected");
        reject(() => PdfObjectEditor.Delete(original, [images[0] with { Bounds = new(0,0,1,1) }]), "forged object bounds cannot retarget a native edit");
        reject(() => PdfObjectEditor.Delete(original, [images[0], images[0]]), "duplicate selection cannot edit an occurrence twice");
        var container = PdfObjectEditor.Read(original, 0, true).First(o => o.Kind == PdfPageObjectKind.Form);
        reject(() => PdfObjectEditor.Delete(original, [container, images[0]]), "ancestor and descendant cannot be edited together");
        reject(() => PdfObjectEditor.Transform(original, selection, PdfAffineMatrix.Scale(0,1)), "singular object transform rejected atomically");
        reject(() => PdfObjectEditor.Transform(original, selection, PdfAffineMatrix.Translate(double.NaN,0)), "nonfinite transform rejected");

        var rectangle = paths[0];
        var sized = PdfObjectEditor.SetBounds(original, [rectangle], new(75,350,140,95));
        check(Near(PdfObjectEditor.Read(sized,0).First(o => o.Kind == PdfPageObjectKind.Path).Bounds, new(75,350,140,95)), "exact path geometry edits native coordinates");
        var rotated = PdfObjectEditor.Transform(original,[rectangle],PdfAffineMatrix.Around(rectangle.Bounds.Center,PdfAffineMatrix.Rotate(90)));
        var rb = PdfObjectEditor.Read(rotated,0).First(o => o.Kind == PdfPageObjectKind.Path).Bounds;
        check(Math.Abs(rb.Width-rectangle.Bounds.Height)<.01 && Math.Abs(rb.Height-rectangle.Bounds.Width)<.01, "native vector quarter-turn exchanges dimensions");
        var reflected = PdfObjectEditor.Transform(original,[rectangle],PdfAffineMatrix.Around(rectangle.Bounds.Center,PdfAffineMatrix.Scale(-1,1)));
        check(Near(PdfObjectEditor.Read(reflected,0).First(o => o.Kind == PdfPageObjectKind.Path).Bounds,rectangle.Bounds), "vector reflection preserves selection extents");
        var changedPath = PdfObjectEditor.SetPath(original, rectangle, [new("re",[48,410,170,90])]);
        check(Math.Abs(PdfObjectEditor.Read(changedPath,0).First(o => o.Kind==PdfPageObjectKind.Path).Bounds.Width-170)<.01, "path node replacement writes real rectangle operands");
        var curve = paths.Single(o=>o.Nodes.Any(n=>n.Operator=="c"));
        var curveNodes = curve.Nodes.Select(n=>new PdfPathNode(n.Operator,n.Values.ToArray())).ToArray();
        curveNodes[1].Values[1] += 35;
        var reshaped = PdfObjectEditor.SetPath(original,curve,curveNodes);
        check(PdfObjectEditor.Read(reshaped,0).Single(o=>o.Kind==PdfPageObjectKind.Path && o.Nodes.Any(n=>n.Operator=="c")).Nodes[1].Values[1] == curveNodes[1].Values[1], "Bezier control-point mutation persists in native path");
        reject(()=>PdfObjectEditor.SetPath(original,rectangle,[new("l",[2,3])]),"path must begin with move or rectangle");
        reject(()=>PdfObjectEditor.SetPath(original,rectangle,[new("m",[double.NaN,3])]),"nonfinite path nodes rejected");
        reject(()=>PdfObjectEditor.SetAppearance(original,[images[0]],new(Fill:0xFF000000)), "path appearance cannot silently paint over an image");

        var blue = PdfObjectEditor.SetAppearance(original,[rectangle],new(Fill:0xFF0000FF, FillEnabled:true));
        check(Pixel(blue,new(rectangle.Bounds.X+10,rectangle.Bounds.Y+10)).Blue > 245, "native path fill has the requested color");
        var aligned = PdfObjectEditor.Align(original,paths.Take(2).ToArray(),PdfObjectAlignment.Left);
        var alignedPaths=PdfObjectEditor.Read(aligned,0).Where(o=>o.Kind==PdfPageObjectKind.Path).Take(2).ToArray();
        check(Math.Abs(alignedPaths[0].Bounds.X-alignedPaths[1].Bounds.X)<.01,"alignment applies one native transform per selected object");
        var distributed=PdfObjectEditor.Distribute(original,paths.Take(3).ToArray(),true);
        var centers=PdfObjectEditor.Read(distributed,0).Where(o=>o.Kind==PdfPageObjectKind.Path).Take(3).Select(o=>o.Bounds.Center.X).Order().ToArray();
        check(Math.Abs((centers[1]-centers[0])-(centers[2]-centers[1]))<.02,"distribution uses evenly spaced native object centers");

        var overlap = new PointD(160,405);
        var beforeColor = Pixel(original,overlap);
        var front = PdfObjectEditor.Arrange(original,[rectangle],PdfObjectOrder.Front);
        check(Pixel(front,overlap).Blue > Pixel(front,overlap).Red && beforeColor.Red>beforeColor.Blue, "arrange to front preserves captured blue fill over red sibling");
        check(Render(front,1).SequenceEqual(beforePage1),"arrange leaves shared content on other pages unchanged");
        var grouped=PdfObjectEditor.Group(original,paths.Take(2).ToArray());
        var group=PdfObjectEditor.Read(grouped,0).Single(o=>o.Kind==PdfPageObjectKind.Form);
        check(Near(group.Bounds, PdfObjectEditor.SelectionBounds(paths.Take(2).ToArray())),"generated native group bounds equal selected union");
        check(Render(grouped,0).SequenceEqual(Render(original,0)),"grouping leaves original page pixels unchanged");
        var ungrouped=PdfObjectEditor.Ungroup(grouped,group);
        check(Render(ungrouped,0).SequenceEqual(Render(original,0)),"ungrouping leaves page pixels unchanged");
        check(PdfObjectEditor.Read(ungrouped,0).Count(o=>o.Kind==PdfPageObjectKind.Path)==paths.Length,"ungroup exposes editable native paths again");
        reject(()=>PdfObjectEditor.Group(original,[paths[0],paths[2]]),"group rejects intervening unselected objects");
        reject(()=>PdfObjectEditor.Group(original,[rectangle,images[0]]),"group rejects incompatible native scopes");

        var firstText=text.Single(o=>o.Text=="Edit mixed native objects");
        var secondText=text.Single(o=>o.Text=="Inherited font remains unchanged");
        var deletedText=PdfObjectEditor.Delete(original,[firstText]);
        check(!TextReader.ExtractText(deletedText).Contains(firstText.Text),"delete removes actual native text painting");
        check(RegionEqual(Render(original,0),Render(deletedText,0),secondText.Bounds.Inflate(4)),"text deletion preserves inherited font state for following text");
        var movedText=PdfObjectEditor.Transform(original,[firstText],PdfAffineMatrix.Translate(12,-25));
        check(RegionEqual(Render(original,0),Render(movedText,0),secondText.Bounds.Inflate(4)),"text transformation preserves following text rendering");
        var duplicated=PdfObjectEditor.Duplicate(original,selection,new(25,130));
        check(PdfObjectEditor.Read(duplicated,0).Count(o=>o.Kind==PdfPageObjectKind.Image)==3,"mixed duplication inserts another native image occurrence");
        check(TextReader.Words(duplicated,duplicated.Pages[0]).Count(w=>w.Text=="Shared")==3,"duplicated native text remains searchable");
        check(Render(duplicated,1).SequenceEqual(beforePage1),"mixed duplication preserves other source page");

        var clipboard=PdfObjectEditor.Copy(original,[rectangle,images[0],nativeText]);
        var pasted=PdfObjectEditor.Paste(new PdfWorkspace(),0,clipboard,new(40,60));
        var pastedObjects=PdfObjectEditor.Read(pasted,0);
        check(pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Path)==1 && pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Image)==1 && pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Text)==1,"cross-document paste retains mixed native object types");
        check(TextReader.ExtractText(pasted).Contains(nativeText.Text),"pasted source text remains searchable");
        var sensitive=original with {Sources=original.Sources.Select(s=>s with {Sensitive=true}).ToArray()};
        var sensitivePaste=PdfObjectEditor.Paste(new PdfWorkspace(),0,PdfObjectEditor.Copy(sensitive,[rectangle]),new(40,60));
        check(sensitivePaste.IsSensitive,"private object clipboard propagates source sensitivity");

        check(ReferenceEquals(PdfObjectEditor.Arrange(original,[paths[^1]],PdfObjectOrder.Front),original), "arranging an already-front object leaves source identity and history unchanged");

        var cropBox=new RectD(rectangle.Bounds.X+20,rectangle.Bounds.Y+10,40,30);
        var cropped=PdfObjectEditor.Crop(original,[rectangle],cropBox);
        check(Pixel(cropped,new(rectangle.Bounds.X+5,rectangle.Bounds.Y+5))==SKColors.White,"native object crop hides only the selected path outside its clip");
        check(Pixel(cropped,cropBox.Center).Blue>100,"native object crop retains interior path pixels");
        var clipped=paths[^1];
        var clippedCopy=PdfObjectEditor.Copy(original,[clipped]);
        var clippedPaste=PdfObjectEditor.Paste(new PdfWorkspace(),0,clippedCopy,new(clipped.Bounds.X,clipped.Bounds.Y));
        check(Pixel(clippedPaste,new(30,640))==SKColors.White,"copy and paste preserve inherited clipping outside visible source");
        reject(()=>PdfObjectEditor.Arrange(original,[clipped],PdfObjectOrder.Back),"arrangement across clip boundaries is rejected");

        using var face=SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        var reflow=PdfObjectEditor.ReplaceTextBlock(original,firstText,"Żółć café – Reviewed\nSecond line",face,14,new(48,430,300,90));
        check(TextReader.ExtractText(reflow).Contains("Żółć café") && !TextReader.ExtractText(reflow).Contains(firstText.Text),"replacement block writes searchable Unicode instead of overlays");
        check(RegionEqual(Render(original,0),Render(reflow,0),secondText.Bounds.Inflate(4)),"block replacement preserves neighboring inherited-font text");
        reject(()=>PdfObjectEditor.ReplaceTextBlock(original,firstText,"Text that cannot fit",face,14,new(40,40,30,5)),"overflowing block replacement is rejected without partial edits");
        reject(()=>PdfObjectEditor.ReplaceTextBlock(original,firstText,"مرحبا",face,14,new(40,40,300,50)),"unshaped complex replacement rejected explicitly");
        var inserted=PdfObjectEditor.InsertPath(new PdfWorkspace(),0,[new("re",[30,40,80,60])]);
        check(Near(PdfObjectEditor.Read(inserted,0).Single().Bounds,new(30,40,80,60)),"inserted vector remains an editable native path");
        var withText=PdfObjectEditor.InsertText(inserted,0,"Native searchable text",face,12,new(40,160,300,60));
        check(TextReader.ExtractText(withText).Contains("Native searchable text"),"native text insertion works on a new page");
        var roundtrip=PdfDocumentEngine.Open(PdfDocumentEngine.Save(reflow,face).Bytes,"reflow.pdf");
        check(TextReader.ExtractText(roundtrip).Contains("Żółć café"),"Unicode block survives structured save and reopen");
        check(PdfObjectEditor.Read(roundtrip,0).Any(o=>o.Kind==PdfPageObjectKind.Path),"native paths remain discoverable after save/reopen");
        var workspaceRoundtrip=PdfDocumentEngine.PrepareWorkspace(WorkspaceJson.Load(WorkspaceJson.Save(grouped)));
        check(PdfObjectEditor.Read(workspaceRoundtrip,0).Any(o=>o.Kind==PdfPageObjectKind.Form),"generated group survives workspace serialization");

        ObjectSafetyTests.Run(check, reject);

        Directory.CreateDirectory("artifacts/structured");
        foreach(var (name,document) in new[]{("unified-original",original),("unified-moved",moved),("unified-arranged",front),("unified-grouped",grouped),("unified-ungrouped",ungrouped),("unified-pasted",pasted),("unified-reflow",reflow),("unified-cropped",cropped),("unified-clipped-paste",clippedPaste)})
            File.WriteAllBytes($"artifacts/structured/{name}.pdf",PdfDocumentEngine.Save(document,face).Bytes);
        Directory.CreateDirectory("artifacts/engine");
        File.WriteAllBytes("artifacts/engine/unified-objects.pdf",original.Sources[0].Bytes);
    }

    private static byte[] Render(PdfWorkspace workspace,int page=0)
    { using var renderer=new PdfRenderer(); return renderer.ExportPng(workspace,page,1); }
    private static SKColor Pixel(PdfWorkspace workspace,PointD p)
    { using var bitmap=SKBitmap.Decode(Render(workspace));return bitmap.GetPixel((int)p.X,(int)p.Y); }
    private static bool Near(RectD a,RectD b) => Math.Abs(a.X-b.X)<.02 && Math.Abs(a.Y-b.Y)<.02 && Math.Abs(a.Width-b.Width)<.02 && Math.Abs(a.Height-b.Height)<.02;
    private static bool RegionEqual(byte[] first,byte[] second,RectD region)
    {
        using var a=SKBitmap.Decode(first); using var b=SKBitmap.Decode(second);
        for(var y=Math.Max(0,(int)region.Y);y<Math.Min(a.Height,region.Bottom);y++)
            for(var x=Math.Max(0,(int)region.X);x<Math.Min(a.Width,region.Right);x++)
                if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
}
