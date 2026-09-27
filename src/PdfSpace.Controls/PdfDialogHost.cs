using Microsoft.UI.Input;
using Windows.System;
namespace PdfSpace.Controls;

/// <summary>Reusable modal host with keyboard focus containment, inside the Uno visual tree on every target.</summary>
public sealed class PdfDialogHost : Grid
{
    private TaskCompletionSource<string?>? _completion;
    private Control? _returnFocus;
    private readonly List<Control> _focusTargets = [];
    public bool IsOpen => _completion is not null;
    public PdfDialogHost()
    {
        Visibility = Visibility.Collapsed; Background = PdfTheme.Brush("#65000000");
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        KeyDown += OnKey;
    }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { Complete(null); e.Handled = true; }
        else if (e.Key == VirtualKey.Tab && XamlRoot is not null && _focusTargets.Count > 0)
        {
            var current = FocusManager.GetFocusedElement(XamlRoot) as Control;
            var index = current is null ? -1 : _focusTargets.IndexOf(current);
            var backwards = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            index = (index + (backwards ? -1 : 1) + _focusTargets.Count) % _focusTargets.Count;
            _focusTargets[index].Focus(FocusState.Keyboard); e.Handled = true;
        }
    }
    public Task<string?> PromptAsync(string title, string description, string initial = "", bool multiline = false, string acceptLabel = "Apply", bool input = true, bool secret = false)
    {
        if (_completion is not null) return Task.FromResult<string?>(null);
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously); var task = _completion.Task;
        _returnFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Control;
        Children.Clear(); _focusTargets.Clear(); Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, title);
        var content = PdfTheme.Column(17); content.Margin = new Thickness(28);
        content.Children.Add(PdfTheme.Text(title, 22, bold: true));
        var message = PdfTheme.Text(description, 13, "#626262"); message.TextWrapping = TextWrapping.Wrap; content.Children.Add(message);
        var field = new PdfTextField(title) { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 116 : 36 };
        var password = secret ? new PasswordBox { MinWidth = 0, MinHeight = 36, FontFamily = PdfTheme.Font, PasswordRevealMode = PasswordRevealMode.Hidden } : null;
        if (password is not null) AutomationProperties.SetName(password, title);
        Control inputControl = password is null ? field : password;
        string InputValue() => password is null ? field.Text : password.Password;
        if (input) { content.Children.Add(inputControl); _focusTargets.Add(inputControl); }
        inputControl.Loaded += (_, _) => { if (IsOpen) inputControl.Focus(FocusState.Programmatic); };
        var buttons = PdfTheme.Row(8); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var cancel = new PdfCommandButton("Cancel", action: () => Complete(null)); buttons.Children.Add(cancel); _focusTargets.Add(cancel);
        var accept = new PdfCommandButton(acceptLabel, action: () => Complete(input ? InputValue() : "accepted")) { MinWidth = 85 }; accept.Primary(); buttons.Children.Add(accept); _focusTargets.Add(accept); content.Children.Add(buttons);
        Children.Add(new Border { Child = content, MaxWidth = 500, MinWidth = 300, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#BBBBBB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) });
        inputControl.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && !multiline) { Complete(InputValue()); e.Handled = true; } };
        DispatcherQueue.TryEnqueue(() => { if (!IsOpen) return; if (input) { inputControl.Focus(FocusState.Programmatic); if (!secret) field.SelectAll(); } else accept.Focus(FocusState.Programmatic); });
        return task;
    }
    public Task<string?> SecretAsync(string title, string description, string accept = "Continue") => PromptAsync(title, description, acceptLabel: accept, secret: true);
    public async Task<bool> ConfirmAsync(string title, string description, string accept = "Continue") => await PromptAsync(title, description, acceptLabel: accept, input: false) is not null;
    private void Complete(string? text)
    {
        var completion = _completion; _completion = null; foreach (var password in _focusTargets.OfType<PasswordBox>()) password.Password = ""; _focusTargets.Clear(); Children.Clear(); Visibility = Visibility.Collapsed;
        if (_returnFocus?.XamlRoot is not null) _returnFocus.Focus(FocusState.Programmatic);
        _returnFocus = null; completion?.TrySetResult(text);
    }
}
