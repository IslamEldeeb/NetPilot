using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNetPilotLocalTraffic(this IServiceCollection services, IConfiguration configuration)
    {
        var url = configuration["NetPilot:LocalTraffic:Url"];
        if (string.IsNullOrWhiteSpace(url))
            services.AddSingleton<ILocalTrafficSource, NullLocalTrafficSource>();
        else
        {
            var endpoint = new Uri(url, UriKind.Absolute);
            if (endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException("LocalTraffic:Url must be an HTTP(S) URL without embedded credentials.");
            services.AddHttpClient("LocalTraffic", client => client.Timeout = TimeSpan.FromSeconds(5))
                .RedactLoggedHeaders(["Authorization"]);
            services.AddSingleton<ILocalTrafficSource>(sp => new HttpLocalTrafficSource(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("LocalTraffic"), endpoint,
                configuration["NetPilot:LocalTraffic:Token"]));
        }
        services.AddSingleton<LocalTrafficTracker>();
        return services;
    }

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
        services.AddSingleton<LitePreferencesStore>();
        services.AddSingleton<NetPilot.Core.Settings.IPreferencesStore>(sp => sp.GetRequiredService<LitePreferencesStore>());
        services.AddSingleton<NetPilot.Core.Settings.ISetupProgressStore>(sp => sp.GetRequiredService<LitePreferencesStore>());
        services.AddSingleton<NetPilot.Core.Safety.ISafetyStore, LiteSafetyStore>();
        services.AddSingleton<IAgentHeartbeatStore, LiteAgentHeartbeatStore>();
        services.AddSingleton<IPolicyStore, LitePolicyStore>();
        services.AddSingleton<IPushSubscriptionStore, LitePushSubscriptionStore>();
        services.AddSingleton<INotificationLogStore, LiteNotificationLogStore>();
        services.AddSingleton<IVapidKeyStore, VapidKeyStore>();
        services.AddSingleton<IQuotaActionTokenService, QuotaActionTokenService>();
        services.AddSingleton<QuotaActionHandler>();
        services.AddSingleton<IActionClaimStore, LiteActionClaimStore>();
        services.AddSingleton<IRuleStore, LiteRuleStore>();
        services.AddSingleton<IRuleFiringStore, LiteRuleFiringStore>();
        services.AddSingleton<IQuotaStore, LiteQuotaStore>();
        services.AddSingleton<IQuotaStateStore, LiteQuotaStateStore>();
        services.AddSingleton<IProtectedDeviceStore, LiteProtectedDeviceStore>();
        services.AddSingleton<IActivityLogStore, LiteActivityLogStore>();
        services.AddSingleton<IRouterConnectionStore, LiteRouterConnectionStore>();
        services.AddSingleton<IUsageStore, LiteUsageStore>();
        services.AddSingleton<ILocalTrafficStore, LiteLocalTrafficStore>();
        services.AddSingleton<IDeviceClassifier, HeuristicDeviceClassifier>();
        services.AddSingleton<RouterPasswordProtector>();
        return services;
    }
}
