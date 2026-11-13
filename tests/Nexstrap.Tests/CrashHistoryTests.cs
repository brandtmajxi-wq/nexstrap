using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class CrashHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexstrap-crash-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private CrashHistory Create(MemorySettings settings, bool searching = false) =>
        new(new ManualSession(), settings, new NoHistory(), () => searching, NullLogger.Instance, Path.Combine(_folder, "Crashes.json"));

    [Fact]
    public void Crashes_are_saved_newest_first_with_the_game_name()
    {
        var settings = new MemorySettings();
        settings.Current.Launcher.RecentExperiences.Add(new RecentExperience { PlaceId = 5, Name = "Doors" });
        var history = Create(settings);
        var t = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        history.Add(new RobloxCrash(null, -1, @"C:\logs\a.log"), t);
        history.Add(new RobloxCrash(5, 3, @"C:\logs\b.log"), t.AddMinutes(5));

        Assert.Equal(2, history.Recent.Count);
        Assert.Equal("Doors", history.Recent[0].Name);
        Assert.Equal(3, history.Recent[0].ExitCode);
        Assert.Equal("", history.Recent[1].Name);

        // Saved to disk and read back by the next start.
        Assert.Equal(2, Create(settings).Recent.Count);
    }

    [Fact]
    public void Only_the_last_twenty_are_kept()
    {
        var history = Create(new MemorySettings());
        var t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 25; i++) history.Add(new RobloxCrash(i, 1, "x.log"), t.AddHours(i));
        Assert.Equal(CrashHistory.Keep, history.Recent.Count);
        Assert.Equal(24, history.Recent[0].PlaceId);
        Assert.Equal(5, history.Recent[^1].PlaceId);
    }

    [Fact]
    public void Region_searches_and_clearing_leave_nothing()
    {
        var searching = Create(new MemorySettings(), searching: true);
        searching.Add(new RobloxCrash(1, 1, "x.log"), DateTime.UtcNow);
        Assert.Empty(searching.Recent);

        var history = Create(new MemorySettings());
        history.Add(new RobloxCrash(1, 1, "x.log"), DateTime.UtcNow);
        history.Clear();
        Assert.Empty(history.Recent);
    }

    private sealed class NoHistory : IPlayHistoryService
    {
        public bool IsRecording => false;
        public IReadOnlyList<GamePlayStats> Games => [];
        public IReadOnlyList<PlaySession> Sessions => [];
        public double TotalSeconds => 0;
        public int TotalSessions => 0;
        public double SecondsSince(DateTime utc) => 0;
        public double SecondsFor(long placeId) => 0;
        public void Clear() { }
        public void Flush() { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
