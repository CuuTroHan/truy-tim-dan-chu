using Microsoft.Data.Sqlite;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace TruyTimDanChu.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void History_IsRoomScopedAndKeysetPaged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ttdc-history-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteMatchStore($"Data Source={path}");
            store.RecordRoomCreated(new RoomInstance("r1", "DC-1111", "One", "secret", 60, true, false, true, false, false));
            store.RecordRoomCreated(new RoomInstance("r2", "DC-2222", "Two", "other", 60, true, false, true, false, false));
            using var db = new SqliteConnection($"Data Source={path}"); db.Open();
            for (var i = 0; i < 3; i++)
            {
                using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO Matches(MatchId,RoomId,Status,StartedAtUtc,EndedAtUtc,TimeLimitSeconds,EndReason,CreatedAtUtc) VALUES($id,'r1','Timeout',$s,$e,60,'Timeout',$s)";
                var start = DateTimeOffset.UtcNow.AddMinutes(-10 - i); var end = start.AddMinutes(1); cmd.Parameters.AddWithValue("$id", $"m{i}"); cmd.Parameters.AddWithValue("$s", start.ToString("O")); cmd.Parameters.AddWithValue("$e", end.ToString("O")); cmd.ExecuteNonQuery();
            }
            Assert.True(store.VerifyRoomAdmin("r1", "secret")); Assert.False(store.VerifyRoomAdmin("r2", "secret"));
            var first = store.ReadHistoryPage(new MatchHistoryRequest("r1", "secret", 2));
            Assert.Equal(2, first.Items.Count); Assert.True(first.HasMore);
            var second = store.ReadHistoryPage(new MatchHistoryRequest("r1", "secret", 2, first.NextCursorEndedAtUtc, first.NextCursorMatchId));
            Assert.Single(second.Items); Assert.NotEqual(first.Items[0].MatchId, second.Items[0].MatchId);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
