using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

/// <summary>One painted occurrence, not a shared PDF image resource. Coordinates are source logical points.</summary>
public sealed record PdfImageOccurrence(Guid PageId, Guid SourceId, int SourcePage, string SourceHash,
    string ScopePath, int OperatorIndex, string ResourceName, RectD Bounds, PdfAffineMatrix UnitToPage,
    int PixelWidth, int PixelHeight, bool Editable, string Limitation);

/// <summary>Native copy-on-write image editing. No rasterized page or cover annotation is substituted.</summary>
public static class PdfImageEditor
{
    public static PdfImageOccurrence[] Read(PdfWorkspace workspace, int pageIndex)
    {
        var state = workspace.Pages[pageIndex]; if (state.SourceId is not { } id) return [];
        var source = workspace.Sources.First(s => s.Id == id); using var native = PdfDocumentEngine.OpenNative(source.Bytes);
        var inspection = PdfDocumentEngine.Inspect(source.Bytes); var locked = inspection.SignatureFields > 0 || inspection.HasXfa;
        var page = native.Pages[state.SourcePage - 1]; var logical = LogicalMatrix(new SourceGeometry(page));
        var images = new List<PdfImageOccurrence>(); var hash = PdfContentGraph.Hash(source.Bytes);
        foreach (var scope in PdfContentGraph.Read(page))
            foreach (var (index, operation, matrix) in PdfContentGraph.Operations(scope))
            {
                if (operation.Name != "Do" || operation.Operands.FirstOrDefault() is not CName name) continue;
                var image = PdfObjects.Dictionary(PdfObjects.Dictionary(scope.Resources.Elements["/XObject"])?.Elements[name.Name]);
                if (image is null || PdfObjects.Text(image.Elements["/Subtype"]) != "Image") continue;
                var transform = logical * matrix; var bounds = Bounds(transform); var editable = !locked && bounds.Width > 1e-8 && bounds.Height > 1e-8;
                try { _ = transform.Inverse(); } catch (NotSupportedException) { editable = false; }
                images.Add(new(state.Id, id, state.SourcePage, hash, scope.Path, index, name.Name, bounds, transform,
                    image.Elements.GetInteger("/Width"), image.Elements.GetInteger("/Height"), editable,
                    locked ? "Signed/certified or XFA source; editing is blocked." : "Edits only this placement. Existing clipping remains in force. Deletion/replacement is not secure redaction."));
            }
        return images.OrderBy(image => string.Join("/", (image.ScopePath.Length == 0 ? [] : image.ScopePath.Split('/')).Append(image.OperatorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)).Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture).ToString("D6", System.Globalization.CultureInfo.InvariantCulture))), StringComparer.Ordinal).ToArray();
    }
    public static PdfWorkspace SetBounds(PdfWorkspace workspace, PdfImageOccurrence target, RectD bounds)
    {
        ValidateBounds(bounds);
        if (bounds == target.Bounds) return workspace;
        var delta = PdfAffineMatrix.Translate(bounds.X, bounds.Y) * PdfAffineMatrix.Scale(bounds.Width / target.Bounds.Width, bounds.Height / target.Bounds.Height) * PdfAffineMatrix.Translate(-target.Bounds.X, -target.Bounds.Y);
        return Transform(workspace, target, delta);
    }
    public static PdfWorkspace Transform(PdfWorkspace workspace, PdfImageOccurrence target, PdfAffineMatrix logicalDelta)
    {
        if (!logicalDelta.IsFinite) throw new ArgumentException("Image transform must be finite.");
        _ = logicalDelta.Inverse();
        var resultBounds = Bounds(logicalDelta * target.UnitToPage); ValidateBounds(resultBounds);
        return Mutate(workspace, target, (native, content, resources) =>
        {
            var delta = target.UnitToPage.Inverse() * logicalDelta * target.UnitToPage;
            PdfContentGraph.Insert(content, target.OperatorIndex + 1, "Q\n");
            PdfContentGraph.Insert(content, target.OperatorIndex, "q\n" + delta.Operator);
        });
    }
    public static PdfWorkspace Rotate(PdfWorkspace workspace, PdfImageOccurrence target, double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentException("Rotation must be finite.");
        return Transform(workspace, target, PdfAffineMatrix.Around(target.Bounds.Center, PdfAffineMatrix.Rotate(degrees % 360)));
    }
    public static PdfWorkspace Flip(PdfWorkspace workspace, PdfImageOccurrence target, bool horizontal) =>
        Transform(workspace, target, PdfAffineMatrix.Around(target.Bounds.Center, PdfAffineMatrix.Scale(horizontal ? -1 : 1, horizontal ? 1 : -1)));
    public static PdfWorkspace Delete(PdfWorkspace workspace, PdfImageOccurrence target) =>
        Mutate(workspace, target, (native, content, resources) => content.RemoveAt(target.OperatorIndex));
    public static PdfWorkspace Replace(PdfWorkspace workspace, PdfImageOccurrence target, byte[] encodedImage) =>
        Mutate(workspace, target, (native, content, resources) =>
        {
            var resource = CreateImage(native, encodedImage);
            ((COperator)content[target.OperatorIndex]).Operands[0] = new CName(PdfContentGraph.AddResource(native, resources, "/XObject", resource));
        });
    public static PdfWorkspace Insert(PdfWorkspace workspace, int pageIndex, byte[] encodedImage, RectD bounds)
    {
        WorkspaceJson.Validate(workspace); ValidateBounds(bounds);
        var state = workspace.Pages[pageIndex];
        var source = workspace.Sources.FirstOrDefault(s => s.Id == state.SourceId);
        using var native = source is null ? new PdfDocument() : PdfDocumentEngine.OpenNative(source.Bytes);
        if (source is not null)
        {
            var info = PdfDocumentEngine.Inspect(source.Bytes);
            if (info.SignatureFields > 0 || info.HasXfa) throw new NotSupportedException("Signed/certified and XFA sources cannot be edited.");
        }
        var page = source is null ? native.AddPage() : native.Pages[state.SourcePage - 1];
        if (source is null) { page.Width = PdfSharp.Drawing.XUnit.FromPoint(state.Width); page.Height = PdfSharp.Drawing.XUnit.FromPoint(state.Height); }
        var matrix = LogicalMatrix(new SourceGeometry(page)).Inverse() * new PdfAffineMatrix(bounds.Width, 0, 0, -bounds.Height, bounds.X, bounds.Bottom);
        PdfContentGraph.Edit(native, page, "", (content, resources) =>
        {
            var name = PdfContentGraph.AddResource(native, resources, "/XObject", CreateImage(native, encodedImage));
            // Isolate the existing stream's persistent graphics state from the newly inserted object.
            PdfContentGraph.Insert(content, 0, "q\n");
            PdfContentGraph.Insert(content, content.Count, "Q\nq\n" + matrix.Operator + name + " Do\nQ\n");
        });
        if (source is not null) return PdfContentGraph.Commit(workspace, source, state.Id, state.SourcePage, native);
        var newSource = new PdfSource(Guid.NewGuid(), "Inserted image.pdf", PdfDocumentEngine.Bytes(native));
        return PdfDocumentEngine.PrepareWorkspace(workspace with { Sources = [..workspace.Sources, newSource], Pages = workspace.Pages.Select(p => p.Id == state.Id ? p with { SourceId = newSource.Id, SourcePage = 1 } : p).ToArray() });
    }
    private static PdfWorkspace Mutate(PdfWorkspace workspace, PdfImageOccurrence target, Action<PdfDocument, CSequence, PdfDictionary> change)
    {
        if (!target.Editable) throw new NotSupportedException(target.Limitation);
        var source = PdfContentGraph.ValidateTarget(workspace, target.SourceId, target.SourcePage, target.PageId, target.SourceHash);
        using var native = PdfDocumentEngine.OpenNative(source.Bytes); var page = native.Pages[target.SourcePage - 1];
        var scope = PdfContentGraph.Read(page).SingleOrDefault(s => s.Path == target.ScopePath) ?? throw new InvalidOperationException("Image occurrence no longer exists.");
        var actual = PdfContentGraph.Operations(scope).FirstOrDefault(item => item.Index == target.OperatorIndex);
        if (actual.Operation is not { Name: "Do" } || actual.Operation.Operands.FirstOrDefault() is not CName name || name.Name != target.ResourceName ||
            PdfObjects.Text(PdfObjects.Dictionary(PdfObjects.Dictionary(scope.Resources.Elements["/XObject"])?.Elements[name.Name])?.Elements["/Subtype"]) != "Image" ||
            LogicalMatrix(new SourceGeometry(page)) * actual.Matrix != target.UnitToPage)
            throw new InvalidOperationException("The selected image no longer matches its source content.");
        PdfContentGraph.Edit(native, page, target.ScopePath, (content, resources) => change(native, content, resources));
        return PdfContentGraph.Commit(workspace, source, target.PageId, target.SourcePage, native);
    }
    internal static PdfAffineMatrix LogicalMatrix(SourceGeometry geometry)
    {
        var zero = geometry.ToLogical(0, 0); var x = geometry.ToLogical(1, 0) - zero; var y = geometry.ToLogical(0, 1) - zero;
        return new(x.X, x.Y, y.X, y.Y, zero.X, zero.Y);
    }
    private static RectD Bounds(PdfAffineMatrix matrix)
    {
        var points = new[] { matrix.Transform(new(0, 0)), matrix.Transform(new(1, 0)), matrix.Transform(new(1, 1)), matrix.Transform(new(0, 1)) };
        return new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
    }
    private static void ValidateBounds(RectD bounds)
    {
        if (!bounds.IsFinite || bounds.Width is < .01 or > 100000 || bounds.Height is < .01 or > 100000 || Math.Abs(bounds.X) > 100000 || Math.Abs(bounds.Y) > 100000)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Image bounds must be finite with positive dimensions within 100,000 points.");
    }
    private static PdfDictionary CreateImage(PdfDocument native, byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length is 0 or > 32 * 1024 * 1024) throw new InvalidDataException("Replacement image must be at most 32 MB.");
        using var data = SKData.CreateCopy(encoded); using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or damaged image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 16_000_000)
            throw new InvalidDataException("Image exceeds the 16 megapixel or 8192-pixel dimension limit.");
        if (codec.EncodedOrigin != SKEncodedOrigin.TopLeft) throw new NotSupportedException("Normalize the image orientation before inserting or replacing it.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Image decoding failed.");
        var pixels = bitmap.GetPixelSpan(); var rgb = new byte[checked(info.Width * info.Height * 3)]; var alpha = new byte[checked(info.Width * info.Height)]; var hasAlpha = false;
        for (var y = 0; y < info.Height; y++)
            for (var x = 0; x < info.Width; x++)
            {
                var i = y * info.Width + x; var p = y * bitmap.RowBytes + x * 4;
                rgb[i * 3] = pixels[p]; rgb[i * 3 + 1] = pixels[p + 1]; rgb[i * 3 + 2] = pixels[p + 2];
                alpha[i] = pixels[p + 3]; hasAlpha |= alpha[i] != 255;
            }
        PdfDictionary Image(byte[] bytes, string colorSpace)
        {
            var image = new PdfDictionary(native); image.Elements.SetName("/Type", "/XObject"); image.Elements.SetName("/Subtype", "/Image");
            image.Elements.SetInteger("/Width", info.Width); image.Elements.SetInteger("/Height", info.Height); image.Elements.SetInteger("/BitsPerComponent", 8); image.Elements.SetName("/ColorSpace", colorSpace);
            using var output = new MemoryStream(); using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, true)) deflate.Write(bytes);
            image.CreateStream(output.ToArray()); image.Elements.SetName("/Filter", "/FlateDecode"); native.Internals.AddObject(image); return image;
        }
        var result = Image(rgb, "/DeviceRGB"); if (hasAlpha) result.Elements["/SMask"] = Image(alpha, "/DeviceGray").Reference!;
        return result;
    }
}
