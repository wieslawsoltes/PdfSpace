using PdfSpace.Core;
namespace PdfSpace.Pdf;

/// <summary>Column-vector PDF affine transform: (a*x+c*y+e, b*x+d*y+f).</summary>
public readonly record struct PdfAffineMatrix(double A, double B, double C, double D, double E, double F)
{
    public static PdfAffineMatrix Identity => new(1, 0, 0, 1, 0, 0);
    public bool IsFinite => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F);
    public PointD Transform(PointD p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);
    public static PdfAffineMatrix operator *(PdfAffineMatrix left, PdfAffineMatrix right) => new(
        left.A * right.A + left.C * right.B, left.B * right.A + left.D * right.B,
        left.A * right.C + left.C * right.D, left.B * right.C + left.D * right.D,
        left.A * right.E + left.C * right.F + left.E, left.B * right.E + left.D * right.F + left.F);
    public PdfAffineMatrix Inverse()
    {
        var determinant = A * D - B * C;
        if (!IsFinite || !double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12) throw new NotSupportedException("Singular image transform cannot be edited.");
        return new(D / determinant, -B / determinant, -C / determinant, A / determinant, (C * F - D * E) / determinant, (B * E - A * F) / determinant);
    }
    public static PdfAffineMatrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static PdfAffineMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
    public static PdfAffineMatrix Rotate(double degrees)
    { var angle = degrees * Math.PI / 180; var cos = Math.Cos(angle); var sin = Math.Sin(angle); return new(cos, sin, -sin, cos, 0, 0); }
    public static PdfAffineMatrix Around(PointD center, PdfAffineMatrix transform) => Translate(center.X, center.Y) * transform * Translate(-center.X, -center.Y);
    internal string Operator => $"{PdfObjects.F(A)} {PdfObjects.F(B)} {PdfObjects.F(C)} {PdfObjects.F(D)} {PdfObjects.F(E)} {PdfObjects.F(F)} cm\n";
}
