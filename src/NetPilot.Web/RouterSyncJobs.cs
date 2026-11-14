namespace NetPilot.Web;

/// <summary>Serializes dashboard-initiated sync jobs within this Web process.</summary>
public sealed class RouterSyncJobs(IServiceScopeFactory scopes, ILogger<RouterSyncJobs> logger)
{
    private readonly object _gate = new();
    private RouterSyncStatus _status = new(false, null, false);
    public event Action? Changed;
    public RouterSyncStatus Status { get { lock (_gate) return _status; } }
    public bool TryStart()
    {
        lock (_gate)
        {
            if (_status.Running) return false;
            _status = new(true, "Syncing speed policies and reading router limits…", false);
        }
        Notify();
        _ = Task.Run(RunAsync);
        return true;
    }
    private async Task RunAsync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DeviceRouterActions>().SyncAndReadBackAsync();
            Set(new(false, "Sync finished and router limits refreshed. Check Activity for individual write failures.", false));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background router sync failed");
            Set(new(false, "Sync or router readback failed. Some changes may have been applied; check Activity and retry if needed.", true));
        }
    }
    private void Set(RouterSyncStatus value) { lock (_gate) _status = value; Notify(); }
    private void Notify() { try { Changed?.Invoke(); } catch (Exception ex) { logger.LogDebug(ex, "Sync status subscriber unavailable"); } }
}
public sealed record RouterSyncStatus(bool Running, string? Message, bool Failed);
