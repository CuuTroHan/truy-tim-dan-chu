using System.Collections.Concurrent;
using TruyTimDanChu.Game;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class TeamMemberSession
{
    public string PlayerId { get; }
    public string DisplayName { get; }
    public string AvatarId { get; set; }
    public string AccentColor { get; }
    public float X { get; set; } = 500f;
    public float Y { get; set; } = 366f;
    public int Facing { get; set; } = 0;
    public int Walking { get; set; } = 0;
    public bool IsConnected { get; set; } = true;
    public int LastProcessedSequence { get; set; } = 0;
    public long LastInputTimestamp { get; set; } = 0;

    public long LastSimulatedTime { get; set; } = 0;
    public const int InputTtlMs = 400;

    public TeamMemberSession(string playerId, string displayName, string avatarId, string accentColor, float x = 500f, float y = 366f)
    {
        PlayerId = playerId;
        DisplayName = displayName;
        AvatarId = avatarId;
        AccentColor = accentColor;
        X = x;
        Y = y;
    }

    public TeamMemberState ToState() =>
        new(PlayerId, DisplayName, AvatarId, AccentColor, X, Y, Facing, Walking, IsConnected);

    public bool ProcessMovement(int keys, int sequence, long serverTimeMs, out MovementAck ack)
    {
        // Sanitize keys (only 0..15 allowed)
        keys &= 0xF;

        // Sequence ordering check: discard older or duplicate sequence
        if (sequence <= LastProcessedSequence)
        {
            ack = new MovementAck(false, "OUTDATED_SEQUENCE", sequence, X, Y, Facing, Walking, serverTimeMs);
            return false;
        }

        // Clamp delta time to maximum 50ms (prevents warping / speed hacking)
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

        TownCollision.SimulateStep(X, Y, keys, dtSeconds, out var newX, out var newY, out var facing, out var walking);
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
    public TeamProgress Progress { get; } = new();

    public int MemberCount => _members.Count;

    public TeamGameInstance(string matchId, string roomId, string teamId, string teamName, string teamColor)
    {
        MatchId = matchId;
        RoomId = roomId;
        TeamId = teamId;
        TeamName = teamName;
        TeamColor = teamColor;
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

    private readonly HashSet<string> _processedCommands = new();

    public bool TryProcessInteract(string playerId, string objectId, string commandId, out InteractResponse response)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(playerId, out var member))
            {
                response = new InteractResponse(false, InteractErrorCodes.PlayerNotInTeam, objectId, false, null);
                return false;
            }

            if (!TownCollision.Places.TryGetValue(objectId, out var place))
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

            if (!string.IsNullOrEmpty(commandId) && _processedCommands.Contains(commandId))
            {
                response = new InteractResponse(true, null, objectId, false, GetSnapshot());
                return true;
            }

            bool mutated = false;

            if (Progress.Chapter == Chapter.Opening)
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

            if (!string.IsNullOrEmpty(commandId))
            {
                _processedCommands.Add(commandId);
                if (_processedCommands.Count > 1000)
                {
                    _processedCommands.Clear();
                }
            }

            response = new InteractResponse(true, null, objectId, mutated, GetSnapshot());
            return true;
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
                Progress.Shards
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
