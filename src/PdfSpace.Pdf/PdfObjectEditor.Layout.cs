using PdfSpace.Core;
using PdfSpace.Layout;

namespace PdfSpace.Pdf;

/// <summary>Native bounding-box dimensions to match; individual object centers remain fixed.</summary>
public enum PdfObjectSizeMatch { Width, Height, Both }

public static partial class PdfObjectEditor
{
    /// <summary>Aligns source-space bounds to an explicitly indexed member of the selection. The reference never moves.</summary>
    public static PdfWorkspace AlignToObject(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects,
        int referenceIndex, PdfObjectAlignment alignment)
    {
        ValidateLayoutSelection(objects, 2);
        ValidateAlignment(alignment);
        var reference = ReferenceObject(objects, referenceIndex);
        var changes = new PdfObjectChange[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            var offset = i == referenceIndex ? default : AlignmentOffset(objects[i].Bounds, reference.Bounds, alignment);
            changes[i] = new(objects[i], PdfAffineMatrix.Translate(offset.X, offset.Y));
        }
        return Transform(workspace, changes);
    }

    /// <summary>Aligns each selected object's displayed bounds to the visible page crop, honoring workspace rotation.</summary>
    public static PdfWorkspace AlignToPage(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, PdfObjectAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateLayoutSelection(objects, 1);
        ValidateAlignment(alignment);
        var page = workspace.Pages.FirstOrDefault(p => p.Id == objects[0].PageId)
            ?? throw new InvalidOperationException("The selected source page is no longer present.");
        var target = new RectD(0, 0, page.DisplayWidth, page.DisplayHeight);
        var origin = PageGeometry.ToPage(page, default);
        var changes = new PdfObjectChange[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            var b = objects[i].Bounds;
            var displayed = RectD.Between(PageGeometry.ToDisplay(page, new(b.X, b.Y)), PageGeometry.ToDisplay(page, new(b.Right, b.Bottom)));
            var offset = AlignmentOffset(displayed, target, alignment);
            var delta = PageGeometry.ToPage(page, offset) - origin;
            changes[i] = new(objects[i], PdfAffineMatrix.Translate(delta.X, delta.Y));
        }
        return Transform(workspace, changes);
    }

    /// <summary>Matches selected native bounds to a reference member without translating centers. Not text reflow.</summary>
    public static PdfWorkspace MatchSize(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects,
        int referenceIndex, PdfObjectSizeMatch dimensions)
    {
        ValidateLayoutSelection(objects, 2);
        if (!Enum.IsDefined(dimensions)) throw new ArgumentOutOfRangeException(nameof(dimensions));
        var reference = ReferenceObject(objects, referenceIndex).Bounds;
        var width = dimensions is PdfObjectSizeMatch.Width or PdfObjectSizeMatch.Both;
        var height = dimensions is PdfObjectSizeMatch.Height or PdfObjectSizeMatch.Both;
        var changes = new PdfObjectChange[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            var b = objects[i].Bounds;
            if (width && (b.Width <= 1e-8 || reference.Width <= 1e-8) || height && (b.Height <= 1e-8 || reference.Height <= 1e-8))
                throw new NotSupportedException("Matching a dimension requires a nonzero reference and target extent on that axis.");
            var sx = width ? reference.Width / b.Width : 1;
            var sy = height ? reference.Height / b.Height : 1;
            changes[i] = new(objects[i], i == referenceIndex || sx == 1 && sy == 1 ? PdfAffineMatrix.Identity :
                PdfAffineMatrix.Around(b.Center, PdfAffineMatrix.Scale(sx, sy)));
        }
        return Transform(workspace, changes);
    }

    /// <summary>Equalizes nonnegative gaps, keeping the first and last objects fixed. Negative-gap layouts are rejected atomically.</summary>
    public static PdfWorkspace DistributeSpacing(PdfWorkspace workspace, IReadOnlyList<PdfPageObject> objects, bool horizontal)
    {
        ValidateLayoutSelection(objects, 3);
        var ordered = objects.OrderBy(o => horizontal ? o.Bounds.X : o.Bounds.Y).ToArray();
        double Start(PdfPageObject o) => horizontal ? o.Bounds.X : o.Bounds.Y;
        double Extent(PdfPageObject o) => horizontal ? o.Bounds.Width : o.Bounds.Height;
        var first = Start(ordered[0]);
        var last = Start(ordered[^1]);
        // The last leading edge is fixed, so only widths/heights before the last object consume this span.
        var total = 0d;
        var compensation = 0d;
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var adjusted = Extent(ordered[i]) - compensation;
            var next = total + adjusted;
            compensation = (next - total) - adjusted;
            total = next;
        }
        var gap = (last - first - total) / (ordered.Length - 1);
        if (!double.IsFinite(gap) || gap < 0)
            throw new NotSupportedException("The outer objects leave insufficient room for nonoverlapping equal gaps. Move them farther apart first.");
        var changes = new PdfObjectChange[ordered.Length];
        var prefix = 0d;
        for (var i = 0; i < ordered.Length; i++)
        {
            var shift = i == 0 || i == ordered.Length - 1 ? 0 : first + prefix + gap * i - Start(ordered[i]);
            changes[i] = new(ordered[i], horizontal ? PdfAffineMatrix.Translate(shift, 0) : PdfAffineMatrix.Translate(0, shift));
            prefix += Extent(ordered[i]);
        }
        return Transform(workspace, changes);
    }

    private static void ValidateLayoutSelection(IReadOnlyList<PdfPageObject> objects, int minimum)
    {
        PdfObjectSelectionValidator.Validate(objects);
        if (objects.Count < minimum) throw new ArgumentException($"Select at least {minimum} native objects.", nameof(objects));
        foreach (var item in objects) ValidateBounds(item.Bounds);
    }

    private static PdfPageObject ReferenceObject(IReadOnlyList<PdfPageObject> objects, int index) =>
        (uint)index < (uint)objects.Count ? objects[index] : throw new ArgumentOutOfRangeException(nameof(index));

    private static void ValidateAlignment(PdfObjectAlignment alignment)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
    }

    private static PointD AlignmentOffset(RectD bounds, RectD reference, PdfObjectAlignment alignment) => alignment switch
    {
        PdfObjectAlignment.Left => new(reference.X - bounds.X, 0),
        PdfObjectAlignment.Center => new(reference.Center.X - bounds.Center.X, 0),
        PdfObjectAlignment.Right => new(reference.Right - bounds.Right, 0),
        PdfObjectAlignment.Top => new(0, reference.Y - bounds.Y),
        PdfObjectAlignment.Middle => new(0, reference.Center.Y - bounds.Center.Y),
        _ => new(0, reference.Bottom - bounds.Bottom)
    };
}
