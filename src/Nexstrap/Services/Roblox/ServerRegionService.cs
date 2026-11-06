using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Roblox;

public enum RegionSearchOutcome { Found, NotFound, Failed }

/// <summary>One step of a search; <paramref name="Landed"/> is empty before the join finishes, and null when the location is unknown.</summary>
public sealed record RegionSearchProgress(int Attempt, int Total, bool Joined, string? Landed);

public interface IServerRegionService
{
    /// <summary>True while a search is joining servers.</summary>
    bool IsSearching { get; }

    /// <summary>Location of the server you're on, or null when not in a game or it isn't known.</summary>
    string? CurrentRegion { get; }

    /// <summary>Location of a server you've been on since Nexstrap opened.</summary>
    string? KnownRegion(string serverId);

    /// <summary>
    /// Joins public servers one at a time until one is in <paramref name="region"/>. Cancelling leaves you on the current server.
    /// Roblox's server list has no locations, and asking before joining needs your Roblox sign-in, which Nexstrap never touches.
    /// </summary>
    Task<RegionSearchOutcome> FindAsync(long placeId, string? name, string region, IProgress<RegionSearchProgress> progress, CancellationToken ct);

    /// <summary>May be raised on a background thread.</summary>
    event EventHandler? Changed;
}

/// <summary>Reads each server's location from the address Roblox writes to its own log.</summary>
public sealed class ServerRegionService : IServerRegionService
{
    internal const int MaxTries = 8;

    private readonly IRobloxSessionService _session;
    private readonly IRobloxLauncher _launcher;
    private readonly ILaunchCoordinator _launch;
    private readonly IServerBrowserService _servers;
    private readonly ILogger<ServerRegionService> _logger;
    private readonly Dictionary<string, string> _known = new(StringComparer.OrdinalIgnoreCase);

    public ServerRegionService(IRobloxSessionService session, IRobloxLauncher launcher, ILaunchCoordinator launch,
        IServerBrowserService servers, ILogger<ServerRegionService> logger)
    {
        _session = session;
        _launcher = launcher;
        _launch = launch;
        _servers = servers;
        _logger = logger;
        session.Changed += (_, _) => Learn(_session.Current);
    }

    /// <summary>Roblox can take a while to start and load a game.</summary>
    internal TimeSpan JoinTimeout { get; init; } = TimeSpan.FromSeconds(75);

    internal TimeSpan PollDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public event EventHandler? Changed;

    public bool IsSearching { get; private set; }

    public string? CurrentRegion =>
        _session.Current is { Kind: RobloxActivityKind.InGame, ServerAddress: { } address } ? RobloxRegions.Find(address) : null;

    public string? KnownRegion(string serverId)
    {
        lock (_known) return _known.GetValueOrDefault(serverId);
    }

    private void Learn(RobloxActivity? activity)
    {
        if (activity is { Kind: RobloxActivityKind.InGame, JobId: { } job } && RobloxRegions.Find(activity.ServerAddress) is { } region)
            lock (_known) _known[job] = region;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<RegionSearchOutcome> FindAsync(long placeId, string? name, string region, IProgress<RegionSearchProgress> progress, CancellationToken ct)
    {
        IsSearching = true;
        try { return await SearchAsync(placeId, name, region, progress, ct); }
        finally { IsSearching = false; }
    }

    private async Task<RegionSearchOutcome> SearchAsync(long placeId, string? name, string region, IProgress<RegionSearchProgress> progress, CancellationToken ct)
    {
        if (_session.Current?.PlaceId == placeId && CurrentRegion == region) return RegionSearchOutcome.Found;

        var page = await _servers.GetServersAsync(placeId, mostPlayersFirst: true, excludeFull: true, ct: ct);
        var candidates = Candidates(page.Servers, region);
        var total = Math.Min(MaxTries, candidates.Count);

        for (var attempt = 1; attempt <= total; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new RegionSearchProgress(attempt, total, false, null));

            var previousServer = _session.Current?.JobId;
            if (_launcher.IsRobloxRunning() && !await Task.Run(() => _launcher.CloseRoblox(TimeSpan.FromSeconds(10)), ct))
            {
                _logger.LogWarning("Roblox didn't close for the region search");
                return RegionSearchOutcome.Failed;
            }

            var result = await _launch.HopAsync(placeId, name, candidates[attempt - 1].Id);
            if (result is not { Success: true }) return RegionSearchOutcome.Failed;

            if (await WaitForServerAsync(placeId, previousServer, ct) is not { } address)
            {
                _logger.LogInformation("Region search: no server address after {Timeout}", JoinTimeout);
                continue;
            }

            var landed = RobloxRegions.Find(address);
            _logger.LogInformation("Region search: try {Attempt} landed in {Region}", attempt, landed ?? "an unknown location");
            if (landed == region) return RegionSearchOutcome.Found;
            progress.Report(new RegionSearchProgress(attempt, total, true, landed));
        }
        return RegionSearchOutcome.NotFound;
    }

    /// <summary>Servers already known to be elsewhere are skipped, and ones known to match go first.</summary>
    internal List<GameServer> Candidates(IReadOnlyList<GameServer> servers, string region) =>
        servers.Where(s => !s.IsFull && (KnownRegion(s.Id) is not { } known || known == region))
            .OrderByDescending(s => KnownRegion(s.Id) == region)
            .ToList();

    /// <summary>Ignores the server just left, which can still be showing for a moment after Roblox closes.</summary>
    private async Task<string?> WaitForServerAsync(long placeId, string? previousServer, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + JoinTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_session.Current is { Kind: RobloxActivityKind.InGame, ServerAddress: { } address } current && current.PlaceId == placeId && current.JobId != previousServer)
                return address;
            await Task.Delay(PollDelay, ct);
        }
        return null;
    }
}
