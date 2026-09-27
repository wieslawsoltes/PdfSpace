namespace PdfSpace.Pdf;

public sealed record PdfInspection(int Pages, int FormWidgets, int SignatureFields, bool HasXfa, bool HasJavaScript, bool Encrypted, int Annotations, int Outlines);
public sealed record PdfWriteResult(byte[] Bytes, bool PreservedSourceCatalog, string[] Warnings);
public sealed record PdfProtectionOptions(string UserPassword, string OwnerPassword, bool AllowPrint = true, bool AllowCopy = true, bool AllowEdit = true)
{
    public void Validate()
    {
        static bool Valid(string value) => value is not null && value.Length >= 8 && !value.Contains('\0') && new System.Text.UTF8Encoding(false, true).GetByteCount(value) <= 127;
        if (!Valid(UserPassword) || !Valid(OwnerPassword)) throw new ArgumentException("Passwords require at least eight characters and at most 127 UTF-8 bytes, without null characters.");
        if (UserPassword.Normalize(System.Text.NormalizationForm.FormKC) == OwnerPassword.Normalize(System.Text.NormalizationForm.FormKC)) throw new ArgumentException("Choose different opening and owner passwords.");
    }
    public override string ToString() => $"PDF protection: passwords=[redacted], print={AllowPrint}, copy={AllowCopy}, edit={AllowEdit}";
}
public sealed class PdfPasswordRequiredException(string message, Exception? inner = null) : Exception(message, inner);
