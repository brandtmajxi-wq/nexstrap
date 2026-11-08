using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

/// <summary>The Roblox Studio card on Home.</summary>
public sealed partial class StudioViewModel(IStudioService studio, IDialogService dialogs, IShellService shell, ILogger<StudioViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? _installCts;
    private bool _refreshed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(InstallCommand), nameof(OpenFolderCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installFraction;

    [ObservableProperty]
    private string _installStage = "";

    [ObservableProperty]
    private string _message = "";

    public bool IsInstalled => studio.Preferred is not null;

    public string StatusText => studio.Preferred is { } build
        ? Loc.Format("Studio.Installed", build.DisplayVersion, SourceLabel(build.Source))
        : Loc.Get("Studio.NotInstalled");

    public string DetailText => studio.Preferred is null
        ? Loc.Get("Studio.NotInstalled.Text")
        : studio.UpdateAvailable && studio.Latest is { } latest
            ? Loc.Format("Studio.UpdateAvailable", latest.Version)
            : studio.Latest is null ? "" : Loc.Get("Studio.UpToDate");

    public bool UpdateAvailable => studio.UpdateAvailable;

    /// <summary>Install when there's no build, or update when Nexstrap's copy is behind or the only copy is another launcher's.</summary>
    public bool ShowInstall => !IsInstalling && (studio.Preferred is not { Source: InstallationSource.Nexstrap } || studio.UpdateAvailable);

    public string InstallText => studio.Preferred is { Source: InstallationSource.Nexstrap } ? Loc.Get("Studio.Update") : Loc.Get("Studio.Install");

    private static string SourceLabel(InstallationSource source) => source switch
    {
        InstallationSource.Nexstrap => "Nexstrap",
        InstallationSource.Official => "Roblox",
        _ => source.ToString()
    };

    public void RefreshOnce()
    {
        if (_refreshed) return;
        _refreshed = true;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try { await studio.RefreshAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "Studio scan failed"); }
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(IsInstalled), nameof(StatusText), nameof(DetailText), nameof(UpdateAvailable), nameof(ShowInstall), nameof(InstallText) })
            OnPropertyChanged(name);
        OpenCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(ShowInstall));

    private bool CanOpen() => !IsInstalling && studio.Preferred is not null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        var result = studio.Open();
        Message = result.Success ? Loc.Get("Studio.Starting") : "";
        if (!result.Success) dialogs.Show(Loc.Get("Studio.DidntStart"), result.Message, DialogKind.Error);
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void OpenFolder()
    {
        if (studio.Preferred is { } build) shell.OpenFolder(build.Directory);
    }

    private bool CanInstall() => !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (!dialogs.Confirm(Loc.Get("Studio.Install.Title"), Loc.Get("Studio.Install.Text"), Loc.Get("Studio.Install"), Loc.Get("Appearance.Later")))
            return;

        IsInstalling = true;
        InstallFraction = 0;
        InstallStage = Loc.Get("Home.Preparing");
        Message = "";
        _installCts = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(p =>
        {
            InstallStage = p.BytesTotal > 0 ? $"{p.Stage} · {p.BytesDone / 1048576} / {p.BytesTotal / 1048576} MB" : p.Stage;
            InstallFraction = p.Fraction;
        });

        try
        {
            await studio.InstallLatestAsync(progress, _installCts.Token);
            Message = Loc.Format("Studio.Ready", studio.Preferred?.DisplayVersion ?? "");
        }
        catch (OperationCanceledException)
        {
            Message = Loc.Get("Home.InstallationCancelledNothingWasChanged");
        }
        catch (InstallException ex)
        {
            dialogs.Show(Loc.Get("Studio.InstallFailed"), ex.Message, DialogKind.Error, ex.InnerException?.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            logger.LogError(ex, "Studio install failed");
            dialogs.Show(Loc.Get("Studio.InstallFailed"), Loc.Get("Home.AFileOrNetworkErrorStopped") + ex.Message, DialogKind.Error, ex.ToString());
        }
        finally
        {
            _installCts.Dispose();
            _installCts = null;
            IsInstalling = false;
            Notify();
        }
    }

    [RelayCommand]
    private void CancelInstall() => _installCts?.Cancel();
}
