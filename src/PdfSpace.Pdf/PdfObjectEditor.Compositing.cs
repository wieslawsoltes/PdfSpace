using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

public static partial class PdfObjectEditor
{
    /// <summary>Overrides paint opacity/blending on paths, images, shading and safe text objects.
    /// Text overrides apply immediately before every text-showing operator, including internal
    /// graphics-state changes. Forms require editing their contents; this is not group opacity.</summary>
    public static PdfWorkspace SetCompositing(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfObjectCompositing compositing)
    {
        ArgumentNullException.ThrowIfNull(compositing);
        foreach (var alpha in new[] { compositing.FillOpacity, compositing.StrokeOpacity })
            if (alpha is { } value && (!double.IsFinite(value) || value is < 0 or > 1))
                throw new ArgumentOutOfRangeException(nameof(compositing), "Opacity must be from zero to one.");
        if (compositing.BlendMode is { } blend && !Enum.IsDefined(blend))
            throw new ArgumentOutOfRangeException(nameof(compositing));
        var changes = compositing.FillOpacity is not null || compositing.StrokeOpacity is not null || compositing.BlendMode is not null;
        return Edit(workspace, objects, (native, items, _) =>
        {
            if (items.Any(item => item.Object.Kind == PdfPageObjectKind.Form))
                throw new NotSupportedException("Edit individual objects inside the Form. Per-paint opacity is not whole-group opacity.");
            if (!changes) return;
            var affected = items.Where(item =>
                compositing.FillOpacity is { } f && item.Object.Paint.FillOpacity != f ||
                compositing.StrokeOpacity is { } s && item.Object.Paint.StrokeOpacity != s ||
                compositing.BlendMode is { } b && item.Object.Paint.BlendMode != b ||
                (compositing.FillOpacity is not null || compositing.StrokeOpacity is not null) && item.Object.Paint.AlphaIsShape != false).ToArray();
            if (affected.Length == 0) { changes = false; return; }
            native.Version = Math.Max(native.Version, 14);
            var gs = new PdfDictionary(native);
            gs.Elements.SetName("/Type", "/ExtGState");
            if (compositing.FillOpacity is { } fill) gs.Elements.SetReal("/ca", fill);
            if (compositing.StrokeOpacity is { } stroke) gs.Elements.SetReal("/CA", stroke);
            if (compositing.FillOpacity is not null || compositing.StrokeOpacity is not null)
                gs.Elements.SetBoolean("/AIS", false); // Explicit opacity, not source shape.
            if (compositing.BlendMode is { } mode) gs.Elements.SetName("/BM", "/" + mode);
            // Do not clear existing soft masks, overprint or transfer functions.
            ChangeScopes(native, affected, (content, resources, item) =>
            {
                var name = PdfContentGraph.AddResource(native, resources, "/ExtGState", gs);
                if (item.Object.Kind == PdfPageObjectKind.Text)
                {
                    var body = new CSequence();
                    for (var i = item.Object.Start; i <= item.Object.End; i++)
                    {
                        if (content[i] is COperator { Name: "Tj" or "TJ" or "'" or "\"" })
                            Append(body, name + " gs\n");
                        body.Add(content[i]);
                    }
                    Replace(content, item, "q\n", Encoding.Latin1.GetString(PdfContentGraph.Serialize(body)), "Q\n", true);
                }
                else Replace(content, item, "q\n" + name + " gs\n", null, "Q\n", false);
            });
        }, hasChanges: () => changes);
    }
}
