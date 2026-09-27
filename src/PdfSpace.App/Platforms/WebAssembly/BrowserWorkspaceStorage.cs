using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using PdfSpace.Storage;
namespace PdfSpace.App;

internal sealed class BrowserWorkspaceStorage : IWorkspaceStorage
{
    public async Task<WorkspaceFile?> OpenAsync() => Decode(await BrowserFiles.Open());
    public async Task<WorkspaceFile?> OpenFormDataAsync() => Decode(await BrowserFiles.OpenFormData());
    private static WorkspaceFile? Decode(string result)
    {
        if (string.IsNullOrEmpty(result)) return null;
        using var json = JsonDocument.Parse(result);
        return new(json.RootElement.GetProperty("name").GetString()!, Convert.FromBase64String(json.RootElement.GetProperty("base64").GetString()!));
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType) => await BrowserFiles.Download(name, Convert.ToBase64String(bytes), contentType);
    public async Task<string?> ReadRecoveryAsync() => await BrowserFiles.Load();
    public async Task WriteRecoveryAsync(string workspace) => await BrowserFiles.Save(workspace);
    public async Task ClearRecoveryAsync() => await BrowserFiles.Clear();
    public async Task CopyTextAsync(string text) => await BrowserFiles.Copy(text);
    public async Task PrintAsync(string name, byte[] pdf) => await BrowserFiles.Print(name, Convert.ToBase64String(pdf));
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.pdfSpaceFiles.open")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Open();
    [JSImport("globalThis.pdfSpaceFiles.openFormData")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> OpenFormData();
    [JSImport("globalThis.pdfSpaceFiles.download")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Download(string name, string base64, string type);
    [JSImport("globalThis.pdfSpaceFiles.load")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Load();
    [JSImport("globalThis.pdfSpaceFiles.save")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Save(string workspace);
    [JSImport("globalThis.pdfSpaceFiles.clear")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Clear();
    [JSImport("globalThis.pdfSpaceFiles.copy")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Copy(string text);
    [JSImport("globalThis.pdfSpaceFiles.print")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Print(string name, string base64);
    [JSImport("globalThis.pdfSpaceFiles.isTestMode")] internal static partial bool IsTestMode();
    [JSImport("globalThis.pdfSpaceFiles.publishDiagnostics")] internal static partial void PublishDiagnostics(string json);
    [JSImport("globalThis.pdfSpaceFiles.setCanvasFocus")] internal static partial void SetCanvasFocus(bool focused);
    [JSImport("globalThis.pdfSpaceFiles.setDirty")] internal static partial void SetDirty(bool dirty);
}
