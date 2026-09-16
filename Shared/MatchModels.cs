using TruyTimDanChu.Game;

namespace TruyTimDanChu.Shared;

public sealed record TeamMemberState(
    string PlayerId,
    string DisplayName,
    string AvatarId,
    string AccentColor,
    float X,
    float Y,
    int Facing,
    int Walking,
    bool IsConnected
);

public sealed record TeamProgressSnapshot(
    Chapter Chapter,
    bool[] Spoken,
    bool[] Lamps,
    bool[] Clues,
    int Version,
    int Shards,
    int WrongAnswerCount = 0,
    IReadOnlyList<ChapterTiming>? ChapterTimings = null,
    bool DraftDone = false,
    int DraftFailures = 0,
    bool[]? Lore = null,
    bool RiverDone = false,
    int RiverFailures = 0,
    bool NewsDone = false,
    int NewsNoise = 0,
    bool FinaleOrderCompleted = false,
    int ReturnStep = 0,
    bool FinaleDone = false,
    DateTimeOffset? FinishedAtUtc = null
);

public sealed record MatchSnapshot(
    string MatchId,
    string TeamId,
    string TeamName,
    string TeamColor,
    IReadOnlyList<TeamMemberState> Members,
    TeamProgressSnapshot Progress,
    DateTimeOffset? ServerTimeUtc = null
);

public sealed record TeamGameStateSnapshot(
    string MatchId,
    string TeamId,
    string TeamName,
    string TeamColor,
    IReadOnlyList<TeamMemberState> Members,
    TeamProgressSnapshot Progress,
    DateTimeOffset? ServerTimeUtc = null
)
{
    public MatchSnapshot ToMatchSnapshot() =>
        new(MatchId, TeamId, TeamName, TeamColor, Members, Progress, ServerTimeUtc);
}

public sealed record GetTeamStateRequest(
    string RoomId,
    string PlayerId
);

public sealed record GetTeamStateResponse(
    bool Success,
    string? ErrorCode,
    TeamGameStateSnapshot? State
);

public enum TeamResultStatus
{
    Completed,
    TimedOut,
    EndedEarly,
    Abandoned
}

public enum PublicTeamStatus
{
    Playing,
    TeamOffline,
    Completed,
    TimedOut,
    EndedEarly
}

public sealed record ResultMemberSnapshot(
    string PlayerId,
    string DisplayName,
    string AvatarId
);

public sealed record TeamResultSnapshot(
    string MatchId,
    string TeamId,
    string TeamName,
    string TeamColor,
    int? Rank,
    TeamResultStatus Status,
    IReadOnlyList<ResultMemberSnapshot> Members,
    DateTimeOffset? FinishedAtUtc,
    long? ElapsedMilliseconds,
    int WrongAnswerCount,
    long? PenultimateElapsedMilliseconds,
    IReadOnlyList<ChapterTiming> ChapterTimings
);

public sealed record MatchResultsSnapshot(
    string RoomId,
    string MatchId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    MatchEndReason EndReason,
    IReadOnlyList<TeamResultSnapshot> Teams
);

public sealed record PublicTeamProgressSnapshot(
    string TeamId,
    string TeamName,
    string TeamColor,
    int Shards,
    PublicTeamStatus Status,
    int? ProvisionalRank = null
);

public sealed record PublicProgressSnapshot(
    string MatchId,
    int RoomVersion,
    bool IsVisible,
    IReadOnlyList<PublicTeamProgressSnapshot> Teams
);
