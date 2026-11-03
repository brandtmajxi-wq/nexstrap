namespace Nexstrap.Models;

/// <summary>Value type implied by a FastFlag's name prefix.</summary>
public enum FastFlagKind
{
    Bool,
    Int,
    String
}

/// <summary>One user-configured flag. Disabled entries are kept but not written for Roblox.</summary>
public sealed class FastFlagEntry
{
    public string Name { get; set; } = "";

    /// <summary>Raw text as typed ("True", "60", "abc"); validated against the name's kind.</summary>
    public string Value { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

/// <summary>Persisted as FastFlags.json in Nexstrap's data folder.</summary>
public sealed class FastFlagDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<FastFlagEntry> Flags { get; set; } = [];
}

/// <summary>A flag Nexstrap can describe. <see cref="Allowlisted"/> reflects Roblox's published allowlist.</summary>
public sealed record KnownFlag(
    string Name,
    FastFlagKind Kind,
    string Category,
    string Description,
    bool Allowlisted,
    string? SuggestedValue = null,
    string? Note = null);
