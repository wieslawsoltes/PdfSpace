using Uno.WinUI.Graphics2DSK;

namespace PdfSpace.Workbench;

/// <summary>Reusable, allocation-light placement preview. It intentionally shows no document content.
/// The host owns the typeface; updating settings performs no PDF parsing or rewriting.</summary>
public sealed class PdfPageMarkPreview : SKCanvasElement
{
    private PdfPageMarkText[] _lines = [];
    private PdfPageMarkSettings _settings = new();
    private SKTypeface? _typeface;
    private double _pageWidth = 595, _pageHeight = 842;
    public void Update(PdfPageMarkSettings settings, int ordinal, int pageCount, double width, double height, SKTypeface typeface)
    {
        var lines = PdfPageMarks.Layout(settings, ordinal, pageCount, width, height, typeface);
        _settings = settings; _typeface = typeface; _pageWidth = width; _pageHeight = height; _lines = lines;
        Invalidate();
    }
    public void Clear() { _lines = []; Invalidate(); }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Clear(new SKColor(242, 243, 245));
        var scale = Math.Min(Math.Max(1, area.Width - 16) / _pageWidth, Math.Max(1, area.Height - 16) / _pageHeight);
        canvas.Save();
        canvas.Translate((float)((area.Width - _pageWidth * scale) / 2), (float)((area.Height - _pageHeight * scale) / 2));
        canvas.Scale((float)scale);
        using var paper = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(0, 0, (float)_pageWidth, (float)_pageHeight, paper);
        using var font = new SKFont(_typeface ?? SKTypeface.Default, (float)_settings.FontSize);
        using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(_settings.Color).WithAlpha((byte)Math.Round(_settings.Opacity * 255)) };
        foreach (var line in _lines)
        {
            canvas.Save();
            canvas.RotateDegrees((float)line.Rotation, (float)_pageWidth / 2, (float)_pageHeight / 2);
            canvas.DrawText(line.Text, (float)line.X, (float)line.Baseline, SKTextAlign.Left, font, ink);
            canvas.Restore();
        }
        canvas.Restore();
    }
}
