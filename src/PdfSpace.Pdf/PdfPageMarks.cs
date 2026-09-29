using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using SkiaSharp;

namespace PdfSpace.Pdf;

public sealed record PdfPageMarkInfo(int PageIndex, PdfPageMarkSettings Settings, int Ordinal, int PageCount, bool Intact)
{
    public string TypefaceFingerprint { get; init; } = "";
}
public sealed record PdfPageMarkResult(PdfWorkspace Workspace, int ChangedPages, int EmbeddedFonts, long OutputBytes, IReadOnlyList<string> Warnings);
public sealed record PdfPageMarkText(string Text, double X, double Baseline, double Rotation);

/// <summary>Batch native headers/footers and text watermarks. Managed updates replace only verified,
/// application-owned streams. Structured-save restrictions apply; source deletion is not sanitization.</summary>
public static class PdfPageMarks
{
    private const string Key = "/PdfSpacePageMarks";
    private const string IsolationKey = "/PdfSpaceMarkIsolation";
    public const int MaximumPagesPerOperation = 500;
    private sealed record Owned(PdfDictionary Metadata, PdfDictionary Stream, PdfDictionary Form, string Resource, PdfPageMarkInfo Info);

    public static IReadOnlyList<PdfPageMarkInfo> Read(PdfWorkspace workspace)
    {
        WorkspaceJson.Validate(workspace);
        var result = new List<PdfPageMarkInfo>();
        var fingerprint = new PdfMarkFingerprint();
        foreach (var source in workspace.Sources)
        {
            using var document = PdfDocumentEngine.OpenNative(source.Bytes);
            var pages = workspace.Pages.Select((p, i) => (p, i)).Where(x => x.p.SourceId == source.Id);
            foreach (var (page, index) in pages)
                foreach (var owned in ReadOwned(document.Pages[page.SourcePage - 1], index, fingerprint)) result.Add(owned.Info);
        }
        return result;
    }

    public static PdfPageMarkResult Apply(PdfWorkspace workspace, IReadOnlyList<int> pageIndices, PdfPageMarkSettings settings,
        SKTypeface typeface, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate(); ArgumentNullException.ThrowIfNull(typeface);
        var indices = ValidatePages(workspace, pageIndices); cancellationToken.ThrowIfCancellationRequested();
        var fontIdentity = FontIdentity(typeface);
        var selected = indices.ToHashSet();
        var existing = Read(workspace).Where(m => selected.Contains(m.PageIndex) && m.Settings.Kind == settings.Kind).ToDictionary(m => m.PageIndex);
        if (existing.Values.Any(m => !m.Intact)) throw ModifiedMark();
        var changed = indices.Where((index, ordinal) => !existing.TryGetValue(index, out var old) || old.Settings != settings || old.Ordinal != ordinal ||
            old.PageCount != workspace.Pages.Length || old.TypefaceFingerprint != fontIdentity || workspace.Pages[index].Crop is not null || workspace.Pages[index].Rotation != 0).ToArray();
        if (changed.Length == 0) return Unchanged(workspace);
        // Complete layout and font coverage validation precede the serialized edit.
        using var metrics = new MarkMetrics(typeface, settings.FontSize);
        var plans = indices.Select((index, ordinal) => (Index: index, Ordinal: ordinal,
            Lines: LayoutCore(settings, ordinal, workspace.Pages.Length, workspace.Pages[index].DisplayWidth, workspace.Pages[index].DisplayHeight, metrics))).ToArray();
        var saved = PdfDocumentEngine.Save(workspace, typeface);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocumentEngine.OpenNative(saved.Bytes);
        var font = new OcrPdfFont(document, typeface, plans.SelectMany(p => p.Lines.Select(t => t.Text)));
        var fingerprint = new PdfMarkFingerprint();
        var changedSet = changed.ToHashSet();
        foreach (var plan in plans.Where(p => changedSet.Contains(p.Index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.Pages[plan.Index]; Detach(page);
            var owned = ReadOwned(page, plan.Index, fingerprint).ToList();
            if (owned.Any(m => !m.Info.Intact)) throw ModifiedMark();
            var originals = BaseContents(page, owned);
            RemoveKind(page, owned, settings.Kind);
            var resources = PdfContentGraph.Resources(page);
            var form = CreateForm(document, page, plan.Lines, settings, font);
            var resource = PdfContentGraph.AddResource(document, resources, "/XObject", form);
            page.Elements["/Resources"] = resources;
            var stream = Stream(document, "q\n" + resource + " Do\nQ\n");
            var metadata = new PdfDictionary(document);
            metadata.Elements.SetInteger("/Version", 1);
            metadata.Elements.SetString("/Settings", JsonSerializer.Serialize(settings, PdfPageMarkJsonContext.Default.PdfPageMarkSettings));
            metadata.Elements.SetInteger("/Ordinal", plan.Ordinal); metadata.Elements.SetInteger("/Pages", workspace.Pages.Length);
            metadata.Elements.SetName("/Resource", resource); metadata.Elements["/Stream"] = stream.Reference!;
            metadata.Elements.SetString("/Typeface", fontIdentity);
            metadata.Elements.SetString("/Fingerprint", RecordHash(fingerprint, form, settings, plan.Ordinal, workspace.Pages.Length, fontIdentity));
            owned.Add(new(metadata, stream, form, resource, new PdfPageMarkInfo(plan.Index, settings, plan.Ordinal, workspace.Pages.Length, true) { TypefaceFingerprint = fontIdentity }));
            Compose(page, originals, owned);
        }
        var bytes = PdfDocumentEngine.Bytes(document);
        cancellationToken.ThrowIfCancellationRequested();
        return Finish(workspace, bytes, changed.Length, 1, saved.Warnings);
    }

    public static PdfPageMarkResult Remove(PdfWorkspace workspace, IReadOnlyList<int> pageIndices, PdfPageMarkKind kind,
        SKTypeface typeface, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(typeface); var indices = ValidatePages(workspace, pageIndices);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = indices.ToHashSet();
        var marks = Read(workspace).Where(m => selected.Contains(m.PageIndex) && m.Settings.Kind == kind).ToArray();
        if (marks.Any(m => !m.Intact)) throw ModifiedMark();
        if (marks.Length == 0) return Unchanged(workspace);
        var saved = PdfDocumentEngine.Save(workspace, typeface);
        using var document = PdfDocumentEngine.OpenNative(saved.Bytes);
        var fingerprint = new PdfMarkFingerprint();
        foreach (var mark in marks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.Pages[mark.PageIndex]; Detach(page);
            var owned = ReadOwned(page, mark.PageIndex, fingerprint).ToList();
            if (owned.Any(m => !m.Info.Intact)) throw ModifiedMark();
            var originals = BaseContents(page, owned); RemoveKind(page, owned, kind); Compose(page, originals, owned);
        }
        var bytes = PdfDocumentEngine.Bytes(document); cancellationToken.ThrowIfCancellationRequested();
        return Finish(workspace, bytes, marks.Length, 0, saved.Warnings);
    }

    /// <summary>Pure, deterministic display-space text layout, also usable by a host preview.</summary>
    public static PdfPageMarkText[] Layout(PdfPageMarkSettings settings, int ordinal, int pageCount, double width, double height, SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate(); ArgumentNullException.ThrowIfNull(typeface);
        using var metrics = new MarkMetrics(typeface, settings.FontSize);
        return LayoutCore(settings, ordinal, pageCount, width, height, metrics);
    }
    private static PdfPageMarkText[] LayoutCore(PdfPageMarkSettings settings, int ordinal, int pageCount, double width, double height, MarkMetrics metrics)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 1 or > 100000 || height is < 1 or > 100000)
            throw new ArgumentException("Invalid displayed page dimensions.");
        var ascent = metrics.Ascent; var descent = metrics.Descent;
        if (settings.Kind == PdfPageMarkKind.Watermark)
        {
            var text = settings.ExpandValidated(settings.WatermarkText, ordinal, pageCount); var textWidth = metrics.Measure(text);
            var rotation = Math.IEEERemainder(settings.Rotation, 360); var radians = rotation * Math.PI / 180;
            var w = Math.Abs(Math.Cos(radians) * textWidth) + Math.Abs(Math.Sin(radians) * (ascent + descent));
            var h = Math.Abs(Math.Sin(radians) * textWidth) + Math.Abs(Math.Cos(radians) * (ascent + descent));
            if (w > width || h > height) throw new ArgumentException("Watermark exceeds the visible page. Reduce font size or shorten its text.");
            return [new(text, (width - textWidth) / 2, height / 2 + (ascent - descent) / 2, rotation)];
        }
        if (settings.LeftMargin + settings.RightMargin >= width || settings.TopMargin + settings.BottomMargin + 2 * (ascent + descent) >= height)
            throw new ArgumentException("Header/footer margins leave insufficient page space.");
        var output = new List<PdfPageMarkText>();
        foreach (var (templates, baseline) in new[]
        {
            (new[] { settings.HeaderLeft, settings.HeaderCenter, settings.HeaderRight }, settings.TopMargin + ascent),
            (new[] { settings.FooterLeft, settings.FooterCenter, settings.FooterRight }, height - settings.BottomMargin - descent)
        })
        {
            var previousRight = settings.LeftMargin - 4;
            for (var slot = 0; slot < 3; slot++)
            {
                var text = settings.ExpandValidated(templates[slot], ordinal, pageCount); if (text.Length == 0) continue;
                var w = metrics.Measure(text);
                var x = slot switch { 0 => settings.LeftMargin, 1 => (width + settings.LeftMargin - settings.RightMargin - w) / 2, _ => width - settings.RightMargin - w };
                if (x < previousRight + 4 - .001 || x + w > width - settings.RightMargin + .001)
                    throw new ArgumentException("Header/footer text overlaps adjacent slots or margins. Shorten it or reduce the font size.");
                output.Add(new(text, x, baseline, 0)); previousRight = x + w;
            }
        }
        return output.ToArray();
    }

    // One native SKFont and one per-rune width lookup across a complete batch, not per page/slot.
    private sealed class MarkMetrics(SKTypeface typeface, double size) : IDisposable
    {
        private readonly SKFont _font = new(typeface, 1000);
        private readonly Dictionary<Rune, double> _widths = [];
        public double Ascent => -_font.Metrics.Ascent * size / 1000;
        public double Descent => _font.Metrics.Descent * size / 1000;
        public double Measure(string text)
        {
            var width = 0d;
            foreach (var rune in text.EnumerateRunes())
            {
                if (!_widths.TryGetValue(rune, out var advance))
                {
                    var value = rune.ToString(); var glyphs = _font.GetGlyphs(value);
                    if (glyphs.Length != 1 || glyphs[0] == 0) throw new NotSupportedException($"The selected font lacks U+{rune.Value:X4}.");
                    _widths.Add(rune, advance = Math.Max(1, _font.MeasureText(value)) * size / 1000);
                }
                width += advance;
            }
            return width;
        }
        public void Dispose() => _font.Dispose();
    }
    private static string FontIdentity(SKTypeface typeface)
    {
        using var stream = typeface.OpenStream(out var index) ?? throw new NotSupportedException("Native marks require an embeddable TrueType font.");
        if (stream.Length is < 12 or > 16 * 1024 * 1024 || index != 0) throw new NotSupportedException("Unsupported page-mark font.");
        var bytes = new byte[stream.Length];
        if (stream.Read(bytes, bytes.Length) != bytes.Length || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes) != 0x00010000)
            throw new NotSupportedException("Native marks require TrueType sfnt outlines, not CFF or a font collection.");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static string RecordHash(PdfMarkFingerprint fingerprint, PdfDictionary form, PdfPageMarkSettings settings, int ordinal, int pages, string fontIdentity)
    {
        var json = JsonSerializer.Serialize(settings, PdfPageMarkJsonContext.Default.PdfPageMarkSettings);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.Hash(form) + "\n" + json + "\n" +
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + pages.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + fontIdentity)));
    }

    private static int[] ValidatePages(PdfWorkspace workspace, IReadOnlyList<int> pageIndices)
    {
        WorkspaceJson.Validate(workspace); ArgumentNullException.ThrowIfNull(pageIndices);
        if (pageIndices.Count is < 1 or > MaximumPagesPerOperation) throw new ArgumentException("Choose 1–500 pages per batch.");
        var indices = pageIndices.Distinct().Order().ToArray();
        if (indices.Any(i => i < 0 || i >= workspace.Pages.Length)) throw new ArgumentOutOfRangeException(nameof(pageIndices));
        return indices;
    }
    private static PdfPageMarkResult Unchanged(PdfWorkspace workspace) => new(workspace, 0, 0, 0, []);
    private static InvalidOperationException ModifiedMark() => new("A page mark was changed or detached by native object editing. Its ownership record no longer matches. No marks were changed; use the object editor to review it.");

    private static PdfPageMarkResult Finish(PdfWorkspace original, byte[] bytes, int count, int fonts, IReadOnlyList<string> warnings)
    {
        var reopened = PdfDocumentEngine.Open(bytes, original.Title);
        var result = reopened with
        {
            Title = original.Title, Author = original.Author,
            Sources = reopened.Sources.Select(s => s with { Sensitive = original.IsSensitive }).ToArray(),
            Pages = reopened.Pages.Select((p, i) => p with { Id = original.Pages[i].Id, Bookmark = original.Pages[i].Bookmark }).ToArray()
        };
        WorkspaceJson.Validate(result);
        return new(result, count, fonts, bytes.Length, warnings);
    }

    private static void Detach(PdfPage page)
    {
        // Call before the lazily cached Contents property. Imported duplicate pages may alias its array.
        if (PdfObjects.Array(page.Elements["/Contents"]) is { } contents)
        {
            var copy = new PdfArray(page.Owner); foreach (var value in contents.Elements) copy.Elements.Add(value);
            page.Elements["/Contents"] = copy;
        }
        var resources = PdfContentGraph.CopyDictionary(page.Owner, PdfContentGraph.Resources(page));
        resources.Elements["/XObject"] = PdfContentGraph.CopyDictionary(page.Owner, PdfObjects.Dictionary(resources.Elements["/XObject"]));
        page.Elements["/Resources"] = resources;
    }
    private static List<Owned> ReadOwned(PdfPage page, int pageIndex, PdfMarkFingerprint fingerprint)
    {
        var result = new List<Owned>();
        _ = page.Contents; // Normalize PDFsharp content wrappers before comparing resolved stream identities.
        var list = PdfObjects.Array(page.Elements[Key]);
        if (list is null)
        {
            if (page.Elements.ContainsKey(Key)) throw new InvalidDataException("Invalid page-mark ownership container.");
            return result;
        }
        if (list.Elements.Count > 2) throw new InvalidDataException("Too many page-mark ownership records.");
        var kinds = new HashSet<PdfPageMarkKind>();
        foreach (var entry in list.Elements)
        {
            var meta = PdfObjects.Dictionary(entry) ?? throw new InvalidDataException("Invalid page-mark record.");
            if (meta.Elements.GetInteger("/Version") != 1) throw new NotSupportedException("Unknown page-mark metadata version.");
            var json = meta.Elements.GetString("/Settings"); if (json.Length > 20000) throw new InvalidDataException("Oversized page-mark settings.");
            var settings = JsonSerializer.Deserialize(json, PdfPageMarkJsonContext.Default.PdfPageMarkSettings)?.Validate() ?? throw new InvalidDataException("Missing page-mark settings.");
            if (!kinds.Add(settings.Kind)) throw new InvalidDataException("Duplicate page-mark kind.");
            var stream = PdfObjects.Dictionary(meta.Elements["/Stream"]) ?? throw new InvalidDataException("Missing page-mark stream.");
            var resource = meta.Elements.GetName("/Resource");
            var form = PdfObjects.Dictionary(PdfObjects.Dictionary(PdfContentGraph.Resources(page).Elements["/XObject"])?.Elements[resource]) ?? throw ModifiedMark();
            var ordinal = meta.Elements.GetInteger("/Ordinal"); var pages = meta.Elements.GetInteger("/Pages");
            if (ordinal < 0 || ordinal >= MaximumPagesPerOperation || pages is < 1 or > 10000) throw new InvalidDataException("Invalid page-mark numbering record.");
            var fontIdentity = meta.Elements.GetString("/Typeface");
            if (fontIdentity.Length != 64 || fontIdentity.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid page-mark font identity.");
            var intact = resource.StartsWith("/PdfSpace", StringComparison.Ordinal) &&
                page.Contents.Elements.Count(v => ReferenceEquals(PdfObjects.Resolve(v), stream)) == 1 &&
                Exact(stream, "q\n" + resource + " Do\nQ\n") &&
                string.Equals(meta.Elements.GetString("/Fingerprint"), RecordHash(fingerprint, form, settings, ordinal, pages, fontIdentity), StringComparison.Ordinal);
            result.Add(new(meta, stream, form, resource, new PdfPageMarkInfo(pageIndex, settings, ordinal, pages, intact) { TypefaceFingerprint = fontIdentity }));
        }
        return result;
    }
    private static bool Exact(PdfDictionary stream, string expected) => stream.Stream is { Length: <= 1024 } content && content.UnfilteredValue.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(expected));
    private static List<PdfItem> BaseContents(PdfPage page, List<Owned> owned)
    {
        var exclusions = owned.Select(o => o.Stream).ToHashSet(ReferenceEqualityComparer.Instance);
        if (page.Elements.ContainsKey(IsolationKey) && PdfObjects.Array(page.Elements[IsolationKey]) is null) throw ModifiedMark();
        if (PdfObjects.Array(page.Elements[IsolationKey]) is { } guards)
        {
            if (guards.Elements.Count != 2) throw ModifiedMark();
            for (var i = 0; i < 2; i++)
            {
                var stream = PdfObjects.Dictionary(guards.Elements[i]) ?? throw ModifiedMark();
                if (!Exact(stream, i == 0 ? "q\n" : "Q\n") || page.Contents.Elements.Count(v => ReferenceEquals(PdfObjects.Resolve(v), stream)) != 1) throw ModifiedMark();
                exclusions.Add(stream);
            }
        }
        var original = page.Contents.Elements.Where(v => !exclusions.Contains(PdfObjects.Resolve(v))).ToList();
        // Validate state balance without reserializing the original operators or binary content.
        // This deliberately refuses content that the bounded parser cannot safely inspect.
        var state = 0; var text = false;
        using var joined = new MemoryStream();
        foreach (var value in original)
        {
            var content = PdfObjects.Dictionary(value)?.Stream ?? throw new InvalidDataException("Invalid PDF content stream.");
            if (content.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("Oversized PDF content.");
            var bytes = content.UnfilteredValue;
            if (bytes.Length + joined.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("Expanded PDF content exceeds 64 MiB.");
            joined.Write(bytes); joined.WriteByte(10);
        }
        var operations = ContentReader.ReadContent(joined.ToArray());
        if (operations.Count > 500000) throw new InvalidDataException("Too many content operators.");
        foreach (var op in operations.OfType<COperator>())
        {
            if (op.Name == "q") { if (++state > 256) throw new InvalidDataException("Excessive graphics-state nesting."); }
            if (op.Name == "Q" && --state < 0) throw new InvalidDataException("Unbalanced source graphics state.");
            if (op.Name == "BT") { if (text) throw new InvalidDataException("Nested text object."); text = true; }
            if (op.Name == "ET") { if (!text) throw new InvalidDataException("Unbalanced source text state."); text = false; }
            if (op.Name == "Do" && op.Operands.OfType<CName>().Any(n => owned.Any(o => o.Resource == n.Name))) throw ModifiedMark();
        }
        if (state != 0 || text) throw new InvalidDataException("Unbalanced source state; no marks changed.");
        // A child with inherited resources could refer to a managed name. Never remove/rebind that name silently.
        if (owned.Count > 0 && PdfObjects.Dictionary(PdfContentGraph.Resources(page).Elements["/XObject"]) is { } xobjects)
            foreach (var value in xobjects.Elements.Values)
                if (PdfObjects.Dictionary(value) is { } form && form.Elements.GetName("/Subtype") == "/Form" && !form.Elements.ContainsKey("/Resources"))
                    throw new NotSupportedException("Updating page marks beside resource-inheriting Forms is not supported.");
        return original;
    }
    private static void RemoveKind(PdfPage page, List<Owned> owned, PdfPageMarkKind kind)
    {
        foreach (var old in owned.Where(o => o.Info.Settings.Kind == kind).ToArray())
        {
            PdfObjects.Dictionary(PdfContentGraph.Resources(page).Elements["/XObject"])?.Elements.Remove(old.Resource);
            owned.Remove(old);
        }
    }
    private static void Compose(PdfPage page, List<PdfItem> original, List<Owned> owned)
    {
        var contents = page.Contents; contents.Elements.Clear();
        page.Elements.Remove(IsolationKey); page.Elements.Remove(Key);
        if (owned.Count == 0) { foreach (var item in original) contents.Elements.Add(item); return; }
        foreach (var mark in owned.Where(o => o.Info.Settings.BehindContent)) contents.Elements.Add(mark.Stream.Reference!);
        var begin = Stream(page.Owner, "q\n"); var end = Stream(page.Owner, "Q\n");
        contents.Elements.Add(begin.Reference!); foreach (var item in original) contents.Elements.Add(item); contents.Elements.Add(end.Reference!);
        foreach (var mark in owned.Where(o => !o.Info.Settings.BehindContent)) contents.Elements.Add(mark.Stream.Reference!);
        var guards = new PdfArray(page.Owner); guards.Elements.Add(begin.Reference!); guards.Elements.Add(end.Reference!); page.Elements[IsolationKey] = guards;
        var records = new PdfArray(page.Owner); foreach (var mark in owned) records.Elements.Add(mark.Metadata); page.Elements[Key] = records;
    }
    private static PdfDictionary Stream(PdfDocument native, string content)
    {
        var value = new PdfDictionary(native); native.Internals.AddObject(value); value.CreateStream(Encoding.ASCII.GetBytes(content)); return value;
    }
    private static PdfDictionary CreateForm(PdfDocument document, PdfPage page, PdfPageMarkText[] lines, PdfPageMarkSettings settings, OcrPdfFont font)
    {
        var geometry = new SourceGeometry(page);
        var p = geometry.ToPdf(default(PointD)); var x = geometry.ToPdf(new PointD(1, 0)) - p; var y = geometry.ToPdf(new PointD(0, 1)) - p;
        var width = geometry.Rotation % 180 == 0 ? geometry.Box.Width : geometry.Box.Height;
        var height = geometry.Rotation % 180 == 0 ? geometry.Box.Height : geometry.Box.Width;
        var resources = new PdfDictionary(document);
        PdfObjects.DictionaryValue(resources, "/Font").Elements["/F"] = font.Font.Reference!;
        var alpha = new PdfDictionary(document); alpha.Elements.SetReal("/ca", settings.Opacity); alpha.Elements.SetReal("/CA", settings.Opacity);
        alpha.Elements.SetName("/BM", "/Normal"); alpha.Elements.SetName("/SMask", "/None"); alpha.Elements.SetBoolean("/AIS", false);
        PdfObjects.DictionaryValue(resources, "/ExtGState").Elements["/GS"] = alpha;
        string F(double v) => PdfObjects.F(v);
        var color = settings.Color;
        var body = new StringBuilder($"q\n/GS gs\n{F(((color >> 16) & 255) / 255d)} {F(((color >> 8) & 255) / 255d)} {F((color & 255) / 255d)} rg\n");
        foreach (var line in lines)
        {
            body.Append("q\n");
            if (line.Rotation != 0)
            {
                var m = PdfAffineMatrix.Translate(width / 2, height / 2) * PdfAffineMatrix.Rotate(line.Rotation) * PdfAffineMatrix.Translate(-width / 2, -height / 2);
                body.Append($"{F(m.A)} {F(m.B)} {F(m.C)} {F(m.D)} {F(m.E)} {F(m.F)} cm\n");
            }
            body.Append($"BT\n0 Tc 0 Tw 100 Tz 0 Ts 0 Tr\n/F {F(settings.FontSize)} Tf\n1 0 0 -1 {F(line.X)} {F(line.Baseline)} Tm\n<{font.Encode(line.Text)}> Tj\nET\nQ\n");
        }
        body.Append("Q\n");
        var form = Stream(document, body.ToString());
        form.Elements.SetName("/Type", "/XObject"); form.Elements.SetName("/Subtype", "/Form");
        form.Elements["/BBox"] = PdfObjects.Numbers(document, 0, 0, width, height);
        form.Elements["/Matrix"] = PdfObjects.Numbers(document, x.X, x.Y, y.X, y.Y, p.X, p.Y);
        form.Elements["/Resources"] = resources;
        return form;
    }
}
