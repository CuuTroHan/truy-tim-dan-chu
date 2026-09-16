using Microsoft.AspNetCore.SignalR;

namespace TruyTimDanChu.Server;

public sealed class ReservationExpiryWorker : BackgroundService
{
    private readonly ReservationService _reservations;
    private readonly IHubContext<RoomHub> _hub;

    public ReservationExpiryWorker(ReservationService reservations, IHubContext<RoomHub> hub)
    {
        _reservations = reservations;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var released in _reservations.SweepExpired())
                    await _hub.Clients.Group($"team_{released.TeamId}")
                        .SendAsync("PuzzleReservationChanged", released, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host đang tắt bình thường; không báo nhầm BackgroundService failed.
        }
    }
}
