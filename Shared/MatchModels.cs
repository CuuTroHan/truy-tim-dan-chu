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
    int Shards
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

