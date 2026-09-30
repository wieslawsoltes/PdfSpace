using PdfSharp.Pdf;
using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>Classifies encoded stream payloads, not complete on-disk PDF object extents.</summary>
public static class PdfSizeAudit
{
    public const int MaximumObjects = 200_000;
    public const int LargestStreamLimit = 12;
    public const string Scope = "Retained original source buffers; unique indirect encoded stream payloads. " +
        "Not an exported-file size estimate, reachability audit, exact whole-file byte partition or compression guarantee. " +
        "Unwritten annotations/forms/OCR and preview/history buffers are excluded. The PDF parser can decode structural streams; " +
        "this audit does not decode page, image, font or attachment payloads.";

    public static PdfSourceSizeReport Read(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocumentEngine.OpenNative(bytes);
        return ReadNative(document, bytes.LongLength, cancellationToken);
    }

    internal static PdfSourceSizeReport ReadNative(PdfDocument document, long sourceBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var objects = document.Internals.GetAllObjects();
        if (objects.Length > MaximumObjects) throw new InvalidDataException("Space audit exceeds 200,000 indirect objects.");
        var fonts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var contents = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        // A stream may have several referring pages/fonts. Classification must not
        // count those references as copies of its encoded payload.
        foreach (var item in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not PdfDictionary dictionary) continue;
            if (dictionary.Elements.GetName("/Type") == "/FontDescriptor")
                foreach (var key in FontKeys)
                    if (PdfObjects.Dictionary(dictionary.Elements[key]) is { } program) fonts.Add(program);
            if (dictionary.Elements.GetName("/Type") != "/Page") continue;
            var value = PdfObjects.Resolve(dictionary.Elements["/Contents"]);
            if (value is PdfDictionary stream) contents.Add(stream);
            else if (value is PdfArray array)
            {
                if (array.Elements.Count > MaximumObjects) throw new InvalidDataException("Space audit content array is too large.");
                foreach (var entry in array.Elements)
                    if (PdfObjects.Dictionary(entry) is { } member) contents.Add(member);
            }
        }
        var counts = new int[7]; var lengths = new long[7];
        // Bounded top-k heap: O(streamCount log 12), not a sort of all streams.
        var largest = new PriorityQueue<PdfStreamObjectUsage, (long Size, int ReverseNumber)>();
        foreach (var item in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not PdfDictionary { Stream: { } stream } dictionary) continue;
            var type = dictionary.Elements.GetName("/Type");
            var subtype = dictionary.Elements.GetName("/Subtype");
            var kind = fonts.Contains(dictionary) ? PdfStreamKind.FontProgram :
                contents.Contains(dictionary) ? PdfStreamKind.PageContent :
                subtype == "/Image" ? PdfStreamKind.Image :
                subtype == "/Form" ? PdfStreamKind.FormAppearance :
                type == "/EmbeddedFile" ? PdfStreamKind.EmbeddedFile :
                type == "/Metadata" ? PdfStreamKind.Metadata : PdfStreamKind.Other;
            var size = stream.Length;
            counts[(int)kind]++; lengths[(int)kind] = checked(lengths[(int)kind] + size);
            var priority = ((long)size, -dictionary.Reference!.ObjectNumber);
            if (largest.Count < LargestStreamLimit || priority.CompareTo(largest.PeekPriority()) > 0)
            {
                largest.Enqueue(new(dictionary.Reference!.ObjectNumber, dictionary.Reference!.GenerationNumber, kind, size), priority);
                if (largest.Count > LargestStreamLimit) largest.Dequeue();
            }
        }
        var total = lengths.Sum();
        var categories = Enum.GetValues<PdfStreamKind>().Select(kind =>
            new PdfStreamUsage(kind, counts[(int)kind], lengths[(int)kind], total == 0 ? 0 : lengths[(int)kind] * 100d / total)).ToArray();
        var top = largest.UnorderedItems.Select(item => item.Element).OrderByDescending(item => item.EncodedBytes)
            .ThenBy(item => item.ObjectNumber).ToArray();
        return new(sourceBytes, objects.Length, counts.Sum(), total, Array.AsReadOnly(categories), Array.AsReadOnly(top));
    }

    private static readonly string[] FontKeys = ["/FontFile", "/FontFile2", "/FontFile3"];
    private static TPriority PeekPriority<TElement, TPriority>(this PriorityQueue<TElement, TPriority> queue)
    { queue.TryPeek(out _, out var priority); return priority!; }
}
