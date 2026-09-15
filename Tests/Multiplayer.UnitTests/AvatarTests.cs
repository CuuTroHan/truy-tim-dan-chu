using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class AvatarTests
{
    private static (RoomManager Manager, RoomInstance Room, string AdminToken) CreateTestRoom()
    {
        var manager = new RoomManager();
        var createRes = manager.CreateRoom(new CreateRoomRequest("Avatar Test Room", 600));
        Assert.True(createRes.Success);
        var room = manager.GetRoomById(createRes.RoomId!)!;
        return (manager, room, createRes.AdminToken!);
    }

    private static PlayerSnapshot AddPlayer(RoomInstance room, string name, string connId)
    {
        var joinRes = room.TryAddPlayer(name, connId, 40);
        Assert.True(joinRes.Success);
        return joinRes.Player!.GetSnapshot();
    }

    [Fact]
    public void Catalog_HasNinePipoyaAvatars_AllValid()
    {
        Assert.Equal(9, AvatarCatalog.All.Count);
        foreach (var av in AvatarCatalog.All)
        {
            Assert.True(AvatarCatalog.IsValid(av.Id));
            Assert.False(string.IsNullOrWhiteSpace(av.DisplayName));
            Assert.StartsWith("assets/pipoya/", av.AssetPath);
        }
    }

    [Theory]
    [InlineData("quang")]
    [InlineData("trong")]
    [InlineData("kieu_anh")]
    [InlineData("ninh")]
    [InlineData("phuong")]
    [InlineData("dung")]
    [InlineData("bao")]
    [InlineData("han")]
    [InlineData("nam")]
    [InlineData("QUANG")] // Case-insensitive
    public void Catalog_ValidatesWhitelistedIds(string avatarId)
    {
        Assert.True(AvatarCatalog.IsValid(avatarId));
        Assert.NotNull(AvatarCatalog.Get(avatarId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("superman")]
    [InlineData("assets/pipoya/hacked.png")]
    [InlineData("../../../etc/passwd")]
    public void Catalog_RejectsInvalidOrArbitraryIds(string? avatarId)
    {
        Assert.False(AvatarCatalog.IsValid(avatarId));
        Assert.Null(AvatarCatalog.Get(avatarId));
    }

    [Fact]
    public void DefaultAvatar_IsQuang()
    {
        Assert.Equal("quang", AvatarCatalog.DefaultAvatarId);
        var (_, room, _) = CreateTestRoom();
        var player = AddPlayer(room, "Player One", "conn-default");
        Assert.Equal("quang", player.AvatarId);
    }

    [Fact]
    public void TrySelectAvatar_Success_UpdatesPlayerAndVersion()
    {
        var (manager, room, _) = CreateTestRoom();
        var player = AddPlayer(room, "Player One", "conn-1");
        var initialVersion = room.Version;

        var res = manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, player.PlayerId, "bao"));

        Assert.True(res.Success);
        Assert.Null(res.ErrorCode);
        Assert.Equal("bao", res.AvatarId);
        Assert.Equal(initialVersion + 1, res.RoomVersion);

        var snapshot = room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId);
        Assert.Equal("bao", snapshot.AvatarId);
    }

    [Fact]
    public void TrySelectAvatar_RejectsInvalidAvatarId()
    {
        var (manager, room, _) = CreateTestRoom();
        var player = AddPlayer(room, "Player One", "conn-1");
        var initialVersion = room.Version;

        var res = manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, player.PlayerId, "malicious_sprite"));

        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.InvalidAvatarId, res.ErrorCode);
        Assert.Equal(initialVersion, room.Version);

        var snapshot = room.GetPlayerSnapshots().First(p => p.PlayerId == player.PlayerId);
        Assert.Equal("quang", snapshot.AvatarId); // Unchanged
    }

    [Fact]
    public void TrySelectAvatar_RejectsNonExistentPlayer()
    {
        var (manager, room, _) = CreateTestRoom();

        var res = manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, "non-existent-player-id", "han"));

        Assert.False(res.Success);
        Assert.Equal(RoomErrorCodes.PlayerNotFound, res.ErrorCode);
    }

    [Fact]
    public void Discriminator_AssignsStableMarkersForDuplicateAvatars()
    {
        var (manager, room, _) = CreateTestRoom();
        var p1 = AddPlayer(room, "Alice", "conn-a");
        var p2 = AddPlayer(room, "Bob", "conn-b");
        var p3 = AddPlayer(room, "Charlie", "conn-c");

        // Set Alice & Bob to 'ninh', Charlie to 'han'
        manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, p1.PlayerId, "ninh"));
        manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, p2.PlayerId, "ninh"));
        manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, p3.PlayerId, "han"));

        var snapshots = room.GetPlayerSnapshots();

        var disc1 = AvatarCatalog.GetDiscriminator(p1.PlayerId, snapshots);
        var disc2 = AvatarCatalog.GetDiscriminator(p2.PlayerId, snapshots);
        var disc3 = AvatarCatalog.GetDiscriminator(p3.PlayerId, snapshots);

        // Charlie has unique avatar -> no discriminator
        Assert.Equal("", disc3);

        // Alice and Bob share avatar -> discriminators #1 and #2
        Assert.Contains(disc1, new[] { "#1", "#2" });
        Assert.Contains(disc2, new[] { "#1", "#2" });
        Assert.NotEqual(disc1, disc2);

        // Calling multiple times is deterministic
        Assert.Equal(disc1, AvatarCatalog.GetDiscriminator(p1.PlayerId, snapshots));
        Assert.Equal(disc2, AvatarCatalog.GetDiscriminator(p2.PlayerId, snapshots));
    }

    [Fact]
    public void ConcurrentAvatarSelection_ThreadSafeAndConsistent()
    {
        var (manager, room, _) = CreateTestRoom();
        var players = Enumerable.Range(1, 8)
            .Select(i => AddPlayer(room, $"Player_{i}", $"conn-{i}"))
            .ToList();

        var avatarIds = AvatarCatalog.All.Select(a => a.Id).ToArray();

        Parallel.For(0, 50, i =>
        {
            var p = players[i % players.Count];
            var avatar = avatarIds[i % avatarIds.Length];
            manager.SelectAvatar(new SelectAvatarRequest(room.RoomId, p.PlayerId, avatar));
        });

        var snapshots = room.GetPlayerSnapshots();
        Assert.Equal(8, snapshots.Count);
        foreach (var s in snapshots)
        {
            Assert.True(AvatarCatalog.IsValid(s.AvatarId));
        }
    }
}

