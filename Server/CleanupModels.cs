namespace TruyTimDanChu.Server;

public sealed record CleanupResult(IReadOnlyList<string> ExpiredPlayerIds, int RoomVersion)
{
    public bool Changed => ExpiredPlayerIds.Count > 0;
}

public enum RoomCleanupDecision
{
    None,
    Closed,
    Remove
}
