using System.Reflection;
using System.Text.Json;

namespace NetPilot.Core.Backup;

/// <summary>
/// One file captured in a backup, identified by its zip entry name.
/// </summary>
public sealed record BackupFileEntry(string Name, long SizeBytes, string Sha256);

/// <summary>
/// Describes the contents of a backup archive. Stored as manifest.json inside the zip.
/// </summary>
public sealed record BackupManifest
{
    public const int CurrentFormatVersion = 1;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string AppVersion { get; init; } = AppVersionValue;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public IReadOnlyList<BackupFileEntry> Files { get; init; } = [];

    internal static string AppVersionValue =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? "unknown";
}

public static class BackupManifestJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(BackupManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    public static BackupManifest Deserialize(string json) =>
        JsonSerializer.Deserialize<BackupManifest>(json, Options)
        ?? throw new InvalidDataException("The backup manifest is empty.");
}
