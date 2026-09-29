using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;

namespace PdfSpace.Pdf;

/// <summary>Per-operation canonical content checks, not authentication. Shared resources are hashed once.</summary>
internal sealed class PdfMarkFingerprint
{
    private readonly Dictionary<PdfObject, string> _cache = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfObject> _active = new(ReferenceEqualityComparer.Instance);
    private long _expandedBytes;
    public int ResourcesHashed => _cache.Count;
    public string Hash(PdfItem? input)
    {
        var item = PdfObjects.Resolve(input);
        if (item is PdfObject o && _cache.TryGetValue(o, out var cached)) return cached;
        if (item is PdfObject obj && (!_active.Add(obj) || _active.Count > 32 || _cache.Count > 20000))
            throw new InvalidDataException("Cyclic or excessive page-mark resources.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) { var bytes = Encoding.UTF8.GetBytes(s); hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes); }
        switch (item)
        {
            case PdfDictionary dictionary:
                Add("dictionary");
                foreach (var key in dictionary.Elements.Keys.Order(StringComparer.Ordinal))
                {
                    if (dictionary.Stream is not null && key is "/Length" or "/Filter" or "/DecodeParms") continue;
                    Add(key); Add(Hash(dictionary.Elements[key]));
                }
                if (dictionary.Stream is { } stream)
                {
                    if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Oversized page-mark stream.");
                    var bytes = stream.UnfilteredValue; _expandedBytes += bytes.Length;
                    if (bytes.Length > 16 * 1024 * 1024 || _expandedBytes > 128 * 1024 * 1024) throw new InvalidDataException("Expanded page-mark resources exceed the operation budget.");
                    Add("stream"); hash.AppendData(bytes);
                }
                break;
            case PdfArray array:
                Add("array"); foreach (var value in array.Elements) Add(Hash(value)); break;
            // PDFsharp writes PdfReal values with three fractional digits. Integers and reals must hash identically after parsing.
            case PdfReal real: Add("number:" + real.Value.ToString("0.###", CultureInfo.InvariantCulture)); break;
            case PdfInteger integer: Add("number:" + integer.Value.ToString(CultureInfo.InvariantCulture)); break;
            case PdfString text: Add("string:" + text.Value); break;
            case PdfName name: Add("name:" + name.Value); break;
            case PdfBoolean boolean: Add(boolean.Value ? "true" : "false"); break;
            case null: Add("null"); break;
            default: Add(item.ToString() ?? "null"); break;
        }
        var result = Convert.ToHexString(hash.GetHashAndReset());
        if (item is PdfObject owned) { _active.Remove(owned); _cache[owned] = result; }
        return result;
    }
}
