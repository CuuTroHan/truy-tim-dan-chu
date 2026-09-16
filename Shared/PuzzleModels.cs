namespace TruyTimDanChu.Shared;

public static class PuzzleIds
{
    public const string Mirrors = "mirrors";
    public const string Draft = "draft";
    public const string River = "river";
    public const string News = "news";
    public const string Finale = "finale";

    public static bool IsKnown(string? puzzleId) => puzzleId is Mirrors or Draft or River or News or Finale;
}

public static class PuzzleReservationErrorCodes
{
    public const string InvalidPuzzle = "INVALID_PUZZLE";
    public const string PuzzleOccupied = "PUZZLE_OCCUPIED";
    public const string NotReservationOwner = "NOT_RESERVATION_OWNER";
    public const string ReservationExpired = "RESERVATION_EXPIRED";
    public const string InvalidReservationToken = "INVALID_RESERVATION_TOKEN";
    public const string PrerequisiteNotMet = "PREREQUISITE_NOT_MET";
    public const string InvalidAnswerPayload = "INVALID_ANSWER_PAYLOAD";
    public const string InvalidCommandId = "INVALID_COMMAND_ID";
    public const string TeamAlreadyFinished = "TEAM_ALREADY_FINISHED";
    public const string InvalidReturnStep = "INVALID_RETURN_STEP";
}

public sealed record PuzzleReservationKey(string MatchId, string TeamId, string PuzzleId);

/// <summary>Trạng thái công khai gửi cho cả đội. Không bao giờ chứa token chủ khóa.</summary>
public sealed record PuzzleReservationState(
    string MatchId,
    string TeamId,
    string PuzzleId,
    bool IsReserved,
    string? OwnerPlayerId,
    string? OwnerDisplayName,
    DateTimeOffset? ExpiresAtUtc);

public sealed record ReservePuzzleRequest(string RoomId, string PlayerId, string PuzzleId, string? MatchId = null);

public sealed record ReservePuzzleResponse(
    bool Success,
    string? ErrorCode,
    PuzzleReservationState? Reservation,
    string? ReservationToken);

public sealed record ReleasePuzzleRequest(
    string RoomId,
    string PlayerId,
    string PuzzleId,
    string ReservationToken,
    string? MatchId = null);

public sealed record ReleasePuzzleResponse(
    bool Success,
    string? ErrorCode,
    PuzzleReservationState? Reservation);

/// <summary>
/// Endpoint kiểm tra quyền submit của Phase 17. Nội dung đáp án chỉ được xử lý từ Phase 18.
/// </summary>
public sealed record ValidatePuzzleSubmissionRequest(
    string RoomId,
    string PlayerId,
    string PuzzleId,
    string ReservationToken,
    string? MatchId = null);

public sealed record ValidatePuzzleSubmissionResponse(bool Success, string? ErrorCode);

public sealed record SubmitPuzzleRequest(
    string RoomId,
    string PlayerId,
    string PuzzleId,
    string ReservationToken,
    int[] Answer,
    string CommandId,
    string? MatchId = null);

public sealed record SubmitPuzzleResponse(
    bool Success,
    string? ErrorCode,
    bool Correct,
    bool Mutated,
    int WrongAnswerCount,
    TeamGameStateSnapshot? State);
