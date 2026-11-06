using System.IO;
using System.Text;

namespace Nexstrap.Core;

/// <summary>Reads the end of a log file, even while Roblox or Nexstrap is still writing to it.</summary>
public static class LogReader
{
    public const int MaxLines = 3000;
    private const int MaxBytes = 4 * 1024 * 1024;

    public static IReadOnlyList<string> Tail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var partial = stream.Length > MaxBytes;
        if (partial) stream.Seek(-MaxBytes, SeekOrigin.End);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (partial && lines.Count > 0) lines.RemoveAt(0); // starts partway through a line
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Count > MaxLines ? lines.GetRange(lines.Count - MaxLines, MaxLines) : lines;
    }

    public static IReadOnlyList<string> Filter(IReadOnlyList<string> lines, string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? lines
            : lines.Where(l => l.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
}
