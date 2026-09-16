using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Playwright;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Multiplayer.Simulation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> Main()
    {
        var options = SimulationOptions.Parse(Environment.GetCommandLineArgs());
        var scenario = SimulationScenario.CreateClass60();
        var scenarioErrors = scenario.Validate();
        if (options.ValidateOnly)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { valid = scenarioErrors.Count == 0, errors = scenarioErrors, players = scenario.PlayerCount, bots = scenario.BotCount }, Json));
            return scenarioErrors.Count == 0 ? 0 : 2;
        }
        if (scenarioErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", scenarioErrors));

        Directory.CreateDirectory(options.OutputDirectory);
        Directory.CreateDirectory(options.RawVideoDirectory);
        Directory.CreateDirectory(options.ScreenshotDirectory);
        await using var artifacts = new ArtifactWriter(options.OutputDirectory);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(options.Mode == "full" ? 12 : 3));
        var ct = stop.Token;
        var bots = new List<BotClient>();
        var background = new List<Task>();
        IPlaywright? playwright = null;
        IBrowser? chromium = null;
        BrowserView? admin = null;
        var playerViews = new List<BrowserView>();
        var runStarted = Stopwatch.StartNew();
        MatchResultsSnapshot? results = null;
        var assertions = new ConcurrentBag<string>();

        try
        {
            await RequireReadyAsync(options.BaseUrl, ct);
            await artifacts.EventAsync("server-ready", new { options.BaseUrl });
            playwright = await Playwright.CreateAsync();
            chromium = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = options.Headless });
            admin = await BrowserView.CreateAsync(chromium, options, "admin", ct);
            var activeScenario = options.Mode == "dry"
                ? new SimulationScenario(scenario.Teams.Select(x => x with { Capacity = 2, Seats = x.Seats.Take(2).ToArray() }).ToArray())
                : scenario;
            var roomCode = await CreateRoomAndTeamsAsync(admin.Page, activeScenario, options, artifacts, ct);
            await artifacts.EventAsync("room-created", new { roomCode });

            // Leaders must enter first: the Development teleport endpoint moves member 0 and member 1 only.
            foreach (var team in activeScenario.Teams)
            foreach (var seat in team.Seats.Where(x => x.IsLeader))
                bots.Add(await BotClient.ConnectAsync(options.BaseUrl, roomCode, team, seat, artifacts, ct));

            if (options.Mode == "full")
            {
                var recordings = new[]
                {
                    (activeScenario.Teams[0], "team01-player1"), (activeScenario.Teams[0], "team01-player2"),
                    (activeScenario.Teams[7], "team08-player1"), (activeScenario.Teams[7], "team08-player2")
                };
                foreach (var (team, name) in recordings)
                {
                    var view = await BrowserView.CreateAsync(chromium, options, name, ct);
                    playerViews.Add(view);
                    var seat = team.Seats.Where(x => x.IsBrowserObserver).ElementAt(name.EndsWith('1') ? 0 : 1);
                    await JoinBrowserPlayerAsync(view.Page, options.BaseUrl, roomCode, team, seat, ct);
                }
                await playerViews[0].Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "lobby-team01.png"), FullPage = true });
                await playerViews[2].Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "lobby-team08.png"), FullPage = true });
            }

            foreach (var team in activeScenario.Teams)
            foreach (var seat in team.Seats.Where(x => !x.IsLeader && !x.IsBrowserObserver))
                bots.Add(await BotClient.ConnectAsync(options.BaseUrl, roomCode, team, seat, artifacts, ct));

            await AssertRosterAsync(activeScenario, bots, options, artifacts, ct);
            if (options.Mode == "full")
            {
                await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "lobby.png"), FullPage = true });
                await Task.WhenAll(playerViews.Select(x => x.Page.GetByTestId("player-ready-toggle").ClickAsync()));
            }
            await Task.WhenAll(bots.Select(x => x.ReadyAsync(ct)));
            await AssertReadyAsync(activeScenario, bots, ct);

            await admin.Page.GetByTestId("admin-start-match").ClickAsync();
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "countdown.png"), FullPage = true });
            var matchId = await WaitForMatchAsync(bots, ct);
            foreach (var bot in bots) bot.MatchId = matchId;
            await AssertStartedRosterAsync(activeScenario, bots, ct);
            await artifacts.EventAsync("match-started", new { matchId, connectedBots = bots.Count });
            await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "gameplay.png"), FullPage = true });

            foreach (var bot in bots)
            {
                background.Add(bot.HeartbeatLoopAsync(ct));
                background.Add(bot.MovementLoopAsync(ct));
            }

            var schedule = new SimulationSchedule(options.Mode == "full" ? 420 : 22);
            await schedule.WaitUntilAsync(15, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamOpeningAsync, ct);
            await schedule.WaitUntilAsync(options.Mode == "full" ? 45 : 16, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamLightsAsync, ct);
            await schedule.WaitUntilAsync(options.Mode == "full" ? 95 : 17, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamMirrorsAsync, ct);
            if (playerViews.Count > 0)
                await playerViews[0].Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "puzzle.png"), FullPage = true });
            await schedule.WaitUntilAsync(options.Mode == "full" ? 145 : 18, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamDraftAsync, ct);
            await schedule.WaitUntilAsync(options.Mode == "full" ? 195 : 19, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamRiverAsync, ct);
            await schedule.WaitUntilAsync(options.Mode == "full" ? 245 : 20, ct);
            await RunForAllTeamsAsync(activeScenario, bots, options, matchId, TeamNewsAsync, ct);
            await schedule.WaitUntilAsync(options.Mode == "full" ? 300 : 21, ct);

            // Completion is intentionally staggered to make the ranking legible on the recording.
            for (var index = 0; index < activeScenario.Teams.Count; index++)
            {
                if (options.Mode == "full") await schedule.WaitUntilAsync(340 + index * 5, ct);
                await TeamFinaleAsync(activeScenario.Teams[index], Leaders(bots, activeScenario.Teams[index]), options, matchId, ct);
            }

            results = await WaitForResultsAsync(bots, ct);
            VerifyResults(results, activeScenario, assertions);
            await artifacts.EventAsync("match-finished", new { results.MatchId, results.EndReason, teams = results.Teams.Select(x => new { x.TeamName, x.Rank, x.Status }) });
            await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "results.png"), FullPage = true });
            if (options.Mode == "full")
            {
                await ExportAndVerifyHistoryAsync(admin.Page, options, results, ct);
                await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "history.png"), FullPage = true });
            }

            await schedule.WaitUntilAsync(schedule.DurationSeconds, ct);
            VerifyClientObservations(bots, assertions);
            await artifacts.WriteLatencyAsync(bots);
            await artifacts.WriteSessionAsync(new { pass = true, mode = options.Mode, baseUrl = options.BaseUrl, durationSeconds = runStarted.Elapsed.TotalSeconds, room = "synthetic", matchId = results.MatchId, players = activeScenario.PlayerCount, bots = bots.Count, browserPlayers = activeScenario.BrowserPlayerCount, assertions = assertions.Order() });
            await artifacts.WriteReportAsync(true, options, activeScenario, results, bots, assertions, null);
            Console.WriteLine($"PASS {options.OutputDirectory}");
            return 0;
        }
        catch (Exception ex)
        {
            if (admin is not null)
            {
                try
                {
                    await admin.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.ScreenshotDirectory, "failure-admin.png"), FullPage = true });
                    await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "failure-admin.html"), await admin.Page.ContentAsync());
                }
                catch { /* Preserve the original simulation failure. */ }
            }
            await artifacts.EventAsync("run-failed", new { error = ex.GetType().Name, message = ex.Message });
            await artifacts.WriteLatencyAsync(bots);
            await artifacts.WriteSessionAsync(new { pass = false, mode = options.Mode, error = ex.Message, durationSeconds = runStarted.Elapsed.TotalSeconds });
            await artifacts.WriteReportAsync(false, options, SimulationScenario.CreateClass60(), results, bots, assertions, ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            stop.Cancel();
            try { await Task.WhenAll(background.Select(IgnoreCancellation)); } catch { }
            foreach (var bot in bots) await bot.DisposeAsync();
            foreach (var view in playerViews.AsEnumerable().Reverse()) await view.DisposeAsync();
            if (admin is not null) await admin.DisposeAsync();
            if (chromium is not null) await chromium.DisposeAsync();
            playwright?.Dispose();
        }
    }

    private static async Task RequireReadyAsync(string baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if ((await http.GetAsync(baseUrl.TrimEnd('/') + "/health", ct)).IsSuccessStatusCode && (await http.GetAsync(baseUrl.TrimEnd('/') + "/ready", ct)).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(1000, ct);
        }
        throw new InvalidOperationException("Server did not become healthy and ready within 30 seconds.");
    }

    private static async Task<string> CreateRoomAndTeamsAsync(IPage page, SimulationScenario scenario, SimulationOptions options, ArtifactWriter artifacts, CancellationToken ct)
    {
        await page.GotoAsync(options.BaseUrl.TrimEnd('/') + "/online", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
        await page.GetByTestId("create-room-name").FillAsync("Lớp mô phỏng 60 người");
        await page.GetByTestId("create-room-time-limit").FillAsync("30");
        await page.GetByTestId("create-room-submit").ClickAsync();
        await page.GetByTestId("lobby-room-code").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var roomCode = (await page.GetByTestId("lobby-room-code").TextContentAsync())?.Trim();
        if (string.IsNullOrWhiteSpace(roomCode)) throw new InvalidOperationException("Admin UI did not expose room code.");
        foreach (var team in scenario.Teams)
        {
            await page.GetByTestId("team-name-input").FillAsync(team.Name);
            await page.GetByTestId("team-capacity-input").FillAsync(team.Capacity.ToString(CultureInfo.InvariantCulture));
            await page.GetByTestId("add-team-submit").ClickAsync();
            await page.Locator("[data-testid^='team-card-']").Nth(team.Number - 1).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await artifacts.EventAsync("team-created", new { team = team.Name, team.Capacity });
        }
        if (await page.Locator("[data-testid^='team-card-']").CountAsync() != 8) throw new InvalidOperationException("Admin UI did not create eight teams.");
        return roomCode;
    }

    private static async Task JoinBrowserPlayerAsync(IPage page, string baseUrl, string roomCode, SimulationTeam team, SimulationSeat seat, CancellationToken ct)
    {
        await page.GotoAsync(baseUrl.TrimEnd('/') + "/online", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await page.GetByTestId("online-join-tab").ClickAsync(new LocatorClickOptions { Timeout = 60000 });
        await page.GetByTestId("join-room-code").FillAsync(roomCode);
        await page.GetByTestId("join-player-name").FillAsync(seat.DisplayName);
        await page.GetByTestId("join-room-submit").ClickAsync();
        var joinButtons = page.Locator("[data-testid^='join-team-']");
        await joinButtons.Nth(team.Number - 1).ClickAsync();
        await page.GetByTestId("avatar-" + seat.AvatarId).ClickAsync();
    }

    private static async Task AssertRosterAsync(SimulationScenario scenario, IReadOnlyList<BotClient> bots, SimulationOptions options, ArtifactWriter artifacts, CancellationToken ct)
    {
        if (bots.Count != scenario.BotCount) throw new InvalidOperationException($"Expected {scenario.BotCount} SignalR bots, got {bots.Count}.");
        var finalJoin = bots[^1];
        if (finalJoin.RosterCountOnJoin != scenario.PlayerCount) throw new InvalidOperationException($"Expected {scenario.PlayerCount} connected players, snapshot had {finalJoin.RosterCountOnJoin}.");
        if (finalJoin.EndOnFirstFinishOnJoin) throw new InvalidOperationException("Simulation room unexpectedly has EndOnFirstFinish=true.");
        if (scenario.PlayerCount > 64 || scenario.Teams.Any(x => x.Capacity > 8)) throw new InvalidOperationException("Scenario exceeds the simulation room limits.");
        await artifacts.EventAsync("roster-validated", new { scenario.PlayerCount, scenario.BotCount, scenario.BrowserPlayerCount, connected = finalJoin.RosterCountOnJoin, endOnFirstFinish = finalJoin.EndOnFirstFinishOnJoin, teams = scenario.Teams.Select(x => new { x.Number, x.Capacity }) });
    }

    private static async Task AssertReadyAsync(SimulationScenario scenario, IReadOnlyList<BotClient> bots, CancellationToken ct)
    {
        await Task.Delay(250, ct);
        if (bots.Any(x => !x.Ready)) throw new InvalidOperationException("At least one bot was not ready.");
        if (scenario.Teams.Any(x => x.Seats.Count != x.Capacity)) throw new InvalidOperationException("Team capacity drifted before start.");
    }

    private static async Task<string> WaitForMatchAsync(IReadOnlyList<BotClient> bots, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var bot in bots.Where(x => x.IsLeader))
            {
                var reply = await bot.Hub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(bot.RoomId, bot.PlayerId), ct);
                if (reply.Success && reply.State is not null) { bot.MatchId = reply.State.MatchId; return reply.State.MatchId; }
            }
            await Task.Delay(250, ct);
        }
        throw new InvalidOperationException("The UI start command did not produce a playing match.");
    }
    private static async Task AssertStartedRosterAsync(SimulationScenario scenario, IReadOnlyList<BotClient> bots, CancellationToken ct)
    {
        var counts = new List<int>();
        foreach (var team in scenario.Teams)
        {
            var leader = Leaders(bots, team)[0];
            var state = await leader.Hub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", new GetTeamStateRequest(leader.RoomId, leader.PlayerId), ct);
            if (!state.Success || state.State is null) throw new InvalidOperationException($"Could not read started state for {team.Name}: {state.ErrorCode}");
            if (state.State.Members.Count != team.Capacity) throw new InvalidOperationException($"{team.Name} has {state.State.Members.Count}/{team.Capacity} members after start.");
            counts.Add(state.State.Members.Count);
        }
        if (counts.Sum() != scenario.PlayerCount) throw new InvalidOperationException("Started roster does not contain the expected player total.");
    }

    private delegate Task TeamStep(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct);
    private static Task RunForAllTeamsAsync(SimulationScenario scenario, IReadOnlyList<BotClient> bots, SimulationOptions options, string matchId, TeamStep step, CancellationToken ct) =>
        Task.WhenAll(scenario.Teams.Select(team => step(team, Leaders(bots, team), options, matchId, ct)));
    private static IReadOnlyList<BotClient> Leaders(IEnumerable<BotClient> bots, SimulationTeam team) => bots.Where(x => x.TeamNumber == team.Number && x.IsLeader).OrderBy(x => x.DisplayName).ToArray();

    private static async Task TeamOpeningAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    {
        await TeleportAsync(options, leaders[0], 526, 300, ct); await InteractAsync(leaders[0], "trong", matchId, ct);
    }
    private static async Task TeamLightsAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    {
        var targets = new[] { ("phuong", 170f, 280f), ("dung", 205f, 338f), ("bao", 160f, 362f), ("nam", 242f, 308f), ("lamp0", 125f, 260f), ("lamp1", 267f, 260f), ("lamp2", 125f, 388f), ("lamp3", 267f, 388f) };
        for (var index = 0; index < targets.Length; index++) { var leader = leaders[index % 2]; await TeleportAsync(options, leader, targets[index].Item2, targets[index].Item3, ct); await InteractAsync(leader, targets[index].Item1, matchId, ct); }
    }
    private static async Task TeamMirrorsAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    {
        await TeleportAsync(options, leaders[1], 195, 323, ct); var token = await ReserveAsync(leaders[1], PuzzleIds.Mirrors, matchId, ct);
        var wrong = await SubmitAsync(leaders[1], PuzzleIds.Mirrors, token, [0, 1, 2, 3], matchId, ct); if (!wrong.Success || wrong.Correct) throw new InvalidOperationException("Mirror wrong-answer branch was not exercised.");
        await RequireCorrectAsync(leaders[1], PuzzleIds.Mirrors, token, [1, 2, 3, 0], matchId, ct);
    }
    private static async Task TeamDraftAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    { await TeleportAsync(options, leaders[0], 486, 115, ct); var token = await ReserveAsync(leaders[0], PuzzleIds.Draft, matchId, ct); await RequireCorrectAsync(leaders[0], PuzzleIds.Draft, token, [0, 1, 2, 3, 4, 5], matchId, ct); }
    private static async Task TeamRiverAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    { await TeleportAsync(options, leaders[1], 823, 325, ct); var token = await ReserveAsync(leaders[1], PuzzleIds.River, matchId, ct); await RequireCorrectAsync(leaders[1], PuzzleIds.River, token, [1, 2, 3, 0], matchId, ct); }
    private static async Task TeamNewsAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    {
        foreach (var target in new[] { ("clue0", 180f, 520f), ("clue1", 339f, 509f), ("clue2", 286f, 552f) }) { await TeleportAsync(options, leaders[0], target.Item2, target.Item3, ct); await InteractAsync(leaders[0], target.Item1, matchId, ct); }
        await TeleportAsync(options, leaders[0], 246, 516, ct); var token = await ReserveAsync(leaders[0], PuzzleIds.News, matchId, ct);
        var wrong = await SubmitAsync(leaders[0], PuzzleIds.News, token, [0], matchId, ct); if (!wrong.Success || wrong.Correct) throw new InvalidOperationException("News wrong-answer branch was not exercised.");
        await RequireCorrectAsync(leaders[0], PuzzleIds.News, token, [2], matchId, ct);
    }
    private static async Task TeamFinaleAsync(SimulationTeam team, IReadOnlyList<BotClient> leaders, SimulationOptions options, string matchId, CancellationToken ct)
    {
        await TeleportAsync(options, leaders[1], 513, 328, ct); var token = await ReserveAsync(leaders[1], PuzzleIds.Finale, matchId, ct);
        await RequireCorrectAsync(leaders[1], PuzzleIds.Finale, token, [0, 1, 2, 3], matchId, ct);
        for (var step = 0; step < 4; step++) await RequireCorrectAsync(leaders[1], PuzzleIds.Finale, token, [step], matchId, ct);
    }

    private static async Task TeleportAsync(SimulationOptions options, BotClient leader, float x, float y, CancellationToken ct)
    {
        using var http = new HttpClient();
        var url = $"{options.BaseUrl.TrimEnd('/')}/dev/set-member-positions?roomId={Uri.EscapeDataString(leader.RoomId)}&teamId={Uri.EscapeDataString(leader.TeamId)}&aX={x.ToString(CultureInfo.InvariantCulture)}&aY={y.ToString(CultureInfo.InvariantCulture)}&bX={x.ToString(CultureInfo.InvariantCulture)}&bY={y.ToString(CultureInfo.InvariantCulture)}&playerAId={Uri.EscapeDataString(leader.PlayerId)}&playerBId={Uri.EscapeDataString(leader.PlayerId)}";
        var response = await http.PostAsync(url, null, ct); response.EnsureSuccessStatusCode();
    }
    private static async Task InteractAsync(BotClient bot, string objectId, string matchId, CancellationToken ct)
    { var result = await bot.Hub.InvokeAsync<InteractResponse>("Interact", new InteractRequest(bot.RoomId, bot.PlayerId, objectId, Guid.NewGuid().ToString("N"), matchId), ct); if (!result.Success || !result.Mutated || result.State is null) throw new InvalidOperationException($"Interaction {objectId} for {bot.DisplayName} failed: {result.ErrorCode}"); await bot.WaitForTeamVersionAsync(result.State.Progress.Version, ct); }
    private static async Task<string> ReserveAsync(BotClient bot, string puzzle, string matchId, CancellationToken ct)
    { var result = await bot.Hub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle", new ReservePuzzleRequest(bot.RoomId, bot.PlayerId, puzzle, matchId), ct); return result.Success && result.ReservationToken is not null ? result.ReservationToken : throw new InvalidOperationException($"Reservation {puzzle} failed: {result.ErrorCode}"); }
    private static async Task<SubmitPuzzleResponse> SubmitAsync(BotClient bot, string puzzle, string token, int[] answer, string matchId, CancellationToken ct) { var result = await bot.Hub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle", new SubmitPuzzleRequest(bot.RoomId, bot.PlayerId, puzzle, token, answer, Guid.NewGuid().ToString("N"), matchId), ct); if (result.Mutated && result.State is not null) await bot.WaitForTeamVersionAsync(result.State.Progress.Version, ct); return result; }
    private static async Task RequireCorrectAsync(BotClient bot, string puzzle, string token, int[] answer, string matchId, CancellationToken ct)
    { var result = await SubmitAsync(bot, puzzle, token, answer, matchId, ct); if (!result.Success || !result.Correct || !result.Mutated) throw new InvalidOperationException($"Puzzle {puzzle} for {bot.DisplayName} failed: {result.ErrorCode}"); }

    private static async Task<MatchResultsSnapshot> WaitForResultsAsync(IReadOnlyList<BotClient> bots, CancellationToken ct)
    {
        var tasks = bots.Select(x => x.Results.Task).ToArray();
        var completed = await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(TimeSpan.FromSeconds(20), ct));
        if (completed is not Task<MatchResultsSnapshot[]> all) throw new InvalidOperationException("Match did not publish results after all teams finished.");
        return all.Result[0];
    }
    private static void VerifyResults(MatchResultsSnapshot results, SimulationScenario scenario, ConcurrentBag<string> assertions)
    {
        if (results.EndReason != MatchEndReason.AllTeamsFinished) throw new InvalidOperationException($"Expected AllTeamsFinished; got {results.EndReason}.");
        if (results.Teams.Count != scenario.Teams.Count || results.Teams.Any(x => x.Status != TeamResultStatus.Completed)) throw new InvalidOperationException("Results do not contain eight completed teams.");
        var ranks = results.Teams.OrderBy(x => x.Rank).Select(x => x.TeamName).ToArray();
        if (!ranks.SequenceEqual(scenario.Teams.Select(x => x.Name))) throw new InvalidOperationException("Result ranks do not match the intended 01→08 stagger.");
        assertions.Add("Results: 8 completed teams with rank N01→N08.");
    }
    private static void VerifyClientObservations(IEnumerable<BotClient> bots, ConcurrentBag<string> assertions)
    {
        if (bots.Any(x => x.CrossTeamStateEvents > 0)) throw new InvalidOperationException("A client received TeamStateUpdated for a different team.");
        if (bots.Any(x => x.PublicProgressContainedPrivatePosition)) throw new InvalidOperationException("Public progress contained private position data.");
        if (bots.Any(x => x.Disconnected)) throw new InvalidOperationException("At least one bot disconnected during the simulation.");
        assertions.Add("Team-state isolation, public-progress shape and connections were verified by all bot clients.");
    }
    private static async Task ExportAndVerifyHistoryAsync(IPage page, SimulationOptions options, MatchResultsSnapshot results, CancellationToken ct)
    {
        await page.GotoAsync(options.BaseUrl.TrimEnd('/') + "/match-history", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var match = page.GetByTestId("history-match-" + results.MatchId);
        await match.ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(async () => await page.GetByTestId("history-export-csv").ClickAsync());
        var path = Path.Combine(options.OutputDirectory, "results.csv"); await download.SaveAsAsync(path);
        if ((await File.ReadAllLinesAsync(path, ct)).Length != 9) throw new InvalidOperationException("Downloaded CSV does not contain eight result rows.");
    }
    private static async Task IgnoreCancellation(Task task) { try { await task; } catch (OperationCanceledException) { } }
}

internal sealed record SimulationOptions(string BaseUrl, string OutputDirectory, string Mode, bool Headless, bool ValidateOnly)
{
    public string RawVideoDirectory => Path.Combine(OutputDirectory, "raw-webm");
    public string ScreenshotDirectory => Path.Combine(OutputDirectory, "screenshots");
    public static SimulationOptions Parse(string[] args)
    {
        string Get(string name, string fallback) => args.FirstOrDefault(x => x.StartsWith("--" + name + "=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1] ?? fallback;
        var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return new(Get("url", "http://127.0.0.1:5099"), Get("output", Path.Combine("artifacts", "simulation", runId)), Get("mode", "full").ToLowerInvariant(), bool.TryParse(Get("headless", "true"), out var h) && h, args.Any(x => x.Equals("--validate-only", StringComparison.OrdinalIgnoreCase)));
    }
}

internal sealed class BrowserView : IAsyncDisposable
{
    private readonly IBrowserContext _context; private readonly string _finalVideoPath;
    public IPage Page { get; }
    private BrowserView(IBrowserContext context, IPage page, string finalVideoPath) { _context = context; Page = page; _finalVideoPath = finalVideoPath; }
    public static async Task<BrowserView> CreateAsync(IBrowser browser, SimulationOptions options, string name, CancellationToken ct)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 }, RecordVideoDir = options.RawVideoDirectory, RecordVideoSize = new RecordVideoSize { Width = 1280, Height = 720 }, AcceptDownloads = true });
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => File.AppendAllText(Path.Combine(options.OutputDirectory, "browser-errors.log"), $"PAGE {name}: {error}{Environment.NewLine}");
        page.Console += (_, message) => File.AppendAllText(Path.Combine(options.OutputDirectory, "browser-errors.log"), $"CONSOLE {name} {message.Type}: {message.Text}{Environment.NewLine}");
        await page.SetViewportSizeAsync(1280, 720); return new BrowserView(context, page, Path.Combine(options.RawVideoDirectory, name + ".webm"));
    }
    public async ValueTask DisposeAsync()
    {
        await _context.CloseAsync();
        if (Page.Video is null) return;
        var recorded = await Page.Video.PathAsync();
        if (File.Exists(recorded)) File.Move(recorded, _finalVideoPath, true);
    }
}

internal sealed class BotClient : IAsyncDisposable
{
    private readonly ArtifactWriter _artifacts; private readonly SemaphoreSlim _teamStateSignal = new(0); private int _sequence; private int _lastTeamVersion;
    public HubConnection Hub { get; } public string RoomId { get; } public string PlayerId { get; } public string TeamId { get; } public int TeamNumber { get; } public string DisplayName { get; } public bool IsLeader { get; } public string? MatchId { get; set; }
    public bool Ready { get; private set; } public bool Disconnected { get; private set; } public int CrossTeamStateEvents { get; private set; } public bool PublicProgressContainedPrivatePosition { get; private set; }
    public int RosterCountOnJoin { get; private set; } public IReadOnlyList<int> TeamSizesOnJoin { get; private set; } = []; public bool EndOnFirstFinishOnJoin { get; private set; }
    public ConcurrentBag<double> MovementAckMs { get; } = []; public TaskCompletionSource<MatchResultsSnapshot> Results { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BotClient(HubConnection hub, string roomId, string playerId, string teamId, SimulationTeam team, SimulationSeat seat, ArtifactWriter artifacts) { Hub = hub; RoomId = roomId; PlayerId = playerId; TeamId = teamId; TeamNumber = team.Number; DisplayName = seat.DisplayName; IsLeader = seat.IsLeader; _artifacts = artifacts; }
    public static async Task<BotClient> ConnectAsync(string baseUrl, string roomCode, SimulationTeam team, SimulationSeat seat, ArtifactWriter artifacts, CancellationToken ct)
    {
        var hub = new HubConnectionBuilder().WithUrl(baseUrl.TrimEnd('/') + ConnectionProtocol.HubPath).WithAutomaticReconnect().Build(); await hub.StartAsync(ct);
        var handshake = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current, ct); if (!handshake.Accepted) throw new InvalidOperationException("SignalR protocol handshake rejected.");
        var join = await hub.InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(roomCode, seat.DisplayName), ct); if (!join.Success || join.PlayerId is null || join.Room is null || join.Teams is null) throw new InvalidOperationException($"Join {seat.DisplayName} failed: {join.ErrorCode}");
        var teamSnapshot = join.Teams.Single(x => x.Name == team.Name);
        var client = new BotClient(hub, join.Room.RoomId, join.PlayerId, teamSnapshot.TeamId, team, seat, artifacts) { RosterCountOnJoin = join.Players?.Count ?? 0, TeamSizesOnJoin = join.Teams.Select(x => x.MemberCount).ToArray(), EndOnFirstFinishOnJoin = join.Room.EndOnFirstFinish };
        hub.On<TeamGameStateSnapshot>("TeamStateUpdated", state => { if (state.TeamId != client.TeamId) client.CrossTeamStateEvents++; else { Interlocked.Exchange(ref client._lastTeamVersion, Math.Max(Volatile.Read(ref client._lastTeamVersion), state.Progress.Version)); client._teamStateSignal.Release(); } return artifacts.EventAsync("team-state", new { client.DisplayName, state.TeamId, state.Progress.Chapter, state.Progress.Version }); });
        hub.On<PublicProgressSnapshot>("PublicProgressUpdated", progress => { if (JsonSerializer.SerializeToElement(progress).ToString().Contains("\"x\"", StringComparison.OrdinalIgnoreCase)) client.PublicProgressContainedPrivatePosition = true; return artifacts.EventAsync("public-progress", new { client.DisplayName, teams = progress.Teams.Count }); });
        hub.On<MatchFinishedEvent>("MatchFinished", ev => { if (ev.Results is not null) client.Results.TrySetResult(ev.Results); return artifacts.EventAsync("match-finished-observed", new { client.DisplayName, ev.Reason }); });
        hub.Closed += _ => { client.Disconnected = true; return Task.CompletedTask; };
        var teamJoin = await hub.InvokeAsync<JoinTeamResponse>("JoinTeam", new JoinTeamRequest(client.RoomId, client.PlayerId, client.TeamId), ct); if (!teamJoin.Success) throw new InvalidOperationException($"Team join {seat.DisplayName} failed: {teamJoin.ErrorCode}");
        var avatar = await hub.InvokeAsync<SelectAvatarResponse>("SelectAvatar", new SelectAvatarRequest(client.RoomId, client.PlayerId, seat.AvatarId), ct); if (!avatar.Success) throw new InvalidOperationException($"Avatar {seat.DisplayName} failed: {avatar.ErrorCode}");
        await artifacts.EventAsync("bot-connected", new { seat.DisplayName, team = team.Name, seat.IsLeader }); return client;
    }
    public async Task ReadyAsync(CancellationToken ct) { var r = await Hub.InvokeAsync<SetReadyResponse>("SetReady", new SetReadyRequest(RoomId, PlayerId, true), ct); if (!r.Success) throw new InvalidOperationException($"Ready {DisplayName} failed: {r.ErrorCode}"); Ready = true; }
    public async Task WaitForTeamVersionAsync(int version, CancellationToken ct) { while (Volatile.Read(ref _lastTeamVersion) < version) await _teamStateSignal.WaitAsync(ct); }
    public async Task HeartbeatLoopAsync(CancellationToken ct) { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); while (await timer.WaitForNextTickAsync(ct)) { var r = await Hub.InvokeAsync<HeartbeatResponse>("Heartbeat", new HeartbeatRequest(RoomId, PlayerId), ct); if (!r.Success) throw new InvalidOperationException($"Heartbeat {DisplayName} failed: {r.ErrorCode}"); } }
    public async Task MovementLoopAsync(CancellationToken ct) { await Task.Delay((TeamNumber * 37 + DisplayName[^1]) % 100, ct); using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); while (await timer.WaitForNextTickAsync(ct)) { if (MatchId is null) continue; var sw = Stopwatch.GetTimestamp(); var r = await Hub.InvokeAsync<MovementAck>("SendMovement", new PlayerMovementInput(RoomId, PlayerId, (_sequence++ % 4) + 1, _sequence, Environment.TickCount64, MatchId), ct); MovementAckMs.Add((Stopwatch.GetTimestamp() - sw) * 1000d / Stopwatch.Frequency); if (!r.Success) throw new InvalidOperationException($"Movement {DisplayName} failed: {r.ErrorCode}"); } }
    public async ValueTask DisposeAsync() { _teamStateSignal.Dispose(); await Hub.DisposeAsync(); }
}

internal sealed class SimulationSchedule
{
    private readonly Stopwatch _clock = Stopwatch.StartNew(); public int DurationSeconds { get; }
    public SimulationSchedule(int durationSeconds) => DurationSeconds = durationSeconds;
    public Task WaitUntilAsync(int seconds, CancellationToken ct) => Task.Delay(TimeSpan.FromSeconds(Math.Max(0, seconds - _clock.Elapsed.TotalSeconds)), ct);
}

internal sealed class ArtifactWriter : IAsyncDisposable
{
    private readonly string _output; private readonly StreamWriter _events; private readonly SemaphoreSlim _gate = new(1, 1);
    public ArtifactWriter(string output) { _output = output; _events = new StreamWriter(Path.Combine(output, "events.ndjson"), false, new UTF8Encoding(false)) { AutoFlush = true }; }
    public async Task EventAsync(string name, object payload) { await _gate.WaitAsync(); try { await _events.WriteLineAsync(JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, name, payload })); } finally { _gate.Release(); } }
    public async Task WriteSessionAsync(object session) => await File.WriteAllTextAsync(Path.Combine(_output, "session.json"), JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
    public async Task WriteLatencyAsync(IEnumerable<BotClient> bots) { var rows = bots.SelectMany(x => x.MovementAckMs.Select(v => new { x.DisplayName, x.TeamNumber, Milliseconds = v })).OrderBy(x => x.Milliseconds).ToArray(); var sb = new StringBuilder("player,team,movement_ack_ms\n"); foreach (var row in rows) sb.Append(row.DisplayName).Append(',').Append(row.TeamNumber).Append(',').Append(row.Milliseconds.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n'); await File.WriteAllTextAsync(Path.Combine(_output, "latency.csv"), sb.ToString()); }
    public async Task WriteReportAsync(bool pass, SimulationOptions options, SimulationScenario scenario, MatchResultsSnapshot? results, IEnumerable<BotClient> bots, IEnumerable<string> assertions, string? error) { var allBots = bots.ToArray(); var samples = allBots.SelectMany(x => x.MovementAckMs).OrderBy(x => x).ToArray(); double P(double q) => samples.Length == 0 ? 0 : samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * q) - 1)]; var sb = new StringBuilder(); sb.AppendLine("# BIÊN BẢN MÔ PHỎNG LỚP 60 NGƯỜI").AppendLine().AppendLine($"- Kết quả: **{(pass ? "PASS" : "FAIL")}**").AppendLine($"- Chế độ: {options.Mode}; browser headless: {options.Headless}").AppendLine($"- Roster: {scenario.PlayerCount} người, {scenario.Teams.Count} nhóm, {allBots.Length} bot SignalR, {scenario.BrowserPlayerCount} browser người chơi.").AppendLine("- Dữ liệu: hoàn toàn giả lập theo mẫu `Nxx-TVyy`; không ghi token vào artifact.").AppendLine($"- Movement ACK: p50 {P(.50):0.000} ms, p95 {P(.95):0.000} ms, p99 {P(.99):0.000} ms ({samples.Length} mẫu). Browser visual-render p95 không được tự suy diễn từ các số này.").AppendLine(); if (results is not null) { sb.AppendLine("## Kết quả").AppendLine(); foreach (var t in results.Teams.OrderBy(x => x.Rank)) sb.AppendLine($"- #{t.Rank}: {t.TeamName} — {t.Status}, lỗi {t.WrongAnswerCount}."); } sb.AppendLine().AppendLine("## Kiểm tra").AppendLine(); foreach (var a in assertions.Order()) sb.AppendLine($"- {a}"); if (error is not null) sb.AppendLine().AppendLine("## Lỗi").AppendLine().AppendLine(error); await File.WriteAllTextAsync(Path.Combine(_output, "BIEN_BAN_MO_PHONG.md"), sb.ToString()); }
    public async ValueTask DisposeAsync() { await _events.DisposeAsync(); _gate.Dispose(); }
}
