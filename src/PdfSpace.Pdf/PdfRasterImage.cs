using System.Buffers.Binary;
using System.IO.Compression;
using PdfSharp.Pdf;
using SkiaSharp;

namespace PdfSpace.Pdf;

/// <summary>Bounded image import. Ordinary JPEGs keep their original compressed bytes;
/// other inputs are normalized to sRGB and compressed one scanline at a time.</summary>
internal static class PdfRasterImage
{
    internal const int MaximumEncodedBytes = 32 * 1024 * 1024;
    internal const int MaximumPixels = 16_000_000;

    internal static (int Width, int Height) Dimensions(byte[] encoded)
    {
        using var data = OpenData(encoded);
        using var codec = OpenCodec(data);
        return Dimensions(codec);
    }

    internal static PdfDictionary Create(PdfDocument document, byte[] encoded)
    {
        using var data = OpenData(encoded);
        using var codec = OpenCodec(data);
        var (width, height) = Dimensions(codec);
        // EXIF orientation is normalized into samples on the fallback path. This
        // means replacing an already-oriented image never applies orientation twice.
        if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg && codec.EncodedOrigin == SKEncodedOrigin.TopLeft &&
            TryJpeg(encoded, codec.Info.Width, codec.Info.Height, out var components, out var colorTransform))
        {
            var image = Image(document, width, height, components == 1 ? "/DeviceGray" : "/DeviceRGB", "/DCTDecode", encoded.ToArray());
            if (components == 3)
            {
                var parameters = new PdfDictionary(document);
                parameters.Elements.SetInteger("/ColorTransform", colorTransform);
                image.Elements["/DecodeParms"] = parameters;
            }
            return image;
        }

        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The image could not be completely decoded.");
        var pixels = bitmap.GetPixelSpan();
        var hasAlpha = false;
        if (codec.Info.AlphaType != SKAlphaType.Opaque)
            for (var y = 0; y < bitmap.Height && !hasAlpha; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    if (pixels[y * bitmap.RowBytes + x * 4 + 3] != 255) { hasAlpha = true; break; }

        // No full-size RGB or alpha staging arrays. Scratch is at most 4*width
        // bytes, in addition to the single native decoded RGBA bitmap.
        var rgbRow = new byte[checked(width * 3)];
        var alphaRow = hasAlpha ? new byte[width] : null;
        using var rgbBytes = new MemoryStream();
        using var alphaBytes = hasAlpha ? new MemoryStream() : null;
        using (var rgbZip = new ZLibStream(rgbBytes, CompressionLevel.Optimal, true))
        using (var alphaZip = hasAlpha ? new ZLibStream(alphaBytes!, CompressionLevel.Optimal, true) : null)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var (sx, sy) = SourcePixel(codec.EncodedOrigin, x, y, bitmap.Width, bitmap.Height);
                    var p = sy * bitmap.RowBytes + sx * 4;
                    rgbRow[x * 3] = pixels[p]; rgbRow[x * 3 + 1] = pixels[p + 1]; rgbRow[x * 3 + 2] = pixels[p + 2];
                    if (alphaRow is not null) alphaRow[x] = pixels[p + 3];
                }
                rgbZip.Write(rgbRow);
                if (alphaRow is not null) alphaZip!.Write(alphaRow);
            }
        }
        var result = Image(document, width, height, "/DeviceRGB", "/FlateDecode", rgbBytes.ToArray());
        if (alphaBytes is not null)
            result.Elements["/SMask"] = Image(document, width, height, "/DeviceGray", "/FlateDecode", alphaBytes.ToArray()).Reference!;
        return result;
    }

    private static SKData OpenData(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length is 0 or > MaximumEncodedBytes)
            throw new InvalidDataException("Use a PNG or JPEG image up to 32 MB.");
        return SKData.CreateCopy(encoded);
    }

    private static SKCodec OpenCodec(SKData data)
    {
        var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or damaged image. Use PNG or JPEG.");
        try
        {
            var info = codec.Info;
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg) || codec.FrameCount > 1)
                throw new InvalidDataException("Only single-frame PNG and JPEG images are supported.");
            if (info.Width <= 0 || info.Height <= 0 || info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > MaximumPixels)
                throw new InvalidDataException("Image exceeds the 16 megapixel or 8192-pixel dimension limit.");
            if ((int)codec.EncodedOrigin is < 1 or > 8) throw new InvalidDataException("Invalid image orientation.");
            return codec;
        }
        catch { codec.Dispose(); throw; }
    }

    private static (int Width, int Height) Dimensions(SKCodec codec) => (int)codec.EncodedOrigin >= 5
        ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);

    private static (int X, int Y) SourcePixel(SKEncodedOrigin origin, int x, int y, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => (width - 1 - x, y),
        SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
        SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
        SKEncodedOrigin.LeftTop => (y, x),
        SKEncodedOrigin.RightTop => (y, height - 1 - x),
        SKEncodedOrigin.RightBottom => (width - 1 - y, height - 1 - x),
        SKEncodedOrigin.LeftBottom => (width - 1 - y, x),
        _ => (x, y)
    };

    private static PdfDictionary Image(PdfDocument document, int width, int height, string colorSpace, string filter, byte[] samples)
    {
        var image = new PdfDictionary(document);
        image.Elements.SetName("/Type", "/XObject"); image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width); image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/BitsPerComponent", 8); image.Elements.SetName("/ColorSpace", colorSpace);
        image.CreateStream(samples); image.Elements.SetName("/Filter", filter);
        document.Internals.AddObject(image);
        return image;
    }

    // Conservative JPEG passthrough: 8-bit baseline/extended/progressive, Gray,
    // RGB, or YCbCr, no ICC profile. Profiled and CMYK/YCCK inputs go through
    // Skia's color-managed sRGB decoder, not a guessed DeviceRGB interpretation.
    // Validate segment bounds, frame dimensions, scans and EOI before retaining
    // compressed data. Entropy coding is left to the PDF reader's JPEG decoder.
    private static bool TryJpeg(ReadOnlySpan<byte> bytes, int width, int height, out int components, out int colorTransform)
    {
        components = 0; colorTransform = 1;
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) return false;
        var offset = 2; var inScan = false; var sawScan = false; var frame = false; var adobe = -1; var rgbIds = false;
        while (offset < bytes.Length)
        {
            if (inScan)
            {
                var distance = bytes[offset..].IndexOf((byte)255);
                if (distance < 0) return false;
                offset += distance;
            }
            if (bytes[offset++] != 255) return false;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (inScan && (marker == 0 || marker is >= 208 and <= 215)) continue;
            inScan = false;
            if (marker == 217)
            {
                colorTransform = adobe >= 0 ? adobe : rgbIds ? 0 : 1;
                return frame && sawScan && components is 1 or 3 && colorTransform is 0 or 1;
            }
            if (marker is 0 or 216 || marker is >= 208 and <= 215 || offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || offset + length > bytes.Length) return false;
            var payload = bytes.Slice(offset + 2, length - 2); offset += length;
            if (marker == 226 && payload.StartsWith("ICC_PROFILE\0"u8)) return false;
            if (marker == 238 && payload.StartsWith("Adobe"u8))
            {
                if (payload.Length < 12) return false;
                adobe = payload[11];
            }
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
            {
                if (frame || marker is not (192 or 193 or 194) || payload.Length < 6 || payload[0] != 8) return false;
                components = payload[5];
                if (components is not (1 or 3) || payload.Length != 6 + components * 3 ||
                    BinaryPrimitives.ReadUInt16BigEndian(payload[1..]) != height || BinaryPrimitives.ReadUInt16BigEndian(payload[3..]) != width) return false;
                rgbIds = components == 3 && payload[6] == 'R' && payload[9] == 'G' && payload[12] == 'B';
                frame = true;
            }
            if (marker == 218) { if (!frame) return false; sawScan = true; inScan = true; }
        }
        return false;
    }
}
