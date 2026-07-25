namespace NetPilot.Core.Enforcement;

public enum ActivityEventType
{
    DeviceDiscovered,
    DeviceWentOffline,
    DeviceCameBackOnline,
    PolicyApplied,
    PolicySkippedAlreadyCorrect,
    WriteFailed,
    NewCategorySeen,
    UsageCounterReset,
    RouterRebooted,
    DeviceBlocked,
    DeviceUnblocked
}
