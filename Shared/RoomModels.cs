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
    DateTimeOffset? MatchStartTimeUtc = null,
    MatchClockSnapshot? Clock = null,
    DateTimeOffset? MatchFinishedAtUtc = null,
    MatchEndReason? MatchEndReason = null,
    DateTimeOffset? ClosedAtUtc = null
);

public sealed record TeamSnapshot(
    string TeamId,
    string RoomId,
    string Name,
    string Color,
    int Capacity,
    int DisplayOrder,
    int MemberCount = 0,
    DateTimeOffset? FinishedAtUtc = null
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

public sealed record ResumePlayerRequest(
    string RoomCode,
    string PlayerId,
    string ReconnectToken
);

public sealed record ResumePlayerResponse(
    bool Success,
    string? ErrorCode,
    string? PlayerId,
    string? ReconnectToken,
    RoomSnapshot? Room,
    List<PlayerSnapshot>? Players,
    List<TeamSnapshot>? Teams,
    TeamGameStateSnapshot? TeamState,
    PublicProgressSnapshot? PublicProgress,
    IReadOnlyList<PuzzleReservationState>? Reservations = null,
    MatchResultsSnapshot? Results = null
);

public sealed record HeartbeatRequest(string RoomId, string PlayerId);
public sealed record HeartbeatResponse(bool Success, string? ErrorCode, DateTimeOffset ServerTimeUtc);
public sealed record SessionSupersededEvent(string PlayerId);

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

public sealed record AdminNewMatchRequest(string RoomId, string AdminToken, string CommandId);

public sealed record AdminNewMatchResponse(
    bool Success,
    string? ErrorCode,
    RoomSnapshot? Room,
    IReadOnlyList<PlayerSnapshot>? Players,
    IReadOnlyList<TeamSnapshot>? Teams
);

public sealed record AdminPauseMatchRequest(string RoomId, string AdminToken, string MatchId, string CommandId);
public sealed record AdminResumeMatchRequest(string RoomId, string AdminToken, string MatchId, string CommandId);

public sealed record MatchPausedEvent(string MatchId, DateTimeOffset PausedAtUtc, long ElapsedMilliseconds, long RemainingMilliseconds);
public sealed record MatchResumedEvent(string MatchId, DateTimeOffset ResumedAtUtc, DateTimeOffset DeadlineUtc, long TotalPausedMilliseconds);
public sealed record AdminPauseMatchResponse(bool Success, string? ErrorCode, MatchPausedEvent? Event);
public sealed record AdminResumeMatchResponse(bool Success, string? ErrorCode, MatchResumedEvent? Event);

public sealed record AdminEndMatchRequest(string RoomId, string AdminToken, string MatchId, string CommandId, string? Reason = null);
public sealed record AdminCancelMatchRequest(string RoomId, string AdminToken, string? MatchId, string CommandId, string? Reason = null);
public sealed record AdminCloseRoomRequest(string RoomId, string AdminToken, string CommandId, string? Reason = null);
public sealed record MatchEndedEvent(string MatchId, DateTimeOffset EndedAtUtc, MatchEndReason Reason, MatchResultsSnapshot? Results);
public sealed record RoomClosedEvent(string RoomId, DateTimeOffset ClosedAtUtc, string? Reason);
public sealed record AdminActionResponse(bool Success, string? ErrorCode, RoomSnapshot? Room, MatchEndedEvent? Match, int RoomVersion);
public sealed record AdminCloseRoomResponse(bool Success, string? ErrorCode, RoomSnapshot? Room, RoomClosedEvent? Event);

public sealed record ResumeAdminRequest(string RoomId, string AdminToken);
public sealed record AdminDashboardSnapshot(RoomSnapshot Room, IReadOnlyList<TeamSnapshot> Teams, IReadOnlyList<PlayerSnapshot> Players,
    MatchClockSnapshot? Clock, PublicProgressSnapshot? PublicProgress, MatchResultsSnapshot? Results,
    IReadOnlyList<PuzzleReservationState> Reservations, string? LinkedPlayerId = null);
public sealed record ResumeAdminResponse(bool Success, string? ErrorCode, string? AdminToken, AdminDashboardSnapshot? Snapshot, string? PreviousConnectionId = null);
public sealed record AdminSessionSupersededEvent(string RoomId);

public sealed record LateJoinRequest(string RoomCode, string DisplayName, string TeamId, string AvatarId, string CommandId);
public sealed record LateJoinResponse(bool Success, string? ErrorCode, string? PlayerId, string? ReconnectToken,
    RoomSnapshot? Room, TeamGameStateSnapshot? TeamState, PublicProgressSnapshot? PublicProgress);

public sealed record AdminSetCanPlayRequest(string RoomId, string AdminToken, bool AdminCanPlay, string CommandId);
public sealed record AdminJoinAsPlayerRequest(string RoomId, string AdminToken, string TeamId, string DisplayName, string AvatarId, string CommandId);
public sealed record AdminCanPlayResponse(bool Success, string? ErrorCode, RoomSnapshot? Room, PlayerSnapshot? Player, int RoomVersion);
public sealed record AdminEndMatchResponse(bool Success, string? ErrorCode, RoomSnapshot? Room, MatchEndedEvent? Event);
public sealed record AdminCancelMatchResponse(bool Success, string? ErrorCode, RoomSnapshot? Room, MatchEndedEvent? Event);
public sealed record AdminSetCanPlayResponse(bool Success, string? ErrorCode, bool AdminCanPlay, int RoomVersion);
public sealed record AdminJoinAsPlayerResponse(bool Success, string? ErrorCode, string? PlayerId, string? ReconnectToken, RoomSnapshot? Room, TeamGameStateSnapshot? TeamState);
public sealed record AdminSetEndOnFirstFinishRequest(string RoomId, string AdminToken, bool EndOnFirstFinish, string CommandId);
public sealed record AdminSetEndOnFirstFinishResponse(bool Success, string? ErrorCode, bool EndOnFirstFinish, int RoomVersion);

public sealed record MatchHistoryRequest(string RoomId, string AdminToken, int PageSize = 20,
    DateTimeOffset? CursorEndedAtUtc = null, string? CursorMatchId = null);
public sealed record MatchHistoryItem(string MatchId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc,
    MatchEndReason EndReason, int TeamCount, int CompletedTeamCount);
public sealed record MatchHistoryPage(IReadOnlyList<MatchHistoryItem> Items, DateTimeOffset? NextCursorEndedAtUtc,
    string? NextCursorMatchId, bool HasMore);
public sealed record MatchHistoryDetail(string RoomId, string MatchId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, MatchEndReason EndReason, IReadOnlyList<TeamResultSnapshot> Teams);

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
    DateTimeOffset StartedAtUtc,
    MatchClockSnapshot? Clock = null
);

public enum MatchEndReason
{
    AllTeamsFinished,
    Timeout,
    AdminEnded,
    Cancelled,
    FirstTeamFinished,
    Interrupted,
    RoomClosed
}

public sealed record MatchClockSnapshot(
    string MatchId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset ServerNowUtc,
    long ElapsedMilliseconds,
    long RemainingMilliseconds,
    bool IsPaused,
    DateTimeOffset? FinishedAtUtc = null,
    MatchEndReason? EndReason = null
);

public sealed record MatchFinishedEvent(
    string MatchId,
    DateTimeOffset FinishedAtUtc,
    MatchEndReason Reason,
    MatchResultsSnapshot? Results = null
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
    public const string MatchTimedOut = "MATCH_TIMED_OUT";
    public const string MatchFinished = "MATCH_FINISHED";
    public const string MatchIdMismatch = "MATCH_ID_MISMATCH";
    public const string InvalidReconnectToken = "INVALID_RECONNECT_TOKEN";
    public const string ReconnectExpired = "RECONNECT_EXPIRED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string ConnectionNotBound = "CONNECTION_NOT_BOUND";
    public const string MatchPaused = "MATCH_PAUSED";
    public const string MatchNotPaused = "MATCH_NOT_PAUSED";
    public const string MatchAlreadyPaused = "MATCH_ALREADY_PAUSED";
    public const string AdminNotBound = "ADMIN_NOT_BOUND";
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";
    public const string InvalidCommandId = "INVALID_COMMAND_ID";
    public const string LateJoinNotAllowedInState = "LATE_JOIN_NOT_ALLOWED_IN_STATE";
    public const string AdminCanPlayDisabled = "ADMIN_CAN_PLAY_DISABLED";
    public const string MatchCannotBeEnded = "MATCH_CANNOT_BE_ENDED";
    public const string MatchCannotBeCancelled = "MATCH_CANNOT_BE_CANCELLED";
    public const string RoomAlreadyClosed = "ROOM_ALREADY_CLOSED";
    public const string CommandAlreadyApplied = "COMMAND_ALREADY_APPLIED";
    public const string LateJoinDisabled = "LATE_JOIN_DISABLED";
    public const string NoAvailableSlot = "NO_AVAILABLE_SLOT";
    public const string TeamAlreadyFinished = "TEAM_ALREADY_FINISHED";
    public const string AdminSessionRevoked = "ADMIN_SESSION_REVOKED";
    public const string EndOnFirstFinishLocked = "END_ON_FIRST_FINISH_LOCKED";
}
