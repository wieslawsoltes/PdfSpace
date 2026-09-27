namespace PdfSpace.Controls;

/// <summary>Activates a newly attached editor once, without selecting text again after typing starts.</summary>
public static class PdfInputFocus
{
    public static void ActivateWhenLoaded(Control target, Func<bool> isCurrent, bool selectAll = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isCurrent);
        var activated = false;
        void Activate()
        {
            if (activated || !isCurrent()) { target.Loaded -= Loaded; return; }
            if (!target.IsLoaded || target.XamlRoot is null) return;
            // Focus dispatch can reenter the host. Claim activation before dispatch,
            // and detach the Loaded callback so a delayed event never reselects input.
            activated = true;
            target.Loaded -= Loaded;
            if (target.Focus(FocusState.Programmatic) && selectAll && target is TextBox text) text.SelectAll();
        }
        void Loaded(object sender, RoutedEventArgs args) => Activate();
        target.Loaded += Loaded;
        // Supports both already-loaded controls and controls attached later this turn.
        target.DispatcherQueue.TryEnqueue(Activate);
    }
}
