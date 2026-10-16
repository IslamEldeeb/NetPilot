namespace NetPilot.Core.Backup;

/// <summary>
/// Pure plan of what a backup must contain for a given data directory:
/// the LiteDB file and every file of the Data Protection key ring.
/// Never throws; failures are reported via <see cref="BackupPlanResult.Error"/>.
/// </summary>
public static class BackupPlan
{
    public const string DatabaseFileName = "netpilot.db";
    // LiteDB keeps recent commits in this journal until it checkpoints; restoring without it can lose them.
    public const string JournalFileName = "netpilot-log.db";
    public const string KeysDirectoryName = "keys";
    public const string ManifestEntryName = "manifest.json";

    public const string MissingKeysMessage =
        "Data Protection keys not found; a database backup without keys cannot restore the router password or push keys";

    public static BackupPlanResult Create(string dataDir)
    {
        var databasePath = Path.Combine(dataDir, DatabaseFileName);
        var keysDirectory = Path.Combine(dataDir, KeysDirectoryName);

        if (!File.Exists(databasePath))
            return new BackupPlanResult(false, $"Database file not found: {databasePath}", []);

        if (!Directory.Exists(keysDirectory))
            return new BackupPlanResult(false, MissingKeysMessage, []);

        var files = new List<string> { DatabaseFileName };
        if (File.Exists(Path.Combine(dataDir, JournalFileName)))
            files.Add(JournalFileName);
        files.AddRange(Directory.GetFiles(keysDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{KeysDirectoryName}/{Path.GetFileName(path)}"));

        return new BackupPlanResult(true, null, files);
    }
}

public sealed record BackupPlanResult(bool IsValid, string? Error, IReadOnlyList<string> Files);
