using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class TeamIsolationTests
{
    [Fact]
    public void TeamGameInstance_Isolation_MembersContainedOnlyInTheirOwnTeam()
    {
        var teamA = new TeamGameInstance("match1", "room1", "teamA", "Đỏ", "#E53935");
        var teamB = new TeamGameInstance("match1", "room1", "teamB", "Xanh", "#1E88E5");

        teamA.AddMember("pA", "Player A", "bao", 500f, 366f);
        teamA.AddMember("pB", "Player B", "dung", 518f, 366f);

        teamB.AddMember("pC", "Player C", "phuong", 500f, 366f);

        var snapA = teamA.GetSnapshot();
        var snapB = teamB.GetSnapshot();

        // Team A snapshot must only contain pA and pB
        Assert.Equal(2, snapA.Members.Count);
        Assert.Contains(snapA.Members, m => m.PlayerId == "pA");
        Assert.Contains(snapA.Members, m => m.PlayerId == "pB");
        Assert.DoesNotContain(snapA.Members, m => m.PlayerId == "pC");

        // Team B snapshot must only contain pC
        Assert.Single(snapB.Members);
        Assert.Contains(snapB.Members, m => m.PlayerId == "pC");
        Assert.DoesNotContain(snapB.Members, m => m.PlayerId == "pA");
        Assert.DoesNotContain(snapB.Members, m => m.PlayerId == "pB");
    }

    [Fact]
    public void RoomSnapshot_DoesNotLeakPlayerPositionsOrQuestProgress()
    {
        var manager = new RoomManager();
        var res = manager.CreateRoom(new CreateRoomRequest("Phòng Test", 900));
        var room = manager.GetRoomById(res.RoomId!)!;
        var token = res.AdminToken!;

        var tRes = manager.AdminAddTeam(new AdminAddTeamRequest(room.RoomId, token, "Đỏ", "#E53935", 2));
        var joinA = room.TryAddPlayer("Player A", "connA", 40);
        manager.JoinTeam(new JoinTeamRequest(room.RoomId, joinA.Player!.PlayerId, tRes.Team!.TeamId));

        var snap = room.GetSnapshot();

        // Public room snapshot only contains high level metadata, no coordinates or quest progress
        Assert.Equal(room.RoomId, snap.RoomId);
        Assert.Equal(room.RoomCode, snap.RoomCode);
        Assert.Equal(RoomStatus.Lobby, snap.Status);
    }

    [Fact]
    public void MultiplayerSession_CameraFollowsLocalPlayer_AndTeammatesAreNotNpcs()
    {
        var session = new MultiplayerSession("pA", "Player A", "bao");
        var snapshot = new TeamGameStateSnapshot(
            "m1", "t1", "Đỏ", "#E53935",
            new List<TeamMemberState>
            {
                new("pA", "Player A", "bao", "#E53935", 500f, 366f, 0, 0, true),
                new("pB", "Player B", "dung", "#E53935", 518f, 366f, 0, 0, true)
            },
            new TeamProgressSnapshot(Chapter.Opening, new bool[4], new bool[4], new bool[3], 1, 0)
        );

        session.ApplySnapshot(snapshot);

        // Frame generated at initial spawn
        var frame = session.Tick(100.0, 0);

        // Camera must be centered on local player (500 - 240 = 260, 366 - 135 = 231)
        Assert.Equal(500f, frame.X);
        Assert.Equal(366f, frame.Y);
        Assert.Equal(260f, frame.CameraX);
        Assert.Equal(231f, frame.CameraY);

        // Teammate pB must be in Actors with kind = "teammate"
        var teammateActor = frame.Actors.FirstOrDefault(a => a.Name == "Player B");
        Assert.NotNull(teammateActor);
        Assert.Equal("teammate", teammateActor.Kind);
        Assert.Equal("dung", teammateActor.Id);

        // Teammate must NOT be in Objects (so cannot be confused with interactable NPCs or boards)
        Assert.DoesNotContain(frame.Objects, o => o.Label == "Player B");
    }

    [Fact]
    public void IndependentTeamInstances_HaveSameMapBounds_ButIndependentStates()
    {
        var teamA = new TeamGameInstance("m1", "r1", "tA", "Đỏ", "#E53935");
        var teamB = new TeamGameInstance("m1", "r1", "tB", "Xanh", "#1E88E5");

        teamA.AddMember("pA", "Player A", "bao", 500f, 366f);
        teamB.AddMember("pC", "Player C", "phuong", 500f, 366f);

        // Progress mutation on Team A
        teamA.Progress.Spoken[0] = true;
        teamA.Progress.Version++;

        var snapA = teamA.GetSnapshot();
        var snapB = teamB.GetSnapshot();

        Assert.True(snapA.Progress.Spoken[0]);
        Assert.Equal(2, snapA.Progress.Version);

        // Team B must remain untouched
        Assert.False(snapB.Progress.Spoken[0]);
        Assert.Equal(1, snapB.Progress.Version);
    }
}
