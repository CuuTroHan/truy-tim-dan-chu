using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Multiplayer.Simulation;

/// <summary>Immutable synthetic roster used by the local recorded class simulation.</summary>
public sealed record SimulationTeam(int Number, string Name, int Capacity, IReadOnlyList<SimulationSeat> Seats)
{
    public string Label => $"N{Number:00}";
}

public sealed record SimulationSeat(string DisplayName, string AvatarId, bool IsLeader, bool IsBrowserObserver);

public sealed record SimulationScenario(IReadOnlyList<SimulationTeam> Teams)
{
    public int PlayerCount => Teams.Sum(x => x.Seats.Count);
    public int BotCount => Teams.Sum(x => x.Seats.Count(x => !x.IsBrowserObserver));
    public int BrowserPlayerCount => Teams.Sum(x => x.Seats.Count(x => x.IsBrowserObserver));

    public static SimulationScenario CreateClass60()
    {
        var avatars = AvatarCatalog.All.Select(x => x.Id).ToArray();
        var teams = new List<SimulationTeam>();
        for (var teamNumber = 1; teamNumber <= 8; teamNumber++)
        {
            var capacity = teamNumber <= 4 ? 8 : 7;
            var seats = new List<SimulationSeat>();
            for (var seat = 1; seat <= capacity; seat++)
            {
                // Seats 1/2 are the two command leaders. Two real browser views record
                // the same selected teams from seats 3/4.
                var isObserver = (teamNumber is 1 or 8) && seat is 3 or 4;
                seats.Add(new SimulationSeat(
                    $"N{teamNumber:00}-TV{seat:00}",
                    avatars[(teamNumber * 10 + seat) % avatars.Length],
                    seat <= 2,
                    isObserver));
            }
            teams.Add(new SimulationTeam(teamNumber, $"Nhóm {teamNumber:00}", capacity, seats));
        }
        return new SimulationScenario(teams);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Teams.Count != 8) errors.Add("Scenario must contain exactly 8 teams.");
        if (PlayerCount != 60) errors.Add($"Scenario has {PlayerCount} players instead of 60.");
        if (BotCount != 56 || BrowserPlayerCount != 4) errors.Add($"Scenario must contain 56 bots and 4 browser players; got {BotCount}/{BrowserPlayerCount}.");
        if (Teams.Take(4).Any(x => x.Capacity != 8) || Teams.Skip(4).Any(x => x.Capacity != 7)) errors.Add("Team capacities must be 8,8,8,8,7,7,7,7.");
        if (Teams.Any(x => x.Seats.Count != x.Capacity)) errors.Add("A team roster does not match its capacity.");
        if (Teams.SelectMany(x => x.Seats).Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count() != PlayerCount) errors.Add("Synthetic display names are not unique.");
        if (Teams.Any(x => x.Seats.Count(s => s.IsLeader) != 2)) errors.Add("Every team must have exactly two leaders.");
        if (Teams.SelectMany(x => x.Seats).Any(x => AvatarCatalog.Get(x.AvatarId) is null)) errors.Add("Scenario contains an invalid avatar id.");
        return errors;
    }
}
