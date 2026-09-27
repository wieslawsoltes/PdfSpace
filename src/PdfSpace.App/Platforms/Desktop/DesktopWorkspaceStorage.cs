using System.Diagnostics;
using PdfSpace.Storage;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
namespace PdfSpace.App;

internal sealed class DesktopWorkspaceStorage : IWorkspaceStorage
{
    private static string RecoveryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfSpace");
    private static string RecoveryPath => Path.Combine(RecoveryDirectory, "recovery.pdfspace");
    public async Task<WorkspaceFile?> OpenAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add(".pdf"); picker.FileTypeFilter.Add(".pdfspace");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        using var input = await file.OpenStreamForReadAsync(); if (input.Length > 128 * 1024 * 1024) throw new InvalidDataException("The file exceeds the 128 MB limit.");
        using var output = new MemoryStream(); await input.CopyToAsync(output); return new(file.Name, output.ToArray());
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("Document", new List<string> { Path.GetExtension(name) }); var file = await picker.PickSaveFileAsync();
        if (file is null) throw new OperationCanceledException("Save cancelled."); await FileIO.WriteBytesAsync(file, bytes);
    }
    public async Task<string?> ReadRecoveryAsync() => File.Exists(RecoveryPath) ? await File.ReadAllTextAsync(RecoveryPath) : null;
    public async Task WriteRecoveryAsync(string workspace)
    {
        Directory.CreateDirectory(RecoveryDirectory); var temporary = RecoveryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, workspace); File.Move(temporary, RecoveryPath, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task ClearRecoveryAsync() { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); return Task.CompletedTask; }
    public Task CopyTextAsync(string text) { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); return Task.CompletedTask; }
    public async Task PrintAsync(string name, byte[] pdf)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PdfSpace"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(name)); await File.WriteAllBytesAsync(path, pdf);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
