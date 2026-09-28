using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using TextReader=PdfSpace.Documents.PdfReader;

internal static class BrowserMixedObjectVerification
{
    public static void Run(string directory)
    {
        var checks=0;
        void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidDataException(message);
            Console.WriteLine("PASS "+message);checks++;
        }
        PdfWorkspace Open(string file)=>PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory,file)),file);
        var changed=Open("mixed-browser-edited.pdf");
        var objects=PdfObjectEditor.Read(changed,0);
        var image=objects.First(o=>o.Kind==PdfPageObjectKind.Image);
        var path=objects.First(o=>o.Kind==PdfPageObjectKind.Path);
        Check(Math.Abs(image.Bounds.X-70)<.05 && Math.Abs(image.Bounds.Y-240)<.05,"browser mixed selection edits the original nested image invocation");
        Check(Math.Abs(path.Bounds.X-68)<.05 && Math.Abs(path.Bounds.Y-372)<.05,"browser mixed selection edits the original vector path");
        var original=ObjectEditingSample.Create();
        using(var a=new PdfRenderer())using(var b=new PdfRenderer())
            Check(a.ExportPng(original,1,1).SequenceEqual(b.ExportPng(changed,1,1)),"browser mixed edit leaves shared content on the other page identical");
        using(var renderer=new PdfRenderer())using(var bitmap=SKBitmap.Decode(renderer.ExportPng(changed,0,1)))
            Check(bitmap.GetPixel(80,382).Red>180 && bitmap.GetPixel(80,382).Blue<100,"browser color edit persisted in native path paint");
        var curves=Open("mixed-browser-path.pdf");
        var curve=PdfObjectEditor.Read(curves,0).Single(o=>o.Kind==PdfPageObjectKind.Path && o.Nodes.Any(n=>n.Operator=="c"));
        Check(Math.Abs(curve.Nodes[0].Values[0]-318)<.1 && Math.Abs(curve.Nodes[0].Values[1]-448)<.1,"browser point handle updates native path coordinates");
        var grouped=Open("mixed-browser-group.pdf");
        Check(PdfObjectEditor.Read(grouped,0).Count(o=>o.Kind==PdfPageObjectKind.Form)==1,"browser grouping persists as a selectable native Form");
        using(var a=new PdfRenderer())using(var b=new PdfRenderer())
            Check(a.ExportPng(original,0,1).SequenceEqual(b.ExportPng(grouped,0,1)),"browser grouping preserves rendered page pixels");
        var pasted=Open("mixed-browser-pasted.pdf");var pastedObjects=PdfObjectEditor.Read(pasted,0);
        Check(pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Path)==1 && pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Image)==1 && pastedObjects.Count(o=>o.Kind==PdfPageObjectKind.Text)==1,"browser clipboard retains native mixed objects in a new document");
        Check(TextReader.ExtractText(pasted).Contains("Shared image and text"),"pasted browser text is searchable outside PdfSpace");
        var text=Open("mixed-browser-text.pdf");
        Check(TextReader.ExtractText(text).Contains("Żółć café – Object text"),"browser inserted Unicode block survives native PDF extraction");
        Console.WriteLine($"{checks} browser mixed-object verification checks passed.");
    }
}
