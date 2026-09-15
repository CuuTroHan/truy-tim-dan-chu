using Microsoft.AspNetCore.SignalR;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public static class ServerBootstrap
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var origins = builder.Configuration.GetSection("Multiplayer:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddPolicy("Client", policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        builder.Services.Configure<RoomServerOptions>(builder.Configuration.GetSection("Multiplayer:Rooms"));
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 4096;
            options.KeepAliveInterval = TimeSpan.FromSeconds(3);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(12);
        });
        var app = builder.Build();
        var roomManager = app.Services.GetRequiredService<RoomManager>();
        var hubContext = app.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<RoomHub>>();
        roomManager.OnMatchStarted += (roomId, matchId, startTime) =>
        {
            _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("MatchStarted", new MatchStartedEvent(matchId, startTime));
            var room = roomManager.GetRoomById(roomId);
            if (room is not null)
            {
                foreach (var team in room.GetTeamSnapshots())
                {
                    var teamGame = room.GetTeamGame(team.TeamId);
                    if (teamGame is not null)
                    {
                        _ = hubContext.Clients.Group($"team_{team.TeamId}").SendAsync("TeamStateUpdated", teamGame.GetSnapshot());
                    }
                }
            }
        };

        app.UseCors("Client");
        // CORS alone does not restrict WebSocket origins. Non-browser clients may omit Origin.
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (context.Request.Path.StartsWithSegments(ConnectionProtocol.HubPath) && origin.Length > 0)
            {
                var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
                if (!string.Equals(origin, sameOrigin, StringComparison.OrdinalIgnoreCase) &&
                    !origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await next(context);
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapHub<RoomHub>(ConnectionProtocol.HubPath);
        return app;
    }
}
