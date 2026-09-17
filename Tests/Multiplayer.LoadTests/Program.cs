using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TruyTimDanChu.Shared;

var url = Arg("url", "https://www.truytimdanchu.site")!.TrimEnd('/');
var count = int.TryParse(Arg("clients", "60"), out var c) ? Math.Clamp(c, 1, 100) : 60;
var duration = int.TryParse(Arg("duration-seconds", "15"), out var d) ? Math.Max(1, d) : 15;
var mode = Arg("mode", "all")!.ToLowerInvariant(); // "http", "signalr", or "all"

Console.WriteLine($"================================================================================");
Console.WriteLine($"  GIẢ LẬP TẢI {count} NGƯỜI DÙNG ĐỒNG THỜI - TRUY TÌM DÂN CHỦ");
Console.WriteLine($"  Target: {url}");
Console.WriteLine($"  Thời gian test: {duration}s | Mode: {mode}");
Console.WriteLine($"================================================================================\n");

// -------------------------------------------------------------------------
// PHẦN 1: GIẢ LẬP 60 NGƯỜI TẢI WEB ĐỒNG THỜI (HTTP STATIC ASSETS CONCURRENCY)
// -------------------------------------------------------------------------
if (mode is "all" or "http")
{
    Console.WriteLine($"[PHẦN 1] Bắt đầu kiểm tra {count} người dùng đồng thời mở web và tải tài nguyên...");
    var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli | DecompressionMethods.Deflate,
        MaxConnectionsPerServer = 100,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

    var assetsToTest = new[]
    {
        "/",
        "/service-worker.js",
        "/css/app.css",
        "/assets/characters-v2.webp"
    };

    var httpSw = Stopwatch.StartNew();
    var results = new ConcurrentBag<(string Asset, int Status, long Bytes, double Ms, bool IsGzip, string? CacheControl)>();

    Console.WriteLine($"-> Đang kích hoạt {count} luồng tải đồng thời ({count * assetsToTest.Length} requests)...");
    var userTasks = Enumerable.Range(1, count).Select(async userId =>
    {
        foreach (var asset in assetsToTest)
        {
            var reqSw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url + asset);
                req.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                using var res = await http.SendAsync(req);
                var contentBytes = await res.Content.ReadAsByteArrayAsync();
                reqSw.Stop();

                var isGzip = res.Content.Headers.ContentEncoding.Any(x => x.Contains("gzip", StringComparison.OrdinalIgnoreCase));
                var cc = res.Headers.CacheControl?.ToString();
                results.Add((asset, (int)res.StatusCode, contentBytes.Length, reqSw.Elapsed.TotalMilliseconds, isGzip, cc));
            }
            catch (Exception ex)
            {
                reqSw.Stop();
                results.Add((asset, 0, 0, reqSw.Elapsed.TotalMilliseconds, false, ex.Message));
            }
        }
    });

    await Task.WhenAll(userTasks);
    httpSw.Stop();

    var allResults = results.ToList();
    var successCount = allResults.Count(r => r.Status == 200);
    var failCount = allResults.Count(r => r.Status != 200);
    var totalBytes = allResults.Sum(r => r.Bytes);
    var latencies = allResults.Where(r => r.Status == 200).Select(r => r.Ms).OrderBy(x => x).ToList();

    double Pct(List<double> list, double q) => list.Count == 0 ? 0 : list[Math.Min(list.Count - 1, (int)Math.Ceiling(q * list.Count) - 1)];

    Console.WriteLine($"\n--- KẾT QUẢ TẢI WEB ({count} CLIENTS ĐỒNG THỜI) ---");
    Console.WriteLine($"  Tổng số request:       {allResults.Count}");
    Console.WriteLine($"  Thành công (200 OK):    {successCount} ({(successCount * 100.0 / allResults.Count):F1}%)");
    Console.WriteLine($"  Thất bại / Lỗi:        {failCount}");
    Console.WriteLine($"  Tổng dữ liệu tải:      {(totalBytes / 1024.0 / 1024.0):F2} MB");
    Console.WriteLine($"  Tổng thời gian hoàn tất: {httpSw.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  Độ trễ trung bình:     {(latencies.Count > 0 ? latencies.Average() : 0):F0} ms");
    Console.WriteLine($"  Độ trễ P50:            {Pct(latencies, 0.50):F0} ms");
    Console.WriteLine($"  Độ trễ P95:            {Pct(latencies, 0.95):F0} ms");
    Console.WriteLine($"  Độ trễ Max:            {(latencies.Count > 0 ? latencies.Max() : 0):F0} ms");

    if (failCount > 0)
    {
        Console.WriteLine($"  [CẢNH BÁO] Có {failCount} request lỗi:");
        foreach (var f in allResults.Where(r => r.Status != 200).Take(5))
            Console.WriteLine($"    {f.Asset} -> Status {f.Status} ({f.CacheControl})");
    }
    else
    {
        Console.WriteLine($"  ✓ TẤT CẢ {allResults.Count} REQUEST THÀNH CÔNG 100% (KHÔNG CÓ LỖI 503 HOẶC TIMEOUT)!");
    }
    Console.WriteLine("--------------------------------------------------------------------------------\n");
}

// -------------------------------------------------------------------------
// PHẦN 2: GIẢ LẬP 60 NGƯỜI CHƠI SIGNALR REALTIME (WEBSOCKET + MESSAGEPACK)
// -------------------------------------------------------------------------
if (mode is "all" or "signalr")
{
    Console.WriteLine($"[PHẦN 2] Bắt đầu kiểm tra {count} người chơi kết nối đồng thời vào phòng SignalR...");

    // 1. Tạo Hub cho Admin
    var adminHub = new HubConnectionBuilder()
        .WithUrl(url + ConnectionProtocol.HubPath, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
        })
        .AddMessagePackProtocol()
        .Build();

    await adminHub.StartAsync();
    var adminHs = await adminHub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
    if (!adminHs.Accepted) throw new InvalidOperationException("Admin handshake bị từ chối.");

    // Tạo phòng 70 người
    var roomName = $"Sim-{Guid.NewGuid().ToString("N")[..6]}";
    var createRes = await adminHub.InvokeAsync<CreateRoomResponse>("CreateRoom", new CreateRoomRequest(
        roomName, 600, true, true, true, true, false));
    if (!createRes.Success || createRes.Snapshot is null || createRes.RoomId is null || createRes.RoomCode is null || createRes.AdminToken is null)
        throw new InvalidOperationException($"Không thể tạo phòng: {createRes.ErrorCode}");

    var roomId = createRes.RoomId;
    var roomCode = createRes.RoomCode;
    var adminToken = createRes.AdminToken;
    Console.WriteLine($"-> Đã tạo phòng: {roomName} (Mã phòng: {roomCode})");

    // Thêm các đội để đủ chỗ cho 60 người (ví dụ: 8 đội x 8 người = 64 chỗ)
    Console.WriteLine("-> Đang tạo 8 đội chơi...");
    var teamIds = new List<string>();
    // Lấy team mặc định nếu có
    var teamRes1 = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam", new AdminAddTeamRequest(
        roomId, adminToken, "Đội 1", "#4f8fba", 8));
    if (teamRes1.Success && teamRes1.Team is not null) teamIds.Add(teamRes1.Team.TeamId);

    for (var t = 2; t <= 8; t++)
    {
        var addRes = await adminHub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam", new AdminAddTeamRequest(
            roomId, adminToken, $"Đội {t}", "#e5a14d", 8));
        if (addRes.Success && addRes.Team is not null) teamIds.Add(addRes.Team.TeamId);
    }
    Console.WriteLine($"-> Đã tạo {teamIds.Count} đội (Tổng sức chứa: {teamIds.Count * 8} người chơi)");

    // 2. Cho 59 người chơi kết nối và vào phòng ĐỒNG THỜI
    Console.WriteLine($"-> Đang cho {count - 1} người chơi kết nối WebSocket & vào phòng đồng thời...");
    var playerHubs = new ConcurrentBag<(HubConnection Hub, string PlayerId, string TeamId)>();
    var connectErrors = new ConcurrentBag<string>();
    var connectSw = Stopwatch.StartNew();

    var joinTasks = Enumerable.Range(1, count - 1).Select(async i =>
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var hub = new HubConnectionBuilder()
                    .WithUrl(url + ConnectionProtocol.HubPath, options =>
                    {
                        options.Transports = HttpTransportType.WebSockets;
                    })
                    .AddMessagePackProtocol()
                    .Build();

                await hub.StartAsync();
                var hs = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current);
                if (!hs.Accepted) { connectErrors.Add($"User {i}: Handshake rejected"); return; }

                var joinRes = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(
                    roomCode, $"Player-{i:00}"));
                if (!joinRes.Success || joinRes.PlayerId is null)
                {
                    connectErrors.Add($"User {i}: Join failed ({joinRes.ErrorCode})");
                    return;
                }

                // Chọn đội (phân bổ đều vào các đội)
                var targetTeamId = teamIds[i % teamIds.Count];
                await hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(
                    roomId, joinRes.PlayerId, targetTeamId));

                playerHubs.Add((hub, joinRes.PlayerId, targetTeamId));
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 2) connectErrors.Add($"User {i}: {ex.Message}");
                else await Task.Delay(500);
            }
        }
    });

    await Task.WhenAll(joinTasks);
    connectSw.Stop();

    Console.WriteLine($"-> Kết nối hoàn tất trong {connectSw.Elapsed.TotalSeconds:F2}s!");
    Console.WriteLine($"   Thành công: {playerHubs.Count + 1}/{count} người (1 Admin + {playerHubs.Count} Players)");
    if (connectErrors.Count > 0)
    {
        Console.WriteLine($"   Lỗi kết nối ({connectErrors.Count}):");
        foreach (var err in connectErrors.Take(3)) Console.WriteLine($"     - {err}");
    }

    // 3. Admin bắt đầu trận đấu
    Console.WriteLine("-> Admin bấm bắt đầu trận đấu...");
    var startRes = await adminHub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch", new AdminStartMatchRequest(
        roomId, adminToken, 0));
    if (!startRes.Success || startRes.MatchId is null) throw new InvalidOperationException($"Không thể bắt đầu trận đấu: {startRes.ErrorCode}");
    var matchId = startRes.MatchId;
    Console.WriteLine($"✓ Trận đấu đã bắt đầu! MatchId: {matchId}");

    // 4. Giả lập toàn bộ người chơi di chuyển liên tục (realtime input stream)
    Console.WriteLine($"-> Đang giả lập {playerHubs.Count} người chơi gửi tọa độ di chuyển liên tục trong {duration}s...");
    var rtts = new ConcurrentBag<double>();
    var movementErrors = 0;
    var allClients = playerHubs.ToList();
    var moveSw = Stopwatch.StartNew();

    var playerMoveTasks = allClients.Select(async client =>
    {
        var localSeq = 0;
        var endAt = DateTime.UtcNow.AddSeconds(duration);
        while (DateTime.UtcNow < endAt)
        {
            if (client.Hub.State == HubConnectionState.Connected)
            {
                var sendStart = Stopwatch.GetTimestamp();
                try
                {
                    await client.Hub.InvokeAsync<MovementAck>("SendMovement", new PlayerMovementInput(
                        roomId, client.PlayerId, (byte)(1 + (localSeq % 4)), localSeq++, Environment.TickCount64, matchId));
                    var elapsedMs = (Stopwatch.GetTimestamp() - sendStart) * 1000.0 / Stopwatch.Frequency;
                    rtts.Add(elapsedMs);
                }
                catch
                {
                    Interlocked.Increment(ref movementErrors);
                }
            }
            await Task.Delay(100); // 10Hz tick rate
        }
    });

    await Task.WhenAll(playerMoveTasks);
    moveSw.Stop();

    // Dọn dẹp kết nối
    Console.WriteLine("-> Đang đóng các kết nối an toàn...");
    await Task.WhenAll(allClients.Select(c => c.Hub.DisposeAsync().AsTask()));
    await adminHub.DisposeAsync();

    var sortedRtts = rtts.OrderBy(x => x).ToList();
    double Pct2(List<double> list, double q) => list.Count == 0 ? 0 : list[Math.Min(list.Count - 1, (int)Math.Ceiling(q * list.Count) - 1)];

    Console.WriteLine($"\n--- KẾT QUẢ REALTIME SIGNALR ({count} CLIENTS ĐỒNG THỜI) ---");
    Console.WriteLine($"  Số client kết nối:       {playerHubs.Count + 1}/{count}");
    Console.WriteLine($"  Tổng số gói Movement:    {sortedRtts.Count}");
    Console.WriteLine($"  Lỗi gói tin Movement:    {movementErrors}");
    Console.WriteLine($"  Tỷ lệ tin nhắn thành công: {(sortedRtts.Count > 0 ? (sortedRtts.Count * 100.0 / (sortedRtts.Count + movementErrors)) : 0):F2}%");
    Console.WriteLine($"  Ping/RTT trung bình:     {(sortedRtts.Count > 0 ? sortedRtts.Average() : 0):F1} ms");
    Console.WriteLine($"  Ping/RTT P50:            {Pct2(sortedRtts, 0.50):F1} ms");
    Console.WriteLine($"  Ping/RTT P95:            {Pct2(sortedRtts, 0.95):F1} ms");
    Console.WriteLine($"  Ping/RTT P99:            {Pct2(sortedRtts, 0.99):F1} ms");
    Console.WriteLine($"  Ping/RTT Max:            {(sortedRtts.Count > 0 ? sortedRtts.Max() : 0):F1} ms");
    Console.WriteLine("================================================================================\n");

    if (connectErrors.Count == 0 && movementErrors == 0)
    {
        Console.WriteLine(">>> KẾT LUẬN: HỆ THỐNG CHỊU TẢI 60 NGƯỜI CHƠI ĐỒNG THỜI HOÀN HẢO! KHÔNG CÓ LỖI NÀO! <<<");
    }
    else
    {
        Console.WriteLine($">>> KẾT LUẬN: Đã hoàn tất mô phỏng với {connectErrors.Count} lỗi kết nối và {movementErrors} lỗi movement. <<<");
    }
}

static string? Arg(string name, string? fallback)
{
    var key = "--" + name + "=";
    var arg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase));
    return arg is null ? fallback : arg[key.Length..];
}
