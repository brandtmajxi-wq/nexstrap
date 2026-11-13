using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
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
    INavigationService navigation,
    Nexstrap.Services.Configuration.ISettingsService settings) : PageViewModel
{
    private bool _loading;

    // ===== Low-end PC mode =====

    public bool LowEndMode
    {
        get => settings.Current.Launcher.LowEndMode;
        set
        {
            if (value == settings.Current.Launcher.LowEndMode) return;
            settings.Current.Launcher.LowEndMode = value;
            settings.Save();
            Nexstrap.Core.LowEndMode.Set(value);
            OnPropertyChanged();
            Message = value ? Loc.Get("Performance.LowEnd.On") : Loc.Get("Performance.LowEnd.Off");
            if (value) OfferLighterRoblox();
        }
    }

    /// <summary>Asks once, when the mode is switched on, whether Roblox should get lighter settings too.</summary>
    private void OfferLighterRoblox()
    {
        var saver = PerformancePlanner.Presets.First(p => p.Key == "Performance.Preset.PowerSaver");
        var lowEnd = FastFlagPresets.All.First(p => p.Key == "FastFlags.Preset.LowEnd");
        if (!dialogs.Confirm(Loc.Get("Performance.LowEnd.AskTitle"), Loc.Format("Performance.LowEnd.AskText", saver.Name, lowEnd.Name),
                Loc.Get("Performance.LowEnd.AskYes"), Loc.Get("Performance.LowEnd.AskNo")))
            return;

        var flagProblem = FastFlagPresets.Apply(flags, lowEnd);
        ApplyPreset(saver);
        if (flagProblem is not null) Message = flagProblem;
    }

    public override string Title => Loc.Get("Nav.Performance");

    public override string Subtitle => Loc.Get("Page.Performance");

    // ===== Status =====

    public ObservableCollection<StatusLine> Status { get; } = [];

    public string DisplayText => display.RefreshRate is { } hz
        ? Loc.Format("Performance.PrimaryDisplayHzRecommendedCapFPS", hz, PerformancePlanner.RecommendedCap(hz))
        : Loc.Get("Performance.WindowsDidntReportYourDisplaysRefresh");

    public bool PlayUsesNexstrapBuild => client.State.Preferred?.Source == InstallationSource.Nexstrap;

    [ObservableProperty]
    private string? _message;

    // ===== FPS cap =====

    public ObservableCollection<Choice<int>> CapChoices { get; } = [];

    [ObservableProperty]
    private int _cap;

    partial void OnCapChanged(int value)
    {
        if (!_loading) SaveToRoblox(new RobloxUserSettings { FramerateCap = value }, Loc.Format("Performance.FrameRateCapSetToFPS", value));
    }

    [ObservableProperty]
    private bool _noLimit;

    partial void OnNoLimitChanged(bool value)
    {
        if (_loading) return;
        var cap = value ? PerformancePlanner.NoLimitCap : PerformancePlanner.RecommendedCap(display.RefreshRate);
        SaveToRoblox(new RobloxUserSettings { FramerateCap = cap },
            value ? Loc.Get("Performance.NoLimit.Set") : Loc.Format("Performance.FrameRateCapSetToFPS", cap));
    }

    // ===== Presets =====

    public IReadOnlyList<PerformancePreset> Presets => PerformancePlanner.Presets;

    [RelayCommand]
    private void ApplyPreset(PerformancePreset preset)
    {
        var cap = PerformancePlanner.ResolveCap(preset, display.RefreshRate);
        SaveToRoblox(new RobloxUserSettings { FramerateCap = cap, SavedQualityLevel = preset.Quality },
            Loc.Format("Performance.PresetApplied", preset.Name, cap,
                preset.Quality == 0 ? Loc.Get("Performance.QualityAutomatic") : Loc.Format("Performance.QualityOutOf10", preset.Quality)));
    }

    private void SaveToRoblox(RobloxUserSettings changes, string success)
    {
        try
        {
            robloxSettings.Save(changes);
            Message = success + Loc.Get("Performance.RobloxUsesThisNextTimeIt");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            dialogs.Show(Loc.Get("Performance.CouldntChangeRobloxsSettings"), ex.Message, DialogKind.Warning);
        }
        Load();
    }

    // ===== Graphics preferences (allowlisted FastFlags) =====

    public IReadOnlyList<Choice<GraphicsApi>> ApiChoices { get; } =
    [
        new(GraphicsApi.Automatic, () => Loc.Get("Performance.Automatic")),
        new(GraphicsApi.Direct3D11, "Direct3D 11"),
        new(GraphicsApi.Vulkan, "Vulkan"),
        new(GraphicsApi.OpenGL, "OpenGL")
    ];

    public IReadOnlyList<Choice<int>> MsaaChoices { get; } =
    [
        new(-1, () => Loc.Get("Performance.Automatic")), new(0, () => Loc.Get("Performance.Off")), new(1, "1x"), new(2, "2x"), new(4, "4x"), new(8, "8x")
    ];

    public IReadOnlyList<Choice<int>> TextureChoices { get; } =
    [
        new(-1, () => Loc.Get("Performance.Automatic")), new(0, () => Loc.Get("Performance.Lowest")), new(1, () => Loc.Get("Performance.Low")), new(2, () => Loc.Get("Performance.Medium")), new(3, () => Loc.Get("Performance.Highest"))
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

            NoLimit = PerformancePlanner.IsNoLimit(current.FramerateCap);
            var cap = NoLimit ? PerformancePlanner.RecommendedCap(display.RefreshRate) : current.FramerateCap ?? PerformancePlanner.RecommendedCap(display.RefreshRate);
            CapChoices.Clear();
            foreach (var fps in PerformancePlanner.StandardCaps) CapChoices.Add(new Choice<int>(fps, $"{fps} FPS"));
            if (!PerformancePlanner.StandardCaps.Contains(cap)) CapChoices.Add(new Choice<int>(cap, Loc.Format("Performance.FPSCurrent", cap)));
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
