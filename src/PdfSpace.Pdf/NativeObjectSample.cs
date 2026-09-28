using System.Text;
using PdfSharp.Pdf;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

/// <summary>Original native-object fixture: multiple placements share a nested form, text font and image.</summary>
public static class NativeObjectSample
{
    public static PdfWorkspace Create()
    {
        using var document = new PdfDocument();
        PdfDictionary Object(string subtype)
        {
            var value = new PdfDictionary(document); value.Elements.SetName("/Type", "/XObject"); value.Elements.SetName("/Subtype", subtype); document.Internals.AddObject(value); return value;
        }
        var font = new PdfDictionary(document); font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica"); font.Elements.SetName("/Encoding", "/WinAnsiEncoding"); document.Internals.AddObject(font);
        var image = Object("/Image"); image.Elements.SetInteger("/Width", 40); image.Elements.SetInteger("/Height", 24); image.Elements.SetInteger("/BitsPerComponent", 8); image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        var rgb = new byte[40 * 24 * 3];
        for (var y = 0; y < 24; y++) for (var x = 0; x < 40; x++) { var i = (y * 40 + x) * 3; rgb[i] = (byte)(x < 20 ? 35 : 229); rgb[i + 1] = (byte)(y < 12 ? 146 : 82); rgb[i + 2] = 96; }
        image.CreateStream(rgb);
        var resources = new PdfDictionary(document); var fonts = new PdfDictionary(document); fonts.Elements["/F1"] = font.Reference!; resources.Elements["/Font"] = fonts;
        var xobjects = new PdfDictionary(document); xobjects.Elements["/Image"] = image.Reference!; resources.Elements["/XObject"] = xobjects;
        var leaf = Object("/Form"); leaf.Elements["/BBox"] = PdfObjects.Numbers(document, 0, 0, 220, 150); leaf.Elements["/Resources"] = resources;
        leaf.CreateStream(Encoding.ASCII.GetBytes("q 160 0 0 70 20 15 cm /Image Do Q\nBT /F1 14 Tf 20 110 Td (Shared image and text) Tj ET\n"));
        var outerResources = new PdfDictionary(document); var forms = new PdfDictionary(document); forms.Elements["/Leaf"] = leaf.Reference!; outerResources.Elements["/XObject"] = forms;
        var outer = Object("/Form"); outer.Elements["/BBox"] = PdfObjects.Numbers(document, 0, 0, 250, 180); outer.Elements["/Resources"] = outerResources;
        outer.Elements["/Matrix"] = PdfObjects.Numbers(document, 1, 0, 0, 1, 5, 7);
        outer.CreateStream(Encoding.ASCII.GetBytes("q 1 0 0 1 10 10 cm /Leaf Do Q\n"));
        var pageResources = new PdfDictionary(document); var pageForms = new PdfDictionary(document); pageForms.Elements["/Shared"] = outer.Reference!; pageResources.Elements["/XObject"] = pageForms; pageResources.Elements["/Font"] = fonts;
        for (var i = 0; i < 2; i++)
        {
            var page = document.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(595); page.Height = PdfSharp.Drawing.XUnit.FromPoint(842); page.Elements["/Resources"] = pageResources;
            var content = Object("/Form"); content.Elements.Remove("/Type"); content.Elements.Remove("/Subtype");
            content.CreateStream(Encoding.ASCII.GetBytes("BT /F1 24 Tf 48 774 Td (Native PDF objects) Tj ET\nBT /F1 12 Tf 48 739 Td (Each placement is independently editable. The resources are shared.) Tj ET\nq 1 0 0 1 25 500 cm /Shared Do Q\n" + (i == 0 ? "q 1 0 0 1 300 270 cm /Shared Do Q\n" : "")));
            page.Elements["/Contents"] = content.Reference!;
        }
        return PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(document), "Native object editing.pdf");
    }
    public static byte[] ReplacementImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(100, 60)); surface.Canvas.Clear(new SKColor(33, 112, 220));
        using var paint = new SKPaint { Color = new SKColor(247, 180, 58), IsAntialias = true }; surface.Canvas.DrawCircle(35, 30, 20, paint);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
}
