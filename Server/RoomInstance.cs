using System.Collections.Concurrent;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class RoomInstance
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, PlayerSession> _players = new();
    private readonly ConcurrentDictionary<string, TeamInstance> _teams = new();
    private readonly ConcurrentDictionary<string, TeamGameInstance> _teamGames = new();

    public string RoomId { get; }
    public string RoomCode { get; }
    public string RoomName { get; private set; }
    public RoomStatus Status { get; private set; } = RoomStatus.Lobby;
    public string AdminToken { get; }
    public string? AdminConnectionId { get; set; }
    public bool AllowSelfTeamSelection { get; private set; }
    public bool AllowLateJoin { get; private set; }
    public bool ShowLiveLeaderboard { get; private set; }
    public bool AdminCanPlay { get; private set; }
    public bool EndOnFirstFinish { get; private set; }
    public int TimeLimitSeconds { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int Version { get; private set; } = 1;
    public bool IsJoinLocked { get; private set; }
    public bool IsRosterLocked { get; private set; }
    public string? MatchId { get; private set; }
    public DateTimeOffset? MatchStartTimeUtc { get; private set; }
    public Func<DateTimeOffset> UtcNowProvider { get; set; } = () => DateTimeOffset.UtcNow;
    public Action<string, DateTimeOffset>? OnMatchStarted { get; set; }
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
        bool endOnFirstFinish)
    {
        RoomId = roomId;
        RoomCode = roomCode;
        RoomName = roomName;
        AdminToken = adminToken;
        TimeLimitSeconds = timeLimitSeconds;
        AllowSelfTeamSelection = allowSelfTeamSelection;
        AllowLateJoin = allowLateJoin;
        ShowLiveLeaderboard = showLiveLeaderboard;
        AdminCanPlay = adminCanPlay;
        EndOnFirstFinish = endOnFirstFinish;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
    }

    public bool VerifyAdmin(string? token) =>
        !string.IsNullOrEmpty(token) && string.Equals(AdminToken, token, StringComparison.Ordinal);

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
            _teamGames.Clear();
            Version++;
            LastActivityAt = UtcNowProvider();

            return (true, null);
        }
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
            var teamGame = new TeamGameInstance(matchId, RoomId, team.TeamId, team.Name, team.Color);
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
                MatchStartTimeUtc
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
