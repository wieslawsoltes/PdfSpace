using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Skia;

public static class FormFieldPainter
{
    public static void Draw(SKCanvas canvas, PdfFormFieldState field, SKTypeface typeface, bool highlight = false)
    {
        if (field.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported) return;
        var rect = PdfRenderer.Rect(field.Bounds);
        using var paint = new SKPaint { IsAntialias = true, Color = highlight ? new(232, 240, 255) : SKColors.White };
        canvas.Save(); canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        paint.Color = highlight ? new(68, 119, 208) : new(135, 139, 145); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = .7f;
        if (field.Kind == PdfFieldKind.RadioButton) canvas.DrawOval(rect, paint); else canvas.DrawRect(rect, paint);
        if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
        {
            if (field.IsChecked)
            {
                paint.Color = new(30, 45, 67);
                if (field.Kind == PdfFieldKind.RadioButton) { paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(rect.MidX, rect.MidY, Math.Min(rect.Width, rect.Height) * .26f, paint); }
                else { paint.StrokeWidth = Math.Max(1, rect.Height / 9); canvas.DrawLine(rect.Left + rect.Width * .2f, rect.MidY, rect.Left + rect.Width * .43f, rect.Top + rect.Height * .78f, paint); canvas.DrawLine(rect.Left + rect.Width * .43f, rect.Top + rect.Height * .78f, rect.Right - rect.Width * .16f, rect.Top + rect.Height * .22f, paint); }
            }
        }
        else
        {
            var value = field.Options.FirstOrDefault(option => option.Value == field.Value)?.Label ?? field.Value;
            AnnotationPainter.DrawText(canvas, value, rect.Left + 4, rect.Top + 3, field.FontSize, typeface, new(25, 25, 25), Math.Max(1, rect.Width - 8));
            if (field.Kind == PdfFieldKind.ComboBox)
            { paint.Color = new(90, 90, 90); canvas.DrawLine(rect.Right - 13, rect.Top + 9, rect.Right - 9, rect.Top + 13, paint); canvas.DrawLine(rect.Right - 9, rect.Top + 13, rect.Right - 5, rect.Top + 9, paint); }
        }
        canvas.Restore();
    }
}
