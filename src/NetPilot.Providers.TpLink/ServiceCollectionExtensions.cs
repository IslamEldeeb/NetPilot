using Microsoft.Extensions.DependencyInjection;
using NetPilot.Abstractions;

namespace NetPilot.Providers.TpLink;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// v1: an ordinary compile-time DI registration — zero dynamic-loading machinery.
    /// Upgrading to AssemblyLoadContext-based plugin discovery is a later, additive step
    /// once a second real provider exists to prove the contract against.
    /// </summary>
    public static IServiceCollection AddTpLinkProvider(this IServiceCollection services)
    {
        services.AddSingleton<IRouterProvider, TpLinkRouterProvider>();
        return services;
    }
}
