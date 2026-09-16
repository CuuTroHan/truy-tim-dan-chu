using System.Text;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using TruyTimDanChu.Game;
using Xunit;

namespace TruyTimDanChu.Tests;

public sealed class CsvExportTests
{
    [Fact]
    public void Build_UsesBomEscapingAndFormulaNeutralization()
    {
        var team = new TeamResultSnapshot("m", "t", "Đội, \"Ánh\"\nSáng", "#fff", null,
            TeamResultStatus.EndedEarly,
            new[] { new ResultMemberSnapshot("p", "=SUM(A1)", "bao") }, null, null, 2, null, Array.Empty<ChapterTiming>());
        var detail = new MatchHistoryDetail("r", "m", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, MatchEndReason.FirstTeamFinished, new[] { team });
        var csv = Encoding.UTF8.GetString(CsvExport.Build(detail));
        Assert.StartsWith("\uFEFFRank,Team,Members", csv);
        Assert.Contains("\"Đội, \"\"Ánh\"\"\nSáng\"", csv);
        Assert.Contains("'=SUM(A1)", csv);
        Assert.Contains(",EndedEarly,,2,", csv);
    }

    [Fact]
    public void SanitizeFilename_RemovesPathCharacters()
    {
        var value = CsvExport.SanitizeFilename("room/..\\secret:match");
        Assert.DoesNotContain("/", value); Assert.DoesNotContain("\\", value); Assert.DoesNotContain(":", value);
    }
}
