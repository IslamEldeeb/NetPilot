using LiteDB;
using NetPilot.Data;
using NetPilot.Data.Documents;

namespace NetPilot.Data.Tests;

/// <summary>
/// Proves the actual LiteDB round-trip for policy rows written before IsUserConfigured
/// existed — not just that DevicePolicy.InferConfiguredFromLegacy's math is right (that's
/// covered by DevicePolicyTests in NetPilot.Core.Tests), but that a real BSON document
/// missing the field really does deserialize as `null` through LiteDB's POCO mapper, which
/// is the assumption the whole migration-safety story depends on. If LiteDB's mapper ever
/// defaulted a missing bool? to false instead of null, IsUserConfigured ?? Infer(...) would
/// silently stop enforcing every already-deployed policy on upgrade.
/// </summary>
public class LitePolicyStoreLegacyMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netpilot-legacy-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task LegacyRow_WithActiveLimit_InfersConfiguredTrue()
    {
        InsertLegacyDocument(new BsonDocument
        {
            ["_id"] = "mobile",
            ["LimitEnabled"] = true,
            ["DownloadKbps"] = 5000,
            ["UploadKbps"] = 1000,
            ["DefinitionVersion"] = 1
            // IsUserConfigured intentionally absent — simulates a row written pre-migration.
        });

        using var db = new NetPilotDatabase(_dbPath);
        var store = new LitePolicyStore(db);

        var policy = await store.FindByCategoryAsync("mobile", CancellationToken.None);

        Assert.NotNull(policy);
        Assert.True(policy!.IsUserConfigured);
    }

    [Fact]
    public async Task LegacyRow_UnlimitedFallbackShape_InfersConfiguredFalse()
    {
        InsertLegacyDocument(new BsonDocument
        {
            ["_id"] = "television",
            ["LimitEnabled"] = false,
            ["DefinitionVersion"] = 1
            // Exact shape EnsureSeedCategoriesAsync always wrote before this field existed.
        });

        using var db = new NetPilotDatabase(_dbPath);
        var store = new LitePolicyStore(db);

        var policy = await store.FindByCategoryAsync("television", CancellationToken.None);

        Assert.NotNull(policy);
        Assert.False(policy!.IsUserConfigured);
    }

    [Fact]
    public async Task LegacyRow_EditedButLeftDisabled_VersionBumpAloneInfersConfiguredTrue()
    {
        InsertLegacyDocument(new BsonDocument
        {
            ["_id"] = "desktop",
            ["LimitEnabled"] = false,
            ["DefinitionVersion"] = 2 // only WithLimit bumps this, and only on a real edit
        });

        using var db = new NetPilotDatabase(_dbPath);
        var store = new LitePolicyStore(db);

        var policy = await store.FindByCategoryAsync("desktop", CancellationToken.None);

        Assert.NotNull(policy);
        Assert.True(policy!.IsUserConfigured);
    }

    [Fact]
    public async Task NewRow_WrittenByThisVersion_RoundTripsExplicitFalse_NoInferenceNeeded()
    {
        using (var db = new NetPilotDatabase(_dbPath))
        {
            var store = new LitePolicyStore(db);
            await store.UpsertAsync(
                new NetPilot.Core.Policy.DevicePolicy("laptop", NetPilot.Abstractions.SpeedLimit.Unlimited, 1, IsUserConfigured: false),
                CancellationToken.None);
        }

        using var db2 = new NetPilotDatabase(_dbPath);
        var store2 = new LitePolicyStore(db2);
        var policy = await store2.FindByCategoryAsync("laptop", CancellationToken.None);

        Assert.NotNull(policy);
        Assert.False(policy!.IsUserConfigured);
    }

    private void InsertLegacyDocument(BsonDocument doc)
    {
        using var db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
        db.GetCollection<BsonDocument>("policies").Insert(doc);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
