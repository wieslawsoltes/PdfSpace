using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;

namespace PdfSpace.Pdf;

/// <summary>Read-only catalog attachment access. Never resolves file paths, executes actions or launches applications.</summary>
public static class PdfEmbeddedFiles
{
    public const int MaximumFiles = 512;
    public const int MaximumExtractedBytes = 16 * 1024 * 1024;
    private sealed record Entry(PdfEmbeddedFileInfo Info, PdfDictionary? Stream);

    /// <summary>Inspect a bounded EmbeddedFiles name tree without decoding attachment payloads.</summary>
    public static IReadOnlyList<PdfEmbeddedFileInfo> Read(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocumentEngine.OpenNative(bytes);
        var entries = ReadEntries(document, Convert.ToHexString(SHA256.HashData(bytes)), cancellationToken);
        return Array.AsReadOnly(entries.Select(entry => entry.Info).ToArray());
    }

    /// <summary>Revalidate source and descriptor, then extract one unfiltered or Flate-encoded embedded file.
    /// The returned bytes are untrusted and must not be opened automatically.</summary>
    public static byte[] Extract(byte[] bytes, PdfEmbeddedFileInfo selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > PdfSpace.Core.WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("PDF source exceeds 64 MB.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        if (fingerprint != selection.SourceFingerprint) throw new InvalidOperationException("The attachment source changed. Inspect it again.");
        using var document = PdfDocumentEngine.OpenNative(bytes);
        var entry = ReadEntries(document, fingerprint, cancellationToken).SingleOrDefault(entry => entry.Info.Key == selection.Key);
        if (entry is null || entry.Info != selection) throw new InvalidOperationException("The attachment selection is stale or invalid.");
        if (!selection.CanExtract) throw new NotSupportedException(selection.UnavailableReason);
        var encoded = entry.Stream!.Stream!.Value;
        var flate = PdfObjects.Resolve(entry.Stream.Elements["/Filter"]) is not (null or PdfNull);
        byte[] result;
        if (!flate) result = encoded.ToArray();
        else
        {
            // Verify the zlib envelope/checksum explicitly: a truncated deflate stream
            // must not become a silently accepted partial attachment on any host.
            if (encoded.Length < 6 || (encoded[0] & 15) != 8 || (encoded[0] >> 4) > 7 ||
                ((encoded[0] << 8) | encoded[1]) % 31 != 0 || (encoded[1] & 32) != 0)
                throw new InvalidDataException("Invalid or dictionary-dependent attachment zlib stream.");
            using var input = new MemoryStream(encoded, writable: false);
            using var decoder = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(32768);
            uint a = 1, b = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = decoder.Read(buffer, 0, 32768);
                    if (read == 0) break;
                    if (read > MaximumExtractedBytes - output.Length) throw new InvalidDataException("The attachment expands beyond 16 MiB.");
                    output.Write(buffer, 0, read);
                    // 5552-byte reductions bound Adler-32 intermediates in UInt32.
                    for (var start = 0; start < read; start += 5552)
                    {
                        var end = Math.Min(read, start + 5552);
                        for (var i = start; i < end; i++) { a += buffer[i]; b += a; }
                        a %= 65521; b %= 65521;
                    }
                }
                if (((b << 16) | a) != BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(encoded.Length - 4)))
                    throw new InvalidDataException("Attachment zlib checksum does not match the decoded payload.");
                result = output.ToArray();
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (selection.DeclaredBytes is { } expected && result.LongLength != expected)
            throw new InvalidDataException("Attachment length does not match its declared Size.");
        return result;
    }

    private static List<Entry> ReadEntries(PdfDocument document, string fingerprint, CancellationToken cancellationToken)
    {
        if (document.Internals.GetAllObjects().Length > PdfSizeAudit.MaximumObjects)
            throw new InvalidDataException("Attachment inspection exceeds 200,000 indirect objects.");
        var result = new List<Entry>();
        var namesItem = document.Internals.Catalog.Elements["/Names"];
        if (namesItem is null) return result;
        var names = PdfObjects.Dictionary(namesItem) ?? throw new InvalidDataException("Invalid catalog Names dictionary.");
        var treeItem = names.Elements["/EmbeddedFiles"];
        if (treeItem is null) return result;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Visit(PdfDictionary node, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 16 || !seen.Add(node) || seen.Count > 4096 || node.Stream is not null)
                throw new InvalidDataException("Cyclic, shared, streamed or excessive attachment name tree.");
            var valuesItem = node.Elements["/Names"]; var kidsItem = node.Elements["/Kids"];
            if ((valuesItem is null) == (kidsItem is null)) throw new InvalidDataException("An attachment node needs either Names or Kids.");
            var first = result.Count;
            if (valuesItem is not null)
            {
                var values = PdfObjects.Array(valuesItem) ?? throw new InvalidDataException("Invalid attachment name pairs.");
                if (values.Elements.Count % 2 != 0 || values.Elements.Count > MaximumFiles * 2)
                    throw new InvalidDataException("Invalid or excessive attachment name pairs.");
                for (var i = 0; i < values.Elements.Count; i += 2)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = Text(values.Elements[i], 1024, required: true);
                    if (!keys.Add(key) || result.Count == MaximumFiles) throw new InvalidDataException("Duplicate or excessive attachment keys.");
                    var spec = PdfObjects.Dictionary(values.Elements[i + 1]) ?? throw new InvalidDataException("Invalid attachment file specification.");
                    if (spec.Stream is not null) throw new InvalidDataException("Attachment specifications cannot be streams.");
                    var name = Text(spec.Elements["/UF"] ?? spec.Elements["/F"], 1024);
                    if (name.Length == 0) name = key;
                    var description = Text(spec.Elements["/Desc"], 4096);
                    var ef = PdfObjects.Dictionary(spec.Elements["/EF"]);
                    var stream = PdfObjects.Dictionary(ef?.Elements["/UF"] ?? ef?.Elements["/F"]);
                    long? size = null;
                    var parameters = PdfObjects.Dictionary(stream?.Elements["/Params"]);
                    if (parameters?.Elements["/Size"] is { } sizeItem)
                        size = PdfObjects.Resolve(sizeItem) is PdfInteger integer && integer.Value >= 0
                            ? integer.Value : throw new InvalidDataException("Attachment Size must be a nonnegative supported integer.");
                    var mime = PdfObjects.Resolve(stream?.Elements["/Subtype"]) switch
                    {
                        null => "", PdfName value when value.Value.Length <= 256 => value.Value.TrimStart('/'),
                        _ => throw new InvalidDataException("Invalid attachment media type.")
                    };
                    var reason = Unsupported(spec, stream, size);
                    result.Add(new(new(fingerprint, key, name, DownloadFileName(name), DisplayText(description),
                        DisplayText(mime), stream?.Stream?.Length ?? 0, size, reason), stream));
                }
            }
            else
            {
                var kids = PdfObjects.Array(kidsItem) ?? throw new InvalidDataException("Invalid attachment child array.");
                if (kids.Elements.Count is < 1 or > 512) throw new InvalidDataException("Excessive attachment tree fanout.");
                foreach (var kid in kids.Elements)
                    Visit(PdfObjects.Dictionary(kid) ?? throw new InvalidDataException("Invalid attachment child."), depth + 1);
            }
            if (node.Elements["/Limits"] is { } limitsItem)
            {
                var limits = PdfObjects.Array(limitsItem);
                if (result.Count == first || limits is null || limits.Elements.Count != 2 ||
                    Text(limits.Elements[0], 1024, true) != result[first].Info.Key || Text(limits.Elements[1], 1024, true) != result[^1].Info.Key)
                    throw new InvalidDataException("Attachment tree Limits do not match its entries.");
            }
        }
        Visit(PdfObjects.Dictionary(treeItem) ?? throw new InvalidDataException("Invalid EmbeddedFiles tree."), 0);
        // Tolerate noncanonical source ordering without rewriting it; duplicate decoded keys remain errors.
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Info.Key, right.Info.Key));
        return result;
    }

    private static string? Unsupported(PdfDictionary spec, PdfDictionary? stream, long? size)
    {
        if (spec.Elements.ContainsKey("/FS")) return "External or specialized file systems are not followed.";
        if (stream?.Stream is null) return "No embedded file payload is present; external paths are never followed.";
        if (stream.Elements.ContainsKey("/F")) return "External stream data is not followed.";
        if (stream.Stream.Length > MaximumExtractedBytes || size > MaximumExtractedBytes) return "Attachment exceeds the 16 MiB download limit.";
        var filter = PdfObjects.Resolve(stream.Elements["/Filter"]);
        if (filter is PdfArray array)
        {
            if (array.Elements.Count != 1) return "This attachment uses an unsupported filter chain.";
            filter = PdfObjects.Resolve(array.Elements[0]);
            if (filter is not PdfName) return "Invalid attachment filter chain.";
        }
        if (filter is not (null or PdfNull) && (filter is not PdfName name || name.Value != "/FlateDecode"))
            return "Only unfiltered and FlateDecode attachments can be downloaded.";
        var parameters = PdfObjects.Resolve(stream.Elements["/DecodeParms"]);
        if (parameters is PdfArray parameterArray)
        {
            if (parameterArray.Elements.Count != 1) return "Unsupported attachment decoding parameters.";
            parameters = PdfObjects.Resolve(parameterArray.Elements[0]);
        }
        if (parameters is not (null or PdfNull) && (parameters is not PdfDictionary dictionary || dictionary.Elements.Count != 0))
            return "Attachment predictors or other decoding parameters are not supported.";
        return null;
    }

    private static string Text(PdfItem? value, int maximum, bool required = false)
    {
        var item = PdfObjects.Resolve(value);
        if (item is null && !required) return "";
        if (item is not PdfString text || text.Value.Length > maximum)
            throw new InvalidDataException("Invalid or excessive attachment text metadata.");
        // Reject malformed UTF-16, but retain valid names exactly for descriptor validation.
        for (var i = 0; i < text.Value.Length; i++)
            if (char.IsSurrogate(text.Value[i]))
            {
                if (!char.IsHighSurrogate(text.Value[i]) || i + 1 == text.Value.Length || !char.IsLowSurrogate(text.Value[++i]))
                    throw new InvalidDataException("Invalid Unicode attachment metadata.");
            }
        return text.Value;
    }

    public static string DownloadFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name[(Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\')) + 1)..];
        var text = new StringBuilder(Math.Min(name.Length, 120));
        foreach (var rune in name.EnumerateRunes())
        {
            if (text.Length + rune.Utf16SequenceLength > 120) break;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format || rune.Value < 128 && "<>:\"|?*".Contains((char)rune.Value)) text.Append('_');
            else text.Append(rune.ToString());
        }
        var result = text.ToString().Trim(' ', '.');
        if (result.Length == 0) result = "attachment.bin";
        var dot = result.IndexOf('.');
        var stem = (dot < 0 ? result : result[..dot]).TrimEnd(' ');
        stem = stem.Replace('¹', '1').Replace('²', '2').Replace('³', '3');
        if (ReservedNames.Contains(stem)) result = "attachment-" + result;
        if (ActiveExtensions.Contains(Path.GetExtension(result))) result += ".download";
        return result;
    }

    private static string DisplayText(string value) => string.Concat(value.EnumerateRunes().Select(rune =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format ? " " : rune.ToString()));
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    private static readonly HashSet<string> ActiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".dll", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".html", ".htm", ".svg", ".lnk", ".url", ".reg", ".sh", ".desktop", ".app", ".jar" };
}
