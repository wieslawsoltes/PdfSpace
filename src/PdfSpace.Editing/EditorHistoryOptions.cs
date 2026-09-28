namespace PdfSpace.Editing;

/// <summary>Limits undo/redo retention. The byte budget counts distinct original
/// and preview source arrays across current state and history, not all CLR/native memory.</summary>
public sealed record EditorHistoryOptions
{
    public int MaximumEntries { get; init; } = 100;
    public long MaximumSourceBytes { get; init; } = 128L * 1024 * 1024;

    internal void Validate()
    {
        if (MaximumEntries is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(MaximumEntries));
        if (MaximumSourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaximumSourceBytes));
    }
}
