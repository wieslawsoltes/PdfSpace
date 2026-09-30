namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    /// <summary>Local recovery state for the active tab, independent of command feedback.</summary>
    public string RecoveryStatus => Session.Document.IsSensitive ? "disabled" : _active.Recovery;
    private string? _shownRecoveryStatus, _shownRecoveryError;

    private void UpdateRecoveryStatus()
    {
        var state = RecoveryStatus;
        var error = state == "failed" ? _active.RecoveryError : null;
        if (state == _shownRecoveryStatus && error == _shownRecoveryError) return;
        _shownRecoveryStatus = state; _shownRecoveryError = error;
        _recoveryStatus.Text = state switch
        {
            "pending" => "Recovery pending",
            "saving" => "Saving recovery…",
            "saved" => "Recovery saved",
            "failed" => "Recovery failed — export workspace",
            "disabled" => "Recovery disabled",
            _ => "Local recovery"
        };
        _recoveryStatus.Foreground = PdfTheme.Brush(state == "failed" ? "#B12620" : "#858585");
        ToolTipService.SetToolTip(_recoveryStatus, error ?? (state == "disabled"
            ? "Automatic recovery is disabled for protected documents. Workspace copies would be unencrypted."
            : "Recovery keeps the last saved workspace on this device. Export a workspace for a permanent copy."));
    }

    private async Task SaveRecoveryAsync()
    {
        if (_disposed || Session.Document.IsSensitive) return;
        if (_savingRecovery) { _saveAgain = true; return; }
        _savingRecovery = true;
        try
        {
            do
            {
                _saveAgain = false;
                var owner = _active;
                var document = owner.Session.Document;
                if (document.IsSensitive) return;
                owner.Recovery = "saving"; owner.RecoveryError = null;
                UpdateRecoveryStatus(); StateChanged?.Invoke();
                try
                {
                    await _storage.WriteRecoveryAsync(WorkspaceJson.Save(document));
                    if (_disposed) return;
                    if (ReferenceEquals(document, owner.Session.Document)) owner.Recovery = "saved";
                    else
                    {
                        owner.Recovery = "pending";
                        // Do not start saving a newly opened tab merely because an older
                        // tab's write completed. A queued edit on the active tab still drains.
                        if (_active == owner) _saveAgain = true;
                    }
                }
                catch (Exception ex)
                {
                    if (_disposed) return;
                    owner.Recovery = "failed";
                    owner.RecoveryError = "Recovery could not be saved: " + ex.Message + ". Export your workspace now.";
                }
                // A late completion belongs to its original tab, never the current
                // command/status message. Errors remain visible in a separate red badge.
                if (_active == owner) { UpdateRecoveryStatus(); StateChanged?.Invoke(); }
            } while (_saveAgain && !_disposed);
        }
        finally { _savingRecovery = false; }
    }
}
