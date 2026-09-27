using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PdfSpace.Core;

namespace PdfSpace.Documents;

/// <summary>Bounded XFDF field-data and PdfSpace JSON interchange; never resolves document references or executes actions.</summary>
public static class PdfFormDataCodec
{
    public const string XfdfNamespace = "http://ns.adobe.com/xfdf/";
    public const string JsonFormat = "PdfSpace.FormData/1";
    private static readonly XNamespace Namespace = XfdfNamespace;

    public static PdfFormData Read(byte[] bytes, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (bytes.Length is 0 or > PdfFormData.MaximumBytes) throw new InvalidDataException("Form-data files must contain 1 byte to 4 MB.");
        var extension = Path.GetExtension(fileName);
        return extension.ToLowerInvariant() switch
        {
            ".xfdf" => ReadXfdf(bytes),
            ".json" => ReadJson(bytes),
            _ => throw new InvalidDataException("Choose an XFDF or PdfSpace form-data JSON file.")
        };
    }

    public static byte[] WriteXfdf(PdfFormData data)
    {
        ArgumentNullException.ThrowIfNull(data); data.Validate();
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false, CheckCharacters = true
        }))
        {
            writer.WriteStartDocument(); writer.WriteStartElement("xfdf", XfdfNamespace);
            writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteStartElement("fields", XfdfNamespace);
            foreach (var field in data.Fields)
            {
                writer.WriteStartElement("field", XfdfNamespace); writer.WriteAttributeString("name", field.Name);
                foreach (var value in field.Values) writer.WriteElementString("value", XfdfNamespace, value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
        }
        return CheckOutput(output);
    }

    public static byte[] WriteJson(PdfFormData data)
    {
        ArgumentNullException.ThrowIfNull(data); data.Validate();
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("format", JsonFormat); writer.WriteString("document", data.Document); writer.WriteStartArray("fields");
            foreach (var field in data.Fields)
            {
                writer.WriteStartObject(); writer.WriteString("name", field.Name);
                if (field.Values.Length == 1) writer.WriteString("value", field.Values[0]);
                else { writer.WriteStartArray("values"); foreach (var value in field.Values) writer.WriteStringValue(value); writer.WriteEndArray(); }
                if (field.Kind is { } kind) writer.WriteString("type", kind.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return CheckOutput(output);
    }

    private static byte[] CheckOutput(MemoryStream output)
    {
        if (output.Length > PdfFormData.MaximumBytes) throw new InvalidDataException("Encoded form data exceeds 4 MB.");
        return output.ToArray();
    }

    private static PdfFormData ReadJson(byte[] bytes)
    {
        ReadOnlyMemory<byte> input = bytes;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) input = input[3..];
        using var json = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        RequireObject(root);
        if (root.TryGetProperty("format", out var format) && format.GetString() != JsonFormat) throw new InvalidDataException("Unsupported form-data JSON version.");
        if (!root.TryGetProperty("fields", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > PdfFormData.MaximumFields)
            throw new InvalidDataException("Form-data JSON must contain a bounded fields array.");
        var fields = new List<PdfFormDataField>();
        foreach (var entry in entries.EnumerateArray())
        {
            RequireObject(entry);
            if (!entry.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) throw new InvalidDataException("A form field is missing its name.");
            string[] values;
            if (entry.TryGetProperty("value", out var value))
            {
                if (entry.TryGetProperty("values", out _) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("A form field must have unambiguous string values.");
                values = [value.GetString()!];
            }
            else if (entry.TryGetProperty("values", out var many))
            {
                if (many.ValueKind != JsonValueKind.Array || many.GetArrayLength() > PdfFormData.MaximumFields || many.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    throw new InvalidDataException("Invalid form field value array.");
                values = many.EnumerateArray().Select(item => item.GetString()!).ToArray();
            }
            else values = [];
            PdfFieldKind? kind = null;
            if (entry.TryGetProperty("type", out var type))
            {
                if (type.ValueKind != JsonValueKind.String || !Enum.TryParse<PdfFieldKind>(type.GetString(), out var parsed) || !Enum.IsDefined(parsed))
                    throw new InvalidDataException("Unrecognized form field type.");
                kind = parsed;
            }
            fields.Add(new(name.GetString()!, values, kind));
        }
        var result = new PdfFormData(root.TryGetProperty("document", out var title) ? title.GetString() ?? "" : "", fields.ToArray());
        result.Validate(); return result;
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("Form-data objects cannot contain duplicate property names.");
    }

    private static XmlReader Reader(byte[] bytes) => XmlReader.Create(new MemoryStream(bytes, false), new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = PdfFormData.MaximumBytes,
        MaxCharactersFromEntities = 1024, CloseInput = true
    });

    private static PdfFormData ReadXfdf(byte[] bytes)
    {
        // Reject excessive nesting before constructing an XML tree. Neither pass can access external resources.
        using (var scan = Reader(bytes)) while (scan.Read()) if (scan.Depth > 64) throw new InvalidDataException("XFDF nesting exceeds the 64-level limit.");
        using var reader = Reader(bytes);
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (xml.Root is not { } root || root.Name != Namespace + "xfdf") throw new InvalidDataException("The XFDF root must use the Adobe XFDF namespace.");
        var containers = root.Elements(Namespace + "fields").ToArray();
        if (containers.Length != 1) throw new InvalidDataException("XFDF must contain exactly one fields section.");
        var fields = new List<PdfFormDataField>();
        foreach (var field in containers[0].Elements()) Visit(field, "", 0);
        var result = new PdfFormData("", fields.ToArray()); result.Validate(); return result;

        void Visit(XElement element, string prefix, int depth)
        {
            if (depth > 32 || element.Name != Namespace + "field") throw new InvalidDataException("Invalid or excessively nested XFDF field hierarchy.");
            var part = (string?)element.Attribute("name");
            if (string.IsNullOrWhiteSpace(part)) throw new InvalidDataException("An XFDF field has no name.");
            var name = prefix.Length == 0 ? part : prefix + "." + part;
            if (name.Length > 4096) throw new InvalidDataException("An XFDF field name exceeds 4096 characters.");
            var children = element.Elements().ToArray();
            if (children.Any(child => child.Name != Namespace + "field" && child.Name != Namespace + "value"))
                throw new InvalidDataException("Rich-text and other non-plain XFDF field values are not supported.");
            var nested = children.Where(child => child.Name == Namespace + "field").ToArray();
            var values = children.Where(child => child.Name == Namespace + "value").ToArray();
            if (nested.Length > 0)
            {
                if (values.Length > 0) throw new InvalidDataException("An XFDF field cannot contain both terminal values and child fields.");
                foreach (var child in nested) Visit(child, name, depth + 1);
            }
            else
            {
                if (fields.Count >= PdfFormData.MaximumFields || values.Any(value => value.HasElements)) throw new InvalidDataException("Invalid or excessive XFDF values.");
                fields.Add(new(name, values.Select(value => value.Value).ToArray()));
            }
        }
    }
}
