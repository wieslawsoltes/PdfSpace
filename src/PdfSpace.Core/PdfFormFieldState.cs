namespace PdfSpace.Core;

public enum PdfFieldKind { Text, CheckBox, RadioButton, ComboBox, ListBox, Signature, Unsupported }
public sealed record PdfFieldOption(string Value, string Label);

/// <summary>A widget in source-page coordinates. GroupName identifies the logical AcroForm field.</summary>
public sealed record PdfFormFieldState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? SourceKey { get; init; }
    public string Name { get; init; } = "Field";
    public string GroupName { get; init; } = "Field";
    public string Label { get; init; } = "";
    public PdfFieldKind Kind { get; init; }
    public RectD Bounds { get; init; }
    public string Value { get; init; } = "";
    public string DefaultValue { get; init; } = "";
    public string ExportValue { get; init; } = "Yes";
    public PdfFieldOption[] Options { get; init; } = [];
    public bool ReadOnly { get; init; }
    public bool Required { get; init; }
    public bool Multiline { get; init; }
    public int MaxLength { get; init; }
    public double FontSize { get; init; } = 12;
    public bool Modified { get; init; }
    public bool IsChecked => Value == ExportValue;
    public bool CanFill => !ReadOnly && Kind is not (PdfFieldKind.Signature or PdfFieldKind.Unsupported);
}
