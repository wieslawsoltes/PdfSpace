namespace PdfSpace.Storage;

public sealed record WorkspaceFile(string Name, byte[] Bytes);
public interface IWorkspaceStorage
{
    Task<WorkspaceFile?> OpenAsync();
    Task<WorkspaceFile?> OpenImageAsync() => Task.FromException<WorkspaceFile?>(new NotSupportedException("This host has not enabled image file picking."));
    /// <summary>Open an XFDF or form-data JSON file, limited to 4 MB. Existing hosts may opt in without breaking document opening.</summary>
    Task<WorkspaceFile?> OpenFormDataAsync() => Task.FromException<WorkspaceFile?>(new NotSupportedException("This host has not enabled form-data file picking."));
    /// <summary>Explicit arbitrary-file selection for embedding, limited to 16 MiB. Never opens or executes the selected content.</summary>
    Task<WorkspaceFile?> OpenAttachmentAsync() => Task.FromException<WorkspaceFile?>(new NotSupportedException("This host has not enabled attachment file picking."));
    Task SaveAsync(string name, byte[] bytes, string contentType);
    Task<string?> ReadRecoveryAsync();
    Task WriteRecoveryAsync(string workspace);
    Task ClearRecoveryAsync();
    Task CopyTextAsync(string text);
    Task PrintAsync(string name, byte[] pdf);
}
