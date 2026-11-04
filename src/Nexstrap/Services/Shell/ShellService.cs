using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
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
        Start(uri.AbsoluteUri, "Shell.OpenLink");
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Start(path, "Shell.OpenFolder");
    }

    public void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { logger.LogWarning(ex, "Clipboard is busy"); }
    }

    private void Start(string target, string titleKey)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Windows could not open {Target}", target);
            dialogs.Show(Loc.Get(titleKey), Loc.Format("Shell.Failed", target), DialogKind.Warning, ex.Message);
        }
    }
}
