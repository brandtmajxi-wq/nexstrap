using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Clips;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

/// <summary>One screenshot tile. The small picture loads in the background.</summary>
public sealed partial class ScreenshotItem(string path, DateTime taken) : ObservableObject
{
    public string Path { get; } = path;

    public string Taken { get; } = taken.ToString("g");

    [ObservableProperty]
    private ImageSource? _thumbnail;
}

/// <summary>
/// The pictures Roblox saves to Pictures\Roblox, newest first. Nexstrap only reads them; deleting one moves it
/// to the Recycle Bin after asking, so it can be brought back.
/// </summary>
public sealed partial class ScreenshotsViewModel : PageViewModel
{
    internal const int MaxShown = 300;
    private const int ThumbnailWidth = 360;
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ScreenshotsViewModel> _logger;
    private readonly Services.Clips.IClipService _clips;
    private readonly Services.Configuration.ISettingsService _settings;
    private CancellationTokenSource? _loading;

    public ScreenshotsViewModel(IShellService shell, IDialogService dialogs, ILogger<ScreenshotsViewModel> logger,
        Services.Clips.IClipService clips, Services.Configuration.ISettingsService settings)
    {
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;
        _clips = clips;
        _settings = settings;
        clips.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(ClipStatus)));
    }

    // ===== Clips (dev only for now) =====

    public bool ClipsAvailable => _clips.Available;

    public bool RecordClips
    {
        get => _settings.Current.Launcher.RecordClips;
        set
        {
            if (value == _settings.Current.Launcher.RecordClips) return;
            _settings.Current.Launcher.RecordClips = value;
            _settings.Save();
            _clips.Refresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClipStatus));
        }
    }

    public IReadOnlyList<Choice<int>> ClipLengthChoices { get; } = ClipHotkey.LengthChoices
        .Select(s => new Choice<int>(s, () => LengthLabel(s))).ToList();

    internal static string LengthLabel(int seconds) => seconds switch
    {
        30 => Loc.Get("Clips.Length.30"),
        60 => Loc.Get("Clips.Length.60"),
        _ => Loc.Get("Clips.Length.90"),
    };

    public int ClipSeconds
    {
        get => _settings.Current.Launcher.ClipSeconds;
        set
        {
            if (value == _settings.Current.Launcher.ClipSeconds || value <= 0) return;
            _settings.Current.Launcher.ClipSeconds = value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClipStatus));
        }
    }

    /// <summary>True while the key button waits for the new key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    private bool _pickingHotkey;

    public string HotkeyText => PickingHotkey ? Loc.Get("Clips.Key.Press")
        : ClipHotkey.Format(_settings.Current.Launcher.ClipHotkeyModifiers, _settings.Current.Launcher.ClipHotkeyKey);

    partial void OnPickingHotkeyChanged(bool value) => _clips.HotkeyPaused = value;

    [RelayCommand]
    private void PickHotkey() => PickingHotkey = !PickingHotkey;

    /// <summary>Called by the page with the key pressed while picking. False if that key can't be used.</summary>
    public bool TrySetHotkey(System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key)
    {
        if (key == System.Windows.Input.Key.Escape) { PickingHotkey = false; return true; }
        if (ClipHotkey.IsModifier(key)) return true; // still holding Ctrl/Alt/Shift; wait for the real key
        var mods = ClipHotkey.ToModifiers(modifiers);
        if (!ClipHotkey.Allowed(mods, key))
        {
            Summary = Loc.Get("Clips.Key.NotAllowed");
            return false;
        }
        _settings.Current.Launcher.ClipHotkeyModifiers = mods;
        _settings.Current.Launcher.ClipHotkeyKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        _settings.Save();
        PickingHotkey = false; // registers the new key
        OnPropertyChanged(nameof(HotkeyText));
        OnPropertyChanged(nameof(ClipStatus));
        return true;
    }

    public string ClipStatus =>
        !RecordClips ? Loc.Get("Clips.Status.Off")
        : !_clips.HotkeyWorks ? Loc.Format("Clips.Status.KeyTaken", HotkeyText)
        : _clips.IsRecording ? Loc.Format("Clips.Status.Recording", HotkeyText, LengthLabel(ClipSeconds))
        : Loc.Format("Clips.Status.Waiting", HotkeyText);

    [RelayCommand]
    private void OpenClipsFolder()
    {
        Directory.CreateDirectory(_clips.ClipsFolder);
        _shell.OpenFolder(_clips.ClipsFolder);
    }

    /// <summary>The new look for this page (see NewLook).</summary>
    public bool UseNewLook => NewLook.ScreenshotsAndServers;

    public override string Title => Loc.Get("Nav.Screenshots");

    public override string Subtitle => Loc.Get("Page.Screenshots");

    /// <summary>Where Roblox saves screenshots: the Pictures folder, which can be moved, plus "Roblox".</summary>
    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Roblox");

    public ObservableCollection<ScreenshotItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Picture files in a folder (not subfolders), newest first.</summary>
    internal static IReadOnlyList<FileInfo> Find(string folder, int max)
    {
        if (!Directory.Exists(folder)) return [];
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(max)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public override void OnNavigatedTo() => Refresh();

    public override void OnNavigatedFrom()
    {
        _loading?.Cancel();
        PickingHotkey = false;
    }

    [RelayCommand]
    private void Refresh()
    {
        _loading?.Cancel();
        _loading = new CancellationTokenSource();

        var files = Find(Folder, MaxShown);
        Items.Clear();
        foreach (var file in files) Items.Add(new ScreenshotItem(file.FullName, file.LastWriteTime));
        Summary = files.Count == 0 ? "" : Loc.Format("Screenshots.Count", files.Count, Folder);
        OnPropertyChanged(nameof(HasItems));
        _ = LoadThumbnailsAsync(Items.ToList(), _loading.Token);
    }

    /// <summary>Small copies, one at a time on a background thread, so a big folder doesn't hold up the window or use much memory.</summary>
    private async Task LoadThumbnailsAsync(IReadOnlyList<ScreenshotItem> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                item.Thumbnail = await Task.Run(() => LoadSmall(item.Path), cancel);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "Couldn't read screenshot {Path}", item.Path);
            }
        }
    }

    private static ImageSource LoadSmall(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = ThumbnailWidth;
        image.CacheOption = BitmapCacheOption.OnLoad; // reads the file now, so it isn't kept open
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [RelayCommand]
    private void OpenFolder() => _shell.OpenFolder(Folder);

    [RelayCommand]
    private void Open(ScreenshotItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't open {Path}", item.Path);
            _shell.ShowInFolder(item.Path);
        }
    }

    [RelayCommand]
    private void Show(ScreenshotItem item) => _shell.ShowInFolder(item.Path);

    [RelayCommand]
    private void Copy(ScreenshotItem item)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(item.Path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            Clipboard.SetImage(image);
            Summary = Loc.Get("Screenshots.Copied");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            _dialogs.Show(Loc.Get("Screenshots.CouldntCopy"), ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void Delete(ScreenshotItem item)
    {
        if (!_dialogs.Confirm(Loc.Get("Screenshots.Delete.Title"), Loc.Get("Screenshots.Delete.Text"), Loc.Get("Screenshots.Delete"), Loc.Get("Common.Cancel")))
            return;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Items.Remove(item);
            OnPropertyChanged(nameof(HasItems));
            Summary = Items.Count == 0 ? "" : Loc.Format("Screenshots.Count", Items.Count, Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _dialogs.Show(Loc.Get("Screenshots.CouldntDelete"), ex.Message, DialogKind.Warning);
        }
    }
}
