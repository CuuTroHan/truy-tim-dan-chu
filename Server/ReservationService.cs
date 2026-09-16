using System.Security.Cryptography;
using System.Text;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class ReservationService
{
    private sealed record ActiveReservation(
        PuzzleReservationKey Key,
        string OwnerPlayerId,
        string OwnerDisplayName,
        string Token,
        DateTimeOffset ExpiresAtUtc);

    private readonly object _gate = new();
    private readonly Dictionary<PuzzleReservationKey, ActiveReservation> _active = new();

    public TimeSpan TimeToLive { get; }
    public Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;

    public ReservationService(TimeSpan? timeToLive = null)
    {
        TimeToLive = timeToLive ?? TimeSpan.FromSeconds(90);
        if (TimeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
    }

    public ReservePuzzleResponse TryReserve(PuzzleReservationKey key, string playerId, string displayName)
    {
        lock (_gate)
        {
            var now = UtcNowProvider();
            RemoveIfExpired(key, now);

            if (_active.TryGetValue(key, out var current))
            {
                if (current.OwnerPlayerId == playerId)
                    return new ReservePuzzleResponse(true, null, ToPublic(current), current.Token);

                return new ReservePuzzleResponse(false, PuzzleReservationErrorCodes.PuzzleOccupied,
                    ToPublic(current), null);
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var reservation = new ActiveReservation(key, playerId, displayName, token, now + TimeToLive);
            _active[key] = reservation;
            return new ReservePuzzleResponse(true, null, ToPublic(reservation), token);
        }
    }

    public ReleasePuzzleResponse TryRelease(PuzzleReservationKey key, string playerId, string? token)
    {
        lock (_gate)
        {
            var now = UtcNowProvider();
            if (!_active.TryGetValue(key, out var current))
                return new ReleasePuzzleResponse(false, PuzzleReservationErrorCodes.ReservationExpired, Released(key));

            if (current.ExpiresAtUtc <= now)
            {
                _active.Remove(key);
                return new ReleasePuzzleResponse(false, PuzzleReservationErrorCodes.ReservationExpired, Released(key));
            }

            if (current.OwnerPlayerId != playerId)
                return new ReleasePuzzleResponse(false, PuzzleReservationErrorCodes.NotReservationOwner, ToPublic(current));

            if (!TokenEquals(current.Token, token))
                return new ReleasePuzzleResponse(false, PuzzleReservationErrorCodes.InvalidReservationToken, ToPublic(current));

            _active.Remove(key);
            return new ReleasePuzzleResponse(true, null, Released(key));
        }
    }

    public ValidatePuzzleSubmissionResponse ValidateSubmission(PuzzleReservationKey key, string playerId, string? token)
    {
        lock (_gate)
        {
            var now = UtcNowProvider();
            if (!_active.TryGetValue(key, out var current) || current.ExpiresAtUtc <= now)
            {
                _active.Remove(key);
                return new ValidatePuzzleSubmissionResponse(false, PuzzleReservationErrorCodes.ReservationExpired);
            }

            if (current.OwnerPlayerId != playerId)
                return new ValidatePuzzleSubmissionResponse(false, PuzzleReservationErrorCodes.NotReservationOwner);

            return TokenEquals(current.Token, token)
                ? new ValidatePuzzleSubmissionResponse(true, null)
                : new ValidatePuzzleSubmissionResponse(false, PuzzleReservationErrorCodes.InvalidReservationToken);
        }
    }

    public IReadOnlyList<PuzzleReservationState> ReleaseByOwner(string playerId)
    {
        lock (_gate)
        {
            var keys = _active.Values.Where(x => x.OwnerPlayerId == playerId).Select(x => x.Key).ToArray();
            foreach (var key in keys) _active.Remove(key);
            return keys.Select(Released).ToArray();
        }
    }

    public IReadOnlyList<PuzzleReservationState> GetByOwner(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return Array.Empty<PuzzleReservationState>();
        lock (_gate)
        {
            var now = UtcNowProvider();
            var expired = _active.Values.Where(x => x.ExpiresAtUtc <= now).Select(x => x.Key).ToArray();
            foreach (var key in expired) _active.Remove(key);
            return _active.Values
                .Where(x => string.Equals(x.OwnerPlayerId, playerId, StringComparison.Ordinal))
                .Select(ToPublic)
                .ToArray();
        }
    }

    public IReadOnlyList<PuzzleReservationState> ReleaseByMatch(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return Array.Empty<PuzzleReservationState>();
        lock (_gate)
        {
            var keys = _active.Keys.Where(x => string.Equals(x.MatchId, matchId, StringComparison.Ordinal)).ToArray();
            foreach (var key in keys) _active.Remove(key);
            return keys.Select(Released).ToArray();
        }
    }

    public IReadOnlyList<PuzzleReservationState> SweepExpired()
    {
        lock (_gate)
        {
            var now = UtcNowProvider();
            var keys = _active.Values.Where(x => x.ExpiresAtUtc <= now).Select(x => x.Key).ToArray();
            foreach (var key in keys) _active.Remove(key);
            return keys.Select(Released).ToArray();
        }
    }

    private void RemoveIfExpired(PuzzleReservationKey key, DateTimeOffset now)
    {
        if (_active.TryGetValue(key, out var existing) && existing.ExpiresAtUtc <= now)
            _active.Remove(key);
    }

    private static bool TokenEquals(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual)) return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static PuzzleReservationState ToPublic(ActiveReservation value) =>
        new(value.Key.MatchId, value.Key.TeamId, value.Key.PuzzleId, true,
            value.OwnerPlayerId, value.OwnerDisplayName, value.ExpiresAtUtc);

    private static PuzzleReservationState Released(PuzzleReservationKey key) =>
        new(key.MatchId, key.TeamId, key.PuzzleId, false, null, null, null);
}
