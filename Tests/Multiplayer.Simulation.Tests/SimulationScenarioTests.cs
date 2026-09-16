using TruyTimDanChu.Multiplayer.Simulation;
using Xunit;

namespace TruyTimDanChu.Multiplayer.Simulation.Tests;

public sealed class SimulationScenarioTests
{
    [Fact]
    public void Class60_has_exact_roster_capacity_and_client_mix()
    {
        var scenario = SimulationScenario.CreateClass60();
        Assert.Empty(scenario.Validate());
        Assert.Equal(new[] { 8, 8, 8, 8, 7, 7, 7, 7 }, scenario.Teams.Select(x => x.Capacity));
        Assert.Equal(60, scenario.PlayerCount);
        Assert.Equal(56, scenario.BotCount);
        Assert.Equal(4, scenario.BrowserPlayerCount);
    }

    [Fact]
    public void Class60_uses_unique_synthetic_structured_names()
    {
        var names = SimulationScenario.CreateClass60().Teams.SelectMany(x => x.Seats).Select(x => x.DisplayName).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Matches("^N(0[1-8])-TV(0[1-8])$", name));
    }
}
