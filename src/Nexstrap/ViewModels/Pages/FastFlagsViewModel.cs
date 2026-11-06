using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

public enum FlagFilter
{
    All,
    Enabled,
    Disabled,
    AppliedByRoblox,
    IgnoredByRoblox
}

/// <summary>One editable row in the flag list.</summary>
public sealed partial class FlagRow : ObservableObject
{
    private readonly IFastFlagService _service;

    public FlagRow(FastFlagEntry entry, IFastFlagService service)
    {
        _service = service;
        Name = entry.Name;
        Kind = FastFlagCatalog.KindOf(entry.Name) ?? FastFlagKind.String;
        Known = FastFlagCatalog.Find(entry.Name);
        _enabled = entry.Enabled;
        _editValue = entry.Value;
    }

    public string Name { get; }

    public FastFlagKind Kind { get; }

    public KnownFlag? Known { get; }

    public bool IsAllowlisted => Known?.Allowlisted == true;

    public bool IsBool => Kind == FastFlagKind.Bool;

    public string KindLabel => Kind switch { FastFlagKind.Bool => Loc.Get("FastFlags.Bool"), FastFlagKind.Int => Loc.Get("FastFlags.Number"), _ => Loc.Get("LaunchScreen.Text") };

    public string StatusLabel => IsAllowlisted ? Loc.Get("FastFlags.AppliedByRoblox") : Loc.Get("FastFlags.IgnoredByRoblox");

    public string? Description => Known?.LocalizedNote ?? Known?.LocalizedDescription
        ?? Loc.Get("FastFlags.NotOnRobloxsAllowlistSoThe");

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _service.SetEnabled(Name, value);

    [ObservableProperty]
    private string _editValue;

    [ObservableProperty]
    private string? _valueError;

    public bool BoolValue
    {
        get => bool.TryParse(EditValue, out var b) && b;
        set
        {
            EditValue = value ? "True" : "False";
            CommitValue();
        }
    }

    [RelayCommand]
    private void CommitValue()
    {
        ValueError = _service.Set(Name, EditValue, Enabled);
        if (ValueError is null) EditValue = FastFlagCatalog.Normalise(Name, EditValue);
        OnPropertyChanged(nameof(BoolValue));
    }
}

/// <summary>A catalog entry the user can add with one click.</summary>
public sealed class KnownFlagRow(KnownFlag flag, bool isAdded)
{
    public KnownFlag Flag { get; } = flag;
    public bool IsAdded { get; } = isAdded;
    public bool CanAdd => !IsAdded;
}

public sealed partial class FastFlagsViewModel : PageViewModel
{
    public IReadOnlyList<FastFlagPreset> Presets => FastFlagPresets.All;

    [ObservableProperty]
    private string? _presetMessage;

    [RelayCommand]
    private void ApplyFlagPreset(FastFlagPreset preset)
    {
        PresetMessage = FastFlagPresets.Apply(_flags, preset) ?? Loc.Format("FastFlags.Preset.Added", preset.Name);
    }

    private const string JsonFilter = "JSON (*.json)|*.json";

    private readonly IFastFlagService _flags;
    private readonly IRobloxClientService _client;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly IShellService _shell;
    private readonly INavigationService _navigation;
    private bool _rebuilding;

    public FastFlagsViewModel(IFastFlagService flags, IRobloxClientService client, IDialogService dialogs,
        IFileDialogService files, IShellService shell, INavigationService navigation)
    {
        _flags = flags;
        _client = client;
        _dialogs = dialogs;
        _files = files;
        _shell = shell;
        _navigation = navigation;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => Matches((FlagRow)o);

        flags.Changed += (_, _) => Rebuild();
        client.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(OnClientChanged);
        Rebuild();
    }

    public override string Title => Loc.Get("Nav.FastFlags");

    public override string Subtitle => Loc.Get("Page.FastFlags");

    // ===== Where flags go =====

    private RobloxInstallation? Target => _client.State.Preferred;

    public bool TargetIsNexstrap => Target?.Source == InstallationSource.Nexstrap;

    public string TargetText => Target switch
    {
        null => Loc.Get("FastFlags.NoRobloxBuildFoundInstallOne"),
        { Source: InstallationSource.Nexstrap } t => Loc.Format("FastFlags.WrittenIntoNexstrapsRobloxEachTime", t.DisplayVersion),
        { } t => Loc.Format("FastFlags.PlayCurrentlyUsesSBuildAnd", t.SourceLabel)
    };

    [RelayCommand]
    private void GoHome() => _navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void OpenAllowlist() => _shell.OpenUrl(FastFlagCatalog.AllowlistUrl);

    public string AllowlistText =>
        Loc.Format("FastFlags.SinceSeptember2025RobloxOnlyApplies", FastFlagCatalog.Known.Count(k => k.Allowlisted), DateTime.Parse(FastFlagCatalog.AllowlistCheckedOn, System.Globalization.CultureInfo.InvariantCulture).ToString("d"));

    // ===== Flag list =====

    public ObservableCollection<FlagRow> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public bool HasFlags => Rows.Count > 0;

    public string CountText
    {
        get
        {
            var enabled = Rows.Count(r => r.Enabled);
            var applied = Rows.Count(r => r.Enabled && r.IsAllowlisted);
            return Rows.Count == 0 ? Loc.Get("FastFlags.NoFlagsYet") : Loc.Format("FastFlags.FlagsEnabledAppliedByRoblox", Rows.Count, enabled, applied);
        }
    }

    [ObservableProperty]
    private string _search = "";

    partial void OnSearchChanged(string value) => RowsView.Refresh();

    public IReadOnlyList<Choice<FlagFilter>> FilterChoices { get; } =
    [
        new(FlagFilter.All, () => Loc.Get("FastFlags.AllFlags")),
        new(FlagFilter.Enabled, () => Loc.Get("FastFlags.Enabled")),
        new(FlagFilter.Disabled, () => Loc.Get("FastFlags.Disabled")),
        new(FlagFilter.AppliedByRoblox, () => Loc.Get("FastFlags.AppliedByRoblox")),
        new(FlagFilter.IgnoredByRoblox, () => Loc.Get("FastFlags.IgnoredByRoblox"))
    ];

    [ObservableProperty]
    private FlagFilter _filter = FlagFilter.All;

    partial void OnFilterChanged(FlagFilter value) => RowsView.Refresh();

    private bool Matches(FlagRow row)
    {
        if (!string.IsNullOrWhiteSpace(Search) && !row.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        return Filter switch
        {
            FlagFilter.Enabled => row.Enabled,
            FlagFilter.Disabled => !row.Enabled,
            FlagFilter.AppliedByRoblox => row.IsAllowlisted,
            FlagFilter.IgnoredByRoblox => !row.IsAllowlisted,
            _ => true
        };
    }

    [RelayCommand]
    private void Delete(FlagRow row) => _flags.Remove(row.Name);

    // ===== Add =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameHint))]
    private string _newName = "";

    [ObservableProperty]
    private string _newValue = "";

    [ObservableProperty]
    private string? _addError;

    public string? NewNameHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewName)) return null;
            if (FastFlagCatalog.Find(NewName) is { } known)
                return (known.Allowlisted ? Loc.Get("FastFlags.AppliedByRoblox2") : Loc.Get("FastFlags.IgnoredByRoblox2")) + (known.LocalizedNote ?? known.LocalizedDescription);
            return FastFlagCatalog.KindOf(NewName) is { } kind
                ? Loc.Format("FastFlags.FlagNotOnRobloxsAllowlistSo", kind)
                : null;
        }
    }

    partial void OnNewNameChanged(string value)
    {
        AddError = null;
        if (string.IsNullOrWhiteSpace(NewValue) && FastFlagCatalog.Find(value)?.SuggestedValue is { } suggested)
            NewValue = suggested;
    }

    [RelayCommand]
    private void Add()
    {
        AddError = _flags.Set(NewName, NewValue);
        if (AddError is not null) return;
        NewName = "";
        NewValue = "";
    }

    // ===== Catalog =====

    public ObservableCollection<KnownFlagRow> KnownFlags { get; } = [];

    [RelayCommand]
    private void AddKnown(KnownFlagRow row) => _flags.Set(row.Flag.Name, row.Flag.SuggestedValue ?? "");

    // ===== Import / export / backups / reset =====

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<FlagBackup> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [RelayCommand]
    private void ImportFile()
    {
        var path = _files.OpenFile(Loc.Get("FastFlags.ImportFastFlags"), JsonFilter);
        if (path is null) return;
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntReadThatFile"), ex.Message, DialogKind.Error);
            return;
        }
        RunImport(json, Path.GetFileName(path));
    }

    public bool CanImportFromTarget => Target is not null;

    [RelayCommand]
    private void ImportFromInstallation()
    {
        if (Target is not { } target) return;
        string? json;
        try { json = _flags.ReadFromInstallation(target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntReadTheFlags"), ex.Message, DialogKind.Error);
            return;
        }
        if (string.IsNullOrWhiteSpace(json))
        {
            StatusMessage = Loc.Format("FastFlags.SBuildHasNoFastFlagsFile", target.SourceLabel);
            return;
        }
        RunImport(json, $"{target.SourceLabel}'s build");
    }

    private void RunImport(string json, string source)
    {
        try
        {
            var result = _flags.Import(json, replaceExisting: false);
            StatusMessage = result.Skipped.Count > 0 ? Loc.Format("FastFlags.ImportedFromSkipped", source, result.Added, result.Updated, result.Skipped.Count) : Loc.Format("FastFlags.ImportedFrom", source, result.Added, result.Updated);
            if (result.Skipped.Count > 0)
                _dialogs.Show(Loc.Get("FastFlags.SomeFlagsWereSkipped"), Loc.Format("FastFlags.EntriesWerentValidFastFlagsAndWere", result.Skipped.Count),
                    DialogKind.Warning, string.Join(Environment.NewLine, result.Skipped));
        }
        catch (InvalidDataException ex)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntImport"), ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void Export()
    {
        var path = _files.SaveFile(Loc.Get("FastFlags.ExportFastFlags"), JsonFilter, "ClientAppSettings.json");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, _flags.Export());
            StatusMessage = Loc.Format("FastFlags.ExportedEnabledFlagsTo", Rows.Count(r => r.Enabled), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntExport"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void Restore(FlagBackup backup)
    {
        if (!_dialogs.Confirm(Loc.Get("FastFlags.RestoreThisBackup"),
                Loc.Format("FastFlags.YourFlagsWillBeReplacedWith", backup.FlagCount, backup.CreatedLocal),
                Loc.Get("FastFlags.Restore")))
            return;
        try
        {
            _flags.Restore(backup);
            StatusMessage = Loc.Format("FastFlags.RestoredTheBackupFrom", backup.CreatedLocal);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntRestore"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        if (!_dialogs.Confirm(Loc.Get("FastFlags.RemoveAllFastFlags"),
                Loc.Get("FastFlags.EveryFlagWillBeRemovedAnd"), Loc.Get("FastFlags.RemoveAll")))
            return;
        _flags.Reset();
        StatusMessage = Loc.Get("FastFlags.AllFlagsRemovedABackupWas");
    }

    // ===== Plumbing =====

    public override void OnNavigatedTo()
    {
        RefreshBackups();
        OnClientChanged();
        // Opening this page first (e.g. --page FastFlags) happens before Home has scanned for Roblox.
        if (_client.State.Installations.Count == 0)
            _ = _client.RefreshAsync(checkLatest: false);
    }

    private void Rebuild()
    {
        if (_rebuilding) return;

        // Edits to an existing row (value, toggle) keep the rows so focus isn't lost mid-typing;
        // only additions/removals (or bulk changes) recreate them.
        if (KnownFlags.Count > 0 && RowsMatchStoredFlags())
        {
            OnPropertyChanged(nameof(CountText));
            RefreshBackups();
            return;
        }

        _rebuilding = true;
        try
        {
            Rows.Clear();
            foreach (var entry in _flags.Flags.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new FlagRow(entry, _flags));

            KnownFlags.Clear();
            foreach (var known in FastFlagCatalog.Known)
                KnownFlags.Add(new KnownFlagRow(known, _flags.Flags.Any(f => f.Name.Equals(known.Name, StringComparison.Ordinal))));

            RefreshBackups();
            OnPropertyChanged(nameof(HasFlags));
            OnPropertyChanged(nameof(CountText));
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>True when every row already shows exactly what's stored (same names, toggles and values).</summary>
    private bool RowsMatchStoredFlags()
    {
        var stored = _flags.Flags.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (stored.Count != Rows.Count) return false;
        for (var i = 0; i < stored.Count; i++)
        {
            var row = Rows[i];
            if (!row.Name.Equals(stored[i].Name, StringComparison.Ordinal) || row.Enabled != stored[i].Enabled) return false;
            var shown = FastFlagCatalog.ValidateValue(row.Name, row.EditValue) is null ? FastFlagCatalog.Normalise(row.Name, row.EditValue) : row.EditValue;
            if (shown != stored[i].Value && row.ValueError is null) return false;
        }
        return true;
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _flags.ListBackups().Take(8)) Backups.Add(b);
        OnPropertyChanged(nameof(HasBackups));
    }

    private void OnClientChanged()
    {
        OnPropertyChanged(nameof(TargetIsNexstrap));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(CanImportFromTarget));
    }
}
