using System.Collections.Concurrent;
using TruyTimDanChu.Game;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class TeamMemberSession
{
    public string PlayerId { get; }
    public string DisplayName { get; }
    public string AvatarId { get; }
    public string AccentColor { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Facing { get; set; }
    public int Walking { get; set; }
    public bool IsConnected { get; set; } = true;
    public int LastProcessedSequence { get; set; }
    public long LastSimulatedTime { get; set; }
    public long LastInputTimestamp { get; set; }
    public const long InputTtlMs = 150;

    public TeamMemberSession(string playerId, string displayName, string avatarId, string accentColor,
        float spawnX = 500f, float spawnY = 366f)
    {
        PlayerId = playerId;
        DisplayName = displayName;
        AvatarId = avatarId;
        AccentColor = accentColor;
        X = spawnX;
        Y = spawnY;
    }

    public TeamMemberState ToState() =>
        new(PlayerId, DisplayName, AvatarId, AccentColor, X, Y, Facing, Walking, IsConnected);

    public bool ProcessMovement(int keys, int sequence, long serverTimeMs, out MovementAck ack)
    {
        if (sequence <= LastProcessedSequence)
        {
            ack = new MovementAck(false, "OUTDATED_SEQUENCE", LastProcessedSequence, X, Y, Facing, Walking, serverTimeMs);
            return false;
        }

        float dtSeconds;
        if (LastSimulatedTime <= 0)
        {
            dtSeconds = 0.016f;
        }
        else
        {
            var dtMs = Math.Clamp(serverTimeMs - LastSimulatedTime, 0, 50);
            dtSeconds = (float)(dtMs / 1000.0);
        }

        if (keys == 0)
        {
            Walking = 0;
            LastSimulatedTime = serverTimeMs;
            LastProcessedSequence = sequence;
            LastInputTimestamp = serverTimeMs;
            ack = new MovementAck(true, null, sequence, X, Y, Facing, Walking, serverTimeMs);
            return true;
        }

        TownCollision.SimulateStep(X, Y, keys, dtSeconds, out var newX, out var newY, out var facing, out var walking, Facing);
        X = newX;
        Y = newY;
        Facing = facing;
        Walking = walking;

        LastSimulatedTime = serverTimeMs;
        LastProcessedSequence = sequence;
        LastInputTimestamp = serverTimeMs;

        ack = new MovementAck(true, null, sequence, X, Y, Facing, Walking, serverTimeMs);
        return true;
    }

    public bool CheckTtl(long serverTimeMs)
    {
        if (Walking != 0 && (serverTimeMs - LastInputTimestamp > InputTtlMs))
        {
            Walking = 0;
            return true;
        }
        return false;
    }
}

public sealed class TeamGameInstance
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, TeamMemberSession> _members = new();

    public string MatchId { get; }
    public string RoomId { get; }
    public string TeamId { get; }
    public string TeamName { get; }
    public string TeamColor { get; }
    public DateTimeOffset MatchStartedAtUtc { get; }
    public Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;
    public Func<long> ElapsedMillisecondsProvider { get; set; }
    public TeamProgress Progress { get; } = new();

    public int MemberCount => _members.Count;
    public IReadOnlyList<string> MemberIds => _members.Keys.ToList();

    public TeamGameInstance(string matchId, string roomId, string teamId, string teamName, string teamColor,
        DateTimeOffset? matchStartedAtUtc = null)
    {
        MatchId = matchId;
        RoomId = roomId;
        TeamId = teamId;
        TeamName = teamName;
        TeamColor = teamColor;
        MatchStartedAtUtc = matchStartedAtUtc ?? DateTimeOffset.UtcNow;
        ElapsedMillisecondsProvider = () => Math.Max(0, (long)(UtcNowProvider() - MatchStartedAtUtc).TotalMilliseconds);
    }

    public void AddMember(string playerId, string displayName, string avatarId, float spawnX = 500f, float spawnY = 366f)
    {
        var session = new TeamMemberSession(playerId, displayName, avatarId, TeamColor, spawnX, spawnY);
        _members[playerId] = session;
    }

    public TeamMemberSession? GetMember(string playerId)
    {
        _members.TryGetValue(playerId, out var session);
        return session;
    }

    public bool HasMember(string playerId) => _members.ContainsKey(playerId);

    public bool CanReservePuzzle(string playerId, string puzzleId, out string? errorCode)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(playerId, out var member))
            {
                errorCode = InteractErrorCodes.PlayerNotInTeam;
                return false;
            }

            if (!PuzzleIds.IsKnown(puzzleId))
            {
                errorCode = PuzzleReservationErrorCodes.InvalidPuzzle;
                return false;
            }

            var objectId = puzzleId switch
            {
                PuzzleIds.Mirrors => "mirror_board",
                PuzzleIds.Draft => "draft_board",
                PuzzleIds.River => "river_board",
                PuzzleIds.News => "news_board",
                PuzzleIds.Finale => "final_board",
                _ => string.Empty
            };

            var place = TownCollision.GetInteractionPlace(objectId, Progress.Chapter);
            if (string.IsNullOrEmpty(place.Kind))
            {
                errorCode = PuzzleReservationErrorCodes.InvalidPuzzle;
                return false;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                errorCode = PuzzleReservationErrorCodes.TeamAlreadyFinished;
                return false;
            }

            var distance = MathF.Sqrt((member.X - place.X) * (member.X - place.X) +
                                      (member.Y - place.Y) * (member.Y - place.Y));
            if (distance > TownCollision.MaxInteractionDistance)
            {
                errorCode = InteractErrorCodes.OutOfRange;
                return false;
            }

            if (puzzleId == PuzzleIds.Mirrors &&
                (Progress.Chapter != Chapter.Lights || !Progress.Lamps.All(x => x)))
            {
                errorCode = PuzzleReservationErrorCodes.PrerequisiteNotMet;
                return false;
            }

            if (puzzleId == PuzzleIds.Draft && Progress.Chapter != Chapter.Draft)
            {
                errorCode = PuzzleReservationErrorCodes.PrerequisiteNotMet;
                return false;
            }

            if (puzzleId == PuzzleIds.River && (Progress.Chapter != Chapter.River || Progress.RiverDone))
            {
                errorCode = PuzzleReservationErrorCodes.PrerequisiteNotMet;
                return false;
            }

            if (puzzleId == PuzzleIds.News && (Progress.Chapter != Chapter.News || Progress.NewsDone))
            {
                errorCode = PuzzleReservationErrorCodes.PrerequisiteNotMet;
                return false;
            }

            if (puzzleId == PuzzleIds.Finale && (Progress.Chapter != Chapter.Finale || Progress.FinaleDone))
            {
                errorCode = PuzzleReservationErrorCodes.PrerequisiteNotMet;
                return false;
            }

            errorCode = null;
            return true;
        }
    }

    public void UpdateMemberPosition(string playerId, float x, float y, int facing, int walking)
    {
        if (_members.TryGetValue(playerId, out var session))
        {
            session.X = x;
            session.Y = y;
            session.Facing = facing;
            session.Walking = walking;
        }
    }

    public void SetMemberConnection(string playerId, bool isConnected)
    {
        if (_members.TryGetValue(playerId, out var session))
        {
            session.IsConnected = isConnected;
        }
    }

    public bool TryProcessMovement(string playerId, int keys, int sequence, long serverTimeMs,
        out MovementAck ack, out PlayerMovedBroadcast? broadcast)
    {
        if (!_members.TryGetValue(playerId, out var session))
        {
            ack = new MovementAck(false, RoomErrorCodes.PlayerNotFound, sequence, 0, 0, 0, 0, serverTimeMs);
            broadcast = null;
            return false;
        }

        lock (_gate)
        {
            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                ack = new MovementAck(false, PuzzleReservationErrorCodes.TeamAlreadyFinished, sequence, session.X, session.Y, session.Facing, 0, serverTimeMs);
                broadcast = null;
                return false;
            }
        }

        lock (session)
        {
            var success = session.ProcessMovement(keys, sequence, serverTimeMs, out ack);
            if (success)
            {
                broadcast = new PlayerMovedBroadcast(playerId, session.X, session.Y, session.Facing, session.Walking, sequence, serverTimeMs);
            }
            else
            {
                broadcast = null;
            }
            return success;
        }
    }

    private readonly Dictionary<(string PlayerId, string ObjectId, string CommandId), InteractResponse> _processedInteractCommands = new();
    private readonly Queue<(string PlayerId, string ObjectId, string CommandId)> _processedInteractCommandOrder = new();
    private const int MaxProcessedInteractCommands = 500;
    private readonly Dictionary<(string PlayerId, string PuzzleId, string CommandId), SubmitPuzzleResponse> _processedPuzzleCommands = new();
    private readonly Queue<(string PlayerId, string PuzzleId, string CommandId)> _processedPuzzleCommandOrder = new();

    public bool TryGetInteractCommandReplay(string playerId, string objectId, string? commandId, out InteractResponse response)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                var cacheKey = (playerId, objectId, commandId);
                if (_processedInteractCommands.TryGetValue(cacheKey, out var cached))
                {
                    response = cached with { Mutated = false, State = GetSnapshot() };
                    return true;
                }
            }

            response = new InteractResponse(false, "INVALID_COMMAND_ID", objectId, false, null);
            return false;
        }
    }

    public bool TryGetPuzzleCommandReplay(string playerId, string puzzleId, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                var cacheKey = (playerId, puzzleId, commandId);
                if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
                {
                    response = cached with { Mutated = false, State = GetSnapshot() };
                    return true;
                }
            }

            response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                false, false, Progress.WrongAnswerCount, null);
            return false;
        }
    }

    public bool TrySubmitMirrors(string playerId, int[]? answer, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!_members.ContainsKey(playerId))
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.PlayerNotInTeam,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 100)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var cacheKey = (playerId, PuzzleIds.Mirrors, commandId);
            if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
            {
                response = cached with { Mutated = false, State = GetSnapshot() };
                return true;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (Progress.Chapter != Chapter.Lights)
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.InvalidChapter,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (!Progress.Spoken.All(x => x) || !Progress.Lamps.All(x => x))
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.PrerequisiteNotMet,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (answer is null || answer.Length != 4 || answer.Any(value => value is < 0 or > 3))
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var correct = answer.SequenceEqual(new[] { 1, 2, 3, 0 });
            if (correct)
            {
                var completedAt = UtcNowProvider();
                Progress.Chapter = Chapter.Draft;
                if (!Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Lights))
                {
                    Progress.ChapterTimings.Add(new ChapterTiming(
                        Chapter.Lights,
                        completedAt,
                        ElapsedMillisecondsProvider()));
                }
            }
            else
            {
                Progress.WrongAnswerCount++;
            }

            Progress.Version++;
            response = new SubmitPuzzleResponse(true, null, correct, true,
                Progress.WrongAnswerCount, GetSnapshot());
            _processedPuzzleCommands[cacheKey] = response with { State = null };
            _processedPuzzleCommandOrder.Enqueue(cacheKey);
            while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                _processedPuzzleCommands.Remove(oldest);
            return true;
        }
    }

    public bool TrySubmitDraft(string playerId, int[]? answer, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!_members.ContainsKey(playerId))
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.PlayerNotInTeam,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 100)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var cacheKey = (playerId, PuzzleIds.Draft, commandId);
            if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
            {
                response = cached with { Mutated = false, State = GetSnapshot() };
                return true;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (Progress.Chapter != Chapter.Draft)
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.InvalidChapter,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (answer is null || answer.Length != 6 || answer.Any(value => value is < 0 or > 5) || answer.Distinct().Count() != 6)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var correct = answer.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 });
            if (correct)
            {
                var completedAt = UtcNowProvider();
                Progress.DraftDone = true;
                Progress.Chapter = Chapter.River;
                if (!Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Draft))
                {
                    Progress.ChapterTimings.Add(new ChapterTiming(
                        Chapter.Draft,
                        completedAt,
                        ElapsedMillisecondsProvider()));
                }
            }
            else
            {
                Progress.WrongAnswerCount++;
                Progress.DraftFailures++;
            }

            Progress.Version++;
            response = new SubmitPuzzleResponse(true, null, correct, true,
                Progress.WrongAnswerCount, GetSnapshot());
            _processedPuzzleCommands[cacheKey] = response with { State = null };
            _processedPuzzleCommandOrder.Enqueue(cacheKey);
            while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                _processedPuzzleCommands.Remove(oldest);
            return true;
        }
    }

    public bool TrySubmitRiver(string playerId, int[]? answer, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!_members.ContainsKey(playerId))
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.PlayerNotInTeam,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 100)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var cacheKey = (playerId, PuzzleIds.River, commandId);
            if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
            {
                response = cached with { Mutated = false, State = GetSnapshot() };
                return true;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (Progress.Chapter != Chapter.River)
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.InvalidChapter,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (answer is null || answer.Length != 4 || answer.Any(value => value is < 0 or > 3))
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var correct = answer.SequenceEqual(new[] { 1, 2, 3, 0 });
            if (correct)
            {
                var completedAt = UtcNowProvider();
                Progress.RiverDone = true;
                Progress.Chapter = Chapter.News;
                if (!Progress.ChapterTimings.Any(x => x.Chapter == Chapter.River))
                {
                    Progress.ChapterTimings.Add(new ChapterTiming(
                        Chapter.River,
                        completedAt,
                        ElapsedMillisecondsProvider()));
                }
            }
            else
            {
                Progress.WrongAnswerCount++;
                Progress.RiverFailures++;
            }

            Progress.Version++;
            response = new SubmitPuzzleResponse(true, null, correct, true,
                Progress.WrongAnswerCount, GetSnapshot());
            _processedPuzzleCommands[cacheKey] = response with { State = null };
            _processedPuzzleCommandOrder.Enqueue(cacheKey);
            while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                _processedPuzzleCommands.Remove(oldest);
            return true;
        }
    }

    public bool TrySubmitNews(string playerId, int[]? answer, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!_members.ContainsKey(playerId))
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.PlayerNotInTeam,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            if (string.IsNullOrEmpty(commandId))
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            var cacheKey = (playerId, PuzzleIds.News, commandId);
            if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
            {
                response = cached with { Mutated = false, State = GetSnapshot() };
                return true;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (Progress.Chapter != Chapter.News || Progress.NewsDone)
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.InvalidChapter,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (answer is null || answer.Length != 1 || answer[0] is < 0 or > 2)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var choice = answer[0];
            bool correct = false;

            if (choice == 0)
            {
                Progress.WrongAnswerCount++;
                Progress.NewsNoise = Math.Min(3, Progress.NewsNoise + 1);
            }
            else if (choice == 1)
            {
                Progress.WrongAnswerCount++;
            }
            else if (choice == 2)
            {
                if (!Progress.Clues.Take(3).All(x => x))
                {
                    response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.PrerequisiteNotMet,
                        false, false, Progress.WrongAnswerCount, GetSnapshot());
                    return false;
                }

                correct = true;
                var completedAt = UtcNowProvider();
                Progress.NewsDone = true;
                Progress.NewsNoise = 0;
                Progress.Chapter = Chapter.Finale;
                if (!Progress.ChapterTimings.Any(x => x.Chapter == Chapter.News))
                {
                    Progress.ChapterTimings.Add(new ChapterTiming(
                        Chapter.News,
                        completedAt,
                        Math.Max(0, (long)(completedAt - MatchStartedAtUtc).TotalMilliseconds)));
                }
            }

            Progress.Version++;
            response = new SubmitPuzzleResponse(true, null, correct, true,
                Progress.WrongAnswerCount, GetSnapshot());
            _processedPuzzleCommands[cacheKey] = response with { State = null };
            _processedPuzzleCommandOrder.Enqueue(cacheKey);
            while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                _processedPuzzleCommands.Remove(oldest);
            return true;
        }
    }

    public bool TrySubmitFinale(string playerId, int[]? answer, string? commandId, out SubmitPuzzleResponse response)
    {
        lock (_gate)
        {
            if (!_members.ContainsKey(playerId))
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.PlayerNotInTeam,
                    false, false, Progress.WrongAnswerCount, null);
                return false;
            }

            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 100)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidCommandId,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            var cacheKey = (playerId, PuzzleIds.Finale, commandId);
            if (_processedPuzzleCommands.TryGetValue(cacheKey, out var cached))
            {
                response = cached with { Mutated = false, State = GetSnapshot() };
                return true;
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (Progress.Chapter != Chapter.Finale)
            {
                response = new SubmitPuzzleResponse(false, InteractErrorCodes.InvalidChapter,
                    false, false, Progress.WrongAnswerCount, GetSnapshot());
                return false;
            }

            if (!Progress.FinaleOrderCompleted)
            {
                if (answer is null || answer.Length != 4 || answer.Any(value => value is < 0 or > 3) || answer.Distinct().Count() != 4)
                {
                    response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                        false, false, Progress.WrongAnswerCount, GetSnapshot());
                    return false;
                }

                var correct = answer.SequenceEqual(new[] { 0, 1, 2, 3 });
                if (correct)
                {
                    Progress.FinaleOrderCompleted = true;
                    Progress.ReturnStep = 0;
                }
                else
                {
                    Progress.WrongAnswerCount++;
                }

                Progress.Version++;
                response = new SubmitPuzzleResponse(true, null, correct, true,
                    Progress.WrongAnswerCount, GetSnapshot());
                _processedPuzzleCommands[cacheKey] = response with { State = null };
                _processedPuzzleCommandOrder.Enqueue(cacheKey);
                while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                    _processedPuzzleCommands.Remove(oldest);
                return true;
            }
            else
            {
                if (answer is null || answer.Length != 1 || answer[0] is < 0 or > 3)
                {
                    response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidAnswerPayload,
                        false, false, Progress.WrongAnswerCount, GetSnapshot());
                    return false;
                }

                var step = answer[0];
                if (step != Progress.ReturnStep)
                {
                    response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidReturnStep,
                        false, false, Progress.WrongAnswerCount, GetSnapshot());
                    return false;
                }

                if (step < 3)
                {
                    Progress.ReturnStep = step + 1;
                }
                else
                {
                    var completedAt = UtcNowProvider();
                    Progress.ReturnStep = 4;
                    Progress.FinaleDone = true;
                    Progress.Chapter = Chapter.Complete;
                    Progress.FinishedAtUtc = completedAt;
                    if (!Progress.ChapterTimings.Any(x => x.Chapter == Chapter.Finale))
                    {
                        Progress.ChapterTimings.Add(new ChapterTiming(
                            Chapter.Finale,
                            completedAt,
                            ElapsedMillisecondsProvider()));
                    }
                }

                Progress.Version++;
                response = new SubmitPuzzleResponse(true, null, true, true,
                    Progress.WrongAnswerCount, GetSnapshot());
                _processedPuzzleCommands[cacheKey] = response with { State = null };
                _processedPuzzleCommandOrder.Enqueue(cacheKey);
                while (_processedPuzzleCommands.Count > 1000 && _processedPuzzleCommandOrder.TryDequeue(out var oldest))
                    _processedPuzzleCommands.Remove(oldest);
                return true;
            }
        }
    }

    public bool TryProcessInteract(string playerId, string objectId, string commandId, out InteractResponse response)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(playerId, out var member))
            {
                response = new InteractResponse(false, InteractErrorCodes.PlayerNotInTeam, objectId, false, null);
                return false;
            }

            if (!string.IsNullOrEmpty(commandId))
            {
                var cacheKey = (playerId, objectId, commandId);
                if (_processedInteractCommands.TryGetValue(cacheKey, out var cached))
                {
                    response = cached with { Mutated = false, State = GetSnapshot() };
                    return true;
                }
            }

            if (Progress.FinishedAtUtc.HasValue || Progress.FinaleDone)
            {
                response = new InteractResponse(false, PuzzleReservationErrorCodes.TeamAlreadyFinished, objectId, false, GetSnapshot());
                return false;
            }

            var place = TownCollision.GetInteractionPlace(objectId, Progress.Chapter);
            if (string.IsNullOrEmpty(place.Kind))
            {
                response = new InteractResponse(false, InteractErrorCodes.ObjectNotFound, objectId, false, null);
                return false;
            }

            float dx = member.X - place.X;
            float dy = member.Y - place.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > TownCollision.MaxInteractionDistance)
            {
                response = new InteractResponse(false, InteractErrorCodes.OutOfRange, objectId, false, null);
                return false;
            }

            bool mutated = false;

            // Lore can be read at any chapter while match is Playing and team is not Finished
            if (objectId.StartsWith("lore") && int.TryParse(objectId[^1..], out var loreId) && loreId is >= 0 and < 4)
            {
                if (!Progress.Lore[loreId])
                {
                    Progress.Lore[loreId] = true;
                    Progress.Version++;
                    mutated = true;
                }
            }
            // Clues must be collected in Chapter.News
            else if (objectId.StartsWith("clue") && int.TryParse(objectId[^1..], out var clueId) && clueId is >= 0 and < 3)
            {
                if (Progress.Chapter != Chapter.News)
                {
                    response = new InteractResponse(false, InteractErrorCodes.InvalidChapter, objectId, false, GetSnapshot());
                    CacheInteractCommand(playerId, objectId, commandId, response);
                    return false;
                }

                if (!Progress.Clues[clueId])
                {
                    Progress.Clues[clueId] = true;
                    Progress.Version++;
                    mutated = true;
                }
            }
            // NPC Bao in Chapter.News maps to Clues[2]
            else if (objectId == "bao" && Progress.Chapter == Chapter.News)
            {
                if (!Progress.Clues[2])
                {
                    Progress.Clues[2] = true;
                    Progress.Version++;
                    mutated = true;
                }
            }
            else if (Progress.Chapter == Chapter.Opening)
            {
                if (objectId is "trong" or "final_board" or "kieu_anh")
                {
                    Progress.Chapter = Chapter.Lights;
                    Progress.Version++;
                    mutated = true;
                }
            }
            else if (Progress.Chapter == Chapter.Lights)
            {
                int neighborIndex = Array.IndexOf(TownCollision.NeighborIds, objectId);
                if (neighborIndex >= 0)
                {
                    if (!Progress.Spoken[neighborIndex])
                    {
                        Progress.Spoken[neighborIndex] = true;
                        Progress.Version++;
                        mutated = true;
                    }
                }
                else if (objectId.StartsWith("lamp") && int.TryParse(objectId[^1..], out var lampId) && lampId >= 0 && lampId < 4)
                {
                    if (!Progress.Spoken[lampId])
                    {
                        response = new InteractResponse(false, "PREREQUISITE_NOT_MET", objectId, false, GetSnapshot());
                        CacheInteractCommand(playerId, objectId, commandId, response);
                        return false;
                    }

                    if (!Progress.Lamps[lampId])
                    {
                        Progress.Lamps[lampId] = true;
                        Progress.Version++;
                        mutated = true;
                    }
                }
            }

            response = new InteractResponse(true, null, objectId, mutated, GetSnapshot());
            CacheInteractCommand(playerId, objectId, commandId, response);
            return true;
        }
    }

    private void CacheInteractCommand(string playerId, string objectId, string? commandId, InteractResponse response)
    {
        if (string.IsNullOrEmpty(commandId)) return;
        var cacheKey = (playerId, objectId, commandId);
        if (!_processedInteractCommands.ContainsKey(cacheKey))
        {
            _processedInteractCommands[cacheKey] = response;
            _processedInteractCommandOrder.Enqueue(cacheKey);
            if (_processedInteractCommandOrder.Count > MaxProcessedInteractCommands)
            {
                var oldest = _processedInteractCommandOrder.Dequeue();
                _processedInteractCommands.Remove(oldest);
            }
        }
    }

    public TeamGameStateSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var memberStates = _members.Values.Select(m => m.ToState()).ToList();
            var progressSnapshot = new TeamProgressSnapshot(
                Progress.Chapter,
                (bool[])Progress.Spoken.Clone(),
                (bool[])Progress.Lamps.Clone(),
                (bool[])Progress.Clues.Clone(),
                Progress.Version,
                Progress.Shards,
                Progress.WrongAnswerCount,
                Progress.ChapterTimings.ToArray(),
                Progress.DraftDone,
                Progress.DraftFailures,
                (bool[])Progress.Lore.Clone(),
                Progress.RiverDone,
                Progress.RiverFailures,
                Progress.NewsDone,
                Progress.NewsNoise,
                Progress.FinaleOrderCompleted,
                Progress.ReturnStep,
                Progress.FinaleDone,
                Progress.FinishedAtUtc
            );

            return new TeamGameStateSnapshot(
                MatchId,
                TeamId,
                TeamName,
                TeamColor,
                memberStates,
                progressSnapshot,
                DateTimeOffset.UtcNow
            );
        }
    }
}
