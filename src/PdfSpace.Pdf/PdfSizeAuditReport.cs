using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfSpace.Pdf;

[JsonConverter(typeof(JsonStringEnumConverter<PdfStreamKind>))]
public enum PdfStreamKind { PageContent, Image, FontProgram, FormAppearance, EmbeddedFile, Metadata, Other }
public sealed record PdfStreamUsage(PdfStreamKind Kind, int StreamCount, long EncodedBytes, double PercentOfStreamBytes);
public sealed record PdfStreamObjectUsage(int ObjectNumber, int Generation, PdfStreamKind Kind, long EncodedBytes);
public sealed record PdfSourceSizeReport(long FileBytes, int IndirectObjectCount, int StreamCount, long EncodedStreamBytes,
    IReadOnlyList<PdfStreamUsage> Categories, IReadOnlyList<PdfStreamObjectUsage> LargestStreams);
public sealed record PdfAuditedSource(int SourceNumber, int Aliases, PdfSourceSizeReport Usage);
public sealed record PdfWorkspaceSizeReport(int SchemaVersion, string Scope, int SourceEntries, int UniqueSourceBuffers,
    long RetainedFileBytes, long EncodedStreamBytes, IReadOnlyList<PdfStreamUsage> Categories, IReadOnlyList<PdfAuditedSource> Sources)
{
    /// <summary>Source-generated serialization is safe for browser trimming; no PDF contents or file names are included.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, PdfSizeAuditJson.Default.PdfWorkspaceSizeReport);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PdfWorkspaceSizeReport))]
internal partial class PdfSizeAuditJson : JsonSerializerContext;
