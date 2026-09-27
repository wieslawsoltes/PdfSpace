using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
namespace PdfSpace.Controls;

public enum PdfIconKind { None, Menu, Home, File, Folder, Close, Plus, Search, Save, Print, Share, Info, Settings, Edit, Export, Pages, Comment, Pen, Text, Sign, Check, Arrow, Line, Rectangle, Ellipse, Hand, Select, Highlight, Underline, Strikeout, Crop, Rotate, ZoomIn, ZoomOut, FitPage, FitWidth, Up, Down, Left, Right, Undo, Redo, Trash, Copy, Bookmark, Lock, Redact, Measure, Image, Download, Attachment, More, Help, Star, Grid }

/// <summary>Original, resolution-independent outline icons. No proprietary artwork or icon fonts.</summary>
public sealed class PdfIcon : SKCanvasElement
{
    private PdfIconKind _kind;
    private uint _color = 0xFF363636;
    public PdfIconKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public uint Color { get => _color; set { _color = value; Invalidate(); } }
    private static readonly Dictionary<PdfIconKind, string> Data = new()
    {
        [PdfIconKind.Menu] = "M4 6H20M4 12H20M4 18H20", [PdfIconKind.Home] = "M3 11L12 3L21 11M5 10V21H10V15H14V21H19V10",
        [PdfIconKind.File] = "M5 3H14L19 8V21H5ZM14 3V8H19M8 13H16M8 17H14", [PdfIconKind.Folder] = "M3 6H10L12 9H21V20H3ZM3 9H21",
        [PdfIconKind.Close] = "M6 6L18 18M6 18L18 6", [PdfIconKind.Plus] = "M12 4V20M4 12H20", [PdfIconKind.Search] = "M16 16L21 21M18 10A8 8 0 1 1 2 10A8 8 0 1 1 18 10",
        [PdfIconKind.Save] = "M4 3H17L21 7V21H3V3ZM7 3V9H16V3M7 21V14H17V21", [PdfIconKind.Print] = "M6 8V3H18V8M6 18H3V9H21V18H18M6 14H18V22H6ZM17 11H18",
        [PdfIconKind.Share] = "M12 16V2M7 7L12 2L17 7M5 10H3V22H21V10H19", [PdfIconKind.Info] = "M12 10V17M12 7H12.01M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12",
        [PdfIconKind.Settings] = "M4 6H20M4 12H20M4 18H20M8 3V9M16 9V15M10 15V21", [PdfIconKind.Edit] = "M4 17L16 5L20 9L8 21H4ZM14 7L18 11M16 5L18 3L22 7L20 9",
        [PdfIconKind.Export] = "M12 3H4V21H20V13M12 12L22 2M15 2H22V9", [PdfIconKind.Pages] = "M8 7H21V22H8ZM3 17V2H16M11 11H18M11 15H18",
        [PdfIconKind.Comment] = "M3 3H21V17H10L5 22V17H3ZM7 8H17M7 12H14", [PdfIconKind.Pen] = "M5 20L8 13L17 4L21 8L12 17ZM8 13L12 17M3 22H21",
        [PdfIconKind.Text] = "M3 4H21M12 4V21M8 21H16M3 4V8M21 4V8", [PdfIconKind.Sign] = "M3 17C7 15 14 2 10 3C5 4 9 22 14 13C17 8 16 20 21 14M3 22H21",
        [PdfIconKind.Check] = "M4 12L9 18L21 4", [PdfIconKind.Arrow] = "M4 20L20 4M9 4H20V15", [PdfIconKind.Line] = "M4 20L20 4",
        [PdfIconKind.Rectangle] = "M3 5H21V19H3Z", [PdfIconKind.Ellipse] = "M22 12A10 7 0 1 1 2 12A10 7 0 1 1 22 12",
        [PdfIconKind.Hand] = "M7 11V5C7 2 10 2 10 5V11M10 5V3C10 1 13 1 13 3V11M13 5C13 2 16 2 16 5V12M16 8C16 5 19 5 19 8V16C19 23 9 23 7 19L3 13C2 10 5 9 7 13",
        [PdfIconKind.Select] = "M5 2V20L10 15L14 23L18 21L14 14H21Z", [PdfIconKind.Highlight] = "M5 15L15 5L20 10L10 20ZM7 17L4 20M3 23H21M15 5L17 3L22 8L20 10",
        [PdfIconKind.Underline] = "M6 3V13C6 21 18 21 18 13V3M4 23H20", [PdfIconKind.Strikeout] = "M18 5C12 0 4 4 7 9C9 12 17 11 17 17C17 23 6 22 5 18M3 12H21",
        [PdfIconKind.Crop] = "M6 2V18H22M2 6H18V22M9 3V3M21 15H21", [PdfIconKind.Rotate] = "M4 10C5 1 20 1 21 11C22 20 11 24 6 18M4 3V10H11",
        [PdfIconKind.ZoomIn] = "M16 16L22 22M18 10A8 8 0 1 1 2 10A8 8 0 1 1 18 10M6 10H14M10 6V14", [PdfIconKind.ZoomOut] = "M16 16L22 22M18 10A8 8 0 1 1 2 10A8 8 0 1 1 18 10M6 10H14",
        [PdfIconKind.FitPage] = "M5 2H19V22H5ZM9 7H15V17H9Z", [PdfIconKind.FitWidth] = "M2 4V20M22 4V20M5 12H19M8 8L4 12L8 16M16 8L20 12L16 16",
        [PdfIconKind.Up] = "M5 15L12 8L19 15", [PdfIconKind.Down] = "M5 9L12 16L19 9", [PdfIconKind.Left] = "M15 5L8 12L15 19", [PdfIconKind.Right] = "M9 5L16 12L9 19",
        [PdfIconKind.Undo] = "M3 10H14C23 10 23 21 14 21M9 4L3 10L9 16", [PdfIconKind.Redo] = "M21 10H10C1 10 1 21 10 21M15 4L21 10L15 16",
        [PdfIconKind.Trash] = "M3 6H21M8 6V2H16V6M5 6L6 22H18L19 6M10 10V18M14 10V18", [PdfIconKind.Copy] = "M8 8H21V22H8ZM3 16V2H16",
        [PdfIconKind.Bookmark] = "M6 2H18V22L12 17L6 22Z", [PdfIconKind.Lock] = "M4 10H20V22H4ZM7 10V6C7 0 17 0 17 6V10M12 15V18",
        [PdfIconKind.Redact] = "M3 4H21V20H3ZM6 8H18V16H6ZM8 10L16 14M8 14L16 10", [PdfIconKind.Measure] = "M3 17L17 3L22 8L8 22ZM6 14L9 17M10 10L13 13M14 6L17 9",
        [PdfIconKind.Image] = "M3 3H21V21H3ZM3 17L8 11L13 16L17 12L21 17M17 7H17.01", [PdfIconKind.Download] = "M12 2V17M6 11L12 17L18 11M3 17V22H21V17",
        [PdfIconKind.Attachment] = "M8 13L16 5C21 1 26 8 20 13L10 22C3 26 -1 17 4 12L13 3M7 15L15 7", [PdfIconKind.More] = "M4 12H4.01M12 12H12.01M20 12H20.01",
        [PdfIconKind.Help] = "M9 8C9 3 17 3 17 8C17 11 12 10 12 15M12 18H12.01M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12",
        [PdfIconKind.Star] = "M12 2L15 8L22 9L17 14L18 22L12 18L6 22L7 14L2 9L9 8Z", [PdfIconKind.Grid] = "M3 3H10V10H3ZM14 3H21V10H14ZM3 14H10V21H3ZM14 14H21V21H14Z"
    };
    private static readonly Dictionary<PdfIconKind, SKPath> Paths = Data.ToDictionary(p => p.Key, p => SKPath.ParseSvgPathData(p.Value));
    public PdfIcon() { Width = 19; Height = 19; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (!Paths.TryGetValue(Kind, out var path)) return;
        canvas.Save(); canvas.Scale((float)area.Width / 24, (float)area.Height / 24);
        using var paint = new SKPaint { Color = new((byte)(_color >> 16), (byte)(_color >> 8), (byte)_color, (byte)(_color >> 24)), IsAntialias = true, StrokeWidth = 1.65f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawPath(path, paint); canvas.Restore();
    }
}
