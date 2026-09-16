using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class RankingTests
{
    private static TeamResultSnapshot Team(string id, long? elapsed, int errors, long? news, TeamResultStatus status = TeamResultStatus.Completed) =>
        new("m", id, id, "#fff", null, status, Array.Empty<ResultMemberSnapshot>(),
            status == TeamResultStatus.Completed ? DateTimeOffset.UtcNow : null, elapsed, errors, news, Array.Empty<ChapterTiming>());

    [Fact]
    public void RanksByElapsedThenErrorsThenNews()
    {
        var result = RankingService.Rank(new[]
        {
            Team("slow", 200, 0, 10),
            Team("few-errors", 100, 0, 20),
            Team("more-errors", 100, 1, 1),
        });

        Assert.Equal(new int?[] { 1, 2, 3 }, result.OrderBy(x => x.TeamId).Select(x => x.Rank).ToArray());
    }

    [Fact]
    public void ExactTieUsesCompetitionRanking()
    {
        var result = RankingService.Rank(new[] { Team("a", 100, 0, 10), Team("b", 100, 0, 10), Team("c", 200, 0, 10) });
        Assert.Equal(new int?[] { 1, 1, 3 }, result.OrderBy(x => x.TeamId).Select(x => x.Rank).ToArray());
    }

    [Fact]
    public void DnfNeverReceivesWinningRank()
    {
        var result = RankingService.Rank(new[] { Team("done", 100, 0, 10), Team("dnf", null, 5, null, TeamResultStatus.TimedOut) });
        var dnf = result.Single(x => x.TeamId == "dnf");
        Assert.Null(dnf.Rank);
    }
}
