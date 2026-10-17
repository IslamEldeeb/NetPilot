using NetPilot.Abstractions;
using NetPilot.Core.Actions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Agent;

/// <summary>
/// The reconciliation loop: one read, per-device fingerprint compare, write only what's
/// wrong — every {PollIntervalSeconds} (default 180s). Never crashes the whole worker on a
/// single bad tick (router offline, bad password, transient network error); logs and
/// retries next tick instead.
/// </summary>
public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IPolicyStore policyStore,
    IRouterConnectionStore connectionStore,
    RouterPasswordProtector passwordProtector,
    IRouterProvider routerProvider,
    PolicyReconciliationService reconciliationService,
    UsageTrackingService usageTrackingService,
    QuotaEnforcementService quotaEnforcementService,
    RuleEvaluationService ruleEvaluationService,
    IAgentHeartbeatStore heartbeatStore,
    RulesOptions rulesOptions,
    TimeProvider clock, TemporaryActionEnforcement? temporaryActions = null) : BackgroundService
{
    private bool _connected;
    private AgentHeartbeat _heartbeat = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await policyStore.EnsureSeedCategoriesAsync(stoppingToken);
        await SeedConnectionFromEnvironmentAsync(stoppingToken);

        await LoadHeartbeatAsync(stoppingToken);
        var pollInterval = TimeSpan.FromSeconds(configuration.GetValue("NetPilot:PollIntervalSeconds", 180));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunHeartbeatTickAsync(stoppingToken);
            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    internal async Task RunHeartbeatTickAsync(CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        var provider = new TickRouterProvider(routerProvider, clock);
        await StartHeartbeatAsync(ct);
        string? error = null;
        try
        {
            if (!_connected && !await TryConnectAsync(provider, ct))
                error = "No router configured";
            else
                error = await RunTickCoreAsync(provider, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Exception messages can contain credentials or protocol payloads.
            logger.LogError("Reconciliation tick failed — will reconnect and retry next tick.");
            error = "Reconciliation tick failed";
            _connected = false;
        }
        if (ct.IsCancellationRequested) return; // Shutdown is not evidence of a completed tick.
        await CompleteHeartbeatAsync(provider, started, error, ct);
    }

    private async Task StartHeartbeatAsync(CancellationToken ct)
    {
        _heartbeat = _heartbeat with
        {
            LastTickStartedUtc = clock.GetUtcNow(),
            TickIntervalSeconds = configuration.GetValue("NetPilot:PollIntervalSeconds", 180),
            AppVersion = typeof(Worker).Assembly.GetName().Version?.ToString() ?? "unknown",
            EffectiveModes = new(rulesOptions.ForceDryRun, rulesOptions.Enabled)
        };
        await SaveHeartbeatAsync(ct);
    }

    private async Task CompleteHeartbeatAsync(TickRouterProvider provider, long started, string? error, CancellationToken ct)
    {
        error = provider.ErrorSummary ?? error;
        if (provider.ErrorSummary is not null) _connected = false;
        _heartbeat = _heartbeat with
        {
            LastTickCompletedUtc = clock.GetUtcNow(),
            LastTickDurationMs = clock.GetElapsedTime(started).TotalMilliseconds,
            LastSuccessfulRouterReadUtc = provider.LastSuccessfulReadUtc ?? _heartbeat.LastSuccessfulRouterReadUtc,
            ConsecutiveFailedTicks = error is null ? 0 : (int)Math.Min((long)_heartbeat.ConsecutiveFailedTicks + 1, int.MaxValue),
            LastErrorSummary = error
        };
        await SaveHeartbeatAsync(ct);
    }

    internal async Task LoadHeartbeatAsync(CancellationToken ct)
    {
        try { _heartbeat = await heartbeatStore.GetAsync(ct) ?? new(); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Could not read Agent heartbeat; starting fresh."); }
    }

    private async Task SaveHeartbeatAsync(CancellationToken ct)
    {
        try { await heartbeatStore.SaveAsync(_heartbeat, ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Could not save Agent heartbeat; continuing reconciliation."); }
    }

    internal async Task RunTickAsync(CancellationToken stoppingToken) =>
        await RunTickCoreAsync(routerProvider, stoppingToken);

    private async Task<string?> RunTickCoreAsync(IRouterProvider provider, CancellationToken stoppingToken)
    {
        var snapshots = await reconciliationService.ReconcileAsync(provider, stoppingToken);
        await usageTrackingService.TrackAsync(snapshots, stoppingToken);
        string? temporaryError = null;
        try
        {
            if (temporaryActions is not null) await temporaryActions.EvaluateAsync(provider, stoppingToken);
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError("Temporary action evaluation failed — will retry next tick.");
            temporaryError = "Temporary action evaluation failed";
        }
        await quotaEnforcementService.EvaluateAsync(provider, snapshots, stoppingToken);
        try
        {
            await ruleEvaluationService.EvaluateAsync(provider, snapshots, stoppingToken);
            return temporaryError;
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError("Rules evaluation failed — will retry next tick.");
            return "Rules evaluation failed";
        }
    }

    /// <summary>Convenience for `docker compose up` first run — ROUTER_HOST/ROUTER_PASSWORD seed the record if it's empty. No-op after that; the dashboard is the source of truth.</summary>
    private async Task SeedConnectionFromEnvironmentAsync(CancellationToken ct)
    {
        var envHost = Environment.GetEnvironmentVariable("ROUTER_HOST");
        var envPassword = Environment.GetEnvironmentVariable("ROUTER_PASSWORD");
        if (string.IsNullOrWhiteSpace(envHost) || string.IsNullOrWhiteSpace(envPassword))
            return;

        await connectionStore.SeedFromEnvironmentIfEmptyAsync(
            routerProvider.ProviderId, envHost, passwordProtector.Encrypt(envPassword), ct);
    }

    private async Task<bool> TryConnectAsync(IRouterProvider provider, CancellationToken ct)
    {
        var connection = await connectionStore.GetAsync(ct);
        if (connection is null)
            return false;

        var password = passwordProtector.Decrypt(connection.EncryptedPassword);
        var settings = new RouterConnectionSettings(connection.Host, connection.UseHttps, connection.Username, password);

        await provider.ConnectAsync(settings, ct);
        _connected = true;
        logger.LogInformation("Connected to router at {Host}", connection.Host);
        return true;
    }
}
