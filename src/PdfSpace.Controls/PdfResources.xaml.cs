namespace PdfSpace.Controls;
public sealed partial class PdfResources : ResourceDictionary
{
    public static PdfResources Shared { get; } = new();
    public PdfResources() => InitializeComponent();
}
