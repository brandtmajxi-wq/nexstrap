using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexstrap.Services.Dialogs;

namespace Nexstrap.Services.Shell;

public interface IShellService
{
    void OpenUrl(string url);

    void OpenFolder(string path);

    void CopyToClipboard(string text);
}

/// <summary>Opens links and folders through Explorer, reporting failures to the user.</summary>
public sealed class ShellService(IDialogService dialogs, ILogger<ShellService> logger) : IShellService
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            logger.LogWarning("Refused to open non-web URL {Url}", url);
            return;
        }
        Start(uri.AbsoluteUri, "open the link");
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Start(path, "open the folder");
    }

    public void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { logger.LogWarning(ex, "Clipboard is busy"); }
    }

    private void Start(string target, string action)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}: {Target}", action, target);
            dialogs.Show("Couldn't " + action, $"Windows could not open:\n{target}", DialogKind.Warning, ex.Message);
        }
    }
}
