namespace TruyTimDanChu.Server;

public sealed class RoomServerOptions
{
    public int MaxRooms { get; set; } = 100;
    public int MaxTeamsPerRoom { get; set; } = 10;
    public int MaxPlayersPerTeam { get; set; } = 8;
    public int MaxPlayersPerRoom { get; set; } = 40;
    public int MinTimeLimitSeconds { get; set; } = 60;
    public int MaxTimeLimitSeconds { get; set; } = 7200;
    public int InactivityTimeoutMinutes { get; set; } = 60;
    public int ReconnectGraceSeconds { get; set; } = 90;
    public int HeartbeatIntervalSeconds { get; set; } = 10;
    public int SessionCleanupIntervalSeconds { get; set; } = 5;
    public int LobbyInactivityTimeoutMinutes { get; set; } = 60;
    public int ClosedRoomRetentionMinutes { get; set; } = 10;
    public bool RequireHttps { get; set; } = false;
}

