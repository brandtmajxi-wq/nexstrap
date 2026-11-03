using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Models;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Performance;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

public sealed partial class PerformanceViewModel(
    IRobloxUserSettingsService robloxSettings,
    IRobloxLauncher launcher,
    IRobloxClientService client,
    IFastFlagService flags,
    IDisplayInfo display,
    IDialogService dialogs,
    INavigationService navigation) : PageViewModel
{
    private bool _loading;

    public override string Title => "Performance";

    public override string Subtitle => "Frame rate and graphics. These settings change limits and quality; they can't make your PC faster.";

    // ===== Status =====

    public ObservableCollection<StatusLine> Status { get; } = [];

    public string DisplayText => display.RefreshRate is { } hz
        ? $"Primary display: {hz} Hz. Recommended cap: {PerformancePlanner.RecommendedCap(hz)} FPS."
        : "Windows didn't report your display's refresh rate.";

    public bool PlayUsesNexstrapBuild => client.State.Preferred?.Source == InstallationSource.Nexstrap;

    [ObservableProperty]
    private string? _message;

    // ===== FPS cap =====

    public ObservableCollection<Choice<int>> CapChoices { get; } = [];

    [ObservableProperty]
    private int _cap;

    partial void OnCapChanged(int value)
    {
        if (!_loading) SaveToRoblox(new RobloxUserSettings { FramerateCap = value }, $"Frame-rate cap set to {value} FPS.");
    }

    // ===== Presets =====

    public IReadOnlyList<PerformancePreset> Presets => PerformancePlanner.Presets;

    [RelayCommand]
    private void ApplyPreset(PerformancePreset preset)
    {
        var cap = PerformancePlanner.ResolveCap(preset, display.RefreshRate);
        SaveToRoblox(new RobloxUserSettings { FramerateCap = cap, SavedQualityLevel = preset.Quality },
            $"{preset.Name} applied: {cap} FPS cap, quality {(preset.Quality == 0 ? "automatic" : preset.Quality + " of 10")}.");
    }

    private void SaveToRoblox(RobloxUserSettings changes, string success)
    {
        try
        {
            robloxSettings.Save(changes);
            Message = success + " Roblox uses this next time it starts.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            dialogs.Show("Couldn't change Roblox's settings", ex.Message, DialogKind.Warning);
        }
        Load();
    }

    // ===== Graphics preferences (allowlisted FastFlags) =====

    public IReadOnlyList<Choice<GraphicsApi>> ApiChoices { get; } =
    [
        new(GraphicsApi.Automatic, "Automatic"),
        new(GraphicsApi.Direct3D11, "Direct3D 11"),
        new(GraphicsApi.Vulkan, "Vulkan"),
        new(GraphicsApi.OpenGL, "OpenGL")
    ];

    public IReadOnlyList<Choice<int>> MsaaChoices { get; } =
    [
        new(-1, "Automatic"), new(0, "Off"), new(1, "1x"), new(2, "2x"), new(4, "4x"), new(8, "8x")
    ];

    public IReadOnlyList<Choice<int>> TextureChoices { get; } =
    [
        new(-1, "Automatic"), new(0, "Lowest"), new(1, "Low"), new(2, "Medium"), new(3, "Highest")
    ];

    [ObservableProperty]
    private GraphicsApi _api;

    [ObservableProperty]
    private int _msaa = -1;

    [ObservableProperty]
    private int _textures = -1;

    partial void OnApiChanged(GraphicsApi value)
    {
        if (_loading) return;
        PerformancePlanner.ApplyApi(flags, value);
        Load();
    }

    partial void OnMsaaChanged(int value)
    {
        if (_loading) return;
        PerformancePlanner.ApplyMsaa(flags, value);
        Load();
    }

    partial void OnTexturesChanged(int value)
    {
        if (_loading) return;
        PerformancePlanner.ApplyTextures(flags, value);
        Load();
    }

    [RelayCommand]
    private void GoHome() => navigation.NavigateTo<HomeViewModel>();

    // ===== Lifecycle =====

    public override void OnNavigatedTo()
    {
        Load();
        if (client.State.Installations.Count == 0)
            _ = client.RefreshAsync(checkLatest: false).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Load));
    }

    private void Load()
    {
        _loading = true;
        try
        {
            RobloxUserSettings current;
            try { current = robloxSettings.Load(); }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException) { current = new RobloxUserSettings(); }

            var cap = current.FramerateCap ?? PerformancePlanner.RecommendedCap(display.RefreshRate);
            CapChoices.Clear();
            foreach (var fps in PerformancePlanner.StandardCaps) CapChoices.Add(new Choice<int>(fps, $"{fps} FPS"));
            if (!PerformancePlanner.StandardCaps.Contains(cap)) CapChoices.Add(new Choice<int>(cap, $"{cap} FPS (current)"));
            Cap = cap;
            OnPropertyChanged(nameof(Cap)); // re-select after the list was rebuilt

            Api = PerformancePlanner.ReadApi(flags.Flags);
            Msaa = PerformancePlanner.ReadInt(flags.Flags, PerformancePlanner.ForceMsaa);
            Textures = PerformancePlanner.ReadInt(flags.Flags, PerformancePlanner.TextureOverride);

            Status.Clear();
            foreach (var line in PerformancePlanner.BuildStatus(current.FramerateCap, display.RefreshRate, robloxSettings.Exists,
                         launcher.IsRobloxRunning(), PlayUsesNexstrapBuild, flags.Flags))
                Status.Add(line);
            OnPropertyChanged(nameof(PlayUsesNexstrapBuild));
        }
        finally
        {
            _loading = false;
        }
    }
}
