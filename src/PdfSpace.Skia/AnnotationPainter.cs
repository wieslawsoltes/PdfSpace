using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Skia;

public static class AnnotationPainter
{
    public static void Draw(SKCanvas canvas, Annotation a, SKTypeface typeface)
    {
        var r = PdfRenderer.Rect(a.Bounds); var color = PdfRenderer.Color(a.Color);
        using var paint = new SKPaint { Color = color, StrokeWidth = (float)a.StrokeWidth, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        switch (a.Kind)
        {
            case AnnotationKind.RedactionMark:
                paint.Style = SKPaintStyle.Fill; paint.Color = new SKColor(180, 20, 30, 90); canvas.DrawRect(r, paint);
                paint.Style = SKPaintStyle.Stroke; paint.Color = new SKColor(180, 20, 30); canvas.DrawRect(r, paint); break;
            case AnnotationKind.Link:
                paint.Color = new SKColor(20, 100, 210); paint.StrokeWidth = .8f; canvas.DrawRect(r, paint); break;
            case AnnotationKind.Highlight:
                paint.Style = SKPaintStyle.Fill; paint.Color = color.WithAlpha(75); paint.BlendMode = SKBlendMode.Multiply; canvas.DrawRect(r, paint); break;
            case AnnotationKind.Underline: canvas.DrawLine(r.Left, r.Bottom, r.Right, r.Bottom, paint); break;
            case AnnotationKind.Strikeout: canvas.DrawLine(r.Left, r.MidY, r.Right, r.MidY, paint); break;
            case AnnotationKind.Rectangle: canvas.DrawRect(r, paint); break;
            case AnnotationKind.Ellipse: canvas.DrawOval(r, paint); break;
            case AnnotationKind.Ink:
            case AnnotationKind.Signature:
                using (var path = new SKPath())
                {
                    if (a.Points.Length == 1) { paint.Style = SKPaintStyle.Fill; canvas.DrawCircle((float)a.Points[0].X, (float)a.Points[0].Y, (float)a.StrokeWidth / 2, paint); }
                    for (var i = 0; i < a.Points.Length; i++) { var p = a.Points[i]; if (i == 0) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y); }
                    canvas.DrawPath(path, paint);
                }
                break;
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                var start = a.Points.Length > 0 ? a.Points[0] : new PointD(r.Left, r.Top); var end = a.Points.Length > 1 ? a.Points[^1] : new PointD(r.Right, r.Bottom);
                canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint);
                if (a.Kind == AnnotationKind.Arrow)
                {
                    var angle = Math.Atan2(end.Y - start.Y, end.X - start.X); var length = Math.Max(8, a.StrokeWidth * 4);
                    foreach (var offset in new[] { -.5, .5 }) canvas.DrawLine((float)end.X, (float)end.Y, (float)(end.X - length * Math.Cos(angle + offset)), (float)(end.Y - length * Math.Sin(angle + offset)), paint);
                }
                break;
            case AnnotationKind.Note:
                paint.Style = SKPaintStyle.Fill; paint.Color = color; canvas.DrawRoundRect(r, 3, 3, paint); paint.Color = SKColors.White; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
                canvas.DrawLine(r.Left + 5, r.Top + 7, r.Right - 5, r.Top + 7, paint); canvas.DrawLine(r.Left + 5, r.Top + 12, r.Right - 5, r.Top + 12, paint); break;
            case AnnotationKind.Check:
                canvas.DrawLine(r.Left, r.MidY, r.Left + r.Width * .35f, r.Bottom, paint); canvas.DrawLine(r.Left + r.Width * .35f, r.Bottom, r.Right, r.Top, paint); break;
            case AnnotationKind.Stamp:
                canvas.DrawRoundRect(r, 4, 4, paint); DrawText(canvas, a.Text.Length == 0 ? "APPROVED" : a.Text, r.Left + 10, r.Top + 7, Math.Min(a.FontSize, r.Height - 12), typeface, color, r.Width - 20); break;
            case AnnotationKind.Text:
                DrawText(canvas, a.Text, r.Left, r.Top, a.FontSize, typeface, color, r.Width); break;
        }
    }
    public static void DrawText(SKCanvas canvas, string text, double x, double y, double size, SKTypeface typeface, SKColor color, double maxWidth = 10000)
    {
        using var font = new SKFont(typeface, (float)size); using var paint = new SKPaint { Color = color, IsAntialias = true };
        var top = (float)y - font.Metrics.Ascent;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var current = "";
            foreach (var word in line.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > maxWidth) { canvas.DrawText(current, (float)x, top, font, paint); top += (float)size * 1.35f; current = word; }
                else current = candidate;
            }
            canvas.DrawText(current, (float)x, top, font, paint); top += (float)size * 1.35f;
        }
    }
}
