using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace TruyTimDanChu.Tests;

public sealed class FirstFinishTests
{
    [Fact]
    public void EndOnFirstFinish_DefaultFalse_AllowsOtherTeamToContinue()
    {
        var room = CreateRoom(false, out var red, out var blue, out var redPlayer);
        Complete(room.GetTeamGame(red)!, redPlayer);
        room.TryMarkTeamFinished(red, room.GetTeamGame(red)!.Progress.FinishedAtUtc!.Value, out _);
        Assert.False(room.TryFinalizeTeamFinished(room.MatchId!, red, out _));
        Assert.Equal(RoomStatus.Playing, room.Status);
        Assert.False(room.GetTeamGame(blue)!.Progress.FinaleDone);
    }

    [Fact]
    public void EndOnFirstFinish_True_FinishesOnceAndMarksOthersEndedEarly()
    {
        var room = CreateRoom(true, out var red, out _, out var redPlayer);
        Complete(room.GetTeamGame(red)!, redPlayer);
        room.TryMarkTeamFinished(red, room.GetTeamGame(red)!.Progress.FinishedAtUtc!.Value, out _);
        Assert.True(room.TryFinalizeTeamFinished(room.MatchId!, red, out var evt));
        Assert.Equal(MatchEndReason.FirstTeamFinished, evt!.Reason);
        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Single(evt.Results!.Teams, x => x.Status == TeamResultStatus.Completed);
        Assert.Single(evt.Results.Teams, x => x.Status == TeamResultStatus.EndedEarly);
        Assert.False(room.TryFinalizeTeamFinished(room.MatchId!, red, out _));
    }

    [Fact]
    public void EndOnFirstFinish_ToggleOnlyLobbyAndResetsReady()
    {
        var room = new RoomInstance(Guid.NewGuid().ToString("N"), "DC-TEST", "Test", "token", 900, true, false, true, false, false);
        room.TryAddTeam("Red", "#FF0000", 2, 2, 2, 10); room.TryAddTeam("Blue", "#0000FF", 2, 2, 2, 10);
        room.TryAddPlayer("PP", "conn", 10); room.TryAddPlayer("QQ", "conn2", 10);
        var p = room.GetPlayerSnapshots()[0].PlayerId; var q = room.GetPlayerSnapshots()[1].PlayerId;
        room.TryJoinTeam(p, room.GetTeamSnapshots()[0].TeamId); room.TryJoinTeam(q, room.GetTeamSnapshots()[1].TeamId);
        room.TrySetReady(p, true); room.TrySetReady(q, true);
        var result = room.TrySetEndOnFirstFinish("token", true);
        Assert.True(result.Success);
        Assert.True(room.EndOnFirstFinish);
        Assert.False(room.GetPlayer(p)!.IsReady);
        room.TrySetReady(p, true); room.TrySetReady(q, true);
        var started = room.TryStartMatch("token", 0); room.TryForceAdvanceToPlaying(started.MatchId!);
        var locked = room.TrySetEndOnFirstFinish("token", false);
        Assert.False(locked.Success);
        Assert.Equal(RoomErrorCodes.EndOnFirstFinishLocked, locked.ErrorCode);
    }

    private static RoomInstance CreateRoom(bool first, out string red, out string blue, out string player)
    {
        var room = new RoomInstance(Guid.NewGuid().ToString("N"), "DC-TEST", "Test", "token", 900, true, false, true, false, first);
        room.TryAddTeam("Red", "#FF0000", 2, 2, 2, 10);
        room.TryAddTeam("Blue", "#0000FF", 2, 2, 2, 10);
        red = room.GetTeamSnapshots()[0].TeamId; blue = room.GetTeamSnapshots()[1].TeamId;
        room.TryAddPlayer("Player", "conn", 10); room.TryAddPlayer("BluePlayer", "conn2", 10);
        player = room.GetPlayerSnapshots()[0].PlayerId;
        var bluePlayer = room.GetPlayerSnapshots()[1].PlayerId;
        room.TryJoinTeam(player, red); room.TryJoinTeam(bluePlayer, blue);
        room.TrySetReady(player, true); room.TrySetReady(bluePlayer, true);
        var start = room.TryStartMatch("token", 0); room.TryForceAdvanceToPlaying(start.MatchId!);
        return room;
    }

    private static void Complete(TeamGameInstance game, string player)
    {
        game.Progress.Chapter = Chapter.Finale;
        Array.Fill(game.Progress.Spoken, true); Array.Fill(game.Progress.Lamps, true); Array.Fill(game.Progress.Clues, true);
        game.Progress.DraftDone = true; game.Progress.RiverDone = true; game.Progress.NewsDone = true;
        game.TrySubmitFinale(player, [0, 1, 2, 3], "order", out _);
        game.TrySubmitFinale(player, [0], "s0", out _); game.TrySubmitFinale(player, [1], "s1", out _);
        game.TrySubmitFinale(player, [2], "s2", out _); game.TrySubmitFinale(player, [3], "s3", out _);
    }
}
