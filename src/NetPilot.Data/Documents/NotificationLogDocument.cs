using LiteDB;
using NetPilot.Core.Notifications;

namespace NetPilot.Data.Documents;

public class NotificationLogDocument
{
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public DateTimeOffset AtUtc { get; set; }
    public string Tag { get; set; } = "";
    public string Title { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public NotificationOutcome Outcome { get; set; }
    public string? Error { get; set; }
}
