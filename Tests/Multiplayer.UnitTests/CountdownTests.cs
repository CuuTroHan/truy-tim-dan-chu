using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class CountdownTests
{
    private static (RoomManager Manager, RoomInstance Room, string AdminToken, PlayerSnapshot PlayerA, PlayerSnapshot PlayerB, TeamSnapshot Team1, TeamSnapshot Team2)
        CreateReadyRoom(DateTimeOffset? initialTime = null)
    {
        var manager = new RoomManager();
        var res = manager.CreateRoom(new CreateRoomRequest("Phòng Thi Đấu Đếm Ngược", 600));
        Assert.True(res.Success);
        var room = manager.GetRoomById(res.RoomId!)!;
        if (initialTime.HasValue)
        {
            room.UtcNowProvider = () => initialTime.Value;
        }

        var token = res.AdminToken!;

        // Add 2 teams
        var t1Res = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Đỏ", "#E53935", 2));
        var t2Res = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Xanh", "#1E88E5", 2));
        Assert.True(t1Res.Success);
        Assert.True(t2Res.Success);

        // Add 2 players
        var joinA = room.TryAddPlayer("Minh Đức", "conn_a", 40);
        var joinB = room.TryAddPlayer("Thanh Hằng", "conn_b", 40);
        Assert.True(joinA.Success);
        Assert.True(joinB.Success);

        var playerA = joinA.Player!.GetSnapshot();
        var playerB = joinB.Player!.GetSnapshot();

        // Assign teams
        manager.JoinTeam(new JoinTeamRequest(room.RoomId, playerA.PlayerId, t1Res.Team!.TeamId));
        manager.JoinTeam(new JoinTeamRequest(room.RoomId, playerB.PlayerId, t2Res.Team!.TeamId));

        // Set ready
        manager.SetReady(new SetReadyRequest(room.RoomId, playerA.PlayerId, true));
        manager.SetReady(new SetReadyRequest(room.RoomId, playerB.PlayerId, true));

        return (manager, room, token, playerA, playerB, t1Res.Team, t2Res.Team);
    }

    [Fact]
    public void AdminStartMatch_ValidLobby_TransitionsToCountdownAndSetsMatchIdAndStartTime()
    {
        var now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var (manager, room, token, _, _, _, _) = CreateReadyRoom(now);
        var initialVersion = room.Version;

        var res = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));

        Assert.True(res.Success);
        Assert.Null(res.ErrorCode);
        Assert.NotNull(res.MatchId);
        Assert.Equal(now.AddSeconds(3), res.MatchStartTimeUtc);
        Assert.Equal(RoomStatus.Countdown, room.Status);
        Assert.Equal(res.MatchId, room.MatchId);
        Assert.Equal(res.MatchStartTimeUtc, room.MatchStartTimeUtc);
        Assert.True(room.Version > initialVersion);
    }

    [Fact]
    public void AdminStartMatch_UnreadyOrInvalidLobby_FailsWithAppropriateError()
    {
        var (manager, room, token, playerA, _, _, _) = CreateReadyRoom();

        // 1. Unready player A
        manager.SetReady(new SetReadyRequest(room.RoomId, playerA.PlayerId, false));
        var res1 = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.False(res1.Success);
        Assert.Equal(RoomErrorCodes.NotReadyToStart, res1.ErrorCode);

        // 2. Ready again, but invalid admin token
        manager.SetReady(new SetReadyRequest(room.RoomId, playerA.PlayerId, true));
        var res2 = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, "wrong_token", 3));
        Assert.False(res2.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, res2.ErrorCode);
    }

    [Fact]
    public void AdminStartMatch_WhenAlreadyCountdownOrPlaying_RejectsDuplicateStart()
    {
        var (manager, room, token, _, _, _, _) = CreateReadyRoom();

        // First start -> Countdown
        var res1 = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.True(res1.Success);

        // Duplicate start during countdown -> Rejected
        var res2 = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.False(res2.Success);
        Assert.Equal(RoomErrorCodes.CountdownAlreadyActive, res2.ErrorCode);

        // Force transition to Playing
        room.TryForceAdvanceToPlaying(res1.MatchId!);
        Assert.Equal(RoomStatus.Playing, room.Status);

        // Start attempt during Playing -> Rejected
        var res3 = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.False(res3.Success);
        Assert.Equal(RoomErrorCodes.MatchAlreadyStarted, res3.ErrorCode);
    }

    [Fact]
    public void Mutations_BlockedDuringCountdown()
    {
        var (manager, room, token, playerA, _, team1, team2) = CreateReadyRoom();
        var startRes = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.True(startRes.Success);
        Assert.Equal(RoomStatus.Countdown, room.Status);

        // Mutating teams
        var a1 = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đội Vàng", "#FDD835", 2));
        Assert.False(a1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, a1.ErrorCode);

        var u1 = manager.AdminUpdateTeam(new AdminUpdateTeamRequest(room.RoomId, token, team1.TeamId, "Đội Đỏ Mới", "#E53935", 2));
        Assert.False(u1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, u1.ErrorCode);

        var r1 = manager.AdminRemoveTeam(new AdminRemoveTeamRequest(room.RoomId, token, team1.TeamId));
        Assert.False(r1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, r1.ErrorCode);

        var o1 = manager.AdminReorderTeams(new AdminReorderTeamsRequest(room.RoomId, token, [team2.TeamId, team1.TeamId]));
        Assert.False(o1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, o1.ErrorCode);

        // Player actions
        var (j1, _) = manager.JoinTeam(new JoinTeamRequest(room.RoomId, playerA.PlayerId, team2.TeamId));
        Assert.False(j1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, j1.ErrorCode);

        var (l1, _) = manager.LeaveTeam(new LeaveTeamRequest(room.RoomId, playerA.PlayerId));
        Assert.False(l1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, l1.ErrorCode);

        var v1 = manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, playerA.PlayerId, "kieu_anh"));
        Assert.False(v1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, v1.ErrorCode);

        var rd1 = manager.SetReady(new SetReadyRequest(room.RoomId, playerA.PlayerId, false));
        Assert.False(rd1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, rd1.ErrorCode);

        // Admin policy toggles
        var lk1 = manager.AdminSetJoinLock(new AdminSetJoinLockRequest(room.RoomId, token, true));
        Assert.False(lk1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, lk1.ErrorCode);

        var rk1 = manager.AdminSetRosterLock(new AdminSetRosterLockRequest(room.RoomId, token, true));
        Assert.False(rk1.Success);
        Assert.Equal(RoomErrorCodes.InvalidRoomStatus, rk1.ErrorCode);

        // New player join
        var p1 = manager.JoinRoom(new JoinRoomRequest(room.RoomCode, "Khách Mới"), "conn_c");
        Assert.False(p1.Success);
        Assert.Equal(RoomErrorCodes.JoinNotAllowed, p1.ErrorCode);
    }

    [Fact]
    public void AdminCancelCountdown_RevertsToLobbyAndInvalidatesCallbacks()
    {
        var (manager, room, token, _, _, _, _) = CreateReadyRoom();
        var startRes = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.True(startRes.Success);
        Assert.Equal(RoomStatus.Countdown, room.Status);

        // Cancel countdown
        var cancelRes = manager.AdminCancelCountdown(new AdminCancelCountdownRequest(room.RoomId, token));
        Assert.True(cancelRes.Success);
        Assert.Null(cancelRes.ErrorCode);
        Assert.Equal(RoomStatus.Lobby, room.Status);
        Assert.Null(room.MatchId);
        Assert.Null(room.MatchStartTimeUtc);

        // Attempting to cancel when not in countdown fails
        var cancelRes2 = manager.AdminCancelCountdown(new AdminCancelCountdownRequest(room.RoomId, token));
        Assert.False(cancelRes2.Success);
        Assert.Equal(RoomErrorCodes.CountdownNotActive, cancelRes2.ErrorCode);

        // Advancing the canceled match does not start
        var advanced = room.TryForceAdvanceToPlaying(startRes.MatchId!);
        Assert.False(advanced);
        Assert.Equal(RoomStatus.Lobby, room.Status);
    }

    [Fact]
    public void ClockAdvance_TransitionsToPlayingExactlyOnce()
    {
        var (manager, room, token, _, _, _, _) = CreateReadyRoom();
        var callbackCount = 0;
        string? startedMatchId = null;
        DateTimeOffset? startedTime = null;

        room.OnMatchStarted = (mId, sTime) =>
        {
            callbackCount++;
            startedMatchId = mId;
            startedTime = sTime;
        };

        var startRes = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
        Assert.True(startRes.Success);

        // Advance to playing
        var firstAdvance = room.TryForceAdvanceToPlaying(startRes.MatchId!);
        Assert.True(firstAdvance);
        Assert.Equal(RoomStatus.Playing, room.Status);
        Assert.Equal(1, callbackCount);
        Assert.Equal(startRes.MatchId, startedMatchId);
        Assert.Equal(startRes.MatchStartTimeUtc, startedTime);

        // Second advance attempt does nothing
        var secondAdvance = room.TryForceAdvanceToPlaying(startRes.MatchId!);
        Assert.False(secondAdvance);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void ConcurrentStartAttempts_OnlyOneSucceeds()
    {
        var (manager, room, token, _, _, _, _) = CreateReadyRoom();
        var successCount = 0;
        var failCount = 0;

        Parallel.For(0, 20, _ =>
        {
            var res = manager.AdminStartMatch(new AdminStartMatchRequest(room.RoomId, token, 3));
            if (res.Success) Interlocked.Increment(ref successCount);
            else if (res.ErrorCode == RoomErrorCodes.CountdownAlreadyActive) Interlocked.Increment(ref failCount);
        });

        Assert.Equal(1, successCount);
        Assert.Equal(19, failCount);
        Assert.Equal(RoomStatus.Countdown, room.Status);
    }
}
