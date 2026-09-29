using System.Text.Json;
using PdfSpace.Pdf;

namespace PdfSpace.Workbench;

/// <summary>
/// Reuses a serialized immutable descriptor array for opt-in diagnostics. Geometry,
/// selection and pointer state remain live; old source documents are never retained.
/// This is not used by the production renderer or document writer.
/// </summary>
internal sealed class PdfObjectDiagnostics
{
    private const int MaximumCachedBytes = 16 * 1024 * 1024;
    private WeakReference<PdfPageObject[]>? _source;
    private byte[]? _bytes;
    private bool _redacted;
    public long BuildCount { get; private set; }
    public int CachedBytes => _bytes?.Length ?? 0;

    public void Clear() { _source = null; _bytes = null; }

    public void Write(Utf8JsonWriter json, PdfPageObject[] objects, bool redactText)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(objects);
        if (_bytes is not null && _redacted == redactText && _source is not null &&
            _source.TryGetTarget(out var remembered) && ReferenceEquals(remembered, objects))
        {
            // The bytes were produced only by Utf8JsonWriter, not by a document or caller.
            json.WriteRawValue(_bytes, skipInputValidation: true);
            return;
        }
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteArray(writer, objects, redactText);
        var bytes = buffer.ToArray(); BuildCount++;
        if (bytes.Length <= MaximumCachedBytes)
        {
            _bytes = bytes; _redacted = redactText;
            if (_source is null) _source = new(objects); else _source.SetTarget(objects);
        }
        else Clear();
        json.WriteRawValue(bytes, skipInputValidation: true);
    }

    private static void WriteArray(Utf8JsonWriter json, PdfPageObject[] objects, bool redactText)
    {
        json.WriteStartArray();
        for (var i = 0; i < objects.Length; i++)
        {
            var item = objects[i];
            json.WriteStartObject();
            json.WriteNumber("index", i);
            json.WriteString("kind", item.Kind.ToString());
            json.WriteString("text", redactText ? "[protected]" : item.Text);
            json.WriteNumber("x", item.Bounds.X);
            json.WriteNumber("y", item.Bounds.Y);
            json.WriteNumber("width", item.Bounds.Width);
            json.WriteNumber("height", item.Bounds.Height);
            json.WriteBoolean("editable", item.Editable);
            void PaintNumber(string name, double? value) { if (value is { } n) json.WriteNumber(name, n); else json.WriteNull(name); }
            PaintNumber("fillOpacity", item.Paint.FillOpacity);
            PaintNumber("strokeOpacity", item.Paint.StrokeOpacity);
            PaintNumber("strokeWidth", item.Paint.StrokeWidth);
            PaintNumber("miterLimit", item.Paint.MiterLimit);
            PaintNumber("dashPhase", item.Paint.Dash?.Phase);
            json.WriteString("blend", item.Paint.BlendMode?.ToString());
            json.WriteString("lineCap", item.Paint.LineCap?.ToString());
            json.WriteString("lineJoin", item.Paint.LineJoin?.ToString());
            if (item.Paint.Dash is { } dash)
            { json.WriteStartArray("dash"); foreach (var length in dash.Lengths) json.WriteNumberValue(length); json.WriteEndArray(); }
            else json.WriteNull("dash");
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
