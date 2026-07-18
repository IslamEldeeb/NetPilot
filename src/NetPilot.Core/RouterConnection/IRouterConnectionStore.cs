namespace NetPilot.Core.RouterConnection;

public interface IRouterConnectionStore
{
    Task<RouterConnection?> GetAsync(CancellationToken ct);
    Task SaveAsync(RouterConnection connection, CancellationToken ct);

    /// <summary>Seeds from ROUTER_HOST/ROUTER_PASSWORD env vars on first run only; no-op if a record already exists.</summary>
    Task SeedFromEnvironmentIfEmptyAsync(string providerId, string? host, string? encryptedPassword, CancellationToken ct);
}
