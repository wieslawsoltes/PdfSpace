using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Pdf;

namespace PdfSpace.App;

/// <summary>Uses an isolated, short-lived QPDF WebAssembly worker; rendering remains in Uno/Skia.</summary>
internal sealed class BrowserPdfSecurityProvider : IPdfSecurityProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PdfUnlockResult> UnlockAsync(byte[] source, string? ownerPassword = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var result = JsonDocument.Parse(await BrowserPdfCrypto.Unlock(Convert.ToBase64String(source), ownerPassword ?? ""));
            cancellationToken.ThrowIfCancellationRequested();
            var root = result.RootElement;
            ThrowOnError(root);
            var encrypted = root.GetProperty("encrypted").GetBoolean();
            var bytes = encrypted ? Convert.FromBase64String(root.GetProperty("base64").GetString()!) : source;
            if (bytes.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("Unlocked PDF exceeds the 64 MB working-copy limit.");
            return new(bytes, encrypted);
        }
        finally { _gate.Release(); }
    }

    public async Task<byte[]> EncryptAsync(byte[] source, PdfProtectionOptions protection, CancellationToken cancellationToken = default)
    {
        protection.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var result = JsonDocument.Parse(await BrowserPdfCrypto.Encrypt(Convert.ToBase64String(source), protection.UserPassword, protection.OwnerPassword, protection.AllowPrint, protection.AllowCopy, protection.AllowEdit));
            cancellationToken.ThrowIfCancellationRequested();
            ThrowOnError(result.RootElement);
            return Convert.FromBase64String(result.RootElement.GetProperty("base64").GetString()!);
        }
        finally { _gate.Release(); }
    }

    private static void ThrowOnError(JsonElement result)
    {
        var code = result.GetProperty("code").GetString();
        if (code == "ok") return;
        if (code == "owner-required") throw new PdfPasswordRequiredException("Enter the correct owner/editing password. Opening-only password permissions are not bypassed.");
        throw new InvalidOperationException(result.GetProperty("message").GetString() ?? "The PDF security operation failed.");
    }
}

internal static partial class BrowserPdfCrypto
{
    [JSImport("globalThis.pdfSpaceSecurity.unlock")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Unlock(string base64, string password);

    [JSImport("globalThis.pdfSpaceSecurity.encrypt")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Encrypt(string base64, string userPassword, string ownerPassword, bool allowPrint, bool allowCopy, bool allowEdit);
}
