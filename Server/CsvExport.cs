using System.Globalization;
using System.Text;
using TruyTimDanChu.Shared;
using TruyTimDanChu.Game;

namespace TruyTimDanChu.Server;

public static class CsvExport
{
    public const string Header = "Rank,Team,Members,Status,CompletionTime,WrongAnswers,LightsMs,DraftMs,RiverMs,NewsMs,FinaleMs";

    public static byte[] Build(MatchHistoryDetail detail)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF').Append(Header).Append("\r\n");
        foreach (var team in detail.Teams)
        {
            var timings = team.ChapterTimings.ToDictionary(x => x.Chapter, x => x.ElapsedMilliseconds);
            var elapsed = team.ElapsedMilliseconds.HasValue ? FormatDuration(team.ElapsedMilliseconds.Value) : string.Empty;
            var row = new[]
            {
                team.Rank?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                SafeCell(team.TeamName),
                SafeCell(string.Join("; ", team.Members.Select(x => x.DisplayName))),
                team.Status.ToString(), elapsed,
                team.WrongAnswerCount.ToString(CultureInfo.InvariantCulture),
                Timing(timings, Chapter.Lights), Timing(timings, Chapter.Draft), Timing(timings, Chapter.River),
                Timing(timings, Chapter.News), Timing(timings, Chapter.Finale)
            };
            sb.Append(string.Join(',', row.Select(Escape))).Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static string SanitizeFilename(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "match-results" : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        return name.Length > 80 ? name[..80] : name;
    }

    private static string Timing(IReadOnlyDictionary<Chapter, long> values, Chapter chapter)
        => values.TryGetValue(chapter, out var value) ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatDuration(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private static string SafeCell(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@\t\r".Contains(trimmed[0])) return "'" + value;
        return value;
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
