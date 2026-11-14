namespace NetPilot.Core.Health;

public static class RouterPollingSettings
{
    public const int MinimumSeconds = 60;
    public const int MaximumSeconds = 3600;
    public static void Validate(int seconds)
    {
        if (seconds is < MinimumSeconds or > MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Choose an interval between 1 and 60 minutes.");
    }
}

public interface IRouterPollingSettingsStore
{
    Task<int?> GetIntervalSecondsAsync(CancellationToken ct);
    Task SaveIntervalSecondsAsync(int seconds, CancellationToken ct);
}
