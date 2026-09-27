namespace PdfSpace.Core;

/// <summary>Fully-qualified AcroForm name and exact export values. An absent value clears a supported field.</summary>
public sealed record PdfFormDataField(string Name, string[] Values, PdfFieldKind? Kind = null);
