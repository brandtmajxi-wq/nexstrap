using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Roblox;

public enum RobloxActivityKind
{
    /// <summary>Process started, menu not shown yet.</summary>
    Starting,
    /// <summary>In the Roblox app (home, avatar, friends…), not in a game.</summary>
    Menu,
    InGame
}

public sealed record RobloxActivity(RobloxActivityKind Kind, DateTime SinceUtc, long? PlaceId = null, long? UniverseId = null,
    string? JobId = null, long? UserId = null);

/// <summary>
/// Reads the Roblox Player's own log lines (verified against 0.745 logs):
///   "[FLog::SingleSurfaceApp] setStage: (stage:LuaApp)"          → menu
///   "[FLog::Output] ! Joining game '{job}' place {placeId} at …"  → in game
///   "[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:…, universeid:…, …, userid:…"
///   "[FLog::SingleSurfaceApp] leaveUGCGameInternal"               → back to menu
/// </summary>
public static partial class RobloxLogParser
{
    /// <summary>Returns the new activity, or null if the line changes nothing.</summary>
    public static RobloxActivity? Apply(RobloxActivity current, string line)
    {
        if (line.Contains("[FLog::SingleSurfaceApp] setStage: (stage:LuaApp)", StringComparison.Ordinal)
            || line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal))
            return current.Kind == RobloxActivityKind.Menu ? null : new RobloxActivity(RobloxActivityKind.Menu, Timestamp(line));

        var join = JoiningGame().Match(line);
        if (join.Success && long.TryParse(join.Groups[2].Value, out var place))
            return new RobloxActivity(RobloxActivityKind.InGame, Timestamp(line), place, null, join.Groups[1].Value);

        var load = JoinLoadTime().Match(line);
        if (load.Success && current.Kind == RobloxActivityKind.InGame
            && long.TryParse(load.Groups[1].Value, out var loadPlace) && loadPlace == current.PlaceId
            && long.TryParse(load.Groups[2].Value, out var universe))
        {
            long? user = long.TryParse(load.Groups[3].Value, out var u) ? u : current.UserId;
            if (current.UniverseId != universe || current.UserId != user)
                return current with { UniverseId = universe, UserId = user };
        }

        return null;
    }

    internal static DateTime Timestamp(string line)
    {
        var comma = line.IndexOf(',');
        return comma > 0 && DateTime.TryParse(line[..comma], CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : DateTime.UtcNow;
    }

    [GeneratedRegex(@"! Joining game '([0-9a-fA-F-]+)' place (\d+)")]
    private static partial Regex JoiningGame();

    [GeneratedRegex(@"Report game_join_loadtime: placeid:(\d+),.*?universeid:(\d+)(?:.*?userid:(\d+))?")]
    private static partial Regex JoinLoadTime();
}

public interface IRobloxSessionService
{
    /// <summary>True while a Roblox process launched by Nexstrap is running.</summary>
    bool IsActive { get; }

    RobloxActivity? Current { get; }

    /// <summary>Raised on activity changes and when the session ends (Current becomes null). Background thread.</summary>
    event EventHandler? Changed;

    /// <summary>Start following a Roblox process Nexstrap just launched.</summary>
    void Track(int processId, DateTime launchedAtUtc);

    /// <summary>Completes when the tracked session ends (immediately if none).</summary>
    Task WaitForEndAsync();
}

/// <summary>Follows one Roblox Player process and its log file until it exits.</summary>
public sealed class RobloxSessionService(ILogger<RobloxSessionService> logger) : IRobloxSessionService
{
    private static readonly string LogsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    private CancellationTokenSource? _cts;
    private Task _run = Task.CompletedTask;

    public bool IsActive => Current is not null;

    public RobloxActivity? Current { get; private set; }

    public event EventHandler? Changed;

    public void Track(int processId, DateTime launchedAtUtc)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Set(new RobloxActivity(RobloxActivityKind.Starting, launchedAtUtc));
        _run = Task.Run(() => RunAsync(processId, launchedAtUtc, token), token);
    }

    public Task WaitForEndAsync() => _run;

    private async Task RunAsync(int processId, DateTime launchedAtUtc, CancellationToken ct)
    {
        try
        {
            using var process = await ResolveProcessAsync(processId, ct);
            if (process is null)
            {
                logger.LogInformation("Roblox exited before its session could be tracked");
                return;
            }

            var log = await FindLogAsync(launchedAtUtc, ct);
            logger.LogInformation("Tracking Roblox pid {Pid}, log {Log}", process.Id, log is null ? "(none)" : Path.GetFileName(log));

            if (log is null)
            {
                // No log means no menu/game detail; still keep a generic session while Roblox runs.
                Set(new RobloxActivity(RobloxActivityKind.Menu, launchedAtUtc));
                await process.WaitForExitAsync(ct);
                return;
            }

            await TailAsync(log, process, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roblox session tracking failed");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                logger.LogInformation("Roblox session ended");
                Set(null);
            }
        }
    }

    /// <summary>
    /// The process we started may hand over to an already-running client and exit;
    /// in that case follow the client that's still running.
    /// </summary>
    private static async Task<Process?> ResolveProcessAsync(int processId, CancellationToken ct)
    {
        try
        {
            var started = Process.GetProcessById(processId);
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            if (!started.HasExited) return started;
            started.Dispose();
        }
        catch (ArgumentException) { /* already gone */ }

        return Process.GetProcessesByName("RobloxPlayerBeta").OrderByDescending(p => p.StartTime).FirstOrDefault();
    }

    private static async Task<string?> FindLogAsync(DateTime launchedAtUtc, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (Directory.Exists(LogsDir))
            {
                var log = new DirectoryInfo(LogsDir).GetFiles("*_Player_*_last.log")
                    .Where(f => !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                    .Where(f => f.CreationTimeUtc >= launchedAtUtc.AddSeconds(-10))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .FirstOrDefault();
                if (log is not null) return log.FullName;
            }
            await Task.Delay(1000, ct);
        }
        return null;
    }

    private async Task TailAsync(string path, Process process, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var pending = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var chunk = await reader.ReadToEndAsync(ct);
            if (chunk.Length > 0)
            {
                pending.Append(chunk);
                var text = pending.ToString();
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    foreach (var line in text[..lastNewline].Split('\n'))
                        if (Current is { } current && RobloxLogParser.Apply(current, line) is { } next)
                        {
                            logger.LogInformation("Roblox activity: {Kind} {Place}", next.Kind, next.PlaceId);
                            Set(next);
                        }
                    pending.Remove(0, lastNewline + 1);
                }
            }
            else if (process.HasExited)
            {
                return;
            }
            else
            {
                await Task.Delay(750, ct);
            }
        }
    }

    private void Set(RobloxActivity? activity)
    {
        Current = activity;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
