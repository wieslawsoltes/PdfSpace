using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>A decrypted, owner-authorized working copy. Never persist it implicitly.</summary>
public sealed record PdfUnlockResult(byte[] Bytes, bool WasEncrypted);

/// <summary>Platform cryptography boundary; implementations must authenticate owner access before decryption.</summary>
public interface IPdfSecurityProvider
{
    Task<PdfUnlockResult> UnlockAsync(byte[] source, string? ownerPassword = null, CancellationToken cancellationToken = default);
    Task<byte[]> EncryptAsync(byte[] source, PdfProtectionOptions protection, CancellationToken cancellationToken = default);
}

/// <summary>Desktop implementation using PDFsharp and the native .NET cryptographic provider.</summary>
public sealed class NativePdfSecurityProvider : IPdfSecurityProvider
{
    public Task<PdfUnlockResult> UnlockAsync(byte[] source, string? ownerPassword = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocumentEngine.OpenNative(source, ownerPassword);
        var encrypted = document.SecurityHandler.Elements.GetName("/Filter") == "/Standard";
        if (!encrypted) return Task.FromResult(new PdfUnlockResult(source, false));
        // Modify mode requires owner access; do not retry in Import mode to sidestep permissions.
        document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
        var bytes = PdfDocumentEngine.Bytes(document);
        if (bytes.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("Unlocked PDF exceeds the 64 MB working-copy limit.");
        return Task.FromResult(new PdfUnlockResult(bytes, true));
    }

    public Task<byte[]> EncryptAsync(byte[] source, PdfProtectionOptions protection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        protection.Validate();
        using var document = PdfDocumentEngine.OpenNative(source);
        document.SecuritySettings.UserPassword = protection.UserPassword;
        document.SecuritySettings.OwnerPassword = protection.OwnerPassword;
        document.SecurityHandler.SetEncryptionToV5();
        document.SecuritySettings.PermitPrint = document.SecuritySettings.PermitFullQualityPrint = protection.AllowPrint;
        document.SecuritySettings.PermitExtractContent = protection.AllowCopy;
        document.SecuritySettings.PermitModifyDocument = document.SecuritySettings.PermitAnnotations =
            document.SecuritySettings.PermitFormsFill = document.SecuritySettings.PermitAssembleDocument = protection.AllowEdit;
        var bytes = PdfDocumentEngine.Bytes(document);
        return Task.FromResult(bytes);
    }
}
