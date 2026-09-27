namespace PdfSpace.Core;

/// <summary>A recognized word in logical source-page coordinates. Confidence is an estimate, not a correctness guarantee.</summary>
public sealed record PdfOcrWord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public RectD Bounds { get; init; }
    public double Confidence { get; init; }
    public int Line { get; init; }
    public bool Reviewed { get; init; }
}
