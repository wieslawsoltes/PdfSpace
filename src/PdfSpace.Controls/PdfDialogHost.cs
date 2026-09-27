using Windows.System;
namespace PdfSpace.Controls;

/// <summary>Reusable custom modal host. Dialogs stay inside the Uno visual tree on every target.</summary>
public sealed class PdfDialogHost : Grid
{
    private TaskCompletionSource<string?>? _completion;
    private Control? _returnFocus;
    public bool IsOpen => _completion is not null;
    public PdfDialogHost() { Visibility = Visibility.Collapsed; Background = PdfTheme.Brush("#65000000"); KeyDown += OnKey; }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    { if (e.Key == VirtualKey.Escape) { Complete(null); e.Handled = true; } }
    public Task<string?> PromptAsync(string title, string description, string initial = "", bool multiline = false, string acceptLabel = "Apply", bool input = true)
    {
        if (_completion is not null) return Task.FromResult<string?>(null);
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously); var task = _completion.Task;
        _returnFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        Children.Clear(); Visibility = Visibility.Visible;
        var content = PdfTheme.Column(17); content.Margin = new Thickness(28);
        content.Children.Add(PdfTheme.Text(title, 22, bold: true));
        var message = PdfTheme.Text(description, 13, "#626262"); message.TextWrapping = TextWrapping.Wrap; content.Children.Add(message);
        var field = new PdfTextField(title) { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 116 : 36 };
        if (input) content.Children.Add(field);
        var buttons = PdfTheme.Row(8); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Children.Add(new PdfCommandButton("Cancel", action: () => Complete(null)));
        var accept = new PdfCommandButton(acceptLabel, action: () => Complete(input ? field.Text : "accepted")) { MinWidth = 85 }; accept.Primary(); buttons.Children.Add(accept); content.Children.Add(buttons);
        Children.Add(new Border { Child = content, MaxWidth = 500, MinWidth = 300, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#BBBBBB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) });
        field.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && !multiline) { Complete(field.Text); e.Handled = true; } };
        DispatcherQueue.TryEnqueue(() => { if (input) { field.Focus(FocusState.Programmatic); field.SelectAll(); } else accept.Focus(FocusState.Programmatic); });
        return task;
    }
    public async Task<bool> ConfirmAsync(string title, string description, string accept = "Continue") => await PromptAsync(title, description, acceptLabel: accept, input: false) is not null;
    private void Complete(string? text)
    {
        var completion = _completion; _completion = null; Children.Clear(); Visibility = Visibility.Collapsed;
        _returnFocus?.Focus(FocusState.Programmatic); _returnFocus = null; completion?.TrySetResult(text);
    }
}
