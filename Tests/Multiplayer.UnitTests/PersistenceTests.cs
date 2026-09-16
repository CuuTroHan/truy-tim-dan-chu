using Microsoft.Data.Sqlite;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class PersistenceTests
{
    [Fact]
    public void RoomAndMatchAreStoredWithoutRawAdminToken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ttdc-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteMatchStore($"Data Source={path}");
            var room = new RoomInstance("room-1", "DC-1234", "Persist", "raw-admin-token", 60, true, false, true, false, false);
            store.RecordRoomCreated(room);
            store.RecordMatchStarted(room);

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT AdminTokenHash FROM Rooms WHERE RoomId='room-1'";
                var hash = (string)command.ExecuteScalar()!;
                Assert.NotEqual("raw-admin-token", hash);
                Assert.Equal(PlayerSession.HashToken("raw-admin-token"), hash);
            }
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void FinalizeAndRestartMarkActiveInterrupted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ttdc-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteMatchStore($"Data Source={path}");
            var room = new RoomInstance("room-1", "DC-1234", "Persist", "token", 60, true, false, true, false, false);
            store.RecordRoomCreated(room);
            var results = new MatchResultsSnapshot("room-1", "match-1", DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow,
                MatchEndReason.Timeout, Array.Empty<TeamResultSnapshot>());
            using (var seed = new SqliteConnection($"Data Source={path}"))
            {
                seed.Open();
                using var insert = seed.CreateCommand();
                insert.CommandText = "INSERT INTO Matches(MatchId,RoomId,Status,StartedAtUtc,TimeLimitSeconds,CreatedAtUtc) VALUES('match-1','room-1','Active',$now,60,$now)";
                insert.Parameters.AddWithValue("$now", results.StartedAtUtc.ToString("O"));
                insert.ExecuteNonQuery();
            }
            // Finalize is idempotent even when no MatchTeams rows exist.
            store.FinalizeMatch(results);
            Assert.NotNull(store.ReadResults("match-1"));

            var activeRoom = new RoomInstance("room-2", "DC-5678", "Persist", "token", 60, true, false, true, false, false);
            store.RecordRoomCreated(activeRoom);
            // RecordMatchStarted requires a match snapshot; interruption behavior is verified at SQL level below.
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Matches(MatchId,RoomId,Status,StartedAtUtc,TimeLimitSeconds,CreatedAtUtc) VALUES('active-1','room-2','Active',$now,60,$now)";
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
                Assert.Equal(1, store.MarkActiveMatchesInterrupted(DateTimeOffset.UtcNow));
                command.CommandText = "SELECT Status FROM Matches WHERE MatchId='active-1'";
                Assert.Equal("Interrupted", command.ExecuteScalar());
            }
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
