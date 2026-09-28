using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

public sealed record PdfTextRun(Guid SourceId, int SourcePage, int Index, string SourceHash, string Text, string FontResource, bool Editable, string Limitation)
{
    public Guid PageId { get; init; }
    public string ScopePath { get; init; } = "";
    public double FontSize { get; init; }
    public bool CanReplaceFont { get; init; }
}

/// <summary>Edits native text operands, with occurrence isolation for nested/shared forms.</summary>
public static class PdfTextEditor
{
    private sealed record Run(CString[] Operands, FontCodec? Codec, string Font, double Size, int[] Operations, bool ReplaceFont)
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
        var source = workspace.Sources.First(item => item.Id == id);
        using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        var inspection = PdfDocumentEngine.Inspect(source.Bytes); var locked = inspection.SignatureFields > 0 || inspection.HasXfa;
        var hash = PdfContentGraph.Hash(source.Bytes); var result = new List<PdfTextRun>();
        foreach (var scope in PdfContentGraph.Read(native.Pages[page.SourcePage - 1]))
            foreach (var (run, index) in Enumerate(scope.Resources, scope.Content).Select((run, index) => (run, index)))
            {
                var decoded = run.Decode();
                result.Add(new(id, page.SourcePage, index, hash, decoded ?? "[Unsupported font encoding]", run.Font, decoded is not null && !locked,
                    locked ? "Signed/certified or XFA source; editing is blocked." : decoded is null ? "Unsupported source font encoding; no safe text replacement is available." :
                    "Edits only this text occurrence. Original-font mode requires encoded glyphs; replacement-font mode supports independent horizontal text-showing runs. No paragraph reflow.")
                { PageId = page.Id, ScopePath = scope.Path, FontSize = run.Size, CanReplaceFont = !locked && decoded is not null && run.ReplaceFont });
            }
        return result.ToArray();
    }
    public static PdfWorkspace Replace(PdfWorkspace workspace, PdfTextRun target, string replacement) =>
        ReplaceCore(workspace, target, replacement, null, null);
    public static PdfWorkspace ReplaceWithFont(PdfWorkspace workspace, PdfTextRun target, string replacement, SKTypeface typeface, double? fontSize = null)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        if (fontSize is { } size && (!double.IsFinite(size) || size is < 1 or > 1000)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        return ReplaceCore(workspace, target, replacement, typeface, fontSize);
    }
    private static PdfWorkspace ReplaceCore(PdfWorkspace workspace, PdfTextRun target, string replacement, SKTypeface? typeface, double? fontSize)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Length > 100000 || replacement.Any(char.IsControl)) throw new ArgumentException("Use a single line of at most 100,000 characters. Paragraph reflow is not implemented.");
        var source = PdfContentGraph.ValidateTarget(workspace, target.SourceId, target.SourcePage, target.PageId, target.SourceHash);
        using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        if (target.SourcePage < 1 || target.SourcePage > native.PageCount) throw new InvalidDataException("Invalid source page.");
        var page = native.Pages[target.SourcePage - 1];
        PdfContentGraph.Edit(native, page, target.ScopePath, (content, resources) =>
        {
            var runs = Enumerate(resources, content);
            if (target.Index < 0 || target.Index >= runs.Length) throw new InvalidDataException("The text run no longer exists.");
            var run = runs[target.Index];
            if (run.Codec is null || run.Decode() != target.Text || run.Font != target.FontResource) throw new NotSupportedException("This text run has an unsupported or changed font encoding.");
            if (typeface is not null)
            {
                if (!run.ReplaceFont) throw new NotSupportedException("This run uses dependent positioning or clipping text. Replacement-font editing requires an independent horizontal text run.");
                var font = new OcrPdfFont(native, typeface, [replacement]);
                var name = PdfContentGraph.AddResource(native, resources, "/Font", font.Font);
                var index = run.Operations[0]; var original = (COperator)content[index];
                var prefix = original.Name == "TJ" && original.Operands.FirstOrDefault() is CArray array ?
                    string.Join(" ", array.TakeWhile(item => item is not CString).Select(item => item.ToString())) : "";
                var show = prefix.Length > 0 ? $"[{prefix} <{font.Encode(replacement)}>] TJ\n" : $"<{font.Encode(replacement)}> Tj\n";
                content.RemoveAt(index);
                PdfContentGraph.Insert(content, index, $"{name} {PdfObjects.F(fontSize ?? run.Size)} Tf\n" + show + $"{run.Font} {PdfObjects.F(run.Size)} Tf\n");
            }
            else
            {
                var runes = replacement.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
                var capacities = run.Operands.Select(operand => run.Codec.Decode(operand.Value)!.EnumerateRunes().Count()).ToArray();
                if (run.Operands.Length > 1 && runes.Length > capacities.Sum()) throw new NotSupportedException("This text is positioned glyph-by-glyph. Use at most the original character count or choose replacement-font mode for an independent run.");
                var offset = 0; var encoded = new List<string>();
                for (var i = 0; i < run.Operands.Length; i++)
                {
                    var take = run.Operands.Length == 1 ? runes.Length : Math.Min(capacities[i], runes.Length - offset);
                    encoded.Add(run.Codec.Encode(string.Concat(runes.Skip(offset).Take(take)))); offset += take;
                }
                for (var i = 0; i < run.Operands.Length; i++) { run.Operands[i].Value = encoded[i]; run.Operands[i].CStringType = CStringType.HexString; }
            }
        });
        return PdfContentGraph.Commit(workspace, source, target.PageId, target.SourcePage, native);
    }
    private static Run[] Enumerate(PdfDictionary resources, CSequence content)
    {
        var fonts = PdfObjects.Dictionary(resources.Elements["/Font"]);
        var font = ""; double size = 0; var renderMode = 0;
        var stack = new Stack<(string Font, double Size, int RenderMode)>();
        var codecs = new Dictionary<string, FontCodec?>(); var result = new List<Run>();
        var operands = new List<CString>(); var operations = new List<int>();
        bool Independent(int index)
        {
            if (((COperator)content[index]).Name is not ("Tj" or "TJ")) return false;
            for (var i = index + 1; i < content.Count; i++)
                if (content[i] is COperator op)
                {
                    if (op.Name is "ET" or "BT" or "Tm" or "Td" or "TD" or "T*" or "'" or "\"") return true;
                    if (op.Name is "Tj" or "TJ" or "Do") return false;
                }
            return true;
        }
        void Flush()
        {
            if (operands.Count == 0) return;
            if (!codecs.TryGetValue(font, out var codec)) { codec = FontCodec.Create(PdfObjects.Dictionary(fonts?.Elements[font])); codecs[font] = codec; }
            var fontObject = PdfObjects.Dictionary(fonts?.Elements[font]);
            var encoding = PdfObjects.Text(fontObject?.Elements["/Encoding"]);
            var horizontal = !encoding.EndsWith("-V", StringComparison.Ordinal);
            result.Add(new(operands.ToArray(), codec, font, size, operations.ToArray(), horizontal && renderMode < 4 && size > 0 && operations.Count == 1 && Independent(operations[0])));
            operands.Clear(); operations.Clear();
            if (result.Count > 10000) throw new InvalidDataException("Too many text runs in a page scope.");
        }
        for (var index = 0; index < content.Count; index++)
        {
            if (content[index] is not COperator operation) continue;
            if (operation.Name is "BT" or "ET" or "Tm" or "T*" or "Do") Flush();
            else if (operation.Name is "Td" or "TD" && operation.Operands.Count > 1 && Number(operation.Operands[1]) != 0) Flush();
            else if (operation.Name == "q") { Flush(); stack.Push((font, size, renderMode)); }
            else if (operation.Name == "Q" && stack.Count > 0) { Flush(); (font, size, renderMode) = stack.Pop(); }
            else if (operation.Name == "Tr") { Flush(); renderMode = operation.Operands.Count > 0 ? (int)Number(operation.Operands[0]) : 0; }
            else if (operation.Name == "Tf" && operation.Operands.FirstOrDefault() is CName name) { Flush(); font = name.Name; size = operation.Operands.Count > 1 ? Number(operation.Operands[1]) : 0; }
            else if (operation.Name is "Tj" or "TJ" or "'" or "\"")
            {
                if (operation.Name is "'" or "\"") Flush();
                operations.Add(index);
                foreach (var operand in operation.Operands)
                {
                    if (operand is CString text) operands.Add(text);
                    else if (operand is CArray array) operands.AddRange(array.OfType<CString>());
                }
            }
        }
        Flush(); return result.ToArray();
    }
    private static double Number(CObject operand) => operand switch { CInteger value => value.Value, CReal value => value.Value, _ => 0 };
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
