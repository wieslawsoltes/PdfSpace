using System.Buffers;

namespace PdfSpace.Storage;

/// <summary>Bounded input loading for file-picker hosts, including files that grow during a read.
/// Caller retains ownership of the input stream. The budget is payload size, not total process memory.</summary>
public static class WorkspaceFileReader
{
    public static async Task<byte[]> ReadBoundedAsync(Stream input, int maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(input));
        if (maximumBytes is < 0 or > 128 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (input.CanSeek && input.Length - input.Position > maximumBytes) throw new InvalidDataException("The selected file exceeds the input limit.");
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(32768);
        try
        {
            while (true)
            {
                // Read at most one byte beyond the remaining budget. No over-limit
                // block is appended to the output, even for a non-seekable/growing file.
                var available = Math.Min(32768, maximumBytes - (int)output.Length + 1);
                var read = await input.ReadAsync(buffer.AsMemory(0, available), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (read > maximumBytes - output.Length) throw new InvalidDataException("The file grew beyond the input limit.");
                output.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
