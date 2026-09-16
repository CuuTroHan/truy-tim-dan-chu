using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class TeamInstance
{
    public string TeamId { get; }
    public string RoomId { get; }
    public string Name { get; set; }
    public string Color { get; set; }
    public int Capacity { get; set; }
    public int DisplayOrder { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public TeamInstance(string teamId, string roomId, string name, string color, int capacity, int displayOrder)
    {
        TeamId = teamId;
        RoomId = roomId;
        Name = name;
        Color = color;
        Capacity = capacity;
        DisplayOrder = displayOrder;
    }

    public bool TryMarkFinished(DateTimeOffset timestamp)
    {
        if (FinishedAtUtc.HasValue) return false;
        FinishedAtUtc = timestamp;
        return true;
    }

    public void ResetForRematch() => FinishedAtUtc = null;

    public TeamSnapshot GetSnapshot(int memberCount = 0)
    {
        return new TeamSnapshot(TeamId, RoomId, Name, Color, Capacity, DisplayOrder, memberCount, FinishedAtUtc);
    }
}

