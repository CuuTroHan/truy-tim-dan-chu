namespace TruyTimDanChu.Shared;

public enum RoomStatus
{
    Lobby,
    Countdown,
    Playing,
    Paused,
    Finished,
    Closed
}

public sealed record CreateRoomRequest(
    string RoomName,
    int TimeLimitSeconds = 900,
    bool AllowSelfTeamSelection = true,
    bool AllowLateJoin = false,
    bool ShowLiveLeaderboard = true,
    bool AdminCanPlay = false,
    bool EndOnFirstFinish = false
);

public sealed record CreateRoomResponse(
    bool Success,
    string? ErrorCode,
    string? RoomId,
    string? RoomCode,
    string? AdminToken,
    RoomSnapshot? Snapshot
);

public sealed record PlayerSnapshot(
    string PlayerId,
    string DisplayName,
    string? TeamId,
    string AvatarId,
    string AccentColor,
    bool IsReady,
    bool IsConnected,
    bool IsAdmin = false
);

public sealed record RoomSnapshot(
    string RoomId,
    string RoomCode,
    string RoomName,
    RoomStatus Status,
    bool AllowSelfTeamSelection,
    bool AllowLateJoin,
    bool ShowLiveLeaderboard,
    bool AdminCanPlay,
    bool EndOnFirstFinish,
    int TimeLimitSeconds,
    DateTimeOffset CreatedAt,
    int Version,
    bool IsJoinLocked = false,
    bool IsRosterLocked = false,
    string? MatchId = null,
    DateTimeOffset? MatchStartTimeUtc = null
);

public sealed record TeamSnapshot(
    string TeamId,
    string RoomId,
    string Name,
    string Color,
    int Capacity,
    int DisplayOrder,
    int MemberCount = 0
);

public sealed record AdminAddTeamRequest(
    string RoomId,
    string AdminToken,
    string Name,
    string Color,
    int Capacity
);

public sealed record AdminAddTeamResponse(
    bool Success,
    string? ErrorCode,
    TeamSnapshot? Team,
    int RoomVersion
);

public sealed record AdminUpdateTeamRequest(
    string RoomId,
    string AdminToken,
    string TeamId,
    string Name,
    string Color,
    int Capacity
);

public sealed record AdminUpdateTeamResponse(
    bool Success,
    string? ErrorCode,
    TeamSnapshot? Team,
    int RoomVersion
);

public sealed record AdminReorderTeamsRequest(
    string RoomId,
    string AdminToken,
    List<string> OrderedTeamIds
);

public sealed record AdminReorderTeamsResponse(
    bool Success,
    string? ErrorCode,
    List<TeamSnapshot>? Teams,
    int RoomVersion
);

public sealed record AdminRemoveTeamRequest(
    string RoomId,
    string AdminToken,
    string TeamId
);

public sealed record AdminRemoveTeamResponse(
    bool Success,
    string? ErrorCode,
    string? RemovedTeamId,
    int RoomVersion
);

public sealed record JoinTeamRequest(
    string RoomId,
    string PlayerId,
    string TeamId
);

public sealed record JoinTeamResponse(
    bool Success,
    string? ErrorCode,
    string? TeamId,
    int RoomVersion
);

public sealed record LeaveTeamRequest(
    string RoomId,
    string PlayerId
);

public sealed record LeaveTeamResponse(
    bool Success,
    string? ErrorCode,
    int RoomVersion
);

public sealed record PlayerTeamChangedEvent(
    string PlayerId,
    string? OldTeamId,
    string? NewTeamId
);

public sealed record JoinRoomRequest(
    string RoomCode,
    string DisplayName,
    string? ReconnectToken = null
);

public sealed record JoinRoomResponse(
    bool Success,
    string? ErrorCode,
    string? PlayerId,
    string? ReconnectToken,
    RoomSnapshot? Room,
    List<PlayerSnapshot>? Players,
    List<TeamSnapshot>? Teams = null
);

public sealed record AdminMovePlayerRequest(
    string RoomId,
    string AdminToken,
    string PlayerId,
    string? TargetTeamId
);

public sealed record AdminMovePlayerResponse(
    bool Success,
    string? ErrorCode,
    string? PlayerId,
    string? OldTeamId,
    string? NewTeamId,
    int RoomVersion
);

public sealed record AdminKickPlayerRequest(
    string RoomId,
    string AdminToken,
    string PlayerId,
    string? Reason = null
);

public sealed record AdminKickPlayerResponse(
    bool Success,
    string? ErrorCode,
    string? KickedPlayerId,
    int RoomVersion
);

public sealed record AdminToggleSelfTeamSelectionRequest(
    string RoomId,
    string AdminToken,
    bool AllowSelfTeamSelection
);

public sealed record AdminToggleSelfTeamSelectionResponse(
    bool Success,
    string? ErrorCode,
    bool AllowSelfTeamSelection,
    int RoomVersion
);

public sealed record PlayerKickedEvent(
    string PlayerId,
    string? Reason
);

public sealed record SelectAvatarRequest(
    string RoomId,
    string PlayerId,
    string AvatarId
);

public sealed record SelectAvatarResponse(
    bool Success,
    string? ErrorCode,
    string? AvatarId,
    int RoomVersion
);

public sealed record PlayerAvatarChangedEvent(
    string PlayerId,
    string AvatarId
);

public sealed record SetReadyRequest(
    string RoomId,
    string PlayerId,
    bool IsReady
);

public sealed record SetReadyResponse(
    bool Success,
    string? ErrorCode,
    bool IsReady,
    int RoomVersion
);

public sealed record PlayerReadyChangedEvent(
    string PlayerId,
    bool IsReady
);

public sealed record AdminSetJoinLockRequest(
    string RoomId,
    string AdminToken,
    bool IsJoinLocked
);

public sealed record AdminSetJoinLockResponse(
    bool Success,
    string? ErrorCode,
    bool IsJoinLocked,
    int RoomVersion
);

public sealed record JoinLockToggledEvent(
    bool IsJoinLocked
);

public sealed record AdminSetRosterLockRequest(
    string RoomId,
    string AdminToken,
    bool IsRosterLocked
);

public sealed record AdminSetRosterLockResponse(
    bool Success,
    string? ErrorCode,
    bool IsRosterLocked,
    int RoomVersion
);

public sealed record RosterLockToggledEvent(
    bool IsRosterLocked
);

public sealed record ReadyResetEvent(
    string? Reason
);

public sealed record StartMatchValidationResult(
    bool CanStart,
    List<string> BlockingReasons,
    List<string> Warnings
);

public sealed record AdminStartMatchRequest(
    string RoomId,
    string AdminToken,
    int CountdownSeconds = 3
);

public sealed record AdminStartMatchResponse(
    bool Success,
    string? ErrorCode,
    string? MatchId,
    DateTimeOffset? MatchStartTimeUtc,
    int RoomVersion
);

public sealed record CountdownStartedEvent(
    string MatchId,
    int CountdownSeconds,
    DateTimeOffset MatchStartTimeUtc
);

public sealed record AdminCancelCountdownRequest(
    string RoomId,
    string AdminToken
);

public sealed record AdminCancelCountdownResponse(
    bool Success,
    string? ErrorCode,
    int RoomVersion
);

public sealed record CountdownCanceledEvent();

public sealed record MatchStartedEvent(
    string MatchId,
    DateTimeOffset StartedAtUtc
);

public static class RoomErrorCodes
{
    public const string InvalidRoomName = "INVALID_ROOM_NAME";
    public const string InvalidTimeLimit = "INVALID_TIME_LIMIT";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string UnauthorizedAdmin = "UNAUTHORIZED_ADMIN";
    public const string RoomClosed = "ROOM_CLOSED";
    public const string ServerFull = "SERVER_FULL";
    public const string InvalidRoomCode = "INVALID_ROOM_CODE";
    public const string InvalidDisplayName = "INVALID_DISPLAY_NAME";
    public const string DuplicateDisplayName = "DUPLICATE_DISPLAY_NAME";
    public const string RoomFull = "ROOM_FULL";
    public const string JoinNotAllowed = "JOIN_NOT_ALLOWED";
    public const string InvalidTeamName = "INVALID_TEAM_NAME";
    public const string InvalidTeamColor = "INVALID_TEAM_COLOR";
    public const string InvalidTeamCapacity = "INVALID_TEAM_CAPACITY";
    public const string DuplicateTeamName = "DUPLICATE_TEAM_NAME";
    public const string MaxTeamsExceeded = "MAX_TEAMS_EXCEEDED";
    public const string TeamCapacityExceeded = "TEAM_CAPACITY_EXCEEDED";
    public const string TotalCapacityExceeded = "TOTAL_CAPACITY_EXCEEDED";
    public const string InvalidRoomStatus = "INVALID_ROOM_STATUS";
    public const string CapacityBelowOccupancy = "CAPACITY_BELOW_OCCUPANCY";
    public const string TeamNotEmpty = "TEAM_NOT_EMPTY";
    public const string InvalidTeamOrder = "INVALID_TEAM_ORDER";
    public const string TeamNotFound = "TEAM_NOT_FOUND";
    public const string SelfSelectionDisabled = "SELF_SELECTION_DISABLED";
    public const string TeamFull = "TEAM_FULL";
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";
    public const string InvalidAvatarId = "INVALID_AVATAR_ID";
    public const string JoinLocked = "JOIN_LOCKED";
    public const string RosterLocked = "ROSTER_LOCKED";
    public const string PlayerNotInTeam = "PLAYER_NOT_IN_TEAM";
    public const string NotReadyToStart = "NOT_READY_TO_START";
    public const string CountdownAlreadyActive = "COUNTDOWN_ALREADY_ACTIVE";
    public const string MatchAlreadyStarted = "MATCH_ALREADY_STARTED";
    public const string CountdownNotActive = "COUNTDOWN_NOT_ACTIVE";
    public const string CountdownActive = "COUNTDOWN_ACTIVE";
    public const string MatchPlaying = "MATCH_PLAYING";
}
