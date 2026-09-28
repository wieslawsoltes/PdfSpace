using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using TextReader=PdfSpace.Documents.PdfReader;

internal static class ObjectSafetyTests
{
    public static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var inherited = Create("BT /F1 16 Tf 30 180 Td (One) Tj ET\nBT 30 140 Td (Two) Tj ET\nBT 30 100 Td (Three) Tj ET\n");
        var shared = PdfObjectEditor.Read(inherited,0).Where(o=>o.Text is "Two" or "Three").ToArray();
        var copy = PdfObjectEditor.Copy(inherited,shared);
        var pasted = PdfObjectEditor.Paste(new PdfWorkspace(),0,copy,copy.Bounds.Center-new PointD(copy.Bounds.Width/2,copy.Bounds.Height/2));
        check(TextReader.ExtractText(pasted).Contains("Two") && TextReader.ExtractText(pasted).Contains("Three"),"copying shared inherited font state keeps both text objects searchable");
        using(var renderer=new PdfRenderer())
        using(var image=SKBitmap.Decode(renderer.ExportPng(pasted,0,1)))
        {
            var found=false;
            for(var y=90;y<160 && !found;y++) for(var x=25;x<100;x++)
                if(image.GetPixel(x,y).Red<80){found=true;break;}
            check(found,"copied shared-font text renders without missing-resource errors");
        }
        var textClip=Create("BT /F1 40 Tf 20 100 Td 7 Tr (MASK) Tj ET\n0 0 1 rg 0 0 220 220 re f\n");
        var clipped=PdfObjectEditor.Read(textClip,0);
        check(clipped.Where(o=>o.Kind==PdfPageObjectKind.Text).All(o=>!o.Editable),"clipping text itself is not exposed as safely mutable");
        reject(()=>PdfObjectEditor.Copy(textClip,[clipped.Single(o=>o.Kind==PdfPageObjectKind.Path)]),"clipboard refuses unsupported inherited text clipping");
        var variableClip=Create("0 0 100 100 re 1 0 0 1 50 0 cm 100 0 50 100 re W n\n1 0 0 rg 0 0 200 200 re f\n");
        reject(()=>PdfObjectEditor.Copy(variableClip,PdfObjectEditor.Read(variableClip,0)),"clipboard refuses a clip constructed under changing matrices");
        var normal=ObjectEditingSample.Create();
        var paths=PdfObjectEditor.Read(normal,0).Where(o=>o.Kind==PdfPageObjectKind.Path).Take(2).ToArray();
        var grouped=PdfObjectEditor.Group(normal,paths);
        using var pdf=PdfReader.Open(new MemoryStream(grouped.Sources[0].Bytes),PdfDocumentOpenMode.Modify);
        var group=pdf.Internals.GetAllObjects().OfType<PdfDictionary>().Single(d=>d.Elements.GetBoolean("/PdfSpaceGroup"));
        group.Elements["/BBox"]=new PdfArray(pdf,new PdfReal(-1000000),new PdfReal(-1000000),new PdfReal(110),new PdfReal(1000000));
        using var stream=new MemoryStream();pdf.Save(stream,false);
        var altered=PdfDocumentEngine.Open(stream.ToArray(),"clipped-group.pdf");
        var result=PdfObjectEditor.Ungroup(altered,PdfObjectEditor.Read(altered,0).Single(o=>o.Kind==PdfPageObjectKind.Form));
        using(var first=new PdfRenderer())
        using(var second=new PdfRenderer())
            check(first.ExportPng(altered,0,1).SequenceEqual(second.ExportPng(result,0,1)),"ungrouping preserves the actual native group BBox rather than trusting a marker");
        var shape=PdfObjectEditor.Read(normal,0).First(o=>o.Kind==PdfPageObjectKind.Path);
        reject(()=>PdfObjectEditor.SetAppearance(normal,[shape],new(Fill:0x80123456)),"solid RGB replacement cannot silently discard requested alpha");
        reject(()=>PdfObjectEditor.SetAppearance(normal,[shape],new(StrokeWidth:double.PositiveInfinity)),"invalid path stroke widths rejected");
        reject(()=>PdfObjectEditor.Delete(normal,[shape with {Fingerprint="forged"}]),"native operator fingerprint validated before edits");
        check(PdfObjectEditor.Read(normal,0).Any(o=>o.Fingerprint==shape.Fingerprint),"failed edits leave original object source intact");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/unified-inherited-fonts.pdf",PdfDocumentEngine.Save(pasted,SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/unified-clipped-group-before.pdf",PdfDocumentEngine.Save(altered,SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/unified-clipped-group-after.pdf",PdfDocumentEngine.Save(result,SKTypeface.Default).Bytes);
    }

    private static PdfWorkspace Create(string content)
    {
        using var native=new PdfDocument();
        var page=native.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromPoint(240);page.Height=PdfSharp.Drawing.XUnit.FromPoint(240);
        var font=new PdfDictionary(native);
        font.Elements.SetName("/Type","/Font");font.Elements.SetName("/Subtype","/Type1");font.Elements.SetName("/BaseFont","/Helvetica");
        native.Internals.AddObject(font);
        var fonts=new PdfDictionary(native);fonts.Elements["/F1"]=font.Reference!;
        var resources=new PdfDictionary(native);resources.Elements["/Font"]=fonts;page.Elements["/Resources"]=resources;
        var body=new PdfDictionary(native);body.CreateStream(Encoding.ASCII.GetBytes(content));native.Internals.AddObject(body);
        page.Elements["/Contents"]=body.Reference!;
        using var output=new MemoryStream();native.Save(output,false);
        return PdfDocumentEngine.Open(output.ToArray(),"object-safety.pdf");
    }
}
