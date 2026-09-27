namespace PdfSpace.Pdf;

public sealed record PdfInspection(int Pages, int FormWidgets, int SignatureFields, bool HasXfa, bool HasJavaScript, bool Encrypted, int Annotations, int Outlines);
public sealed record PdfWriteResult(byte[] Bytes, bool PreservedSourceCatalog, string[] Warnings);
public sealed record PdfProtectionOptions(string UserPassword, string OwnerPassword, bool AllowPrint = true, bool AllowCopy = true, bool AllowEdit = true);
public sealed class PdfPasswordRequiredException(string message, Exception? inner = null) : Exception(message, inner);
