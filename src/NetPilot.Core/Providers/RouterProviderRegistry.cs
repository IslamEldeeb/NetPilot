using NetPilot.Abstractions;

namespace NetPilot.Core.Providers;

/// <summary>
/// Holds/selects the active router provider. v1: providers are ordinary compile-time DI
/// registrations (services.AddTpLinkProvider(...)) — this registry just picks the one
/// configured as active. Upgrading to AssemblyLoadContext-based plugin discovery later is
/// additive here, not a redesign.
/// </summary>
public class RouterProviderRegistry(IEnumerable<IRouterProvider> providers)
{
    private readonly IReadOnlyList<IRouterProvider> _providers = providers.ToList();

    public IReadOnlyList<IRouterProvider> All => _providers;

    public IRouterProvider GetById(string providerId) =>
        _providers.FirstOrDefault(p => p.ProviderId == providerId)
        ?? throw new InvalidOperationException($"No registered router provider with id '{providerId}'.");

    /// <summary>v1 assumes exactly one configured router; returns it if there's only one registered.</summary>
    public IRouterProvider GetActive()
    {
        if (_providers.Count == 0)
            throw new InvalidOperationException("No router providers are registered.");
        if (_providers.Count > 1)
            throw new InvalidOperationException("Multiple router providers registered — GetById required.");
        return _providers[0];
    }
}
