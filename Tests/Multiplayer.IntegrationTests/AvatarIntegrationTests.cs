using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class AvatarIntegrationTests
{
    private static async Task<WebApplication> StartServer()
    {
        var app = ServerBootstrap.Build([
            "--urls", "http://127.0.0.1:0",
            "--Multiplayer:AllowedOrigins:0", "http://localhost:5267",
            "--Logging:LogLevel:Default", "Warning"
        ]);
        await app.StartAsync();
        return app;
    }

    private static Uri HubUri(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address + ConnectionProtocol.HubPath);
    }

    [Fact]
    public async Task SignalR_SelectAvatar_BroadcastsToRoomAndUpdatesRoster()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Avatar", 600));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;

        // 2. Player A joins
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinARes = await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));
        Assert.True(joinARes.Success);
        var playerAId = joinARes.PlayerId!;

        // 3. Player B joins
        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        var joinBRes = await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));
        Assert.True(joinBRes.Success);
        var playerBId = joinBRes.PlayerId!;

        // Listen for avatar change on Player B's connection
        PlayerAvatarChangedEvent? receivedEvent = null;
        var avatarTcs = new TaskCompletionSource<PlayerAvatarChangedEvent>();
        playerBHub.On<PlayerAvatarChangedEvent>("PlayerAvatarChanged", e =>
        {
            receivedEvent = e;
            avatarTcs.TrySetResult(e);
        });

        // 4. Player A selects valid avatar "han"
        var selectRes = await playerAHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar",
            new SelectAvatarRequest(roomId, playerAId, "han"));
        Assert.True(selectRes.Success);
        Assert.Equal("han", selectRes.AvatarId);

        // Verify Player B received broadcast event
        var completed = await Task.WhenAny(avatarTcs.Task, Task.Delay(2000));
        Assert.Same(avatarTcs.Task, completed);
        Assert.NotNull(receivedEvent);
        Assert.Equal(playerAId, receivedEvent!.PlayerId);
        Assert.Equal("han", receivedEvent.AvatarId);

        // 5. Player A attempts to select invalid avatar
        var invalidRes = await playerAHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar",
            new SelectAvatarRequest(roomId, playerAId, "not_a_real_avatar"));
        Assert.False(invalidRes.Success);
        Assert.Equal(RoomErrorCodes.InvalidAvatarId, invalidRes.ErrorCode);

        // 6. Player B also selects "han" -> verify discriminator
        var selectBRes = await playerBHub.InvokeAsync<SelectAvatarResponse>("SelectAvatar",
            new SelectAvatarRequest(roomId, playerBId, "han"));
        Assert.True(selectBRes.Success);
        Assert.Equal("han", selectBRes.AvatarId);

        // Inspect room snapshot from room manager
        var roomManager = app.Services.GetRequiredService<RoomManager>();
        var room = roomManager.GetRoomById(roomId)!;
        var players = room.GetPlayerSnapshots();
        var discA = AvatarCatalog.GetDiscriminator(playerAId, players);
        var discB = AvatarCatalog.GetDiscriminator(playerBId, players);

        Assert.Contains(discA, new[] { "#1", "#2" });
        Assert.Contains(discB, new[] { "#1", "#2" });
        Assert.NotEqual(discA, discB);
    }
}

