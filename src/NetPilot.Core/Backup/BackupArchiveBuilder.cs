using System.IO.Compression;
using System.Security.Cryptography;

namespace NetPilot.Core.Backup;

/// <summary>
/// Builds a backup zip for a data directory: a consistent copy of the live LiteDB file
/// (copied via a shared-read stream so the running Agent/Web keep working), the Data
/// Protection key ring, and a manifest.json describing the contents.
/// </summary>
public static class BackupArchiveBuilder
{
    public static async Task<BackupArchiveResult> BuildAsync(
        string dataDir, Stream output, CancellationToken cancellationToken = default)
    {
        var plan = BackupPlan.Create(dataDir);
        if (!plan.IsValid)
            return new BackupArchiveResult(false, plan.Error, null);

        var databasePath = Path.Combine(dataDir, BackupPlan.DatabaseFileName);
        var tempDatabasePath = Path.Combine(dataDir, $".backup-db-{Guid.NewGuid():N}.tmp");
        try
        {
            await CopyDatabaseAsync(databasePath, tempDatabasePath, cancellationToken);

            var files = new List<BackupFileEntry>
            {
                await DescribeAsync(tempDatabasePath, BackupPlan.DatabaseFileName, cancellationToken)
            };
            foreach (var entryName in plan.Files.Skip(1))
            {
                var keyPath = Path.Combine(dataDir, entryName.Replace('/', Path.DirectorySeparatorChar));
                files.Add(await DescribeAsync(keyPath, entryName, cancellationToken));
            }

            var manifest = new BackupManifest
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Files = files
            };

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            AddEntry(archive, BackupPlan.DatabaseFileName, tempDatabasePath);
            foreach (var entryName in plan.Files.Skip(1))
            {
                var keyPath = Path.Combine(dataDir, entryName.Replace('/', Path.DirectorySeparatorChar));
                AddEntry(archive, entryName, keyPath);
            }

            var manifestEntry = archive.CreateEntry(BackupPlan.ManifestEntryName);
            await using (var entryStream = manifestEntry.Open())
                await using (var writer = new StreamWriter(entryStream))
                    await writer.WriteAsync(BackupManifestJson.Serialize(manifest));

            return new BackupArchiveResult(true, null, manifest);
        }
        finally
        {
            File.Delete(tempDatabasePath);
        }
    }

    // The live file is opened by both Web and Agent; copy it without locking or touching it.
    private static async Task CopyDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 81920, useAsync: true);
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static void AddEntry(ZipArchive archive, string entryName, string path)
    {
        var entry = archive.CreateEntry(entryName);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var entryStream = entry.Open();
        input.CopyTo(entryStream);
    }

    private static async Task<BackupFileEntry> DescribeAsync(string path, string entryName, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new BackupFileEntry(entryName, length, Convert.ToHexString(hash).ToLowerInvariant());
    }
}

public sealed record BackupArchiveResult(bool Success, string? Error, BackupManifest? Manifest);
