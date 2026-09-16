using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace TruyTimDanChu.Server;

public sealed class SessionCleanupWorker : BackgroundService
{
    private readonly RoomManager _rooms;
    private readonly ReservationService _reservations;
    private readonly IHubContext<RoomHub> _hub;
    private readonly RoomServerOptions _options;

    public SessionCleanupWorker(RoomManager rooms, ReservationService reservations,
        IHubContext<RoomHub> hub, IOptions<RoomServerOptions> options)
    {
        _rooms = rooms;
        _reservations = reservations;
        _hub = hub;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.SessionCleanupIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var room in _rooms.GetRooms())
                {
                    var cleanup = room.CleanupExpiredSessions(now,
                        TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectGraceSeconds)));
                    foreach (var playerId in cleanup.ExpiredPlayerIds)
                    {
                        foreach (var released in _reservations.ReleaseByOwner(playerId))
                            await _hub.Clients.Group($"team_{released.TeamId}")
                                .SendAsync("PuzzleReservationChanged", released, stoppingToken);
                        await _hub.Clients.Group($"room_{room.RoomId}")
                            .SendAsync("PlayerLeft", playerId, stoppingToken);
                    }

                    var decision = room.EvaluateRoomCleanup(now,
                        TimeSpan.FromMinutes(Math.Max(1, _options.LobbyInactivityTimeoutMinutes)),
                        TimeSpan.FromMinutes(Math.Max(1, _options.ClosedRoomRetentionMinutes)));
                    if (decision == RoomCleanupDecision.Closed)
                        await _hub.Clients.Group($"room_{room.RoomId}").SendAsync("RoomStateUpdated", room.GetSnapshot(), stoppingToken);
                    else if (decision == RoomCleanupDecision.Remove)
                        _rooms.RemoveRoom(room);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
