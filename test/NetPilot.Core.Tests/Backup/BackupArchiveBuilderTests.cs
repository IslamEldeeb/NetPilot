using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Core.Backup;

namespace NetPilot.Core.Tests.Backup;

public class BackupArchiveBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netpilot-backup-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ManifestJsonRoundTrips()
    {
        var manifest = new BackupManifest
        {
            CreatedAtUtc = new DateTimeOffset(2026, 10, 16, 12, 30, 0, TimeSpan.Zero),
            AppVersion = "9.9.9-test",
            Files =
            [
                new BackupFileEntry("netpilot.db", 1234, "ab"),
                new BackupFileEntry("keys/key-1.xml", 56, "cd")
            ]
        };

        var json = BackupManifestJson.Serialize(manifest);
        var restored = BackupManifestJson.Deserialize(json);

        Assert.Equal(manifest.CreatedAtUtc, restored.CreatedAtUtc);
        Assert.Equal("9.9.9-test", restored.AppVersion);
        Assert.Equal(BackupManifest.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal("netpilot.db", restored.Files[0].Name);
        Assert.Equal(1234, restored.Files[0].SizeBytes);
        Assert.Equal("ab", restored.Files[0].Sha256);
        Assert.Equal("keys/key-1.xml", restored.Files[1].Name);
        Assert.Equal(56, restored.Files[1].SizeBytes);
        Assert.Equal("cd", restored.Files[1].Sha256);
    }

    [Fact]
    public async Task BuildAsyncProducesZipWithDatabaseKeysAndManifest()
    {
        var dataDir = CreateDataDirectory();
        var dbBytes = Encoding.UTF8.GetBytes("fake lite db contents that stay open");
        await File.WriteAllBytesAsync(Path.Combine(dataDir, BackupPlan.DatabaseFileName), dbBytes);
        await File.WriteAllTextAsync(Path.Combine(dataDir, "keys", "key-1.xml"), "<key id='1'/>");
        await File.WriteAllTextAsync(Path.Combine(dataDir, "keys", "key-2.xml"), "<key id='2'/>");

        await using var liveDb = new FileStream(
            Path.Combine(dataDir, BackupPlan.DatabaseFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        BackupManifest? manifest;
        using (var output = new MemoryStream())
        {
            var result = await BackupArchiveBuilder.BuildAsync(dataDir, output);
            Assert.True(result.Success);
            Assert.Null(result.Error);
            manifest = result.Manifest;
            output.Position = 0;

            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            var entryNames = archive.Entries.Select(e => e.FullName).ToArray();
            Assert.Equal(
                [BackupPlan.DatabaseFileName, "keys/key-1.xml", "keys/key-2.xml", "manifest.json"],
                entryNames);

            using var dbEntry = archive.GetEntry(BackupPlan.DatabaseFileName)!.Open();
            using var dbCopy = new MemoryStream();
            await dbEntry.CopyToAsync(dbCopy);
            Assert.Equal(dbBytes, dbCopy.ToArray());
        }

        Assert.NotNull(manifest);
        var dbManifestEntry = manifest.Files.Single(f => f.Name == BackupPlan.DatabaseFileName);
        Assert.Equal(dbBytes.Length, dbManifestEntry.SizeBytes);
        Assert.Equal(await SHA256.HashDataAsync(new MemoryStream(dbBytes)), Convert.FromHexString(dbManifestEntry.Sha256));
        Assert.True(manifest.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(manifest.CreatedAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void PlanReportsMissingKeysWithClearError()
    {
        var dataDir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, BackupPlan.DatabaseFileName), "db");

        var plan = BackupPlan.Create(dataDir);

        Assert.False(plan.IsValid);
        Assert.Equal(
            "Data Protection keys not found; a database backup without keys cannot restore the router password or push keys",
            plan.Error);
    }

    [Fact]
    public async Task BuildAsyncReturnsFailureResultWhenKeysMissingInsteadOfThrowing()
    {
        var dataDir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, BackupPlan.DatabaseFileName), "db");

        using var output = new MemoryStream();
        var result = await BackupArchiveBuilder.BuildAsync(dataDir, output);

        Assert.False(result.Success);
        Assert.Equal(BackupPlan.MissingKeysMessage, result.Error);
        Assert.Null(result.Manifest);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void PlanListsDatabaseAndEveryKeyFile()
    {
        var dataDir = CreateDataDirectory();
        File.WriteAllText(Path.Combine(dataDir, BackupPlan.DatabaseFileName), "db");
        Directory.CreateDirectory(Path.Combine(dataDir, "keys"));
        File.WriteAllText(Path.Combine(dataDir, "keys", "b.xml"), "2");
        File.WriteAllText(Path.Combine(dataDir, "keys", "a.xml"), "1");

        var plan = BackupPlan.Create(dataDir);

        Assert.True(plan.IsValid);
        Assert.Equal(
            [BackupPlan.DatabaseFileName, "keys/a.xml", "keys/b.xml"],
            plan.Files);
    }

    private string CreateDataDirectory()
    {
        var dataDir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dataDir, "keys"));
        return dataDir;
    }
}
