using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Policy;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LitePolicyStore : IPolicyStore
{
    private readonly ILiteCollection<PolicyDocument> _collection;

    public LitePolicyStore(NetPilotDatabase db)
    {
        _collection = db.GetCollection<PolicyDocument>("policies");
    }

    public Task<DevicePolicy?> FindByCategoryAsync(string categoryKey, CancellationToken ct)
    {
        var doc = _collection.FindById(categoryKey);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task<IReadOnlyList<DevicePolicy>> GetAllAsync(CancellationToken ct)
    {
        IReadOnlyList<DevicePolicy> policies = _collection.FindAll().Select(ToDomain).ToList();
        return Task.FromResult(policies);
    }

    public Task UpsertAsync(DevicePolicy policy, CancellationToken ct)
    {
        _collection.Upsert(ToDocument(policy));
        return Task.CompletedTask;
    }

    public Task EnsureSeedCategoriesAsync(CancellationToken ct)
    {
        foreach (var category in DeviceCategory.SeedCategories)
        {
            if (_collection.FindById(category.Key) is not null)
                continue;

            _collection.Insert(new PolicyDocument
            {
                CategoryKey = category.Key,
                LimitEnabled = false,
                DownloadKbps = null,
                UploadKbps = null,
                DefinitionVersion = 1
            });
        }

        return Task.CompletedTask;
    }

    private static DevicePolicy ToDomain(PolicyDocument doc) =>
        new(doc.CategoryKey, new SpeedLimit(doc.LimitEnabled, doc.DownloadKbps, doc.UploadKbps), doc.DefinitionVersion);

    private static PolicyDocument ToDocument(DevicePolicy policy) => new()
    {
        CategoryKey = policy.CategoryKey,
        LimitEnabled = policy.Limit.Enabled,
        DownloadKbps = policy.Limit.DownloadKbps,
        UploadKbps = policy.Limit.UploadKbps,
        DefinitionVersion = policy.DefinitionVersion
    };
}
