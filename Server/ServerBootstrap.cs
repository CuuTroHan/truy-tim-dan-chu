using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Net.Http.Headers;
using TruyTimDanChu.Game;
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
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
                "application/wasm",
                "application/octet-stream",
                "application/json"
            });
        });
        builder.Services.AddOptions<RoomServerOptions>()
            .Bind(builder.Configuration.GetSection("Multiplayer:Rooms"))
            .Validate(o => o.ReconnectGraceSeconds > 0 &&
                          o.HeartbeatIntervalSeconds > 0 &&
                          o.SessionCleanupIntervalSeconds > 0 &&
                          o.LobbyInactivityTimeoutMinutes > 0 &&
                          o.ClosedRoomRetentionMinutes > 0,
                "Session and room cleanup intervals must be positive.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IMatchStore>(_ =>
            new SqliteMatchStore(builder.Configuration.GetConnectionString("GameDatabase")));
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton(_ => new ReservationService());
        builder.Services.AddHostedService<ReservationExpiryWorker>();
        builder.Services.AddHostedService<MatchTimeoutWorker>();
        builder.Services.AddHostedService<SessionCleanupWorker>();
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 64 * 1024;
            options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });
        var app = builder.Build();
        var roomManager = app.Services.GetRequiredService<RoomManager>();
        var matchStore = app.Services.GetRequiredService<IMatchStore>();
        matchStore.MarkActiveMatchesInterrupted(DateTimeOffset.UtcNow);
        var hubContext = app.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<RoomHub>>();
        roomManager.OnMatchStarted += (roomId, matchId, startTime) =>
        {
            var room = roomManager.GetRoomById(roomId);
            _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("MatchStarted",
                new MatchStartedEvent(matchId, startTime, room?.GetSnapshot().Clock));
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
                var publicProgress = room.GetPublicProgressSnapshot();
                if (publicProgress is not null && publicProgress.IsVisible)
                    _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("PublicProgressUpdated", publicProgress);
            }
        };
        roomManager.OnMatchFinished += (roomId, finished) =>
        {
            _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("MatchFinished", finished);
            _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("RoomStateUpdated",
                roomManager.GetRoomById(roomId)?.GetSnapshot());
            var publicProgress = roomManager.GetRoomById(roomId)?.GetPublicProgressSnapshot();
            if (publicProgress is not null && publicProgress.IsVisible)
                _ = hubContext.Clients.Group($"room_{roomId}").SendAsync("PublicProgressUpdated", publicProgress);
        };

        var roomOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomServerOptions>>().Value;
        if (roomOptions.RequireHttps)
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }
        app.UseResponseCompression();
        app.UseDefaultFiles();
        // Blazor WebAssembly ships ICU tables as fingerprinted .dat assets. The default
        // provider may regard that extension as unknown on a bare Kestrel host, which
        // leaves the runtime at its loading screen although index.html was served.
        app.UseStaticFiles(new StaticFileOptions
        {
            ServeUnknownFileTypes = true,
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/_framework") || path.StartsWith("/css") || path.StartsWith("/assets") || path.StartsWith("/img") || path.StartsWith("/audio"))
                {
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                }
                else if (path.EndsWith("service-worker.js") ||
                         path.EndsWith("service-worker-assets.js") ||
                         path.EndsWith("index.html"))
                {
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
                }
            }
        });
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
        app.MapGet("/ready", (IMatchStore store) =>
            store.CheckReady(out var error)
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready", error }, statusCode: StatusCodes.Status503ServiceUnavailable));
        app.MapGet("/api/rooms/{roomId}/matches", (string roomId, HttpRequest request, IMatchStore store) =>
        {
            var token = BearerToken(request);
            if (!store.VerifyRoomAdmin(roomId, token ?? string.Empty)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var pageSize = int.TryParse(request.Query["pageSize"], out var parsed) ? parsed : 20;
            DateTimeOffset? cursorTime = DateTimeOffset.TryParse(request.Query["cursorEndedAtUtc"], out var dt) ? dt : null;
            var page = store.ReadHistoryPage(new MatchHistoryRequest(roomId, token ?? string.Empty, pageSize, cursorTime, request.Query["cursorMatchId"]));
            return Results.Ok(page);
        });
        app.MapGet("/api/rooms/{roomId}/matches/{matchId}", (string roomId, string matchId, HttpRequest request, IMatchStore store) =>
        {
            var token = BearerToken(request);
            if (!store.VerifyRoomAdmin(roomId, token ?? string.Empty)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var detail = store.ReadHistoryDetail(roomId, matchId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        app.MapGet("/api/rooms/{roomId}/matches/{matchId}/results.csv", (string roomId, string matchId, HttpRequest request, IMatchStore store) =>
        {
            var token = BearerToken(request);
            if (!store.VerifyRoomAdmin(roomId, token ?? string.Empty)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var detail = store.ReadHistoryDetail(roomId, matchId);
            if (detail is null) return Results.NotFound();
            var bytes = CsvExport.Build(detail);
            var filename = CsvExport.SanitizeFilename($"{roomId}-{matchId}-results") + ".csv";
            return Results.File(bytes, "text/csv; charset=utf-8", filename);
        });
        var enableDev = app.Environment.IsDevelopment() ||
                        app.Configuration.GetValue<bool>("Multiplayer:EnableDevEndpoints", true);
        if (enableDev)
        {
            app.MapPost("/dev/prepare-draft", (RoomManager rm, IHubContext<RoomHub> hub, string roomId, string teamId) =>
            {
                var room = rm.GetRoomById(roomId);
                if (room == null) return Results.NotFound();
                var game = room.GetTeamGame(teamId);
                if (game == null) return Results.NotFound();
                game.Progress.Chapter = Chapter.Draft;
                Array.Fill(game.Progress.Spoken, true);
                Array.Fill(game.Progress.Lamps, true);
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
                {
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
                }
                foreach (var memberId in game.MemberIds)
                {
                    game.UpdateMemberPosition(memberId, 486, 115, 0, 0);
                }
                _ = hub.Clients.Group($"team_{teamId}").SendAsync("TeamStateUpdated", game.GetSnapshot());
                return Results.Ok();
            });

            app.MapPost("/dev/prepare-river", (RoomManager rm, IHubContext<RoomHub> hub, string roomId, string teamId) =>
            {
                var room = rm.GetRoomById(roomId);
                if (room == null) return Results.NotFound();
                var game = room.GetTeamGame(teamId);
                if (game == null) return Results.NotFound();
                game.Progress.Chapter = Chapter.River;
                Array.Fill(game.Progress.Spoken, true);
                Array.Fill(game.Progress.Lamps, true);
                game.Progress.DraftDone = true;
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
                foreach (var memberId in game.MemberIds)
                {
                    game.UpdateMemberPosition(memberId, 823, 325, 0, 0);
                }
                _ = hub.Clients.Group($"team_{teamId}").SendAsync("TeamStateUpdated", game.GetSnapshot());
                return Results.Ok();
            });

            app.MapPost("/dev/prepare-news", (RoomManager rm, IHubContext<RoomHub> hub, string roomId, string teamId) =>
            {
                var room = rm.GetRoomById(roomId);
                if (room == null) return Results.NotFound();
                var game = room.GetTeamGame(teamId);
                if (game == null) return Results.NotFound();
                game.Progress.Chapter = Chapter.News;
                Array.Fill(game.Progress.Spoken, true);
                Array.Fill(game.Progress.Lamps, true);
                game.Progress.DraftDone = true;
                game.Progress.RiverDone = true;
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-3), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.River))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
                foreach (var memberId in game.MemberIds)
                {
                    game.UpdateMemberPosition(memberId, 246, 516, 0, 0);
                }
                _ = hub.Clients.Group($"team_{teamId}").SendAsync("TeamStateUpdated", game.GetSnapshot());
                return Results.Ok();
            });

            app.MapPost("/dev/set-member-positions", (RoomManager rm, IHubContext<RoomHub> hub, string roomId, string teamId, float? aX, float? aY, float? bX, float? bY, string? playerAId, string? playerBId) =>
            {
                var room = rm.GetRoomById(roomId);
                if (room == null) return Results.NotFound();
                var game = room.GetTeamGame(teamId);
                if (game == null) return Results.NotFound();
                var members = game.MemberIds.ToList();
                // Explicit ids make a large simulated roster deterministic; old callers retain
                // the original first-two behavior when ids are omitted.
                var first = !string.IsNullOrWhiteSpace(playerAId) && game.HasMember(playerAId) ? playerAId : members.FirstOrDefault();
                var second = !string.IsNullOrWhiteSpace(playerBId) && game.HasMember(playerBId) ? playerBId : members.Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) && aX.HasValue && aY.HasValue)
                    game.UpdateMemberPosition(first, aX.Value, aY.Value, 0, 0);
                if (!string.IsNullOrWhiteSpace(second) && bX.HasValue && bY.HasValue)
                    game.UpdateMemberPosition(second, bX.Value, bY.Value, 0, 0);
                _ = hub.Clients.Group($"team_{teamId}").SendAsync("TeamStateUpdated", game.GetSnapshot());
                return Results.Ok();
            });

            app.MapPost("/dev/prepare-finale", (RoomManager rm, IHubContext<RoomHub> hub, string roomId, string teamId) =>
            {
                var room = rm.GetRoomById(roomId);
                if (room == null) return Results.NotFound();
                var game = room.GetTeamGame(teamId);
                if (game == null) return Results.NotFound();
                game.Progress.Chapter = Chapter.Finale;
                Array.Fill(game.Progress.Spoken, true);
                Array.Fill(game.Progress.Lamps, true);
                Array.Fill(game.Progress.Clues, true);
                game.Progress.DraftDone = true;
                game.Progress.RiverDone = true;
                game.Progress.NewsDone = true;
                game.Progress.FinaleOrderCompleted = false;
                game.Progress.ReturnStep = 0;
                game.Progress.FinaleDone = false;
                game.Progress.FinishedAtUtc = null;
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-4), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-3), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.River))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
                if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.News))
                    game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.News, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
                foreach (var memberId in game.MemberIds)
                {
                    game.UpdateMemberPosition(memberId, 513, 328, 0, 0);
                }
                _ = hub.Clients.Group($"team_{teamId}").SendAsync("TeamStateUpdated", game.GetSnapshot());
                return Results.Ok();
            });
        }
        app.MapHub<RoomHub>(ConnectionProtocol.HubPath);
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            }
        });
        return app;
    }

    private static string? BearerToken(HttpRequest request)
    {
        var value = request.Headers[HeaderNames.Authorization].ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..].Trim() : null;
    }
}
