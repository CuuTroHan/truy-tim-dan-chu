using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Server;
using TruyTimDanChu.Services;
using TruyTimDanChu.Shared;
using Xunit;

public sealed class ConnectionTests
{
    private static async Task<WebApplication> StartServer(string? address = null)
    {
        var app = ServerBootstrap.Build(["--urls", address ?? "http://127.0.0.1:0",
            "--Multiplayer:AllowedOrigins:0", "http://localhost:5267",
            "--Logging:LogLevel:Default", "Warning"]);
        await app.StartAsync();
        return app;
    }
    private static string Address(WebApplication app) => app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    private static Uri HubUri(WebApplication app) => new(Address(app) + ConnectionProtocol.HubPath);
    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }

    [Fact]
    public async Task RealSignalRHandshakeAndUnknownRoomCommand()
    {
        await using var app = await StartServer();
        await using var hub = new HubConnectionBuilder().WithUrl(HubUri(app)).Build();
        await hub.StartAsync();
        var accepted = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        Assert.True(accepted.Accepted);
        var rejected = await hub.InvokeAsync<HandshakeResponse>("Handshake", new HandshakeRequest(0, "old"));
        Assert.False(rejected.Accepted);
        Assert.Equal("PROTOCOL_MISMATCH", rejected.ErrorCode);
        var content = await hub.InvokeAsync<HandshakeResponse>("Handshake", new HandshakeRequest(ConnectionProtocol.Version, "old"));
        Assert.Equal("CONTENT_MISMATCH", content.ErrorCode);
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("CreateRoom"));
    }

    [Theory]
    [InlineData("http://localhost:5267", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task OriginIsRestrictedForNegotiationAndWebSockets(string origin, bool allowed)
    {
        await using var app = await StartServer();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, HubUri(app) + "/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", origin);
        using var response = await http.SendAsync(request);
        Assert.Equal(allowed, response.IsSuccessStatusCode);
        if (allowed) Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", origin);
        var ws = new UriBuilder(HubUri(app)) { Scheme = "ws" }.Uri;
        if (allowed) await socket.ConnectAsync(ws, CancellationToken.None);
        else await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => socket.ConnectAsync(ws, CancellationToken.None));
    }

    [Fact]
    public async Task ConnectionCanRetryAfterServerStopsAndRestarts()
    {
        var app = await StartServer();
        var address = Address(app);
        await using var connection = new MultiplayerConnection(HubUri(app), TimeSpan.FromSeconds(2));
        await connection.ConnectAsync();
        Assert.Equal(OnlineConnectionState.Connected, connection.State);
        await app.StopAsync();
        await app.DisposeAsync();
        await Eventually(() => connection.State == OnlineConnectionState.Disconnected);
        await connection.ConnectAsync();
        Assert.Equal(OnlineConnectionState.Failed, connection.State);
        await using var restarted = await StartServer(address);
        await connection.ConnectAsync();
        Assert.Equal(OnlineConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task RepeatedConnectAndNavigationDoNotDuplicateSubscriptions()
    {
        await using var app = await StartServer();
        for (var visit = 0; visit < 3; visit++)
        {
            var connection = new MultiplayerConnection(HubUri(app), TimeSpan.FromSeconds(2));
            var connectedEvents = 0;
            connection.Changed += () => { if (connection.State == OnlineConnectionState.Connected) connectedEvents++; };
            await Task.WhenAll(connection.ConnectAsync(), connection.ConnectAsync());
            await connection.ConnectAsync();
            Assert.Equal(1, connectedEvents);
            await connection.DisposeAsync();
            await connection.DisposeAsync();
            await connection.ConnectAsync();
            Assert.Equal(1, connectedEvents);
        }
    }

    [Fact]
    public async Task IncompatibleServerIsNotShownAsConnected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.MapHub<IncompatibleHub>(ConnectionProtocol.HubPath);
        await app.StartAsync();
        await using var connection = new MultiplayerConnection(HubUri(app), TimeSpan.FromSeconds(2));
        await connection.ConnectAsync();
        Assert.Equal(OnlineConnectionState.Incompatible, connection.State);
    }

    [Fact]
    public async Task StalledConnectionTimesOutAndCanBeDisposedDuringConnect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost("/hubs/room/negotiate", async context =>
        {
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        await app.StartAsync();
        var connection = new MultiplayerConnection(HubUri(app), TimeSpan.FromMilliseconds(200));
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OnlineConnectionState.Failed, connection.State);
        var attempt = connection.ConnectAsync();
        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await attempt;
    }
}

public sealed class IncompatibleHub : Hub
{
    public HandshakeResponse Handshake(HandshakeRequest request) => new(false, "CONTENT_MISMATCH", 1, "next-map");
}
