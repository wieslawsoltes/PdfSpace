using System.Text;
using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;
/// <summary>An original mixed-object fixture with real paths, text, clipped graphics and shared image forms.</summary>
public static class ObjectEditingSample
{
    public static PdfWorkspace Create()
    {
        var workspace = NativeObjectSample.Create();
        using var native = PdfDocumentEngine.OpenNative(workspace.Sources[0].Bytes);
        var page = native.Pages[0];
        var stream = new PdfDictionary(native);
        stream.CreateStream(Encoding.ASCII.GetBytes("q\n0.08 0.45 0.8 rg 48 410 130 70 re f\n0.92 0.36 0.16 rg 140 390 130 70 re f\n0.15 0.55 0.3 RG 4 w 330 440 m 355 505 465 365 500 440 c S\nQ\nBT /F1 16 Tf 48 345 Td (Edit mixed native objects) Tj ET\nBT 48 320 Td (Inherited font remains unchanged) Tj ET\nq 48 100 180 80 re W n 0.35 0.2 0.65 rg 20 60 250 150 re f Q\n"));
        native.Internals.AddObject(stream);
        var contents = new PdfArray(native);
        contents.Elements.Add(page.Elements["/Contents"]!);
        contents.Elements.Add(stream.Reference!);
        page.Elements["/Contents"] = contents;
        return PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "Object editing studio.pdf");
    }
}
