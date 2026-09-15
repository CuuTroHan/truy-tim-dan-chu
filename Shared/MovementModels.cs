namespace TruyTimDanChu.Shared;

public sealed record PlayerMovementInput(
    string RoomId,
    string PlayerId,
    int Keys,
    int Sequence,
    long ClientTimestampMs
);

public sealed record MovementAck(
    bool Success,
    string? ErrorCode,
    int Sequence,
    float X,
    float Y,
    int Facing,
    int Walking,
    long ServerTimestampMs
);

public sealed record PlayerMovedBroadcast(
    string PlayerId,
    float X,
    float Y,
    int Facing,
    int Walking,
    int Sequence,
    long ServerTimestampMs
);

