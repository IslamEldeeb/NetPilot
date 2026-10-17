using LiteDB;

namespace NetPilot.Data;

internal static class AutomationRevision
{
    public static void Advance(NetPilotDatabase db, string target) => db.GetCollection<BsonDocument>("automation_versions")
        .Upsert(new BsonDocument { ["_id"] = target, ["Revision"] = Guid.NewGuid().ToString() });

    public static string Current(NetPilotDatabase db, string target) =>
        db.GetCollection<BsonDocument>("automation_versions").FindById(target)?["Revision"].AsString ?? "";
}
