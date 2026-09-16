using TruyTimDanChu.Game;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public static class RankingService
{
    public static IReadOnlyList<TeamResultSnapshot> Rank(IEnumerable<TeamResultSnapshot> input)
    {
        var teams = input.ToList();
        var ordered = teams
            .OrderByDescending(x => x.Status == TeamResultStatus.Completed)
            .ThenBy(x => x.Status == TeamResultStatus.Completed ? x.ElapsedMilliseconds ?? long.MaxValue : long.MaxValue)
            .ThenBy(x => x.Status == TeamResultStatus.Completed ? x.WrongAnswerCount : int.MaxValue)
            .ThenBy(x => x.Status == TeamResultStatus.Completed ? x.PenultimateElapsedMilliseconds ?? long.MaxValue : long.MaxValue)
            .ThenBy(x => x.TeamId, StringComparer.Ordinal)
            .ToList();

        int? previousRank = null;
        TeamResultSnapshot? previous = null;
        var completedSeen = 0;
        var result = new List<TeamResultSnapshot>(ordered.Count);
        foreach (var team in ordered)
        {
            if (team.Status != TeamResultStatus.Completed)
            {
                result.Add(team with { Rank = null });
                continue;
            }

            completedSeen++;
            var tied = previous is not null &&
                       previous.ElapsedMilliseconds == team.ElapsedMilliseconds &&
                       previous.WrongAnswerCount == team.WrongAnswerCount &&
                       previous.PenultimateElapsedMilliseconds == team.PenultimateElapsedMilliseconds;
            var rank = tied ? previousRank : completedSeen;
            previousRank = rank;
            previous = team;
            result.Add(team with { Rank = rank });
        }

        return result;
    }

    public static TeamResultSnapshot FromTeam(
        TeamGameInstance game,
        TeamInstance team,
        IReadOnlyList<PlayerSession> roster,
        TeamResultStatus status)
    {
        var snapshot = game.GetSnapshot();
        var timings = snapshot.Progress.ChapterTimings is { } timingList
            ? timingList.ToArray()
            : Array.Empty<ChapterTiming>();
        var newsElapsed = timings.FirstOrDefault(x => x.Chapter == Chapter.News)?.ElapsedMilliseconds;
        return new TeamResultSnapshot(
            game.MatchId,
            game.TeamId,
            team.Name,
            team.Color,
            null,
            status,
            roster.Select(p => new ResultMemberSnapshot(p.PlayerId, p.DisplayName, p.AvatarId)).ToArray(),
            team.FinishedAtUtc,
            team.FinishedAtUtc.HasValue ? timings.LastOrDefault()?.ElapsedMilliseconds : null,
            snapshot.Progress.WrongAnswerCount,
            newsElapsed,
            timings);
    }
}
