using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared LiteDB file, every repository, and a Data Protection key ring
    /// persisted to keyRingPath. Both NetPilot.Agent and NetPilot.Web must call this with
    /// the same dbPath and keyRingPath (both on the shared Docker volume) so either process
    /// can decrypt a router password the other one encrypted.
    /// </summary>
    public static IServiceCollection AddNetPilotData(this IServiceCollection services, string dbPath, string keyRingPath)
    {
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
            .SetApplicationName("NetPilot");

        services.AddSingleton(_ => new NetPilotDatabase(dbPath));
        services.AddSingleton<IDeviceStore, LiteDeviceStore>();
        services.AddSingleton<IPolicyStore, LitePolicyStore>();
        services.AddSingleton<IActivityLogStore, LiteActivityLogStore>();
        services.AddSingleton<IRouterConnectionStore, LiteRouterConnectionStore>();
        services.AddSingleton<IUsageStore, LiteUsageStore>();
        services.AddSingleton<IDeviceClassifier, HeuristicDeviceClassifier>();
        services.AddSingleton<RouterPasswordProtector>();
        return services;
    }
}
