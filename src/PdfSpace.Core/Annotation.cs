namespace PdfSpace.Core;

public enum AnnotationKind { Highlight, Underline, Strikeout, Ink, Rectangle, Ellipse, Line, Arrow, Text, Note, Signature, Stamp, Check }
public enum PdfTool { Select, Hand, Highlight, Underline, Strikeout, Ink, Rectangle, Ellipse, Line, Arrow, Text, Note, Signature, Stamp, Check, Crop, Measure }
public sealed record CommentReply(Guid Id, string Author, string Text, DateTimeOffset Created);

/// <summary>Coordinates are PDF points in the source page's top-left logical coordinate system.</summary>
public sealed record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AnnotationKind Kind { get; init; }
    public RectD Bounds { get; init; }
    public PointD[] Points { get; init; } = [];
    public uint Color { get; init; } = 0xFF1473E6;
    public double StrokeWidth { get; init; } = 2;
    public double FontSize { get; init; } = 14;
    public string Text { get; init; } = "";
    public string Author { get; init; } = "You";
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public bool Resolved { get; init; }
    public CommentReply[] Replies { get; init; } = [];
    public Annotation Move(PointD delta) => this with { Bounds = Bounds.Translate(delta), Points = Points.Select(p => p + delta).ToArray() };
}
