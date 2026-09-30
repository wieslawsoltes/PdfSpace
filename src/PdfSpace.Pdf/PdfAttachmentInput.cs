namespace PdfSpace.Pdf;

/// <summary>One explicitly selected, untrusted file to embed. Callers own the input bytes and
/// must not mutate them during the synchronous operation. Results never retain this array.</summary>
public sealed record PdfAttachmentInput(string FileName, byte[] Bytes,
    string Description = "", string MediaType = "application/octet-stream");
