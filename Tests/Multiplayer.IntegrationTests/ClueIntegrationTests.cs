using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class ClueIntegrationTests
{
    [Fact]
    public async Task SignalR_CluesAndLore_BroadcastTeamState_PreserveFlags_AndIsolateTeams()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);
        await using var admin = await Connect(uri);
        var created = await admin.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Tin tức", 900));
        var roomId = created.RoomId!;
        var red = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Đỏ", "#E53935", 3))).Team!;
        var blue = (await admin.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, created.AdminToken!, "Xanh", "#1E88E5", 2))).Team!;

        var (playerA, aId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "An");
        await using var a = playerA;
        var (playerB, bId) = await Join(uri, created.RoomCode!, roomId, red.TeamId, "Bình");
        await using var b = playerB;
        var (playerC, cId) = await Join(uri, created.RoomCode!, roomId, blue.TeamId, "Chi");
        await using var c = playerC;

        await admin.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, created.AdminToken!, 1));
        await Task.Delay(1200);
        await a.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, aId));
        await b.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, bId));
        await c.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(roomId, cId));

        var room = app.Services.GetRequiredService<RoomManager>().GetRoomById(roomId)!;
        var redGame = room.GetTeamGame(red.TeamId)!;
        var blueGame = room.GetTeamGame(blue.TeamId)!;
        PrepareNews(redGame, aId, bId);

        var blueUpdates = new List<TeamGameStateSnapshot>();
        c.On<TeamGameStateSnapshot>("TeamStateUpdated", state => blueUpdates.Add(state));

        // 1. An đứng ở clue0 (180, 520) tương tác lấy clue0
        var bReceivedClue0 = NextTeamUpdate(b, red.TeamId, s => s.Progress.Clues[0]);
        var resClue0 = await a.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, aId, "clue0", "cmd-a-clue0"));
        Assert.True(resClue0.Success);
        Assert.True(resClue0.Mutated);
        var bState0 = await bReceivedClue0;
        Assert.True(bState0.Progress.Clues[0]);
        Assert.False(bState0.Progress.Clues[1]);
        Assert.False(bState0.Progress.Clues[2]);

        // 2. Bình đứng ở clue1 (339, 509) tương tác lấy clue1
        redGame.UpdateMemberPosition(bId, 339, 509, 0, 0);
        var aReceivedClue1 = NextTeamUpdate(a, red.TeamId, s => s.Progress.Clues[1]);
        var resClue1 = await b.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, bId, "clue1", "cmd-b-clue1"));
        Assert.True(resClue1.Success);
        Assert.True(resClue1.Mutated);
        var aState1 = await aReceivedClue1;
        Assert.True(aState1.Progress.Clues[0]);
        Assert.True(aState1.Progress.Clues[1]);
        Assert.False(aState1.Progress.Clues[2]);

        // 3. Bình di chuyển tới Bảo (259, 537) nói chuyện với Bảo -> cập nhật Clues[2]
        redGame.UpdateMemberPosition(bId, 259, 537, 0, 0);
        var aReceivedClue2 = NextTeamUpdate(a, red.TeamId, s => s.Progress.Clues[2]);
        var resBao = await b.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, bId, "bao", "cmd-b-bao"));
        Assert.True(resBao.Success);
        Assert.True(resBao.Mutated);
        var aState2 = await aReceivedClue2;
        Assert.True(aState2.Progress.Clues[0]);
        Assert.True(aState2.Progress.Clues[1]);
        Assert.True(aState2.Progress.Clues[2]);

        // 4. Replay command lấy clue0 của An: Mutated=false, idempotent
        var replayClue0 = await a.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, aId, "clue0", "cmd-a-clue0"));
        Assert.True(replayClue0.Success);
        Assert.False(replayClue0.Mutated);

        // 5. An tới clue2 (286, 552) tương tác: Clues[2] đã có từ Bảo, Mutated=false
        redGame.UpdateMemberPosition(aId, 286, 552, 0, 0);
        var resClue2 = await a.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, aId, "clue2", "cmd-a-clue2"));
        Assert.True(resClue2.Success);
        Assert.False(resClue2.Mutated);

        // 6. An di chuyển tới lore0 (344, 80) đọc Lore:
        //    B nhận TeamStateUpdated có Lore[0]=true, Chapter vẫn News, 3 mảnh, 0 lỗi
        redGame.UpdateMemberPosition(aId, 344, 80, 0, 0);
        var bReceivedLore0 = NextTeamUpdate(b, red.TeamId, s => s.Progress.Lore != null && s.Progress.Lore[0]);
        var resLore = await a.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, aId, "lore0", "cmd-a-lore0"));
        Assert.True(resLore.Success);
        Assert.True(resLore.Mutated);
        var bLoreState = await bReceivedLore0;
        Assert.NotNull(bLoreState.Progress.Lore);
        Assert.True(bLoreState.Progress.Lore[0]);
        Assert.Equal(Chapter.News, bLoreState.Progress.Chapter);
        Assert.Equal(3, bLoreState.Progress.Shards);
        Assert.Equal(0, bLoreState.Progress.WrongAnswerCount);
        Assert.Equal(3, bLoreState.Progress.ChapterTimings?.Count ?? 0);

        // 7. Team isolation: Đội Xanh không nhận broadcast từ Đội Đỏ, tiến độ Clues/Lore của Đội Xanh vẫn nguyên
        Assert.DoesNotContain(blueUpdates, state => state.TeamId == red.TeamId);
        Assert.All(blueGame.Progress.Clues, Assert.False);
        Assert.All(blueGame.Progress.Lore, Assert.False);
    }

    private static Task<TeamGameStateSnapshot> NextTeamUpdate(HubConnection hub, string teamId,
        Func<TeamGameStateSnapshot, bool> predicate)
    {
        var source = new TaskCompletionSource<TeamGameStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TeamGameStateSnapshot>("TeamStateUpdated", state =>
        {
            if (state.TeamId == teamId && predicate(state)) source.TrySetResult(state);
        });
        return source.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void PrepareNews(TeamGameInstance game, string playerAId, string playerBId)
    {
        game.Progress.Chapter = Chapter.News;
        Array.Fill(game.Progress.Spoken, true);
        Array.Fill(game.Progress.Lamps, true);
        game.Progress.DraftDone = true;
        game.Progress.RiverDone = true;
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Lights, DateTimeOffset.UtcNow.AddMinutes(-3), 60_000));
        }
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.Draft, DateTimeOffset.UtcNow.AddMinutes(-2), 60_000));
        }
        if (!game.Progress.ChapterTimings.Any(x => x.Chapter == Chapter.River))
        {
            game.Progress.ChapterTimings.Add(new ChapterTiming(Chapter.River, DateTimeOffset.UtcNow.AddMinutes(-1), 60_000));
        }
        game.UpdateMemberPosition(playerAId, 180, 520, 0, 0); // clue0
        game.UpdateMemberPosition(playerBId, 339, 509, 0, 0); // clue1
    }

    private static async Task<(HubConnection Hub, string PlayerId)> Join(Uri uri, string code, string roomId,
        string teamId, string name)
    {
        var hub = await Connect(uri);
        var joined = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(code, name));
        await hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, joined.PlayerId!, teamId));
        await hub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, joined.PlayerId!, true));
        return (hub, joined.PlayerId!);
    }

    private static async Task<HubConnection> Connect(Uri uri)
    {
        var hub = new HubConnectionBuilder().WithUrl(uri).Build();
        await hub.StartAsync();
        await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        return hub;
    }

    private static async Task<WebApplication> StartServer()
    {
        var app = ServerBootstrap.Build([
            "--urls", "http://127.0.0.1:0",
            "--Multiplayer:AllowedOrigins:0", "http://localhost:5267",
            "--Logging:LogLevel:Default", "Warning"]);
        await app.StartAsync();
        return app;
    }

    private static Uri HubUri(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address + ConnectionProtocol.HubPath);
    }
}
