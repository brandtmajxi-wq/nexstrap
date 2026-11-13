using Nexstrap.Models;
using Nexstrap.Services.Clips;
using Xunit;

namespace Nexstrap.Tests;

public class ClipTests
{
    private static List<TimeSpan> Pieces(params int[] seconds) => seconds.Select(s => TimeSpan.FromSeconds(s)).ToList();

    [Fact]
    public void Picks_the_newest_pieces_that_make_ninety_seconds()
    {
        var pieces = Pieces(10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 4);
        var picked = ClipRecorder.PickPieces(pieces, ClipRecorder.ClipLength, p => p);

        Assert.Equal(10, picked.Count); // 4 + 9 × 10 = 94 s, trimmed to 90 when saved
        Assert.Equal(TimeSpan.FromSeconds(4), picked[^1]);
        Assert.True(picked.Aggregate(TimeSpan.Zero, (a, b) => a + b) >= ClipRecorder.ClipLength);
    }

    [Fact]
    public void Uses_everything_when_less_than_ninety_seconds_was_recorded()
    {
        var pieces = Pieces(10, 10, 3);
        Assert.Equal(pieces, ClipRecorder.PickPieces(pieces, ClipRecorder.ClipLength, p => p));
    }

    [Fact]
    public void Nothing_recorded_gives_nothing()
    {
        Assert.Empty(ClipRecorder.PickPieces(new List<TimeSpan>(), ClipRecorder.ClipLength, p => p));
    }

    [Fact]
    public void Clips_are_off_by_default_and_save_a_minute_with_ctrl_alt_c()
    {
        var launcher = new AppSettings().Launcher;
        Assert.Equal(TimeSpan.FromSeconds(90), ClipRecorder.ClipLength);
        Assert.False(launcher.RecordClips);
        Assert.Equal(60, launcher.ClipSeconds);
        Assert.Equal("Ctrl+Alt+C", ClipHotkey.Format(launcher.ClipHotkeyModifiers, launcher.ClipHotkeyKey));
    }

    [Fact]
    public void A_shorter_clip_picks_fewer_pieces()
    {
        var pieces = Pieces(10, 10, 10, 10, 10, 10, 10, 10, 10, 10);
        Assert.Equal(3, ClipRecorder.PickPieces(pieces, TimeSpan.FromSeconds(30), p => p).Count);
        Assert.Equal(6, ClipRecorder.PickPieces(pieces, TimeSpan.FromSeconds(60), p => p).Count);
    }

    [Theory]
    [InlineData(0u, System.Windows.Input.Key.F8, true)]
    [InlineData(ClipHotkey.Shift, System.Windows.Input.Key.F9, true)]
    [InlineData(ClipHotkey.Ctrl | ClipHotkey.Alt, System.Windows.Input.Key.C, true)]
    [InlineData(ClipHotkey.Alt, System.Windows.Input.Key.D1, true)]
    [InlineData(0u, System.Windows.Input.Key.C, false)]       // would break typing "c" everywhere
    [InlineData(ClipHotkey.Shift, System.Windows.Input.Key.C, false)]
    [InlineData(0u, System.Windows.Input.Key.Space, false)]
    [InlineData(ClipHotkey.Ctrl, System.Windows.Input.Key.LeftAlt, false)]
    public void Only_keys_that_dont_get_in_the_way_of_typing(uint modifiers, System.Windows.Input.Key key, bool allowed)
    {
        Assert.Equal(allowed, ClipHotkey.Allowed(modifiers, key));
    }

    [Fact]
    public void Keys_are_shown_readably()
    {
        Assert.Equal("F8", ClipHotkey.Format(0, 0x77));
        Assert.Equal("Ctrl+Shift+1", ClipHotkey.Format(ClipHotkey.Ctrl | ClipHotkey.Shift, 0x31));
    }
}
