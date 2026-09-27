using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfSpace.Core;

/// <summary>Editable searchable text separate from the original scanned pixels. Rotation records the reading direction at recognition.</summary>
public sealed record PdfOcrLayer
{
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "eng";
    public string Engine { get; init; } = "Tesseract";
    public int Rotation { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public PdfOcrWord[] Words { get; init; } = [];

    public static void Validate(PdfOcrLayer layer, double width, double height)
    {
        if (layer.Version != 1 || layer.Language is not ("eng" or "pol" or "deu") || layer.Engine is null || layer.Engine.Length > 128 || layer.Rotation is not (0 or 90 or 180 or 270) || layer.Words is null || layer.Words.Length > 20000)
            throw new InvalidDataException("Invalid or unsupported OCR layer.");
        var ids = new HashSet<Guid>();
        foreach (var word in layer.Words)
        {
            if (word is null || !ids.Add(word.Id) || string.IsNullOrWhiteSpace(word.Text) || word.Text.Length > 512 || word.Text.Any(char.IsControl) || !word.Bounds.IsFinite || word.Bounds.X < 0 || word.Bounds.Y < 0 || word.Bounds.Width <= 0 || word.Bounds.Height <= 0 || word.Bounds.Right > width + .02 || word.Bounds.Bottom > height + .02 || !double.IsFinite(word.Confidence) || word.Confidence is < 0 or > 100 || word.Line is < 0 or > 100000)
                throw new InvalidDataException("Invalid OCR word, confidence or page coordinates.");
        }
    }
}

[JsonSerializable(typeof(PdfOcrLayer))]
public partial class PdfOcrJsonContext : JsonSerializerContext;
