using System.Text.Json;
using Microsoft.Data.Sqlite;
using TruyTimDanChu.Shared;
using TruyTimDanChu.Game;

namespace TruyTimDanChu.Server;

public interface IMatchStore
{
    void RecordRoomCreated(RoomInstance room);
    void RecordMatchStarted(RoomInstance room);
    void AppendEvent(string matchId, string roomId, string? teamId, string? playerId,
        string eventType, string? commandId, object payload);
    void FinalizeMatch(MatchResultsSnapshot results);
    int MarkActiveMatchesInterrupted(DateTimeOffset serverStartedAtUtc);
    MatchResultsSnapshot? ReadResults(string matchId);
    bool VerifyRoomAdmin(string roomId, string adminToken);
    MatchHistoryPage ReadHistoryPage(MatchHistoryRequest request);
    MatchHistoryDetail? ReadHistoryDetail(string roomId, string matchId);
    void UpdateAdminTokenHash(string roomId, string tokenHash);
    bool CheckReady(out string? error);
}

/// <summary>Small transactional SQLite store for match history. It intentionally stores allow-listed audit data only.</summary>
public sealed class SqliteMatchStore : IMatchStore
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    static SqliteMatchStore()
    {
        // Microsoft.Data.Sqlite ships the native provider through the bundle package;
        // initialize it explicitly for console/test hosts as well as ASP.NET.
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteMatchStore(string? connectionString = null)
    {
        var value = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=data/truytimdanchu.db"
            : connectionString;
        if (value.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            var path = value[12..].Trim();
            if (!string.Equals(path, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            }
        }
        _connectionString = value;
        Initialize();
    }

    public void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS Rooms(
                    RoomId TEXT PRIMARY KEY, RoomCode TEXT NOT NULL UNIQUE, RoomName TEXT NOT NULL,
                    AdminTokenHash TEXT NOT NULL, TimeLimitSeconds INTEGER NOT NULL,
                    AllowSelfTeamSelection INTEGER NOT NULL, AllowLateJoin INTEGER NOT NULL,
                    ShowLiveLeaderboard INTEGER NOT NULL, AdminCanPlay INTEGER NOT NULL,
                    EndOnFirstFinish INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, ClosedAtUtc TEXT NULL);
                CREATE TABLE IF NOT EXISTS Matches(
                    MatchId TEXT PRIMARY KEY, RoomId TEXT NOT NULL, Status TEXT NOT NULL,
                    StartedAtUtc TEXT NOT NULL, EndedAtUtc TEXT NULL, TimeLimitSeconds INTEGER NOT NULL,
                    EndReason TEXT NULL, CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY(RoomId) REFERENCES Rooms(RoomId));
                CREATE INDEX IF NOT EXISTS IX_Matches_RoomId ON Matches(RoomId);
                CREATE TABLE IF NOT EXISTS MatchTeams(
                    MatchId TEXT NOT NULL, TeamId TEXT NOT NULL, TeamName TEXT NOT NULL, TeamColor TEXT NOT NULL,
                    DisplayOrder INTEGER NOT NULL, ResultStatus TEXT NULL, Rank INTEGER NULL,
                    FinishedAtUtc TEXT NULL, ElapsedMilliseconds INTEGER NULL, WrongAnswerCount INTEGER NOT NULL,
                    PenultimateElapsedMilliseconds INTEGER NULL,
                    PRIMARY KEY(MatchId, TeamId), FOREIGN KEY(MatchId) REFERENCES Matches(MatchId));
                CREATE TABLE IF NOT EXISTS MatchRoster(
                    MatchId TEXT NOT NULL, PlayerId TEXT NOT NULL, TeamId TEXT NULL,
                    DisplayName TEXT NOT NULL, AvatarId TEXT NOT NULL, DisplayOrder INTEGER NOT NULL,
                    PRIMARY KEY(MatchId, PlayerId), FOREIGN KEY(MatchId) REFERENCES Matches(MatchId));
                CREATE TABLE IF NOT EXISTS ChapterTimings(
                    MatchId TEXT NOT NULL, TeamId TEXT NOT NULL, Chapter TEXT NOT NULL,
                    CompletedAtUtc TEXT NOT NULL, ElapsedMilliseconds INTEGER NOT NULL,
                    PRIMARY KEY(MatchId, TeamId, Chapter), FOREIGN KEY(MatchId) REFERENCES Matches(MatchId));
                CREATE TABLE IF NOT EXISTS MatchEventLog(
                    EventId TEXT PRIMARY KEY, MatchId TEXT NOT NULL, RoomId TEXT NOT NULL,
                    TeamId TEXT NULL, PlayerId TEXT NULL, EventType TEXT NOT NULL, CommandId TEXT NULL,
                    ServerTimestampUtc TEXT NOT NULL, PayloadJson TEXT NOT NULL,
                    FOREIGN KEY(MatchId) REFERENCES Matches(MatchId));
                CREATE UNIQUE INDEX IF NOT EXISTS UX_MatchEvent_Command
                    ON MatchEventLog(MatchId, EventType, CommandId) WHERE CommandId IS NOT NULL;
                """;
            command.ExecuteNonQuery();
        }
    }

    public void RecordRoomCreated(RoomInstance room)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO Rooms(RoomId,RoomCode,RoomName,AdminTokenHash,TimeLimitSeconds,
                    AllowSelfTeamSelection,AllowLateJoin,ShowLiveLeaderboard,AdminCanPlay,EndOnFirstFinish,CreatedAtUtc)
                VALUES($id,$code,$name,$hash,$limit,$self,$late,$live,$play,$first,$created)
                """;
            command.Parameters.AddWithValue("$id", room.RoomId);
            command.Parameters.AddWithValue("$code", room.RoomCode);
            command.Parameters.AddWithValue("$name", room.RoomName);
            command.Parameters.AddWithValue("$hash", room.AdminTokenHash);
            command.Parameters.AddWithValue("$limit", room.TimeLimitSeconds);
            command.Parameters.AddWithValue("$self", room.AllowSelfTeamSelection ? 1 : 0);
            command.Parameters.AddWithValue("$late", room.AllowLateJoin ? 1 : 0);
            command.Parameters.AddWithValue("$live", room.ShowLiveLeaderboard ? 1 : 0);
            command.Parameters.AddWithValue("$play", room.AdminCanPlay ? 1 : 0);
            command.Parameters.AddWithValue("$first", room.EndOnFirstFinish ? 1 : 0);
            command.Parameters.AddWithValue("$created", room.CreatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void RecordMatchStarted(RoomInstance room)
    {
        var snapshot = room.GetSnapshot();
        if (snapshot.MatchId is null || snapshot.MatchStartTimeUtc is null) return;
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO Matches(MatchId,RoomId,Status,StartedAtUtc,TimeLimitSeconds,CreatedAtUtc)
                VALUES($match,$room,'Active',$started,$limit,$created)
                """, ("$match", snapshot.MatchId), ("$room", room.RoomId),
                ("$started", snapshot.MatchStartTimeUtc.Value.ToString("O")),
                ("$limit", room.TimeLimitSeconds), ("$created", DateTimeOffset.UtcNow.ToString("O")));
            var teams = room.GetTeamSnapshots();
            foreach (var team in teams)
            {
                Execute(connection, transaction, """
                    INSERT OR IGNORE INTO MatchTeams(MatchId,TeamId,TeamName,TeamColor,DisplayOrder,WrongAnswerCount)
                    VALUES($match,$team,$name,$color,$order,0)
                    """, ("$match", snapshot.MatchId), ("$team", team.TeamId), ("$name", team.Name),
                    ("$color", team.Color), ("$order", team.DisplayOrder));
            }
            var players = room.GetPlayerSnapshots();
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                Execute(connection, transaction, """
                    INSERT OR IGNORE INTO MatchRoster(MatchId,PlayerId,TeamId,DisplayName,AvatarId,DisplayOrder)
                    VALUES($match,$player,$team,$name,$avatar,$order)
                    """, ("$match", snapshot.MatchId), ("$player", player.PlayerId),
                    ("$team", (object?)player.TeamId ?? DBNull.Value), ("$name", player.DisplayName),
                    ("$avatar", player.AvatarId), ("$order", i));
            }
            transaction.Commit();
        }
    }

    public void AppendEvent(string matchId, string roomId, string? teamId, string? playerId,
        string eventType, string? commandId, object payload)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO MatchEventLog(EventId,MatchId,RoomId,TeamId,PlayerId,EventType,CommandId,ServerTimestampUtc,PayloadJson)
                VALUES($id,$match,$room,$team,$player,$type,$command,$time,$payload)
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$match", matchId);
            command.Parameters.AddWithValue("$room", roomId);
            command.Parameters.AddWithValue("$team", (object?)teamId ?? DBNull.Value);
            command.Parameters.AddWithValue("$player", (object?)playerId ?? DBNull.Value);
            command.Parameters.AddWithValue("$type", eventType);
            command.Parameters.AddWithValue("$command", (object?)commandId ?? DBNull.Value);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload));
            command.ExecuteNonQuery();
        }
    }

    public void FinalizeMatch(MatchResultsSnapshot results)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "UPDATE Matches SET Status=$status, EndedAtUtc=$ended, EndReason=$reason WHERE MatchId=$match",
                ("$status", results.EndReason.ToString()), ("$ended", results.FinishedAtUtc.ToString("O")),
                ("$reason", results.EndReason.ToString()), ("$match", results.MatchId));
            foreach (var team in results.Teams)
            {
                Execute(connection, transaction, """
                    UPDATE MatchTeams SET ResultStatus=$status,Rank=$rank,FinishedAtUtc=$finished,
                        ElapsedMilliseconds=$elapsed,WrongAnswerCount=$wrong,PenultimateElapsedMilliseconds=$penultimate
                    WHERE MatchId=$match AND TeamId=$team
                    """, ("$status", team.Status.ToString()), ("$rank", (object?)team.Rank ?? DBNull.Value),
                    ("$finished", (object?)team.FinishedAtUtc?.ToString("O") ?? DBNull.Value),
                    ("$elapsed", (object?)team.ElapsedMilliseconds ?? DBNull.Value), ("$wrong", team.WrongAnswerCount),
                    ("$penultimate", (object?)team.PenultimateElapsedMilliseconds ?? DBNull.Value),
                    ("$match", results.MatchId), ("$team", team.TeamId));
                foreach (var timing in team.ChapterTimings)
                    Execute(connection, transaction, """
                        INSERT OR IGNORE INTO ChapterTimings(MatchId,TeamId,Chapter,CompletedAtUtc,ElapsedMilliseconds)
                        VALUES($match,$team,$chapter,$completed,$elapsed)
                        """, ("$match", results.MatchId), ("$team", team.TeamId),
                        ("$chapter", timing.Chapter.ToString()), ("$completed", timing.CompletedAtUtc.ToString("O")),
                        ("$elapsed", timing.ElapsedMilliseconds));
            }
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO MatchEventLog(EventId,MatchId,RoomId,EventType,ServerTimestampUtc,PayloadJson)
                VALUES($id,$match,$room,'MatchFinished',$time,$payload)
                """, ("$id", Guid.NewGuid().ToString("N")), ("$match", results.MatchId),
                ("$room", results.RoomId), ("$time", results.FinishedAtUtc.ToString("O")),
                ("$payload", JsonSerializer.Serialize(new { results.EndReason, results.FinishedAtUtc })));
            transaction.Commit();
        }
    }

    public int MarkActiveMatchesInterrupted(DateTimeOffset serverStartedAtUtc)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Matches SET Status='Interrupted', EndReason='Interrupted', EndedAtUtc=$time WHERE Status IN ('Active','Paused')";
            command.Parameters.AddWithValue("$time", serverStartedAtUtc.ToString("O"));
            return command.ExecuteNonQuery();
        }
    }

    public MatchResultsSnapshot? ReadResults(string matchId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RoomId,MatchId,StartedAtUtc,EndedAtUtc,EndReason FROM Matches WHERE MatchId=$match";
            command.Parameters.AddWithValue("$match", matchId);
            string roomId, storedMatchId, started, ended, reason;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read() || reader.IsDBNull(3)) return null;
                roomId = reader.GetString(0); storedMatchId = reader.GetString(1);
                started = reader.GetString(2); ended = reader.GetString(3); reason = reader.GetString(4);
            }
            var teams = new List<TeamResultSnapshot>();
            using var teamsCommand = connection.CreateCommand();
            teamsCommand.CommandText = "SELECT TeamId,TeamName,TeamColor,Rank,ResultStatus,FinishedAtUtc,ElapsedMilliseconds,WrongAnswerCount,PenultimateElapsedMilliseconds FROM MatchTeams WHERE MatchId=$match ORDER BY DisplayOrder";
            teamsCommand.Parameters.AddWithValue("$match", matchId);
            using var teamReader = teamsCommand.ExecuteReader();
            while (teamReader.Read())
            {
                var teamId = teamReader.GetString(0);
                var members = ReadRoster(connection, storedMatchId, teamId);
                var timings = ReadTimings(connection, storedMatchId, teamId);
                teams.Add(new TeamResultSnapshot(storedMatchId, teamId, teamReader.GetString(1), teamReader.GetString(2),
                    teamReader.IsDBNull(3) ? null : teamReader.GetInt32(3),
                    teamReader.IsDBNull(4) ? TeamResultStatus.Abandoned : Enum.Parse<TeamResultStatus>(teamReader.GetString(4)),
                    members, teamReader.IsDBNull(5) ? null : DateTimeOffset.Parse(teamReader.GetString(5)),
                    teamReader.IsDBNull(6) ? null : teamReader.GetInt64(6), teamReader.GetInt32(7),
                    teamReader.IsDBNull(8) ? null : teamReader.GetInt64(8), timings));
            }
            return new MatchResultsSnapshot(roomId, storedMatchId, DateTimeOffset.Parse(started), DateTimeOffset.Parse(ended),
                Enum.Parse<MatchEndReason>(reason), teams);
        }
    }

    public bool VerifyRoomAdmin(string roomId, string adminToken)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(adminToken)) return false;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT AdminTokenHash FROM Rooms WHERE RoomId=$room";
            command.Parameters.AddWithValue("$room", roomId);
            var hash = command.ExecuteScalar() as string;
            return hash is not null && RoomInstance.VerifyAdminTokenHash(hash, adminToken);
        }
    }

    public void UpdateAdminTokenHash(string roomId, string tokenHash)
    {
        lock (_gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Rooms SET AdminTokenHash=$hash WHERE RoomId=$room";
            command.Parameters.AddWithValue("$hash", tokenHash); command.Parameters.AddWithValue("$room", roomId); command.ExecuteNonQuery();
        }
    }

    public bool CheckReady(out string? error)
    {
        try
        {
            lock (_gate)
            {
                using var connection = Open(); using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1 FROM Rooms LIMIT 1"; _ = command.ExecuteScalar();
                error = null; return true;
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        { error = ex.GetType().Name; return false; }
    }

    public MatchHistoryPage ReadHistoryPage(MatchHistoryRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var cursor = request.CursorEndedAtUtc.HasValue && !string.IsNullOrWhiteSpace(request.CursorMatchId);
            command.CommandText = $"""
                SELECT m.MatchId,m.StartedAtUtc,m.EndedAtUtc,m.EndReason,
                       COUNT(t.TeamId),SUM(CASE WHEN t.ResultStatus='Completed' THEN 1 ELSE 0 END)
                FROM Matches m LEFT JOIN MatchTeams t ON t.MatchId=m.MatchId
                WHERE m.RoomId=$room AND m.EndedAtUtc IS NOT NULL
                {(cursor ? "AND (m.EndedAtUtc < $cursorTime OR (m.EndedAtUtc = $cursorTime AND m.MatchId < $cursorId))" : "")}
                GROUP BY m.MatchId,m.StartedAtUtc,m.EndedAtUtc,m.EndReason
                ORDER BY m.EndedAtUtc DESC,m.MatchId DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$room", request.RoomId);
            command.Parameters.AddWithValue("$limit", pageSize + 1);
            if (cursor)
            {
                command.Parameters.AddWithValue("$cursorTime", request.CursorEndedAtUtc!.Value.ToString("O"));
                command.Parameters.AddWithValue("$cursorId", request.CursorMatchId!);
            }
            var items = new List<MatchHistoryItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(new MatchHistoryItem(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                    Enum.TryParse<MatchEndReason>(reader.IsDBNull(3) ? null : reader.GetString(3), out var reason) ? reason : MatchEndReason.Interrupted,
                    reader.GetInt32(4), reader.IsDBNull(5) ? 0 : reader.GetInt32(5)));
            var more = items.Count > pageSize;
            if (more) items.RemoveAt(items.Count - 1);
            var last = items.LastOrDefault();
            return new MatchHistoryPage(items, more ? last?.EndedAtUtc : null, more ? last?.MatchId : null, more);
        }
    }

    public MatchHistoryDetail? ReadHistoryDetail(string roomId, string matchId)
    {
        var result = ReadResults(matchId);
        if (result is null || !string.Equals(result.RoomId, roomId, StringComparison.Ordinal)) return null;
        return new MatchHistoryDetail(result.RoomId, result.MatchId, result.StartedAtUtc, result.FinishedAtUtc, result.EndReason, result.Teams);
    }

    private static IReadOnlyList<ResultMemberSnapshot> ReadRoster(SqliteConnection connection, string matchId, string teamId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PlayerId,DisplayName,AvatarId FROM MatchRoster WHERE MatchId=$match AND TeamId=$team ORDER BY DisplayOrder";
        command.Parameters.AddWithValue("$match", matchId); command.Parameters.AddWithValue("$team", teamId);
        using var reader = command.ExecuteReader(); var items = new List<ResultMemberSnapshot>();
        while (reader.Read()) items.Add(new ResultMemberSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return items;
    }

    private static IReadOnlyList<ChapterTiming> ReadTimings(SqliteConnection connection, string matchId, string teamId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Chapter,CompletedAtUtc,ElapsedMilliseconds FROM ChapterTimings WHERE MatchId=$match AND TeamId=$team ORDER BY CompletedAtUtc";
        command.Parameters.AddWithValue("$match", matchId); command.Parameters.AddWithValue("$team", teamId);
        using var reader = command.ExecuteReader(); var items = new List<ChapterTiming>();
        while (reader.Read()) if (Enum.TryParse<Chapter>(reader.GetString(0), out var chapter)) items.Add(new ChapterTiming(chapter, DateTimeOffset.Parse(reader.GetString(1)), reader.GetInt64(2)));
        return items;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
