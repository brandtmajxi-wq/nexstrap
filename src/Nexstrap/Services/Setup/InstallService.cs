using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Setup;

public sealed record InstallOptions(string Folder, bool DesktopShortcut, bool StartMenuShortcut, bool HandleLinks);

public interface IInstallService
{
    string DefaultFolder { get; }

    /// <summary>Where Nexstrap is installed, or null if it isn't.</summary>
    string? InstalledFolder { get; }

    bool IsRunningInstalledCopy { get; }

    bool HasDesktopShortcut { get; }

    bool HasStartMenuShortcut { get; }

    /// <summary>True when a plain double-click on this copy should open setup.</summary>
    bool ShouldOfferSetup(StartupOptions options);

    /// <summary>Returns the language key of the problem, or null if the folder is usable.</summary>
    string? ValidateFolder(string folder);

    /// <summary>Copies this exe into place and registers it. Returns the installed exe.</summary>
    string Install(InstallOptions options);

    /// <summary>Keeps the version shown in Windows' app list right after a self-update.</summary>
    void SyncInstalledVersion();

    /// <summary>True if another Nexstrap from the install folder is open.</summary>
    bool IsAnotherCopyRunning();

    /// <summary>Removes everything except the running exe, which a helper deletes after this process exits.</summary>
    void Uninstall(bool deleteData);
}

/// <summary>
/// Per-user install, the way other bootstrappers do it: the one exe copies itself into a folder and
/// registers with Windows. Nothing needs admin rights, and uninstalling only removes what setup created.
/// </summary>
public sealed class InstallService : IInstallService
{
    internal const string ExeName = AppInfo.Name + ".exe";
    private const string ShortcutName = AppInfo.Name + ".lnk";

    private readonly ISettingsService _settings;
    private readonly IProtocolRegistration _protocol;
    private readonly ILogger<InstallService> _logger;
    private readonly string _uninstallKey;
    private readonly string _startMenu;
    private readonly string _desktop;
    private readonly string _sourceExe;
    private readonly string _dataRoot;
    private readonly string? _runKey;

    public InstallService(ISettingsService settings, IProtocolRegistration protocol, ILogger<InstallService> logger)
        : this(settings, protocol, logger,
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppInfo.Name}",
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path."),
            AppInfo.Paths.Root, WindowsStartup.RunKey) { }

    /// <summary>Tests point these at a scratch registry key and temp folders.</summary>
    internal InstallService(ISettingsService settings, IProtocolRegistration protocol, ILogger<InstallService> logger,
        string uninstallKey, string startMenu, string desktop, string sourceExe, string dataRoot, string? runKey = null)
    {
        _settings = settings;
        _protocol = protocol;
        _logger = logger;
        _uninstallKey = uninstallKey;
        _startMenu = startMenu;
        _desktop = desktop;
        _sourceExe = sourceExe;
        _dataRoot = dataRoot;
        _runKey = runKey;
    }

    public string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);

    public string? InstalledFolder
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_uninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
    }

    public bool IsRunningInstalledCopy => InstalledFolder is { } folder && SamePath(Path.Combine(folder, ExeName), _sourceExe);

    public bool HasDesktopShortcut => File.Exists(Path.Combine(_desktop, ShortcutName));

    public bool HasStartMenuShortcut => File.Exists(Path.Combine(_startMenu, ShortcutName));

    public bool ShouldOfferSetup(StartupOptions options) =>
        options is { CapturePath: null, StartPage: null, JustUpdated: false, Tray: false, Positional.Count: 0 }
        && !_settings.Current.SkippedSetup
        && !IsRunningInstalledCopy;

    public string? ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal))
            return "Setup.Error.Folder";

        string full;
        try { full = Path.GetFullPath(folder); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return "Setup.Error.Folder"; }

        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows })
        {
            var admin = Environment.GetFolderPath(special);
            if (admin.Length > 0 && (SamePath(full, admin) || full.StartsWith(admin.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase)))
                return "Setup.Error.AdminFolder";
        }

        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".nexstrap-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Install folder {Folder} can't be used", full);
            return "Setup.Error.Folder";
        }
    }

    public string Install(InstallOptions options)
    {
        var folder = Path.GetFullPath(options.Folder);
        var target = Path.Combine(folder, ExeName);
        Directory.CreateDirectory(folder);

        // A previous install somewhere else shouldn't stay behind as a second copy.
        var previous = InstalledFolder;

        if (!SamePath(target, _sourceExe)) CopyExe(target);

        UpdateShortcut(Path.Combine(_desktop, ShortcutName), target, options.DesktopShortcut);
        UpdateShortcut(Path.Combine(_startMenu, ShortcutName), target, options.StartMenuShortcut);
        WriteUninstallEntry(folder, target);
        if (_runKey is not null) WindowsStartup.Retarget(_runKey, target);

        if (options.HandleLinks) _protocol.RegisterFor(target);
        else _protocol.Unregister(); // only acts if Nexstrap owns the links

        if (previous is not null && !SamePath(previous, folder))
            TryDelete(Path.Combine(previous, ExeName));

        _settings.Current.SkippedSetup = false;
        _settings.Save();
        _logger.LogInformation("Installed Nexstrap {Version} to {Folder}", AppInfo.VersionString, folder);
        return target;
    }

    public void SyncInstalledVersion()
    {
        // The Windows entry can go missing; the default folder only holds an installed copy, so put it back.
        if (InstalledFolder is null && SamePath(Path.Combine(DefaultFolder, ExeName), _sourceExe))
            WriteUninstallEntry(DefaultFolder, _sourceExe);
        if (!IsRunningInstalledCopy) return;
        using var key = Registry.CurrentUser.OpenSubKey(_uninstallKey, writable: true);
        if (key?.GetValue("DisplayVersion") as string != AppInfo.VersionString)
            key?.SetValue("DisplayVersion", AppInfo.VersionString);
    }

    public bool IsAnotherCopyRunning()
    {
        var processes = Process.GetProcessesByName(AppInfo.Name);
        try
        {
            return processes.Any(p =>
            {
                if (p.Id == Environment.ProcessId) return false;
                try { return p.MainModule?.FileName is { } path && SamePath(path, _sourceExe); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
            });
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public void Uninstall(bool deleteData)
    {
        _protocol.Unregister();
        TryDelete(Path.Combine(_desktop, ShortcutName));
        TryDelete(Path.Combine(_startMenu, ShortcutName));
        Registry.CurrentUser.DeleteSubKeyTree(_uninstallKey, throwOnMissingSubKey: false);
        if (_runKey is not null) WindowsStartup.Remove(_runKey);

        Process.Start(new ProcessStartInfo("cmd.exe", BuildCleanupArguments(_sourceExe, deleteData ? _dataRoot : null))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        })?.Dispose();
        _logger.LogInformation("Uninstalled Nexstrap (data deleted: {Data})", deleteData);
    }

    /// <summary>
    /// Waits for this process to exit, then deletes the exe. The folder is only removed if it's empty,
    /// because the user may have picked one with other files in it.
    /// </summary>
    internal static string BuildCleanupArguments(string exe, string? dataRoot)
    {
        var steps = new List<string>
        {
            "ping 127.0.0.1 -n 4 >nul",
            $"del /f /q \"{exe}\"",
            $"del /f /q \"{exe}.old\"",
            $"rmdir \"{Path.GetDirectoryName(exe)}\""
        };
        if (dataRoot is not null) steps.Add($"rmdir /s /q \"{dataRoot}\"");
        return "/c \"" + string.Join(" & ", steps) + "\"";
    }

    private void CopyExe(string target)
    {
        if (File.Exists(target))
        {
            // The installed copy may be open; Windows lets a running exe be renamed but not replaced.
            var old = target + ".old";
            TryDelete(old);
            File.Move(target, old);
        }
        File.Copy(_sourceExe, target, overwrite: true);
    }

    private void WriteUninstallEntry(string folder, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_uninstallKey);
        key.SetValue("DisplayName", AppInfo.Name);
        key.SetValue("DisplayVersion", AppInfo.VersionString);
        key.SetValue("Publisher", AppInfo.Name);
        key.SetValue("DisplayIcon", $"\"{exe}\",0");
        key.SetValue("InstallLocation", folder);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("URLInfoAbout", AppInfo.RepositoryUrl);
        key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void UpdateShortcut(string path, string exe, bool wanted)
    {
        if (!wanted)
        {
            TryDelete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows can't create shortcuts here.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = exe;
        link.WorkingDirectory = Path.GetDirectoryName(exe);
        link.IconLocation = exe + ",0";
        link.Description = AppInfo.Tagline;
        link.Save();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* left behind, harmless */ }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
