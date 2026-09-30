namespace PdfSpace.Pdf;

/// <summary>Immutable metadata for one catalog EmbeddedFiles entry; never owns PDF or payload buffers.</summary>
public sealed record PdfEmbeddedFileInfo(string SourceFingerprint, string Key, string FileName,
    string DownloadName, string Description, string MediaType, long EncodedBytes, long? DeclaredBytes,
    string? UnavailableReason)
{
    public bool CanExtract => UnavailableReason is null;
}
