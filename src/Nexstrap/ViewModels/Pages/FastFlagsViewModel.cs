using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public string KindLabel => Kind switch { FastFlagKind.Bool => "Bool", FastFlagKind.Int => "Number", _ => "Text" };

    public string StatusLabel => IsAllowlisted ? "Applied by Roblox" : "Ignored by Roblox";

    public string? Description => Known?.Note ?? Known?.Description
        ?? "Not on Roblox's allowlist, so the Roblox Player ignores it.";

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

    public override string Title => "FastFlags";

    public override string Subtitle => "Roblox client configuration flags, with honest notes on what still works.";

    // ===== Where flags go =====

    private RobloxInstallation? Target => _client.State.Preferred;

    public bool TargetIsNexstrap => Target?.Source == InstallationSource.Nexstrap;

    public string TargetText => Target switch
    {
        null => "No Roblox build found. Install one from Home first.",
        { Source: InstallationSource.Nexstrap } t => $"Written into Nexstrap's Roblox {t.DisplayVersion} each time you press Play.",
        { } t => $"Play currently uses {t.SourceLabel}'s build, and Nexstrap never changes another launcher's files. Install a Nexstrap copy of Roblox from Home and these flags will be applied."
    };

    [RelayCommand]
    private void GoHome() => _navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void OpenAllowlist() => _shell.OpenUrl(FastFlagCatalog.AllowlistUrl);

    public string AllowlistText =>
        $"Since September 2025 Roblox only applies flags on its official allowlist ({FastFlagCatalog.Known.Count(k => k.Allowlisted)} flags, checked {FastFlagCatalog.AllowlistCheckedOn}). " +
        "Anything else is ignored. It's harmless, it just does nothing. Roblox can change the list at any time.";

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
            return Rows.Count == 0 ? "No flags yet" : $"{Rows.Count} flags · {enabled} enabled · {applied} applied by Roblox";
        }
    }

    [ObservableProperty]
    private string _search = "";

    partial void OnSearchChanged(string value) => RowsView.Refresh();

    public IReadOnlyList<Choice<FlagFilter>> FilterChoices { get; } =
    [
        new(FlagFilter.All, "All flags"),
        new(FlagFilter.Enabled, "Enabled"),
        new(FlagFilter.Disabled, "Disabled"),
        new(FlagFilter.AppliedByRoblox, "Applied by Roblox"),
        new(FlagFilter.IgnoredByRoblox, "Ignored by Roblox")
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
                return (known.Allowlisted ? "Applied by Roblox. " : "Ignored by Roblox. ") + (known.Note ?? known.Description);
            return FastFlagCatalog.KindOf(NewName) is { } kind
                ? $"{kind} flag. Not on Roblox's allowlist, so it will be ignored."
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
        var path = _files.OpenFile("Import FastFlags", JsonFilter);
        if (path is null) return;
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't read that file", ex.Message, DialogKind.Error);
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
            _dialogs.Show("Couldn't read the flags", ex.Message, DialogKind.Error);
            return;
        }
        if (string.IsNullOrWhiteSpace(json))
        {
            StatusMessage = $"{target.SourceLabel}'s build has no FastFlags file, so there's nothing to import.";
            return;
        }
        RunImport(json, $"{target.SourceLabel}'s build");
    }

    private void RunImport(string json, string source)
    {
        try
        {
            var result = _flags.Import(json, replaceExisting: false);
            StatusMessage = $"Imported from {source}: {result.Added} added, {result.Updated} updated" +
                            (result.Skipped.Count > 0 ? $", {result.Skipped.Count} skipped." : ".") + " A backup was made first.";
            if (result.Skipped.Count > 0)
                _dialogs.Show("Some flags were skipped", $"{result.Skipped.Count} entries weren't valid FastFlags and were left out.",
                    DialogKind.Warning, string.Join(Environment.NewLine, result.Skipped));
        }
        catch (InvalidDataException ex)
        {
            _dialogs.Show("Couldn't import", ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void Export()
    {
        var path = _files.SaveFile("Export FastFlags", JsonFilter, "ClientAppSettings.json");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, _flags.Export());
            StatusMessage = $"Exported {Rows.Count(r => r.Enabled)} enabled flags to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't export", ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void Restore(FlagBackup backup)
    {
        if (!_dialogs.Confirm("Restore this backup?",
                $"Your flags will be replaced with the {backup.FlagCount} flags saved on {backup.CreatedLocal:g}. Your current flags are backed up first.",
                "Restore"))
            return;
        try
        {
            _flags.Restore(backup);
            StatusMessage = $"Restored the backup from {backup.CreatedLocal:g}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.Show("Couldn't restore", ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        if (!_dialogs.Confirm("Remove all FastFlags?",
                "Every flag will be removed and Roblox will use its defaults next launch. A backup is made first so you can restore them.", "Remove all"))
            return;
        _flags.Reset();
        StatusMessage = "All flags removed. A backup was made first.";
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
