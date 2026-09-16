namespace TruyTimDanChu.Server;

/// <summary>Finishes expired matches even when no client sends another command.</summary>
public sealed class MatchTimeoutWorker : BackgroundService
{
    private readonly RoomManager _roomManager;
    private readonly ReservationService _reservations;
    private readonly ILogger<MatchTimeoutWorker> _logger;

    public MatchTimeoutWorker(RoomManager roomManager, ReservationService reservations, ILogger<MatchTimeoutWorker> logger)
    {
        _roomManager = roomManager;
        _reservations = reservations;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var room in _roomManager.GetRooms())
            {
                try
                {
                    if (room.TryExpireMatch(out var finished) && finished is not null)
                    {
                        foreach (var released in _reservations.ReleaseByMatch(finished.MatchId))
                            _logger.LogDebug("Released expired puzzle reservation {PuzzleId} for match {MatchId}", released.PuzzleId, finished.MatchId);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "Match timeout evaluation failed for room {RoomId}", room.RoomId);
                }
            }
        }
    }
}
