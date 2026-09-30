using PdfSpace.Pdf;

internal static class BrowserAttachmentVerification
{
    public static void Run(string directory)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
        Check(File.ReadAllBytes(Path.Combine(directory, "attachment-browser-plain.txt")).SequenceEqual(EmbeddedFileTests.Plain), "browser attachment retains exact original unfiltered bytes");
        Check(File.ReadAllBytes(Path.Combine(directory, "attachment-browser-unicode.txt")).SequenceEqual(EmbeddedFileTests.Unicode), "browser attachment retains exact Flate-decoded Unicode bytes");
        Check(File.ReadAllBytes(Path.Combine(directory, "attachment-browser-active.download")).SequenceEqual(EmbeddedFileTests.Html), "browser active attachment is downloaded unchanged, not executed");
        Check(File.ReadAllBytes(Path.Combine(directory, "attachment-browser-last.txt")).SequenceEqual(EmbeddedFileTests.Plain), "browser paged attachment selection reaches final native entry");
    }
}
