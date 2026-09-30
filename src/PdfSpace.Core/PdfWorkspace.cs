namespace PdfSpace.Core;

/// <summary>Source bytes are immutable and shared across history snapshots; original files are never overwritten.</summary>
public sealed record PdfSource(Guid Id, string Name, byte[] Bytes)
{
    // Reconstructed from the original bytes on load; never persisted in a workspace.
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? PreviewBytes { get; init; }
    public bool Sensitive { get; init; }
}

public sealed record PdfPageState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? SourceId { get; init; }
    public int SourcePage { get; init; } = 1;
    public double Width { get; init; } = 595;
    public double Height { get; init; } = 842;
    public int Rotation { get; init; }
    public RectD? Crop { get; init; }
    public string Bookmark { get; init; } = "";
    public PdfPageLabel? Label { get; init; }
    // Distinguishes explicit physical numbering from a pre-label-version workspace awaiting import.
    public bool LabelInitialized { get; init; }
    public Annotation[] Annotations { get; init; } = [];
    public PdfFormFieldState[] Fields { get; init; } = [];
    public PdfOcrLayer? Ocr { get; init; }
    public RectD VisibleBox => Crop ?? new RectD(0, 0, Width, Height);
    public double DisplayWidth => Rotation % 180 == 0 ? VisibleBox.Width : VisibleBox.Height;
    public double DisplayHeight => Rotation % 180 == 0 ? VisibleBox.Height : VisibleBox.Width;
}

public sealed record PdfWorkspace
{
    public int FormatVersion { get; init; } = 1;
    public string Title { get; init; } = "Untitled.pdf";
    public string Author { get; init; } = "";
    public PdfSource[] Sources { get; init; } = [];
    public PdfPageState[] Pages { get; init; } = [new()];
    public PdfWorkspace UpdatePage(Guid id, Func<PdfPageState, PdfPageState> update) => this with { Pages = Pages.Select(p => p.Id == id ? update(p) : p).ToArray() };
    public bool IsSensitive => Sources.Any(source => source.Sensitive);
    public int FieldCount => Pages.Sum(page => page.Fields.Length);
    public int AnnotationCount => Pages.Sum(p => p.Annotations.Length);
}
