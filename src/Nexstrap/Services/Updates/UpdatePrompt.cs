namespace Nexstrap.Services.Updates;

/// <summary>Decides whether to ask about a newer Nexstrap when the app opens.</summary>
public static class UpdatePrompt
{
    /// <summary>Once per run, and only with the window on screen: a copy waiting in the tray shouldn't pop up a question.</summary>
    public static bool ShouldOffer(UpdateCheckResult? result, bool alreadyOffered, bool windowVisible) =>
        !alreadyOffered && windowVisible && result is { Status: UpdateStatus.UpdateAvailable, Release: not null };
}
