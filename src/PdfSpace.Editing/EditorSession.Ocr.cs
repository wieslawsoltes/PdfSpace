using PdfSpace.Core;
namespace PdfSpace.Editing;

public sealed partial class EditorSession
{
    public void CorrectOcrWord(Guid pageId, Guid wordId, string text)
    {
        text = text.Trim();
        var page = Document.Pages.FirstOrDefault(item => item.Id == pageId) ?? throw new InvalidOperationException("The recognized page no longer exists.");
        var layer = page.Ocr ?? throw new InvalidOperationException("The page has no OCR layer.");
        if (!layer.Words.Any(word => word.Id == wordId)) throw new InvalidOperationException("The recognized word no longer exists.");
        Execute("Correct recognized text", document => document.UpdatePage(pageId, state => state with
        {
            Ocr = layer with { Words = layer.Words.Select(word => word.Id == wordId ? word with { Text = text, Reviewed = true } : word).ToArray() }
        }));
    }
}
