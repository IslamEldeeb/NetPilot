using Microsoft.Extensions.Logging;
using NetPilot.Abstractions;

namespace NetPilot.Core.RouterConnection;

/// <summary>Thrown by RouterSessionManager when no router connection has been saved yet.</summary>
public class RouterNotConfiguredException()
    : InvalidOperationException("No router configured yet — set it from the dashboard, or ROUTER_HOST/ROUTER_PASSWORD env vars on first run.");

/// <summary>
/// Owns the one physical login session against IRouterProvider and serializes every caller
/// behind a single lock. The router's firmware allows exactly one active admin session —
/// before this existed, the Agent's background tick and the Web dashboard's handlers each
/// called IRouterProvider.ConnectAsync independently with no coordination, which had already
/// caused a real login collision in production (see docs/phase3-wireless-management-plan.md
/// §6.1). Every caller that touches the router should go through ExecuteAsync/TestConnectionAsync
/// here instead of calling ConnectAsync itself.
/// </summary>
public class RouterSessionManager(
    IRouterProvider provider,
    IRouterConnectionStore connectionStore,
    IRouterPasswordCipher passwordCipher,
    ILogger<RouterSessionManager> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _connected;

    /// <summary>
    /// Runs `action` against the connected provider, holding the session lock for the whole
    /// call. Connects first (using the stored connection) if not already connected this
    /// session. Throws RouterNotConfiguredException if nothing is saved yet. Any exception
    /// from connecting or from `action` invalidates the cached session, so the next call
    /// re-authenticates rather than assuming a failed call still left a good session behind.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<IRouterProvider, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct);
            try
            {
                return await action(provider);
            }
            catch
            {
                _connected = false;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Non-generic convenience for actions with no return value.</summary>
    public Task ExecuteAsync(Func<IRouterProvider, Task> action, CancellationToken ct) =>
        ExecuteAsync(async p =>
        {
            await action(p);
            return true;
        }, ct);

    /// <summary>
    /// For the dashboard's "Test connection" flow only — connects with caller-supplied
    /// settings that may not be saved yet, rather than the stored connection, still under
    /// the shared lock so it can't race the background tick's login. Always invalidates the
    /// cached session afterward: the settings just used may differ from what's actually
    /// stored, so the next ExecuteAsync call must re-authenticate with the real stored
    /// connection rather than assume the test session is still the right one.
    /// </summary>
    public async Task<T> TestConnectionAsync<T>(RouterConnectionSettings settings, Func<IRouterProvider, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await provider.ConnectAsync(settings, ct);
            return await action(provider);
        }
        finally
        {
            _connected = false;
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connected)
            return;

        var connection = await connectionStore.GetAsync(ct)
            ?? throw new RouterNotConfiguredException();

        var password = passwordCipher.Decrypt(connection.EncryptedPassword);
        var settings = new RouterConnectionSettings(connection.Host, connection.UseHttps, connection.Username, password);
        await provider.ConnectAsync(settings, ct);
        _connected = true;
        logger.LogInformation("Connected to router at {Host}", connection.Host);
    }
}
