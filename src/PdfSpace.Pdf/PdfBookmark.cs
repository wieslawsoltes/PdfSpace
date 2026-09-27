namespace PdfSpace.Pdf;

/// <summary>Read-only source outline entry mapped to the current workspace page sequence.</summary>
public sealed record PdfBookmark(string Title, int? PageIndex, int Depth, string SourceName);
