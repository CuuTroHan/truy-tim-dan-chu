namespace TruyTimDanChu.Shared;

public static class InteractErrorCodes
{
    public const string OutOfRange = "OUT_OF_RANGE";
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";
    public const string InvalidChapter = "INVALID_CHAPTER";
    public const string PlayerNotInTeam = "PLAYER_NOT_IN_TEAM";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string InvalidRoomStatus = "INVALID_ROOM_STATUS";
}

public sealed record InteractRequest(
    string RoomId,
    string PlayerId,
    string ObjectId,
    string CommandId
);

public sealed record InteractResponse(
    bool Success,
    string? ErrorCode,
    string? ObjectId,
    bool Mutated,
    TeamGameStateSnapshot? State
);

