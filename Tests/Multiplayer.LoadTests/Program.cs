using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TruyTimDanChu.Shared;

var url = Arg("url", "http://localhost:5000")!;
var roomCode = Arg("room-code", "");
var count = int.TryParse(Arg("clients", "20"), out var c) ? Math.Clamp(c, 1, 100) : 20;
var duration = int.TryParse(Arg("duration-seconds", "30"), out var d) ? Math.Max(1, d) : 30;
var errors = 0; var rtts = new List<double>(); var clients = new List<(HubConnection Hub, string? RoomId, string? PlayerId, string? MatchId)>();
try
{
    for (var i = 0; i < count; i++)
    {
        var hub = new HubConnectionBuilder().WithUrl(url.TrimEnd('/') + ConnectionProtocol.HubPath).Build();
        await hub.StartAsync();
        var hs = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
        if (!hs.Accepted) errors++;
        string? joinedRoom = null, joinedPlayer = null, joinedMatch = null;
        if (!string.IsNullOrWhiteSpace(roomCode))
        {
            var joined = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, $"Load-{i:00}"));
            if (!joined.Success) errors++;
            joinedRoom = joined.Room?.RoomId; joinedPlayer = joined.PlayerId; joinedMatch = joined.Room?.MatchId;
        }
        clients.Add((hub, joinedRoom, joinedPlayer, joinedMatch));
    }
    var sw = Stopwatch.StartNew(); var seq = 0;
    while (sw.Elapsed < TimeSpan.FromSeconds(duration))
    {
        foreach (var client in clients)
        {
            var hub = client.Hub;
            if (hub.State != HubConnectionState.Connected) { errors++; continue; }
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (client.RoomId is not null && client.PlayerId is not null)
                    await hub.InvokeAsync<MovementAck>("SendMovement", new PlayerMovementInput(client.RoomId, client.PlayerId, 1, seq++, Environment.TickCount64, client.MatchId));
                rtts.Add((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
            }
            catch { errors++; }
        }
        await Task.Delay(100);
    }
}
finally { foreach (var client in clients) await client.Hub.DisposeAsync(); }
var ordered = rtts.OrderBy(x => x).ToArray();
double P(double q) => ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(q * ordered.Length) - 1)];
Console.WriteLine(JsonSerializer.Serialize(new { clients = count, durationSeconds = duration, totalMessages = rtts.Count, errors, p50RttMs = P(.50), p95RttMs = P(.95), p99RttMs = P(.99), note = "Use with a real room fixture and collect server CPU/memory/network separately." }, new JsonSerializerOptions { WriteIndented = true }));

static string? Arg(string name, string? fallback) { var key = "--" + name + "="; var arg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase)); return arg is null ? fallback : arg[key.Length..]; }
