namespace Nexstrap.Models;

/// <summary>Who placed a Roblox Player build on disk.</summary>
public enum InstallationSource
{
    Official,
    Nexstrap,
    Bloxstrap,
    Voidstrap,
    Fishstrap
}

/// <summary>A Roblox Player build found on disk.</summary>
public sealed record RobloxInstallation(
    InstallationSource Source,
    string Directory,
    string ExecutablePath,
    string VersionHash,
    string? ClientVersion,
    DateTime LastModified)
{
    public string SourceLabel => Source switch
    {
        InstallationSource.Official => "Official installer",
        _ => Source.ToString()
    };

    /// <summary>"0.745.0.7451137" when known, otherwise the deployment hash.</summary>
    public string DisplayVersion => ClientVersion ?? VersionHash;
}

/// <summary>The program Windows runs for roblox-player: / roblox: links.</summary>
public sealed record ProtocolHandlerInfo(string? Command)
{
    public bool IsRegistered => !string.IsNullOrWhiteSpace(Command);

    public string OwnerName
    {
        get
        {
            if (!IsRegistered) return "Nobody";
            foreach (var name in new[] { "Nexstrap", "Bloxstrap", "Voidstrap", "Fishstrap" })
                if (Command!.Contains(name, StringComparison.OrdinalIgnoreCase)) return name;
            return Command!.Contains("Roblox", StringComparison.OrdinalIgnoreCase) ? "Roblox" : "Another app";
        }
    }
}
