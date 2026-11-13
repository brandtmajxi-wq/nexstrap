using System.IO;
using Microsoft.Extensions.Logging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Nexstrap.Services.Clips;

/// <summary>
/// Keeps the last minute and a half of one window (Roblox) and the PC's sound, ready to save.
/// It records in 10-second pieces and throws away the ones that are too old, so it never fills the disk.
/// Saving joins the newest pieces and trims them to exactly 90 seconds.
/// </summary>
internal sealed class ClipRecorder : IDisposable
{
    /// <summary>The longest clip you can save; the recorder always keeps this much so the length can change any time.</summary>
    public static readonly TimeSpan ClipLength = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan PieceLength = TimeSpan.FromSeconds(10);
    private const int PoolFrames = 4;

    private readonly string _folder;
    private readonly ILogger _logger;
    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly LoopbackAudio _audio;
    private readonly Timer _rotation;
    private readonly object _gate = new();
    private readonly List<ClipSegment> _finished = [];
    private ClipSegment? _current;
    private SizeInt32 _size;
    private int _pieceNumber;
    private bool _saving, _disposed;

    /// <summary>Raised when the window closes.</summary>
    public event EventHandler? Closed;

    private ClipRecorder(IntPtr window, string folder, ILogger logger)
    {
        _folder = folder;
        _logger = logger;
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "piece-*.mp4")) TryDelete(old);

        _device = CaptureInterop.CreateDevice();
        _item = CaptureInterop.CreateItemForWindow(window);
        _item.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        _size = Even(_item.Size);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, PoolFrames, _size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        // Windows 11 can record without the yellow border; Windows 10 always shows it.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            try { _session.IsBorderRequired = false; } catch (Exception) { }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            try { _session.IsCursorCaptureEnabled = true; } catch (Exception) { }
        _audio = new LoopbackAudio(logger);

        Rotate();
        _session.StartCapture();
        _rotation = new Timer(_ => { if (Elapsed() >= PieceLength) Rotate(); }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public static bool IsSupported
    {
        get
        {
            try { return GraphicsCaptureSession.IsSupported(); }
            catch (Exception) { return false; }
        }
    }

    public static ClipRecorder Start(IntPtr window, string folder, ILogger logger) => new(window, folder, logger);

    private static SizeInt32 Even(SizeInt32 size) => new() { Width = Math.Max(2, (size.Width + 1) & ~1), Height = Math.Max(2, (size.Height + 1) & ~1) };

    private TimeSpan Elapsed()
    {
        lock (_gate) return _current is null ? TimeSpan.MaxValue : _current.Duration;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        // The window changed size (for example going full screen): start a new piece at the new size.
        var size = Even(frame.ContentSize);
        if (size.Width != _size.Width || size.Height != _size.Height)
        {
            frame.Dispose();
            _size = size;
            sender.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, PoolFrames, size);
            Rotate();
            return;
        }

        ClipSegment? current;
        lock (_gate) current = _current;
        if (current?.Offer(frame) != true) frame.Dispose(); // the encoder is busy; skipping a frame is fine
    }

    /// <summary>Starts a new piece and lets the current one finish.</summary>
    private void Rotate()
    {
        ClipSegment? old;
        ClipSegment next;
        lock (_gate)
        {
            if (_disposed) return;
            old = _current;
            next = new ClipSegment(Path.Combine(_folder, $"piece-{++_pieceNumber:D5}.mp4"), _size.Width, _size.Height, _audio);
            _current = next;
        }
        _ = StartPieceAsync(next);
        if (old is not null)
        {
            old.End();
            _ = FinishPieceAsync(old);
        }
    }

    private async Task StartPieceAsync(ClipSegment piece)
    {
        try { await piece.StartAsync(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clips: a recording piece couldn't start");
            piece.End();
        }
    }

    private async Task FinishPieceAsync(ClipSegment piece)
    {
        try { await piece.Completion; }
        catch (Exception ex) { _logger.LogWarning(ex, "Clips: a recording piece couldn't be finished"); }

        lock (_gate)
        {
            if (piece.Succeeded) _finished.Add(piece);
            else TryDelete(piece.Path);
            if (!_saving) DropOldPieces();
        }
    }

    /// <summary>Keeps just enough finished pieces to make a full clip, plus one spare.</summary>
    private void DropOldPieces()
    {
        var kept = TimeSpan.Zero;
        for (var i = _finished.Count - 1; i >= 0; i--)
        {
            if (kept >= ClipLength + PieceLength)
            {
                TryDelete(_finished[i].Path);
                _finished.RemoveAt(i);
                continue;
            }
            kept += _finished[i].Duration;
        }
    }

    /// <summary>
    /// Ends the current piece, waits for it, and saves the last <paramref name="length"/> (up to 90 seconds, or as much
    /// as there is) as one MP4. Returns the saved file's path.
    /// </summary>
    public async Task<string> SaveAsync(string outputFolder, string fileName, TimeSpan length)
    {
        if (length <= TimeSpan.Zero || length > ClipLength) length = ClipLength;
        ClipSegment? last;
        lock (_gate)
        {
            _saving = true;
            last = _current;
        }
        try
        {
            Rotate();
            if (last is not null)
            {
                try { await last.Completion; } catch (Exception) { }
                await Task.Delay(200); // let FinishPieceAsync file it
            }

            List<ClipSegment> pieces;
            lock (_gate) pieces = PickPieces(_finished.ToList(), length);
            if (pieces.Count == 0) throw new InvalidOperationException("Nothing has been recorded yet.");

            var composition = new MediaComposition();
            foreach (var piece in pieces)
                composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(piece.Path)));
            var total = pieces.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Duration);
            if (total > length) composition.Clips[0].TrimTimeFromStart = total - length;

            Directory.CreateDirectory(outputFolder);
            var folder = await StorageFolder.GetFolderFromPathAsync(outputFolder);
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
            var newest = pieces[^1];
            var result = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Fast,
                ClipSegment.Profile(newest.Width, newest.Height, _audio.Working ? _audio.SampleRate : 0));
            if (result != TranscodeFailureReason.None)
            {
                try { await file.DeleteAsync(); } catch (Exception) { }
                throw new InvalidOperationException("Windows couldn't put the clip together: " + result);
            }
            return file.Path;
        }
        finally
        {
            lock (_gate)
            {
                _saving = false;
                DropOldPieces();
            }
        }
    }

    /// <summary>The newest pieces that add up to at least <paramref name="length"/>, oldest first.</summary>
    internal static List<T> PickPieces<T>(IReadOnlyList<T> finished, TimeSpan length, Func<T, TimeSpan> duration)
    {
        var picked = new List<T>();
        var total = TimeSpan.Zero;
        for (var i = finished.Count - 1; i >= 0 && total < length; i--)
        {
            picked.Insert(0, finished[i]);
            total += duration(finished[i]);
        }
        return picked;
    }

    private static List<ClipSegment> PickPieces(IReadOnlyList<ClipSegment> finished, TimeSpan length) =>
        PickPieces(finished, length, p => p.Duration);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        ClipSegment? current;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            current = _current;
            _current = null;
        }
        _rotation.Dispose();
        current?.End();
        try { _session.Dispose(); } catch (Exception) { }
        try { _pool.Dispose(); } catch (Exception) { }
        _audio.Dispose();

        // Nothing is kept once Roblox closes: the pieces are only there to make a clip.
        _ = Task.Run(async () =>
        {
            try { if (current is not null) await current.Completion; } catch (Exception) { }
            await Task.Delay(500);
            lock (_gate) foreach (var p in _finished) TryDelete(p.Path);
            try { foreach (var f in Directory.EnumerateFiles(_folder, "piece-*.mp4")) TryDelete(f); } catch (Exception) { }
        });
    }
}
