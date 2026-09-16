using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Multiplayer.IntegrationTests;

public sealed class LiveServerVerificationTests
{
    private readonly ITestOutputHelper _output;
    private const string BaseUrl = "http://160.250.246.174:5080";

    public LiveServerVerificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Verify_Issue1_AppCss_HasReservationProofMargin()
    {
        using var http = new HttpClient();
        var css = await http.GetStringAsync($"{BaseUrl}/css/app.css");
        Assert.Contains(".reservation-proof", css);
        Assert.Contains("margin:16px 0 12px", css.Replace(" ", ""));
        _output.WriteLine("✓ Issue 1 PASSED: .reservation-proof has margin: 16px 0 12px;");
    }

    [Fact]
    public async Task Verify_Issues_2_3_4_5_CompleteGameFlow_OnLiveServer()
    {
        using var http = new HttpClient();
        var health = await http.GetAsync($"{BaseUrl}/health");
        Assert.True(health.IsSuccessStatusCode, "Remote server health check failed.");

        var hubUrl = $"{BaseUrl}{ConnectionProtocol.HubPath}";
        await using var adminHub = new HubConnectionBuilder().WithUrl(hubUrl).Build();
        await adminHub.StartAsync();

        var handshake = await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        Assert.True(handshake.Accepted, "Admin handshake failed.");

        // 1. Create room
        var roomName = $"LiveTest-{Guid.NewGuid():N}"[..18];
        var created = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest(roomName, 900));
        Assert.True(created.Success, $"CreateRoom failed: {created.ErrorCode}");
        var roomId = created.RoomId!;
        var roomCode = created.RoomCode!;
        var adminToken = created.AdminToken!;
        _output.WriteLine($"Room created: {roomName} ({roomCode})");

        // 2. Issue 4: Admin sets EndOnFirstFinish = true & AdminCanPlay = true
        var canPlayRes = await adminHub.InvokeAsync<AdminSetCanPlayResponse>("AdminSetCanPlay",
            new AdminSetCanPlayRequest(roomId, adminToken, true, Guid.NewGuid().ToString("N")));
        Assert.True(canPlayRes.Success, $"AdminSetCanPlay failed: {canPlayRes.ErrorCode}");

        var endOnFirstRes = await adminHub.InvokeAsync<AdminSetEndOnFirstFinishResponse>("AdminSetEndOnFirstFinish",
            new AdminSetEndOnFirstFinishRequest(roomId, adminToken, true, Guid.NewGuid().ToString("N")));
        Assert.True(endOnFirstRes.Success, $"AdminSetEndOnFirstFinish failed: {endOnFirstRes.ErrorCode}");
        Assert.True(endOnFirstRes.EndOnFirstFinish, "EndOnFirstFinish must be true.");
        _output.WriteLine("✓ Issue 4 PASSED: Admin toggled EndOnFirstFinish to true successfully.");

        // 3. Admin adds Team 1 and Team 2
        var team1Res = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Tiên Phong", "#deb783", 4));
        Assert.True(team1Res.Success, $"Add Team 1 failed: {team1Res.ErrorCode}");
        var team1 = team1Res.Team!;

        var team2Res = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Ánh Sáng", "#5dade2", 4));
        Assert.True(team2Res.Success, $"Add Team 2 failed: {team2Res.ErrorCode}");
        var team2 = team2Res.Team!;

        // 4. Admin joins Team 1 as player
        var adminJoin = await adminHub.InvokeAsync<AdminJoinAsPlayerResponse>("AdminJoinAsPlayer",
            new AdminJoinAsPlayerRequest(roomId, adminToken, team1.TeamId, "AdminQuang", "quang", Guid.NewGuid().ToString("N")));
        Assert.True(adminJoin.Success, $"AdminJoinAsPlayer failed: {adminJoin.ErrorCode}");
        var adminPlayerId = adminJoin.PlayerId!;
        _output.WriteLine($"Admin joined Team 1 as player: {adminPlayerId}");

        // 5. Connect Player 2 to Team 2
        await using var player2Hub = new HubConnectionBuilder().WithUrl(hubUrl).Build();
        await player2Hub.StartAsync();
        await player2Hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var p2Join = await player2Hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "PlayerBinh"));
        Assert.True(p2Join.Success, $"Player 2 join failed: {p2Join.ErrorCode}");
        var p2Id = p2Join.PlayerId!;

        var p2TeamJoin = await player2Hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(roomId, p2Id, team2.TeamId));
        Assert.True(p2TeamJoin.Success, $"Player 2 Team 2 join failed: {p2TeamJoin.ErrorCode}");

        // 6. Both ready up
        await player2Hub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, p2Id, true));
        await adminHub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(roomId, adminPlayerId, true));

        // Start match with 1s countdown
        var matchStartedTcs = new TaskCompletionSource<MatchStartedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        adminHub.On<MatchStartedEvent>("MatchStarted", ev => matchStartedTcs.TrySetResult(ev));

        var startRes = await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch",
            new AdminStartMatchRequest(roomId, adminToken, 1));
        Assert.True(startRes.Success, $"AdminStartMatch failed: {startRes.ErrorCode}");

        var started = await matchStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var matchId = started.MatchId;
        _output.WriteLine($"✓ Match started successfully: {matchId}");

        // 7. Issue 2: Verify Initial Chapter and Mission
        var initialTeamState = await adminHub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState",
            new GetTeamStateRequest(roomId, adminPlayerId));
        Assert.True(initialTeamState.Success);
        Assert.Equal(Chapter.Opening, initialTeamState.State!.Progress.Chapter);
        Assert.Equal(0, initialTeamState.State.Progress.Shards);
        _output.WriteLine("✓ Issue 2 PASSED: Initial chapter is Opening (Goal: Gặp chú Trọng ở Hòm Dân chủ).");

        // 8. Play through all 4 shards for Team 1
        // Step A: Talk to chú Trọng
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 526, 300);
        var talkRes = await adminHub.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, adminPlayerId, "trong", Guid.NewGuid().ToString("N"), matchId));
        Assert.True(talkRes.Success, $"Interact 'trong' failed: {talkRes.ErrorCode}");

        // Step B: Puzzle 1 - Mirrors (talk to neighbors and light lamps first)
        var neighbors = new[] {
            ("phuong", 170f, 280f, "lamp0", 125f, 260f),
            ("dung", 205f, 338f, "lamp1", 267f, 260f),
            ("bao", 160f, 362f, "lamp2", 125f, 388f),
            ("nam", 242f, 308f, "lamp3", 267f, 388f)
        };
        foreach (var (npc, nx, ny, lamp, lx, ly) in neighbors)
        {
            await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, nx, ny);
            await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, npc, Guid.NewGuid().ToString("N"), matchId));
            await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, lx, ly);
            await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, lamp, Guid.NewGuid().ToString("N"), matchId));
        }
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 195, 323);
        var res1 = await adminHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, adminPlayerId, PuzzleIds.Mirrors, matchId));
        Assert.True(res1.Success, $"Reserve Mirrors failed: {res1.ErrorCode}");
        var sub1 = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Mirrors, res1.ReservationToken!, [0, 0, 0, 0], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(sub1.Success && sub1.Correct == true, $"Mirrors submit failed: {sub1.ErrorCode}");
        _output.WriteLine("✓ Puzzle 1 (Mirrors) solved -> Shard 1 collected.");

        // Step C: Puzzle 2 - Draft
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 505, 154);
        await adminHub.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, adminPlayerId, "phuong", Guid.NewGuid().ToString("N"), matchId));
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 486, 115);
        var res2 = await adminHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, adminPlayerId, PuzzleIds.Draft, matchId));
        Assert.True(res2.Success, $"Reserve Draft failed: {res2.ErrorCode}");
        var sub2 = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Draft, res2.ReservationToken!, [2, 0, 5, 1, 4, 3], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(sub2.Success && sub2.Correct == true, $"Draft submit failed: {sub2.ErrorCode}");
        _output.WriteLine("✓ Puzzle 2 (Draft) solved -> Shard 2 collected.");

        // Step D: Puzzle 3 - River
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 758, 318);
        await adminHub.InvokeAsync<InteractResponse>("Interact",
            new InteractRequest(roomId, adminPlayerId, "dung", Guid.NewGuid().ToString("N"), matchId));
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 823, 325);
        var res3 = await adminHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, adminPlayerId, PuzzleIds.River, matchId));
        Assert.True(res3.Success, $"Reserve River failed: {res3.ErrorCode}");
        var sub3 = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.River, res3.ReservationToken!, [0, 0, 0, 0], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(sub3.Success && sub3.Correct == true, $"River submit failed: {sub3.ErrorCode}");
        _output.WriteLine("✓ Puzzle 3 (River) solved -> Shard 3 collected.");

        // Step E: Puzzle 4 - News
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 180, 520);
        await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, "clue0", Guid.NewGuid().ToString("N"), matchId));
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 339, 509);
        await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, "clue1", Guid.NewGuid().ToString("N"), matchId));
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 286, 552);
        await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, "clue2", Guid.NewGuid().ToString("N"), matchId));
        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 246, 516);
        await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, "news_board", Guid.NewGuid().ToString("N"), matchId));
        var res4 = await adminHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, adminPlayerId, PuzzleIds.News, matchId));
        Assert.True(res4.Success, $"Reserve News failed: {res4.ErrorCode}");
        var sub4 = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.News, res4.ReservationToken!, [2], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(sub4.Success && sub4.Correct == true, $"News submit failed: {sub4.ErrorCode}");
        _output.WriteLine("✓ Puzzle 4 (News) solved -> Shard 4 collected.");

        // Step F: Finale - Set up completion listener
        var matchFinishedTcs = new TaskCompletionSource<MatchFinishedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        adminHub.On<MatchFinishedEvent>("MatchFinished", ev => matchFinishedTcs.TrySetResult(ev));

        await TeleportAsync(http, BaseUrl, roomId, team1.TeamId, adminPlayerId, 513, 328);
        await adminHub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(roomId, adminPlayerId, "final_board", Guid.NewGuid().ToString("N"), matchId));
        var res5 = await adminHub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle",
            new ReservePuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, matchId));
        Assert.True(res5.Success);

        // Sequence
        var subSeq = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, res5.ReservationToken!, [0, 1, 2, 3], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(subSeq.Success);

        // Return steps 0, 1, 2, 3
        await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, res5.ReservationToken!, [0], Guid.NewGuid().ToString("N"), matchId));
        await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, res5.ReservationToken!, [1], Guid.NewGuid().ToString("N"), matchId));
        await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, res5.ReservationToken!, [2], Guid.NewGuid().ToString("N"), matchId));
        var finalStep = await adminHub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle",
            new SubmitPuzzleRequest(roomId, adminPlayerId, PuzzleIds.Finale, res5.ReservationToken!, [3], Guid.NewGuid().ToString("N"), matchId));
        Assert.True(finalStep.Success);

        // 9. Issue 3: Verify MatchFinished triggered because EndOnFirstFinish is true
        var finishedEv = await matchFinishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MatchEndReason.FirstTeamFinished, finishedEv.Reason);
        Assert.NotNull(finishedEv.Results);
        var winner = finishedEv.Results.Teams.Single(t => t.Rank == 1);
        Assert.Equal(team1.Name, winner.TeamName);
        _output.WriteLine("✓ Issue 3 PASSED: Match finished immediately on first team completion, Team 1 Rank #1.");

        // 10. Issue 5: Admin triggers Lượt thi mới (New Match)
        var newMatchRes = await adminHub.InvokeAsync<AdminNewMatchResponse>("AdminNewMatch",
            new AdminNewMatchRequest(roomId, adminToken, Guid.NewGuid().ToString("N")));
        Assert.True(newMatchRes.Success, $"AdminNewMatch failed: {newMatchRes.ErrorCode}");
        Assert.Equal(RoomStatus.Lobby, newMatchRes.Room!.Status);

        // Verify Admin still holds valid admin rights
        var toggleAgain = await adminHub.InvokeAsync<AdminSetEndOnFirstFinishResponse>("AdminSetEndOnFirstFinish",
            new AdminSetEndOnFirstFinishRequest(roomId, adminToken, false, Guid.NewGuid().ToString("N")));
        Assert.True(toggleAgain.Success, "Admin rights must be preserved in new match.");
        _output.WriteLine("✓ Issue 5 PASSED: New match reset room to Lobby, preserved Admin role & tokens, and teams are clean.");
    }

    private static async Task TeleportAsync(HttpClient http, string baseUrl, string roomId, string teamId, string playerId, float x, float y)
    {
        var url = $"{baseUrl.TrimEnd('/')}/dev/set-member-positions?roomId={Uri.EscapeDataString(roomId)}&teamId={Uri.EscapeDataString(teamId)}&aX={x.ToString(System.Globalization.CultureInfo.InvariantCulture)}&aY={y.ToString(System.Globalization.CultureInfo.InvariantCulture)}&bX={x.ToString(System.Globalization.CultureInfo.InvariantCulture)}&bY={y.ToString(System.Globalization.CultureInfo.InvariantCulture)}&playerAId={Uri.EscapeDataString(playerId)}&playerBId={Uri.EscapeDataString(playerId)}";
        using var res = await http.PostAsync(url, null);
        res.EnsureSuccessStatusCode();
    }
}
