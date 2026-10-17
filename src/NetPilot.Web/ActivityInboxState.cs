using NetPilot.Core.Activity;

namespace NetPilot.Web;

public sealed class ActivityInboxState(IActivityFeed feed)
{
    public int Unread { get; private set; }
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        Unread = await feed.UnreadCountAsync(default);
        Changed?.Invoke();
    }
}
