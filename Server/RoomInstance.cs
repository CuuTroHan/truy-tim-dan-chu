using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TruyTimDanChu.Game;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class RoomInstance
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, PlayerSession> _players = new();
    private readonly ConcurrentDictionary<string, TeamInstance> _teams = new();
    private readonly ConcurrentDictionary<string, TeamGameInstance> _teamGames = new();
    private readonly Dictionary<string, MatchPausedEvent> _pauseCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MatchResumedEvent> _resumeCommands = new(StringComparer.Ordinal);

    public string RoomId { get; }
    public string RoomCode { get; }
    public string RoomName { get; private set; }
    public RoomStatus Status { get; private set; } = RoomStatus.Lobby;
    public string AdminTokenHash { get; private set; }
    public string? AdminConnectionId { get; set; }
    public bool AllowSelfTeamSelection { get; private set; }
    public bool AllowLateJoin { get; private set; }
    public bool ShowLiveLeaderboard { get; private set; }
    public bool AdminCanPlay { get; private set; }
    public bool EndOnFirstFinish { get; private set; }
    public long AdminGeneration { get; private set; }
    public bool IsAdminRevoked { get; private set; }
    public string? LinkedAdminPlayerId { get; private set; }
    public int TimeLimitSeconds { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int Version { get; private set; } = 1;
    public bool IsJoinLocked { get; private set; }
    public bool IsRosterLocked { get; private set; }
    public string? MatchId { get; private set; }
    public DateTimeOffset? MatchStartTimeUtc { get; private set; }
    public DateTimeOffset? MatchFinishedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public MatchEndReason? MatchEndReason { get; private set; }
    public MatchClock? Clock { get; private set; }
    public MatchResultsSnapshot? Results { get; private set; }
    public Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;
    public TimeProvider TimeProvider { get; }
    public Action<string, DateTimeOffset>? OnMatchStarted { get; set; }
    public Action<string, MatchFinishedEvent>? OnMatchFinished { get; set; }
    public Action<string, MatchPausedEvent>? OnMatchPaused { get; set; }
    public Action<string, MatchResumedEvent>? OnMatchResumed { get; set; }
    private CancellationTokenSource? _countdownCts;

    public int PlayerCount => _players.Count;
    public int TeamCount => _teams.Count;

    public RoomInstance(
        string roomId,
        string roomCode,
        string roomName,
        string adminToken,
        int timeLimitSeconds,
        bool allowSelfTeamSelection,
        bool allowLateJoin,
        bool showLiveLeaderboard,
        bool adminCanPlay,
        bool endOnFirstFinish,
        TimeProvider? timeProvider = null)
    {
        RoomId = roomId;
        RoomCode = roomCode;
        RoomName = roomName;
        AdminTokenHash = PlayerSession.HashToken(adminToken);
        TimeLimitSeconds = timeLimitSeconds;
        AllowSelfTeamSelection = allowSelfTeamSelection;
        AllowLateJoin = allowLateJoin;
        ShowLiveLeaderboard = showLiveLeaderboard;
        AdminCanPlay = adminCanPlay;
        EndOnFirstFinish = endOnFirstFinish;
        TimeProvider = timeProvider ?? TimeProvider.System;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
    }

    public bool VerifyAdmin(string? token)
    {
        if (IsAdminRevoked || string.IsNullOrEmpty(token)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(AdminTokenHash),
            Encoding.UTF8.GetBytes(PlayerSession.HashToken(token)));
    }

    public static bool VerifyAdminTokenHash(string storedHash, string token)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrEmpty(token)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(PlayerSession.HashToken(token)));
    }

    public (bool Success, string? NewToken, string? PreviousConnectionId) TryResumeAdmin(string token, string connectionId)
    {
        lock (_gate)
        {
            if (Status == RoomStatus.Closed || IsAdminRevoked || !VerifyAdmin(token))
                return (false, null, null);
            var previous = AdminConnectionId;
            var newToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            AdminTokenHash = PlayerSession.HashToken(newToken);
            AdminConnectionId = connectionId;
            AdminGeneration++;
            LastActivityAt = UtcNowProvider();
            return (true, newToken, previous);
        }
    }

    public void RevokeAdmin()
    {
        lock (_gate)
        {
            IsAdminRevoked = true;
            AdminConnectionId = null;
            AdminGeneration++;
        }
    }

    public (bool Success, string? ErrorCode, bool Value) TrySetAdminCanPlay(string adminToken, bool value)
    {
        if (!VerifyAdmin(adminToken)) return (false, RoomErrorCodes.UnauthorizedAdmin, AdminCanPlay);
        lock (_gate)
        {
            if (Status != RoomStatus.Lobby) return (false, RoomErrorCodes.InvalidRoomStatus, AdminCanPlay);
            if (AdminCanPlay == value) return (true, null, value);
            AdminCanPlay = value;
            if (!value && LinkedAdminPlayerId is not null)
            {
                if (_players.TryRemove(LinkedAdminPlayerId, out var linked)) linked.Revoke();
                LinkedAdminPlayerId = null;
                ResetAllPlayersReadyLocked();
            }
            Version++;
            LastActivityAt = UtcNowProvider();
            return (true, null, value);
        }
    }

    public (bool Success, string? ErrorCode, PlayerSession? Player, string? ReconnectToken) TryAdminJoinAsPlayer(
        string adminToken, string connectionId, string teamId, string displayName, string avatarId, int maxPlayersPerRoom)
    {
        if (!VerifyAdmin(adminToken)) return (false, RoomErrorCodes.UnauthorizedAdmin, null, null);
        lock (_gate)
        {
            if (Status != RoomStatus.Lobby || !AdminCanPlay) return (false, RoomErrorCodes.InvalidRoomStatus, null, null);
            if (LinkedAdminPlayerId is not null && _players.TryGetValue(LinkedAdminPlayerId, out var existing))
                return (true, null, existing, null);
            if (!AvatarCatalog.IsValid(avatarId)) return (false, RoomErrorCodes.InvalidAvatarId, null, null);
            var name = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 25)
                return (false, RoomErrorCodes.InvalidDisplayName, null, null);
            if (!_teams.TryGetValue(teamId, out var team)) return (false, RoomErrorCodes.TeamNotFound, null, null);
            if (_players.Count >= maxPlayersPerRoom || _players.Values.Count(p => p.TeamId == teamId) >= team.Capacity)
                return (false, RoomErrorCodes.TeamFull, null, null);
            if (_players.Values.Any(p => p.NormalizedName == name.ToUpperInvariant() && p.IsConnected))
                return (false, RoomErrorCodes.DuplicateDisplayName, null, null);
            var playerId = Guid.NewGuid().ToString("N");
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var player = new PlayerSession(playerId, RoomId, name, connectionId, token, true) { TeamId = teamId, AvatarId = avatarId };
            _players[playerId] = player;
            LinkedAdminPlayerId = playerId;
            Version++;
            LastActivityAt = UtcNowProvider();
            return (true, null, player, token);
        }
    }

    public (bool Success, string? ErrorCode, PlayerSession? Player, string? ReconnectToken) TryLateJoin(
        string displayName, string connectionId, string teamId, string avatarId, int maxPlayersPerRoom)
    {
        lock (_gate)
        {
            if (!AllowLateJoin) return (false, RoomErrorCodes.LateJoinDisabled, null, null);
            if (Status is not (RoomStatus.Playing or RoomStatus.Paused)) return (false, RoomErrorCodes.LateJoinNotAllowedInState, null, null);
            if (!_teams.TryGetValue(teamId, out var team)) return (false, RoomErrorCodes.TeamNotFound, null, null);
            if (team.FinishedAtUtc.HasValue) return (false, RoomErrorCodes.TeamAlreadyFinished, null, null);
            if (_players.Count >= maxPlayersPerRoom || _players.Values.Count(p => p.TeamId == teamId) >= team.Capacity)
                return (false, RoomErrorCodes.NoAvailableSlot, null, null);
            var name = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 25) return (false, RoomErrorCodes.InvalidDisplayName, null, null);
            if (!AvatarCatalog.IsValid(avatarId)) return (false, RoomErrorCodes.InvalidAvatarId, null, null);
            if (_players.Values.Any(p => p.NormalizedName == name.ToUpperInvariant() && p.IsConnected)) return (false, RoomErrorCodes.DuplicateDisplayName, null, null);
            var playerId = Guid.NewGuid().ToString("N");
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var player = new PlayerSession(playerId, RoomId, name, connectionId, token) { TeamId = teamId, AvatarId = avatarId };
            _players[playerId] = player;
            if (_teamGames.TryGetValue(teamId, out var game))
            {
                var index = game.MemberCount;
                game.AddMember(playerId, name, avatarId, 500f + index * 18f, 366f + index * 16f);
            }
            Version++;
            LastActivityAt = UtcNowProvider();
            return (true, null, player, token);
        }
    }

    public (bool Success, string? ErrorCode, PlayerSession? Player, string? ReconnectToken) TryAddPlayer(
        string displayName,
        string connectionId,
        int maxPlayersPerRoom)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2 || trimmed.Length > 25)
        {
            return (false, RoomErrorCodes.InvalidDisplayName, null, null);
        }

        if (Status == RoomStatus.Closed)
            return (false, RoomErrorCodes.RoomClosed, null, null);
        if (Status != RoomStatus.Lobby && !AllowLateJoin)
        {
            return (false, RoomErrorCodes.JoinNotAllowed, null, null);
        }

        lock (_gate)
        {
            if (IsJoinLocked)
            {
                return (false, RoomErrorCodes.JoinLocked, null, null);
            }

            if (_players.Count >= maxPlayersPerRoom)
            {
                return (false, RoomErrorCodes.RoomFull, null, null);
            }

            var normalized = trimmed.ToUpperInvariant();
            if (_players.Values.Any(p => p.NormalizedName == normalized && p.IsConnected))
            {
                return (false, RoomErrorCodes.DuplicateDisplayName, null, null);
            }

            var playerId = Guid.NewGuid().ToString("N");
            var reconnectToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            var player = new PlayerSession(playerId, RoomId, trimmed, connectionId, reconnectToken);
            _players[playerId] = player;

            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, player, reconnectToken);
        }
    }

    public List<PlayerSnapshot> GetPlayerSnapshots()
    {
        lock (_gate)
        {
            return _players.Values.Select(p => p.GetSnapshot()).ToList();
        }
    }

    public PlayerSession? GetPlayer(string? playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return null;
        _players.TryGetValue(playerId, out var player);
        return player;
    }

    public PlayerSession? GetPlayerByConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;
        return _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
    }

    public bool RemovePlayer(string playerId)
    {
        if (_players.TryRemove(playerId, out _))
        {
            IncrementVersion();
            return true;
        }
        return false;
    }

    private void ResetAllPlayersReadyLocked()
    {
        foreach (var p in _players.Values)
        {
            p.IsReady = false;
        }
    }

    public (bool Success, string? ErrorCode, TeamSnapshot? Team) TryAddTeam(
        string name,
        string color,
        int capacity,
        int maxTeamsPerRoom,
        int maxPlayersPerTeam,
        int maxPlayersPerRoom)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length < 2 || trimmedName.Length > 30)
        {
            return (false, RoomErrorCodes.InvalidTeamName, null);
        }

        var trimmedColor = color?.Trim();
        if (!IsValidHexColor(trimmedColor))
        {
            return (false, RoomErrorCodes.InvalidTeamColor, null);
        }

        if (capacity < 1 || capacity > maxPlayersPerTeam)
        {
            return (false, RoomErrorCodes.InvalidTeamCapacity, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null);
            }

            if (_teams.Count >= maxTeamsPerRoom)
            {
                return (false, RoomErrorCodes.MaxTeamsExceeded, null);
            }

            var normalizedName = trimmedName.ToUpperInvariant();
            if (_teams.Values.Any(t => t.Name.Trim().ToUpperInvariant() == normalizedName))
            {
                return (false, RoomErrorCodes.DuplicateTeamName, null);
            }

            int currentTotalCapacity = _teams.Values.Sum(t => t.Capacity);
            if (currentTotalCapacity + capacity > maxPlayersPerRoom)
            {
                return (false, RoomErrorCodes.TotalCapacityExceeded, null);
            }

            var teamId = Guid.NewGuid().ToString("N");
            var displayOrder = _teams.Count;
            var team = new TeamInstance(teamId, RoomId, trimmedName, trimmedColor!, capacity, displayOrder);
            _teams[teamId] = team;

            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, team.GetSnapshot(0));
        }
    }

    public List<TeamSnapshot> GetTeamSnapshots()
    {
        lock (_gate)
        {
            return _teams.Values
                .OrderBy(t => t.DisplayOrder)
                .Select(t => t.GetSnapshot(_players.Values.Count(p => p.TeamId == t.TeamId)))
                .ToList();
        }
    }

    public TeamInstance? GetTeam(string? teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return null;
        _teams.TryGetValue(teamId, out var team);
        return team;
    }

    public bool TryMarkTeamFinished(string teamId, DateTimeOffset timestamp, out TeamSnapshot? snapshot)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Playing || !_teams.TryGetValue(teamId, out var team))
            {
                snapshot = null;
                return false;
            }

            var marked = team.TryMarkFinished(timestamp);
            if (marked)
            {
                Version++;
                LastActivityAt = DateTimeOffset.UtcNow;
            }
            snapshot = team.GetSnapshot(_players.Values.Count(p => p.TeamId == teamId));
            return marked;
        }
    }

    public (bool Success, string? ErrorCode, TeamSnapshot? Team) TryUpdateTeam(
        string teamId,
        string name,
        string color,
        int capacity,
        int maxPlayersPerTeam,
        int maxPlayersPerRoom)
    {
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return (false, RoomErrorCodes.TeamNotFound, null);
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length < 2 || trimmedName.Length > 30)
        {
            return (false, RoomErrorCodes.InvalidTeamName, null);
        }

        var trimmedColor = color?.Trim();
        if (!IsValidHexColor(trimmedColor))
        {
            return (false, RoomErrorCodes.InvalidTeamColor, null);
        }

        if (capacity < 1 || capacity > maxPlayersPerTeam)
        {
            return (false, RoomErrorCodes.InvalidTeamCapacity, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null);
            }

            if (!_teams.TryGetValue(teamId, out var existingTeam))
            {
                return (false, RoomErrorCodes.TeamNotFound, null);
            }

            var normalizedName = trimmedName.ToUpperInvariant();
            if (_teams.Values.Any(t => t.TeamId != teamId && t.Name.Trim().ToUpperInvariant() == normalizedName))
            {
                return (false, RoomErrorCodes.DuplicateTeamName, null);
            }

            int currentOccupancy = _players.Values.Count(p => p.TeamId == teamId);
            if (capacity < currentOccupancy)
            {
                return (false, RoomErrorCodes.CapacityBelowOccupancy, null);
            }

            int currentTotalCapacity = _teams.Values.Sum(t => t.Capacity);
            int newTotalCapacity = currentTotalCapacity - existingTeam.Capacity + capacity;
            if (newTotalCapacity > maxPlayersPerRoom)
            {
                return (false, RoomErrorCodes.TotalCapacityExceeded, null);
            }

            existingTeam.Name = trimmedName;
            existingTeam.Color = trimmedColor!;
            existingTeam.Capacity = capacity;

            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, existingTeam.GetSnapshot(currentOccupancy));
        }
    }

    public (bool Success, string? ErrorCode, List<TeamSnapshot>? Teams) TryReorderTeams(List<string> orderedTeamIds)
    {
        if (orderedTeamIds is null)
        {
            return (false, RoomErrorCodes.InvalidTeamOrder, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null);
            }

            if (orderedTeamIds.Count != _teams.Count)
            {
                return (false, RoomErrorCodes.InvalidTeamOrder, null);
            }

            var idSet = new HashSet<string>(orderedTeamIds);
            if (idSet.Count != _teams.Count || !_teams.Keys.All(idSet.Contains))
            {
                return (false, RoomErrorCodes.InvalidTeamOrder, null);
            }

            for (int i = 0; i < orderedTeamIds.Count; i++)
            {
                var id = orderedTeamIds[i];
                _teams[id].DisplayOrder = i;
            }

            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, GetTeamSnapshots());
        }
    }

    public (bool Success, string? ErrorCode) TryRemoveTeam(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return (false, RoomErrorCodes.TeamNotFound);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus);
            }

            if (!_teams.TryGetValue(teamId, out _))
            {
                return (false, RoomErrorCodes.TeamNotFound);
            }

            int currentOccupancy = _players.Values.Count(p => p.TeamId == teamId);
            if (currentOccupancy > 0)
            {
                return (false, RoomErrorCodes.TeamNotEmpty);
            }

            _teams.TryRemove(teamId, out _);

            var remaining = _teams.Values.OrderBy(t => t.DisplayOrder).ToList();
            for (int i = 0; i < remaining.Count; i++)
            {
                remaining[i].DisplayOrder = i;
            }

            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null);
        }
    }

    public (bool Success, string? ErrorCode, string? TeamId, string? OldTeamId) TryJoinTeam(
        string playerId,
        string targetTeamId,
        bool enforceSelfSelection = true)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, null, null);
        }

        if (string.IsNullOrWhiteSpace(targetTeamId))
        {
            return (false, RoomErrorCodes.TeamNotFound, null, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null, null);
            }

            if (enforceSelfSelection && IsRosterLocked)
            {
                return (false, RoomErrorCodes.RosterLocked, null, null);
            }

            if (enforceSelfSelection && !AllowSelfTeamSelection)
            {
                return (false, RoomErrorCodes.SelfSelectionDisabled, null, null);
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return (false, RoomErrorCodes.PlayerNotFound, null, null);
            }

            if (!_teams.TryGetValue(targetTeamId, out var targetTeam))
            {
                return (false, RoomErrorCodes.TeamNotFound, null, null);
            }

            if (player.TeamId == targetTeamId)
            {
                return (true, null, targetTeamId, null);
            }

            int targetOccupancy = _players.Values.Count(p => p.TeamId == targetTeamId);
            if (targetOccupancy >= targetTeam.Capacity)
            {
                return (false, RoomErrorCodes.TeamFull, null, null);
            }

            var oldTeamId = player.TeamId;
            player.TeamId = targetTeamId;
            player.IsReady = false;

            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, targetTeamId, oldTeamId);
        }
    }

    public (bool Success, string? ErrorCode, string? OldTeamId) TryLeaveTeam(
        string playerId,
        bool enforceSelfSelection = true)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null);
            }

            if (enforceSelfSelection && IsRosterLocked)
            {
                return (false, RoomErrorCodes.RosterLocked, null);
            }

            if (enforceSelfSelection && !AllowSelfTeamSelection)
            {
                return (false, RoomErrorCodes.SelfSelectionDisabled, null);
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return (false, RoomErrorCodes.PlayerNotFound, null);
            }

            if (player.TeamId is null)
            {
                return (true, null, null);
            }

            var oldTeamId = player.TeamId;
            player.TeamId = null;
            player.IsReady = false;

            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, oldTeamId);
        }
    }

    public (bool Success, string? ErrorCode, string? OldTeamId, string? NewTeamId) TryAdminMovePlayer(
        string playerId,
        string? targetTeamId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, null, null);
        }

        if (string.IsNullOrEmpty(targetTeamId))
        {
            var (leaveSuccess, leaveError, oldTeam) = TryLeaveTeam(playerId, enforceSelfSelection: false);
            return (leaveSuccess, leaveError, oldTeam, null);
        }

        var (joinSuccess, joinError, newTeam, oldTeamId) = TryJoinTeam(playerId, targetTeamId, enforceSelfSelection: false);
        return (joinSuccess, joinError, oldTeamId, newTeam);
    }

    public (bool Success, string? ErrorCode, string? OldTeamId, string? ConnectionId) TryKickPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, null, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null, null);
            }

            if (!_players.TryRemove(playerId, out var player))
            {
                return (false, RoomErrorCodes.PlayerNotFound, null, null);
            }

            var oldTeamId = player.TeamId;
            var connId = player.ConnectionId;

            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, oldTeamId, connId);
        }
    }

    public (bool Success, string? ErrorCode, bool AllowSelfTeamSelection) TryToggleSelfTeamSelection(bool allow)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, AllowSelfTeamSelection);
            }

            AllowSelfTeamSelection = allow;
            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, AllowSelfTeamSelection);
        }
    }

    public (bool Success, string? ErrorCode, string? AvatarId) TrySelectAvatar(string playerId, string avatarId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, null);
        }

        var trimmedAvatar = avatarId?.Trim();
        if (!AvatarCatalog.IsValid(trimmedAvatar))
        {
            return (false, RoomErrorCodes.InvalidAvatarId, null);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null);
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return (false, RoomErrorCodes.PlayerNotFound, null);
            }

            player.AvatarId = trimmedAvatar!;
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, trimmedAvatar);
        }
    }

    public (bool Success, string? ErrorCode, bool IsReady) TrySetReady(string playerId, bool isReady)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return (false, RoomErrorCodes.PlayerNotFound, false);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, false);
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return (false, RoomErrorCodes.PlayerNotFound, false);
            }

            if (string.IsNullOrEmpty(player.TeamId))
            {
                return (false, RoomErrorCodes.PlayerNotInTeam, false);
            }

            player.IsReady = isReady;
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, isReady);
        }
    }

    public (bool Success, string? ErrorCode, bool IsJoinLocked) TrySetJoinLock(string adminToken, bool isJoinLocked)
    {
        if (!VerifyAdmin(adminToken))
        {
            return (false, RoomErrorCodes.UnauthorizedAdmin, IsJoinLocked);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, IsJoinLocked);
            }

            IsJoinLocked = isJoinLocked;
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, IsJoinLocked);
        }
    }

    public (bool Success, string? ErrorCode, bool IsRosterLocked) TrySetRosterLock(string adminToken, bool isRosterLocked)
    {
        if (!VerifyAdmin(adminToken))
        {
            return (false, RoomErrorCodes.UnauthorizedAdmin, IsRosterLocked);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, IsRosterLocked);
            }

            IsRosterLocked = isRosterLocked;
            ResetAllPlayersReadyLocked();
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
            return (true, null, IsRosterLocked);
        }
    }

    private static bool IsValidHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var trimmed = color.Trim();
        if (!trimmed.StartsWith('#')) return false;
        if (trimmed.Length != 4 && trimmed.Length != 7) return false;
        for (var i = 1; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    public void Touch()
    {
        lock (_gate)
        {
            LastActivityAt = DateTimeOffset.UtcNow;
        }
    }

    public (bool Success, string? ErrorCode, string? MatchId, DateTimeOffset? StartTimeUtc) TryStartMatch(
        string? adminToken,
        int countdownSeconds)
    {
        if (!VerifyAdmin(adminToken))
        {
            return (false, RoomErrorCodes.UnauthorizedAdmin, null, null);
        }

        lock (_gate)
        {
            if (Status == RoomStatus.Playing)
            {
                return (false, RoomErrorCodes.MatchAlreadyStarted, null, null);
            }

            if (Status == RoomStatus.Countdown)
            {
                return (false, RoomErrorCodes.CountdownAlreadyActive, null, null);
            }

            if (Status != RoomStatus.Lobby)
            {
                return (false, RoomErrorCodes.InvalidRoomStatus, null, null);
            }

            var players = _players.Values.Select(p => p.GetSnapshot()).ToList();
            var teams = GetTeamSnapshots();
            var roomSnapshot = GetSnapshot();
            var validation = ReadinessValidator.Validate(roomSnapshot, players, teams);
            if (!validation.CanStart)
            {
                return (false, RoomErrorCodes.NotReadyToStart, null, null);
            }

            var delaySeconds = Math.Clamp(countdownSeconds, 1, 30);
            var now = UtcNowProvider();
            MatchId = Guid.NewGuid().ToString("N");
            MatchStartTimeUtc = now.AddSeconds(delaySeconds);
            Status = RoomStatus.Countdown;
            Version++;
            LastActivityAt = now;

            var matchId = MatchId;
            var startTimeUtc = MatchStartTimeUtc.Value;

            _countdownCts?.Cancel();
            _countdownCts = new CancellationTokenSource();
            var cts = _countdownCts;

            _ = Task.Run(async () =>
            {
                var remaining = startTimeUtc - UtcNowProvider();
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(remaining, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                bool transitioned = false;
                lock (_gate)
                {
                    if (!cts.IsCancellationRequested && Status == RoomStatus.Countdown && MatchId == matchId)
                    {
                        Status = RoomStatus.Playing;
                        Clock = new MatchClock(TimeSpan.FromSeconds(TimeLimitSeconds), TimeProvider);
                        Clock.Start(startTimeUtc);
                        MatchFinishedAtUtc = null;
                        MatchEndReason = null;
                        Results = null;
                        InitializeTeamGames(matchId);
                        Version++;
                        LastActivityAt = UtcNowProvider();
                        transitioned = true;
                    }
                }

                if (transitioned)
                {
                    OnMatchStarted?.Invoke(matchId, startTimeUtc);
                }
            });

            return (true, null, matchId, startTimeUtc);
        }
    }

    public (bool Success, string? ErrorCode) TryCancelCountdown(string? adminToken)
    {
        if (!VerifyAdmin(adminToken))
        {
            return (false, RoomErrorCodes.UnauthorizedAdmin);
        }

        lock (_gate)
        {
            if (Status != RoomStatus.Countdown)
            {
                return (false, RoomErrorCodes.CountdownNotActive);
            }

            _countdownCts?.Cancel();
            _countdownCts = null;
            Status = RoomStatus.Lobby;
            MatchId = null;
            MatchStartTimeUtc = null;
            Clock = null;
            MatchFinishedAtUtc = null;
            MatchEndReason = null;
            Results = null;
            _teamGames.Clear();
            Version++;
            LastActivityAt = UtcNowProvider();

            return (true, null);
        }
    }

    public bool TryEndMatch(string expectedMatchId, MatchEndReason reason, out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            finishedEvent = null;
            if ((Status != RoomStatus.Playing && Status != RoomStatus.Paused) || MatchId != expectedMatchId || Clock is null)
                return false;
            if (!Clock.TryFinish(reason, out var finishedAt)) return false;
            Status = RoomStatus.Finished;
            MatchFinishedAtUtc = finishedAt;
            MatchEndReason = reason;
            Results = BuildResultsLocked(finishedAt, reason);
            Version++;
            LastActivityAt = finishedAt;
            _countdownCts?.Cancel();
            finishedEvent = new MatchFinishedEvent(expectedMatchId, finishedAt, reason, Results);
        }
        OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return true;
    }

    public bool TryCloseRoom(string? reason, out RoomClosedEvent? closedEvent, out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            closedEvent = null;
            finishedEvent = null;
            if (Status == RoomStatus.Closed) return false;
            var now = UtcNowProvider();
            _countdownCts?.Cancel();
            if ((Status == RoomStatus.Playing || Status == RoomStatus.Paused) && MatchId is not null && Clock is not null)
            {
                Clock.TryFinish(global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed, out var finishedAt);
                MatchFinishedAtUtc = finishedAt;
                MatchEndReason = global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed;
                Results = BuildResultsLocked(finishedAt, global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed);
                finishedEvent = new MatchFinishedEvent(MatchId, finishedAt, global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed, Results);
            }
            Status = RoomStatus.Closed;
            ClosedAtUtc = now;
            IsJoinLocked = true;
            IsRosterLocked = true;
            foreach (var player in _players.Values) player.Revoke();
            IsAdminRevoked = true;
            AdminConnectionId = null;
            Version++;
            LastActivityAt = now;
            closedEvent = new RoomClosedEvent(RoomId, now, reason);
        }
        if (finishedEvent is not null) OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return true;
    }

    public bool TryForceAdvanceToPlaying(string expectedMatchId)
    {
        DateTimeOffset startTime;
        lock (_gate)
        {
            if (Status != RoomStatus.Countdown || MatchId != expectedMatchId)
            {
                return false;
            }

            _countdownCts?.Cancel();
            _countdownCts = null;
            Status = RoomStatus.Playing;
            Clock = new MatchClock(TimeSpan.FromSeconds(TimeLimitSeconds), TimeProvider);
            Clock.Start(MatchStartTimeUtc ?? UtcNowProvider());
            MatchFinishedAtUtc = null;
            MatchEndReason = null;
            Results = null;
            InitializeTeamGames(expectedMatchId);
            startTime = MatchStartTimeUtc ?? UtcNowProvider();
            Version++;
            LastActivityAt = UtcNowProvider();
        }
        OnMatchStarted?.Invoke(expectedMatchId, startTime);
        return true;
    }

    private void InitializeTeamGames(string matchId)
    {
        _teamGames.Clear();
        var teams = GetTeamSnapshots();
        foreach (var team in teams)
        {
            var teamGame = new TeamGameInstance(matchId, RoomId, team.TeamId, team.Name, team.Color, MatchStartTimeUtc);
            teamGame.UtcNowProvider = UtcNowProvider;
            teamGame.ElapsedMillisecondsProvider = () => Clock?.GetElapsedMilliseconds() ??
                Math.Max(0, (long)(UtcNowProvider() - teamGame.MatchStartedAtUtc).TotalMilliseconds);
            var teamPlayers = _players.Values.Where(p => p.TeamId == team.TeamId).OrderBy(p => p.JoinedAt).ToList();
            for (int i = 0; i < teamPlayers.Count; i++)
            {
                var p = teamPlayers[i];
                var spawnX = 500f + (i % 2 == 0 ? 1 : -1) * (i * 18f);
                var spawnY = 366f + (i / 2) * 16f;
                teamGame.AddMember(p.PlayerId, p.DisplayName, p.AvatarId, spawnX, spawnY);
            }
            _teamGames[team.TeamId] = teamGame;
        }
    }

    public TeamGameInstance? GetTeamGame(string teamId)
    {
        _teamGames.TryGetValue(teamId, out var instance);
        return instance;
    }

    public TeamGameInstance? GetTeamGameForPlayer(string playerId)
    {
        return _teamGames.Values.FirstOrDefault(t => t.HasMember(playerId));
    }

    public bool IsPlayerConnectionBound(string playerId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(connectionId)) return false;
        lock (_gate)
        {
            return _players.TryGetValue(playerId, out var player) &&
                   player.IsConnected &&
                   string.Equals(player.ConnectionId, connectionId, StringComparison.Ordinal);
        }
    }

    public bool TryMarkPlayerDisconnected(string connectionId, out PlayerSession? player)
    {
        lock (_gate)
        {
            player = _players.Values.FirstOrDefault(p => string.Equals(p.ConnectionId, connectionId, StringComparison.Ordinal));
            if (player is null || !player.IsConnected) return false;
            player.MarkDisconnected(UtcNowProvider());
            return true;
        }
    }

    public CleanupResult CleanupExpiredSessions(DateTimeOffset now, TimeSpan ttl)
    {
        lock (_gate)
        {
            var expired = _players.Values
                .Where(p => !p.IsConnected && !p.IsRevoked && p.DisconnectedAtUtc.HasValue &&
                            now - p.DisconnectedAtUtc.Value >= ttl)
                .Select(p => p.PlayerId)
                .ToArray();
            if (expired.Length == 0) return new CleanupResult(Array.Empty<string>(), Version);

            foreach (var playerId in expired)
            {
                if (_players.TryRemove(playerId, out var player))
                {
                    player.Revoke();
                    if (player.TeamId is not null && _teamGames.TryGetValue(player.TeamId, out var teamGame))
                        teamGame.SetMemberConnection(playerId, false);
                }
            }
            Version++;
            LastActivityAt = now;
            return new CleanupResult(expired, Version);
        }
    }

    public RoomCleanupDecision EvaluateRoomCleanup(DateTimeOffset now, TimeSpan lobbyTtl, TimeSpan closedRetention)
    {
        lock (_gate)
        {
            if (Status == RoomStatus.Lobby && string.IsNullOrWhiteSpace(AdminConnectionId) &&
                _players.Values.All(p => !p.IsConnected) && now - LastActivityAt >= lobbyTtl)
            {
                Status = RoomStatus.Closed;
                ClosedAtUtc = now;
                Version++;
                LastActivityAt = now;
                return RoomCleanupDecision.Closed;
            }
            if (Status == RoomStatus.Closed && ClosedAtUtc.HasValue &&
                now - ClosedAtUtc.Value >= closedRetention)
                return RoomCleanupDecision.Remove;
            return RoomCleanupDecision.None;
        }
    }

    public (bool Success, string? ErrorCode, PlayerSession? Player, string? ReconnectToken, string? PreviousConnectionId) TryResumePlayer(
        string playerId,
        string reconnectToken,
        string connectionId,
        TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(reconnectToken) || string.IsNullOrWhiteSpace(connectionId))
            return (false, RoomErrorCodes.InvalidReconnectToken, null, null, null);
        lock (_gate)
        {
            if (Status == RoomStatus.Closed)
                return (false, RoomErrorCodes.RoomClosed, null, null, null);
            if (!_players.TryGetValue(playerId, out var player))
                return (false, RoomErrorCodes.PlayerNotFound, null, null, null);
            if (player.IsRevoked)
                return (false, RoomErrorCodes.SessionRevoked, null, null, null);
            if (!player.VerifyReconnectToken(reconnectToken))
                return (false, RoomErrorCodes.InvalidReconnectToken, null, null, null);

            var now = UtcNowProvider();
            if (player.DisconnectedAtUtc.HasValue && now - player.DisconnectedAtUtc.Value > ttl)
                return (false, RoomErrorCodes.ReconnectExpired, null, null, null);

            var previousConnectionId = player.ConnectionId;
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            player.RotateReconnectToken(token);
            player.Rebind(connectionId, now);
            Version++;
            LastActivityAt = now;
            return (true, null, player, token, previousConnectionId);
        }
    }

    public bool IsMatchAcceptingCommands(string? expectedMatchId = null)
    {
        lock (_gate)
        {
            return Status == RoomStatus.Playing &&
                   MatchId is not null &&
                   (expectedMatchId is null || string.Equals(MatchId, expectedMatchId, StringComparison.Ordinal)) &&
                   (Clock?.IsBeforeDeadline() ?? false);
        }
    }

    public bool IsCurrentMatch(string? expectedMatchId) =>
        !string.IsNullOrWhiteSpace(expectedMatchId) && string.Equals(MatchId, expectedMatchId, StringComparison.Ordinal);

    public bool TryPauseMatch(string expectedMatchId, string commandId, out MatchPausedEvent? paused)
    {
        lock (_gate)
        {
            paused = null;
            if (!string.IsNullOrWhiteSpace(commandId) && _pauseCommands.TryGetValue(commandId, out var replay))
            {
                paused = replay;
                return true;
            }
            if (Status != RoomStatus.Playing || !IsCurrentMatch(expectedMatchId) || Clock is null)
                return false;
            if (!Clock.TryPause(out var at)) return false;
            paused = new MatchPausedEvent(expectedMatchId, at, Clock.GetElapsedMilliseconds(), Clock.GetRemainingMilliseconds());
            Status = RoomStatus.Paused;
            Version++;
            LastActivityAt = at;
            if (!string.IsNullOrWhiteSpace(commandId)) _pauseCommands[commandId] = paused;
        }
        OnMatchPaused?.Invoke(RoomId, paused);
        return true;
    }

    public bool TryResumeMatch(string expectedMatchId, string commandId, out MatchResumedEvent? resumed)
    {
        lock (_gate)
        {
            resumed = null;
            if (!string.IsNullOrWhiteSpace(commandId) && _resumeCommands.TryGetValue(commandId, out var replay))
            {
                resumed = replay;
                return true;
            }
            if (Status != RoomStatus.Paused || !IsCurrentMatch(expectedMatchId) || Clock is null)
                return false;
            if (!Clock.TryResume(out var at)) return false;
            resumed = new MatchResumedEvent(expectedMatchId, at, Clock.DeadlineUtc, Clock.TotalPausedMilliseconds);
            Status = RoomStatus.Playing;
            Version++;
            LastActivityAt = at;
            if (!string.IsNullOrWhiteSpace(commandId)) _resumeCommands[commandId] = resumed;
        }
        OnMatchResumed?.Invoke(RoomId, resumed);
        return true;
    }

    public bool TryPrepareNewMatch(out RoomSnapshot snapshot)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Finished || MatchId is null) { snapshot = GetSnapshot(); return false; }
            _teamGames.Clear();
            foreach (var team in _teams.Values) team.ResetForRematch();
            foreach (var player in _players.Values) player.IsReady = false;
            MatchId = null;
            MatchStartTimeUtc = null;
            MatchFinishedAtUtc = null;
            MatchEndReason = null;
            Clock = null;
            Results = null;
            IsJoinLocked = false;
            IsRosterLocked = false;
            _pauseCommands.Clear();
            _resumeCommands.Clear();
            Status = RoomStatus.Lobby;
            Version++;
            LastActivityAt = UtcNowProvider();
            snapshot = GetSnapshot();
            return true;
        }
    }

    public bool TryExpireMatch(out MatchFinishedEvent? finishedEvent)
        => TryExpireMatch(null, out finishedEvent);

    public bool TryExpireMatch(string? expectedMatchId, out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            finishedEvent = null;
            if (Status != RoomStatus.Playing || MatchId is null ||
                (expectedMatchId is not null && !string.Equals(MatchId, expectedMatchId, StringComparison.Ordinal)) ||
                Clock is null || Clock.IsBeforeDeadline())
                return false;

            if (!Clock.TryFinish(global::TruyTimDanChu.Shared.MatchEndReason.Timeout, out var finishedAt))
                return false;

            Status = RoomStatus.Finished;
            MatchFinishedAtUtc = finishedAt;
            MatchEndReason = global::TruyTimDanChu.Shared.MatchEndReason.Timeout;
            Results = BuildResultsLocked(finishedAt, global::TruyTimDanChu.Shared.MatchEndReason.Timeout);
            Version++;
            LastActivityAt = finishedAt;
            finishedEvent = new MatchFinishedEvent(MatchId, finishedAt, global::TruyTimDanChu.Shared.MatchEndReason.Timeout, Results);
        }

        OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return true;
    }

    public bool TryFinalizeIfAllTeamsFinished(out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            finishedEvent = null;
            if (Status != RoomStatus.Playing || MatchId is null || MatchFinishedAtUtc.HasValue)
                return false;

            var teams = _teams.Values.ToArray();
            if (teams.Length == 0 || teams.Any(t => !t.FinishedAtUtc.HasValue))
                return false;

            var finishedAt = UtcNowProvider();
            Clock?.TryFinish(global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished, out finishedAt);
            Status = RoomStatus.Finished;
            MatchFinishedAtUtc = finishedAt;
            MatchEndReason = global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished;
            Results = BuildResultsLocked(finishedAt, global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished);
            Version++;
            LastActivityAt = finishedAt;
            finishedEvent = new MatchFinishedEvent(MatchId, finishedAt, MatchEndReason.Value, Results);
        }

        OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return true;
    }

    public (bool Success, string? ErrorCode, bool Value) TrySetEndOnFirstFinish(string adminToken, bool value)
    {
        lock (_gate)
        {
            if (!VerifyAdmin(adminToken)) return (false, RoomErrorCodes.UnauthorizedAdmin, EndOnFirstFinish);
            if (Status != RoomStatus.Lobby) return (false, RoomErrorCodes.EndOnFirstFinishLocked, EndOnFirstFinish);
            if (EndOnFirstFinish == value) return (true, null, value);
            EndOnFirstFinish = value;
            foreach (var player in _players.Values) player.IsReady = false;
            Version++;
            LastActivityAt = UtcNowProvider();
            return (true, null, value);
        }
    }

    public bool TryFinalizeTeamFinished(string expectedMatchId, string teamId, out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            finishedEvent = null;
            if (Status != RoomStatus.Playing || MatchId is null || MatchFinishedAtUtc.HasValue ||
                !string.Equals(MatchId, expectedMatchId, StringComparison.Ordinal)) return false;
            var team = _teams.TryGetValue(teamId, out var selected) ? selected : null;
            if (team is null || !team.FinishedAtUtc.HasValue) return false;
            if (!EndOnFirstFinish)
            {
                if (_teams.Values.Any(x => !x.FinishedAtUtc.HasValue)) return false;
                var allAt = UtcNowProvider();
                Clock?.TryFinish(global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished, out allAt);
                Status = RoomStatus.Finished;
                MatchFinishedAtUtc = allAt;
                MatchEndReason = global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished;
                Results = BuildResultsLocked(allAt, MatchEndReason.Value);
                Version++;
                LastActivityAt = allAt;
                finishedEvent = new MatchFinishedEvent(MatchId, allAt, MatchEndReason.Value, Results);
            }
            else
            {
                var firstAt = team.FinishedAtUtc.Value;
                Clock?.TryFinish(global::TruyTimDanChu.Shared.MatchEndReason.FirstTeamFinished, out firstAt);
                Status = RoomStatus.Finished;
                MatchFinishedAtUtc = firstAt;
                MatchEndReason = global::TruyTimDanChu.Shared.MatchEndReason.FirstTeamFinished;
                Results = BuildResultsLocked(firstAt, MatchEndReason.Value);
                Version++;
                LastActivityAt = firstAt;
                finishedEvent = new MatchFinishedEvent(MatchId, firstAt, MatchEndReason.Value, Results);
            }
        }
        if (finishedEvent is not null) OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return finishedEvent is not null;
    }

    public bool TryCommitTeamFinished(string expectedMatchId, string teamId, DateTimeOffset timestamp, out TeamSnapshot? snapshot, out MatchFinishedEvent? finishedEvent)
    {
        lock (_gate)
        {
            snapshot = null; finishedEvent = null;
            if (Status != RoomStatus.Playing || MatchId is null || !string.Equals(MatchId, expectedMatchId, StringComparison.Ordinal) ||
                !_teams.TryGetValue(teamId, out var team)) return false;
            if (!team.TryMarkFinished(timestamp)) { snapshot = team.GetSnapshot(_players.Values.Count(p => p.TeamId == teamId)); return false; }
            Version++; LastActivityAt = timestamp;
            snapshot = team.GetSnapshot(_players.Values.Count(p => p.TeamId == teamId));
            if (EndOnFirstFinish || _teams.Values.All(x => x.FinishedAtUtc.HasValue))
            {
                var reason = EndOnFirstFinish ? global::TruyTimDanChu.Shared.MatchEndReason.FirstTeamFinished : global::TruyTimDanChu.Shared.MatchEndReason.AllTeamsFinished;
                var finishedAt = timestamp; Clock?.TryFinish(reason, out finishedAt);
                Status = RoomStatus.Finished; MatchFinishedAtUtc = finishedAt; MatchEndReason = reason;
                Results = BuildResultsLocked(finishedAt, reason); Version++; LastActivityAt = finishedAt;
                finishedEvent = new MatchFinishedEvent(MatchId, finishedAt, reason, Results);
            }
        }
        if (finishedEvent is not null) OnMatchFinished?.Invoke(RoomId, finishedEvent);
        return true;
    }

    public PublicProgressSnapshot? GetPublicProgressSnapshot()
    {
        lock (_gate)
        {
            if (MatchId is null) return null;
            var teams = _teams.Values.OrderBy(t => t.DisplayOrder).Select(team =>
            {
                var players = _players.Values.Where(p => p.TeamId == team.TeamId).ToArray();
                var game = _teamGames.TryGetValue(team.TeamId, out var value) ? value : null;
                var progress = game?.Progress;
                var status = team.FinishedAtUtc.HasValue
                    ? PublicTeamStatus.Completed
                    : Status == RoomStatus.Finished
                        ? PublicTeamStatus.TimedOut
                        : players.Length > 0 && players.All(p => !p.IsConnected)
                            ? PublicTeamStatus.TeamOffline
                            : PublicTeamStatus.Playing;
                return new PublicTeamProgressSnapshot(
                    team.TeamId,
                    team.Name,
                    team.Color,
                    progress?.Shards ?? 0,
                    status,
                    null);
            }).ToArray();
            return new PublicProgressSnapshot(MatchId, Version, ShowLiveLeaderboard, ShowLiveLeaderboard ? teams : Array.Empty<PublicTeamProgressSnapshot>());
        }
    }

    private MatchResultsSnapshot BuildResultsLocked(DateTimeOffset finishedAt, MatchEndReason reason)
    {
        var matchId = MatchId ?? string.Empty;
        var startedAt = MatchStartTimeUtc ?? finishedAt;
        var teamResults = _teams.Values
            .OrderBy(t => t.DisplayOrder)
            .Select(team =>
            {
                var game = _teamGames.TryGetValue(team.TeamId, out var value) ? value : null;
                var roster = _players.Values.Where(p => p.TeamId == team.TeamId).OrderBy(p => p.JoinedAt).ToArray();
                if (game is null)
                {
                    return new TeamResultSnapshot(matchId, team.TeamId, team.Name, team.Color, null,
                        TeamResultStatus.TimedOut, roster.Select(p => new ResultMemberSnapshot(p.PlayerId, p.DisplayName, p.AvatarId)).ToArray(),
                        null, null, 0, null, Array.Empty<ChapterTiming>());
                }

                var status = team.FinishedAtUtc.HasValue ? TeamResultStatus.Completed :
                    reason is global::TruyTimDanChu.Shared.MatchEndReason.AdminEnded or global::TruyTimDanChu.Shared.MatchEndReason.FirstTeamFinished ? TeamResultStatus.EndedEarly :
                    reason == global::TruyTimDanChu.Shared.MatchEndReason.Cancelled || reason == global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed ? TeamResultStatus.Abandoned :
                    TeamResultStatus.TimedOut;
                return RankingService.FromTeam(game, team, roster, status);
            })
            .ToArray();

        var ranked = RankingService.Rank(teamResults);
        if (reason is global::TruyTimDanChu.Shared.MatchEndReason.Cancelled or global::TruyTimDanChu.Shared.MatchEndReason.RoomClosed)
            ranked = ranked.Select(t => t with { Rank = null }).ToArray();
        return new MatchResultsSnapshot(RoomId, matchId, startedAt, finishedAt, reason, ranked);
    }

    public RoomSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new RoomSnapshot(
                RoomId,
                RoomCode,
                RoomName,
                Status,
                AllowSelfTeamSelection,
                AllowLateJoin,
                ShowLiveLeaderboard,
                AdminCanPlay,
                EndOnFirstFinish,
                TimeLimitSeconds,
                CreatedAt,
                Version,
                IsJoinLocked,
                IsRosterLocked,
                MatchId,
                MatchStartTimeUtc,
                MatchId is not null && Clock is not null ? Clock.GetSnapshot(MatchId, Status == RoomStatus.Paused) : null,
                MatchFinishedAtUtc,
                MatchEndReason,
                ClosedAtUtc
            );
        }
    }

    public void IncrementVersion()
    {
        lock (_gate)
        {
            Version++;
            LastActivityAt = DateTimeOffset.UtcNow;
        }
    }
}
