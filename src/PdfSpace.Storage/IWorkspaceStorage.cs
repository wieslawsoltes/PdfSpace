namespace PdfSpace.Storage;

public sealed record WorkspaceFile(string Name, byte[] Bytes);
public interface IWorkspaceStorage
{
    Task<WorkspaceFile?> OpenAsync();
    Task SaveAsync(string name, byte[] bytes, string contentType);
    Task<string?> ReadRecoveryAsync();
    Task WriteRecoveryAsync(string workspace);
    Task ClearRecoveryAsync();
    Task CopyTextAsync(string text);
    Task PrintAsync(string name, byte[] pdf);
}
