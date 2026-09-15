using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.IntegrationTests;

public sealed class EditTeamIntegrationTests
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
    public async Task SignalR_EditReorderRemoveTeam_BroadcastsToAllClientsInRoom()
    {
        await using var app = await StartServer();
        var uri = HubUri(app);

        // 1. Admin creates room
        await using var adminHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await adminHub.StartAsync();
        await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);

        var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest("Phòng Biên Tập Đội", 900));
        Assert.True(createRes.Success);
        var roomId = createRes.RoomId!;
        var roomCode = createRes.RoomCode!;
        var adminToken = createRes.AdminToken!;

        // 2. Player A and Player B join
        await using var playerAHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerAHub.StartAsync();
        await playerAHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        await playerAHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player A"));

        await using var playerBHub = new HubConnectionBuilder().WithUrl(uri).Build();
        await playerBHub.StartAsync();
        await playerBHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        await playerBHub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, "Player B"));

        // Event listeners on Player A
        TeamSnapshot? aUpdatedTeam = null;
        List<TeamSnapshot>? aReorderedTeams = null;
        string? aRemovedTeamId = null;

        playerAHub.On<TeamSnapshot>("TeamUpdated", t => aUpdatedTeam = t);
        playerAHub.On<List<TeamSnapshot>>("TeamsReordered", list => aReorderedTeams = list);
        playerAHub.On<string>("TeamRemoved", id => aRemovedTeamId = id);

        // 3. Admin adds Team 1 (Đỏ) and Team 2 (Xanh)
        var addRed = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Đỏ", "#E53935", 3));
        var addBlue = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam",
            new AdminAddTeamRequest(roomId, adminToken, "Đội Xanh", "#1E88E5", 5));
        Assert.True(addRed.Success && addBlue.Success);

        var idRed = addRed.Team!.TeamId;
        var idBlue = addBlue.Team!.TeamId;

        // 4. Admin updates Team 1: đổi tên "Đội Đỏ Rực", đổi màu #FB8C00, đổi sức chứa 4
        var updateRes = await adminHub.InvokeAsync<AdminUpdateTeamResponse>("AdminUpdateTeam",
            new AdminUpdateTeamRequest(roomId, adminToken, idRed, "Đội Đỏ Rực", "#FB8C00", 4));
        Assert.True(updateRes.Success);

        await Task.Delay(100);
        Assert.NotNull(aUpdatedTeam);
        Assert.Equal("Đội Đỏ Rực", aUpdatedTeam.Name);
        Assert.Equal("#FB8C00", aUpdatedTeam.Color);
        Assert.Equal(4, aUpdatedTeam.Capacity);

        // 5. Admin reorders: Đưa Xanh lên đầu [Blue, Red]
        var reorderRes = await adminHub.InvokeAsync<AdminReorderTeamsResponse>("AdminReorderTeams",
            new AdminReorderTeamsRequest(roomId, adminToken, [idBlue, idRed]));
        Assert.True(reorderRes.Success);

        await Task.Delay(100);
        Assert.NotNull(aReorderedTeams);
        Assert.Equal(2, aReorderedTeams.Count);
        Assert.Equal(idBlue, aReorderedTeams[0].TeamId);
        Assert.Equal(0, aReorderedTeams[0].DisplayOrder);
        Assert.Equal(idRed, aReorderedTeams[1].TeamId);
        Assert.Equal(1, aReorderedTeams[1].DisplayOrder);

        // 6. Admin removes Team 1 (Đỏ Rực)
        var removeRes = await adminHub.InvokeAsync<AdminRemoveTeamResponse>("AdminRemoveTeam",
            new AdminRemoveTeamRequest(roomId, adminToken, idRed));
        Assert.True(removeRes.Success);

        await Task.Delay(100);
        Assert.Equal(idRed, aRemovedTeamId);

        // 7. Non-admin unauthorized check
        var nonAdminUpdate = await playerAHub.InvokeAsync<AdminUpdateTeamResponse>("AdminUpdateTeam",
            new AdminUpdateTeamRequest(roomId, "wrong-token", idBlue, "Đội Hack", "#000000", 2));
        Assert.False(nonAdminUpdate.Success);
        Assert.Equal(RoomErrorCodes.UnauthorizedAdmin, nonAdminUpdate.ErrorCode);
    }
}

