using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
namespace PdfSpace.Pdf;

public sealed record PdfTextRun(Guid SourceId, int SourcePage, int Index, string SourceHash, string Text, string FontResource, bool Editable, string Limitation);

/// <summary>Edits actual text-showing operands, not white overlays. Reuses the embedded font and rejects unavailable glyphs.</summary>
public static class PdfTextEditor
{
    private sealed record Run(CString[] Operands, FontCodec? Codec, string Font, string Limitation)
    {
        public string? Decode()
        {
            if (Codec is null) return null;
            var parts = Operands.Select(operand => Codec.Decode(operand.Value)).ToArray();
            return parts.Any(part => part is null) ? null : string.Concat(parts);
        }
    }
    public static PdfTextRun[] Read(PdfWorkspace workspace, int pageIndex)
    {
        var page = workspace.Pages[pageIndex]; if (page.SourceId is not { } id) return [];
        var source = workspace.Sources.First(source => source.Id == id);
        using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        var inspection = PdfDocumentEngine.Inspect(source.Bytes);
        var locked = inspection.SignatureFields > 0 || inspection.HasXfa;
        var content = ContentReader.ReadContent(native.Pages[page.SourcePage - 1]);
        var hash = Convert.ToHexString(SHA256.HashData(source.Bytes));
        return Enumerate(native.Pages[page.SourcePage - 1], content).Select((run, i) =>
        {
            var decoded = run.Decode();
            return new PdfTextRun(id, page.SourcePage, i, hash, decoded ?? "[Unsupported font encoding]", run.Font, decoded is not null && !locked,
                locked ? "Signed/certified or XFA source; editing is blocked." : decoded is null ? run.Limitation : "Uses the original font subset; no paragraph reflow. Only glyphs already encoded by the source font can be inserted.");
        }).ToArray();
    }
    public static PdfWorkspace Replace(PdfWorkspace workspace, PdfTextRun target, string replacement)
    {
        if (replacement.Length > 100000) throw new ArgumentException("Replacement text exceeds the length limit.");
        var source = workspace.Sources.FirstOrDefault(source => source.Id == target.SourceId) ?? throw new InvalidOperationException("The text selection belongs to a different document revision.");
        if (Convert.ToHexString(SHA256.HashData(source.Bytes)) != target.SourceHash) throw new InvalidOperationException("The source has changed. Select the text again.");
        var inspection = PdfDocumentEngine.Inspect(source.Bytes);
        if (inspection.SignatureFields > 0 || inspection.HasXfa) throw new NotSupportedException("Signed/certified and XFA sources cannot be edited.");
        using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        if (target.SourcePage < 1 || target.SourcePage > native.PageCount) throw new InvalidDataException("Invalid source page.");
        var page = native.Pages[target.SourcePage - 1]; var content = ContentReader.ReadContent(page);
        var runs = Enumerate(page, content);
        if (target.Index < 0 || target.Index >= runs.Length) throw new InvalidDataException("The text run no longer exists.");
        var run = runs[target.Index];
        if (run.Codec is null || run.Decode() != target.Text) throw new NotSupportedException("This text run has an unsupported or changed font encoding.");
        var replacementRunes = replacement.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        var capacities = run.Operands.Select(operand => run.Codec.Decode(operand.Value)!.EnumerateRunes().Count()).ToArray();
        if (run.Operands.Length > 1 && replacementRunes.Length > capacities.Sum())
            throw new NotSupportedException("This text is positioned glyph-by-glyph. Use no more characters than the original; automatic paragraph reflow is not implemented.");
        var offset = 0;
        // Encode every replacement before mutating the parsed stream; a missing glyph fails atomically.
        var encoded = new List<string>();
        for (var i = 0; i < run.Operands.Length; i++)
        {
            var take = run.Operands.Length == 1 ? replacementRunes.Length : Math.Min(capacities[i], replacementRunes.Length - offset);
            encoded.Add(run.Codec.Encode(string.Concat(replacementRunes.Skip(offset).Take(take)))); offset += take;
        }
        for (var i = 0; i < run.Operands.Length; i++) { run.Operands[i].Value = encoded[i]; run.Operands[i].CStringType = CStringType.HexString; }
        NormalizeStrings(content);
        page.Contents.ReplaceContent(content);
        var next = source with { Id = Guid.NewGuid(), Bytes = PdfDocumentEngine.Bytes(native), PreviewBytes = null };
        var result = workspace with { Sources = workspace.Sources.Select(item => item.Id == source.Id ? next : item).ToArray(), Pages = workspace.Pages.Select(item => item.SourceId == source.Id ? item with { SourceId = next.Id } : item).ToArray() };
        return PdfDocumentEngine.PrepareWorkspace(result);
    }
    // PDFsharp 6.2 parses hexadecimal operands but its CString writer cannot serialize them.
    // A byte-exact hex wrapper handles all strings without changing text encodings or literal escapes.
    private sealed class HexOperand : CString
    {
        public override string ToString() => "<" + Convert.ToHexString(Encoding.Latin1.GetBytes(Value)) + ">";
    }
    private static void NormalizeStrings(CSequence sequence)
    {
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] is CString text && text.CStringType != CStringType.Dictionary)
            {
                if (text.Value.Any(c => c > 255)) throw new NotSupportedException("A content string has non-byte values; the stream cannot safely be rewritten.");
                sequence[i] = new HexOperand { Value = text.Value, CStringType = CStringType.HexString };
            }
            else if (sequence[i] is COperator operation) NormalizeStrings(operation.Operands);
            else if (sequence[i] is CSequence nested) NormalizeStrings(nested);
        }
    }
    private static Run[] Enumerate(PdfPage page, CSequence content)
    {
        var fontResources = PdfObjects.Dictionary(PdfObjects.Dictionary(page.Elements["/Resources"])?.Elements["/Font"]);
        var font = ""; var stack = new Stack<string>(); var codecs = new Dictionary<string, FontCodec?>(); var result = new List<Run>();
        var operands = new List<CString>();
        void Flush()
        {
            if (operands.Count == 0) return;
            if (!codecs.TryGetValue(font, out var codec)) { codec = FontCodec.Create(PdfObjects.Dictionary(fontResources?.Elements[font])); codecs[font] = codec; }
            result.Add(new(operands.ToArray(), codec, font, "This font requires an unsupported encoding or lacks a usable ToUnicode map.")); operands.Clear();
            if (result.Count > 10000) throw new InvalidDataException("Too many text runs in a page.");
        }
        foreach (var operation in content.OfType<COperator>())
        {
            if (operation.Name is "BT" or "ET" or "Tm" or "T*") Flush();
            else if (operation.Name is "Td" or "TD" && operation.Operands.Count > 1 && operation.Operands[1] is CNumber vertical && (vertical is CReal real ? real.Value : ((CInteger)vertical).Value) != 0) Flush();
            else if (operation.Name == "q") { Flush(); stack.Push(font); }
            else if (operation.Name == "Q" && stack.Count > 0) { Flush(); font = stack.Pop(); }
            else if (operation.Name == "Tf" && operation.Operands.FirstOrDefault() is CName name) { Flush(); font = name.Name; }
            else if (operation.Name is "Tj" or "TJ" or "'" or "\"")
            {
                if (operation.Name is "'" or "\"") Flush();
                foreach (var operand in operation.Operands)
                {
                    if (operand is CString text) operands.Add(text);
                    else if (operand is CArray array) operands.AddRange(array.OfType<CString>());
                }
            }
        }
        Flush(); return result.ToArray();
    }
    private sealed class FontCodec(Dictionary<string, string> map)
    {
        private readonly Dictionary<string, string> _map = map;
        private readonly int[] _sourceLengths = map.Keys.Select(key => key.Length / 2).Distinct().OrderDescending().ToArray();
        private readonly int[] _textLengths = map.Values.Select(value => value.Length).Where(n => n > 0).Distinct().OrderDescending().ToArray();
        private readonly Dictionary<string, string> _reverse = map.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
        public string? Decode(string raw)
        {
            if (raw.Any(c => c > 255)) return null;
            var bytes = Encoding.Latin1.GetBytes(raw); var output = new StringBuilder();
            for (var index = 0; index < bytes.Length;)
            {
                var found = false;
                foreach (var length in _sourceLengths)
                    if (index + length <= bytes.Length && _map.TryGetValue(Convert.ToHexString(bytes.AsSpan(index, length)), out var value))
                    { output.Append(value); index += length; found = true; break; }
                if (!found) return null;
            }
            return output.ToString();
        }
        public string Encode(string text)
        {
            using var bytes = new MemoryStream();
            for (var i = 0; i < text.Length;)
            {
                var found = false;
                foreach (var length in _textLengths)
                    if (i + length <= text.Length && _reverse.TryGetValue(text.Substring(i, length), out var code))
                    { bytes.Write(Convert.FromHexString(code)); i += length; found = true; break; }
                if (!found) throw new NotSupportedException($"The source font does not encode '{char.ConvertFromUtf32(char.ConvertToUtf32(text, i))}'. Choose text using the existing glyphs, or add a new text annotation.");
            }
            return Encoding.Latin1.GetString(bytes.ToArray());
        }
        public static FontCodec? Create(PdfDictionary? font)
        {
            if (font is null) return null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (PdfObjects.Dictionary(font.Elements["/ToUnicode"]) is { Stream: not null } toUnicode)
            {
                var data = toUnicode.Stream.UnfilteredValue;
                if (data.Length > 4 * 1024 * 1024) throw new InvalidDataException("ToUnicode map exceeds the decoding limit.");
                var cmap = Encoding.ASCII.GetString(data);
                foreach (Match block in Match(cmap, @"beginbfchar(.*?)endbfchar"))
                    foreach (Match entry in Match(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                        Add(entry.Groups[1].Value, entry.Groups[2].Value);
                foreach (Match block in Match(cmap, @"beginbfrange(.*?)endbfrange"))
                    foreach (Match entry in Match(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|\[([^\]]*)\])"))
                    {
                        var firstHex = entry.Groups[1].Value; var lastHex = entry.Groups[2].Value;
                        if (firstHex.Length != lastHex.Length || firstHex.Length is < 2 or > 8 || firstHex.Length % 2 != 0) return null;
                        var first = Convert.ToUInt32(firstHex, 16); var last = Convert.ToUInt32(lastHex, 16);
                        if (last < first || last - first > 65535) return null;
                        var destinations = Match(entry.Groups[4].Value, @"<([0-9A-Fa-f]+)>").Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
                        for (uint i = 0; i <= last - first; i++)
                        {
                            string destination;
                            if (destinations.Length > 0) { if (i >= destinations.Length) return null; destination = destinations[i]; }
                            else
                            {
                                var raw = Convert.FromHexString(entry.Groups[3].Value); uint carry = i;
                                for (var pos = raw.Length - 1; pos >= 0 && carry > 0; pos--) { carry += raw[pos]; raw[pos] = (byte)carry; carry >>= 8; }
                                if (carry != 0) return null; destination = Convert.ToHexString(raw);
                            }
                            Add((first + i).ToString("X" + firstHex.Length), destination);
                        }
                    }
            }
            else
            {
                var encoding = PdfObjects.Text(font.Elements["/Encoding"]); var baseFont = PdfObjects.Text(font.Elements["/BaseFont"]);
                if (PdfObjects.Dictionary(font.Elements["/Encoding"]) is not null || PdfObjects.Text(font.Elements["/Subtype"]) is not ("Type1" or "TrueType")) return null;
                if (encoding != "WinAnsiEncoding" && !(encoding.Length == 0 && new[] { "Helvetica", "Times", "Courier" }.Any(name => baseFont.StartsWith(name, StringComparison.Ordinal)))) return null;
                // The portable, unambiguous subset shared by StandardEncoding and WinAnsiEncoding.
                for (var code = 32; code <= 126; code++) map[code.ToString("X2")] = ((char)code).ToString();
            }
            return map.Count == 0 ? null : new(map);
            void Add(string source, string destination)
            {
                if (source.Length is < 2 or > 8 || source.Length % 2 != 0 || destination.Length == 0 || destination.Length % 4 != 0 || destination.Length > 64) return;
                if (map.Count >= 100000) throw new InvalidDataException("Excessive ToUnicode map entries.");
                map[source.ToUpperInvariant()] = Encoding.BigEndianUnicode.GetString(Convert.FromHexString(destination));
            }
        }
        private static MatchCollection Match(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
