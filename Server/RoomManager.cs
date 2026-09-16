using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public interface IRoomCodeGenerator
{
    string GenerateCode();
}

public sealed class DefaultRoomCodeGenerator : IRoomCodeGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public string GenerateCode()
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return $"DC-{new string(chars)}";
    }
}

public sealed class RoomManager
{
    private readonly RoomServerOptions _options;
    private readonly IRoomCodeGenerator _codeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly ReservationService _reservations;
    private readonly IMatchStore? _store;
    private readonly ConcurrentDictionary<string, RoomInstance> _roomsById = new();
    private readonly ConcurrentDictionary<string, RoomInstance> _roomsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AdminNewMatchResponse> _newMatchCommands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _adminActionCommands = new(StringComparer.Ordinal);

    public int RoomCount => _roomsById.Count;
    public event Action<string, string, DateTimeOffset>? OnMatchStarted;
    public event Action<string, MatchFinishedEvent>? OnMatchFinished;

    public bool IsAdminConnectionBound(string roomId, string token, string connectionId)
    {
        var room = GetRoomById(roomId);
        return room is not null && room.VerifyAdmin(token) && string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal);
    }

    public RoomManager(IOptions<RoomServerOptions>? options = null, IRoomCodeGenerator? codeGenerator = null, TimeProvider? timeProvider = null, ReservationService? reservations = null, IMatchStore? store = null)
    {
        _options = options?.Value ?? new RoomServerOptions();
        _codeGenerator = codeGenerator ?? new DefaultRoomCodeGenerator();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reservations = reservations ?? new ReservationService();
        _store = store;
    }

    public CreateRoomResponse CreateRoom(CreateRoomRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new CreateRoomResponse(false, RoomErrorCodes.InvalidRoomName, null, null, null, null);
        }

        var trimmedName = request.RoomName?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length < 3 || trimmedName.Length > 50)
        {
            return new CreateRoomResponse(false, RoomErrorCodes.InvalidRoomName, null, null, null, null);
        }

        if (request.TimeLimitSeconds < _options.MinTimeLimitSeconds || request.TimeLimitSeconds > _options.MaxTimeLimitSeconds)
        {
            return new CreateRoomResponse(false, RoomErrorCodes.InvalidTimeLimit, null, null, null, null);
        }

        if (_roomsById.Count >= _options.MaxRooms)
        {
            return new CreateRoomResponse(false, RoomErrorCodes.ServerFull, null, null, null, null);
        }

        // Sinh mã phòng không trùng với cơ chế retry
        string? roomCode = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = _codeGenerator.GenerateCode();
            if (!_roomsByCode.ContainsKey(code))
            {
                roomCode = code;
                break;
            }
        }

        if (roomCode is null)
        {
            return new CreateRoomResponse(false, RoomErrorCodes.ServerFull, null, null, null, null);
        }

        var roomId = Guid.NewGuid().ToString("N");
        var adminToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var room = new RoomInstance(
            roomId,
            roomCode,
            trimmedName,
            adminToken,
            request.TimeLimitSeconds,
            request.AllowSelfTeamSelection,
            request.AllowLateJoin,
            request.ShowLiveLeaderboard,
            request.AdminCanPlay,
            request.EndOnFirstFinish,
            _timeProvider
        )
        {
            AdminConnectionId = connectionId
        };

        room.OnMatchStarted = (mId, sTime) =>
        {
            _store?.RecordMatchStarted(room);
            OnMatchStarted?.Invoke(room.RoomId, mId, sTime);
        };
        room.OnMatchFinished = (mId, finished) =>
        {
            _reservations.ReleaseByMatch(finished.MatchId);
            if (finished.Results is not null) _store?.FinalizeMatch(finished.Results);
            OnMatchFinished?.Invoke(mId, finished);
        };

        if (!_roomsById.TryAdd(roomId, room) || !_roomsByCode.TryAdd(roomCode, room))
        {
            _roomsById.TryRemove(roomId, out _);
            _roomsByCode.TryRemove(roomCode, out _);
            return new CreateRoomResponse(false, RoomErrorCodes.ServerFull, null, null, null, null);
        }

        _store?.RecordRoomCreated(room);
        return new CreateRoomResponse(true, null, roomId, roomCode, adminToken, room.GetSnapshot());
    }

    public RoomInstance? GetRoomById(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        _roomsById.TryGetValue(roomId, out var room);
        return room;
    }

    public RoomInstance? GetRoomByCode(string? roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode)) return null;
        _roomsByCode.TryGetValue(roomCode.Trim(), out var room);
        return room;
    }

    public IReadOnlyList<RoomInstance> GetRooms() => _roomsById.Values.ToArray();

    public bool RemoveRoom(RoomInstance room)
    {
        if (room is null) return false;
        var removed = _roomsById.TryRemove(room.RoomId, out _);
        _roomsByCode.TryRemove(room.RoomCode, out _);
        return removed;
    }

    public IReadOnlyList<(RoomInstance Room, PlayerSession Player)> FindPlayersByConnection(string connectionId) =>
        _roomsById.Values
            .Select(room => (Room: room, Player: room.GetPlayerByConnection(connectionId)))
            .Where(x => x.Player is not null)
            .Select(x => (x.Room, x.Player!))
            .ToArray();

    public JoinRoomResponse JoinRoom(JoinRoomRequest request, string connectionId)
    {
        if (request is null)
        {
            return new JoinRoomResponse(false, RoomErrorCodes.InvalidRoomCode, null, null, null, null);
        }

        var roomCode = request.RoomCode?.Trim();
        if (string.IsNullOrEmpty(roomCode))
        {
            return new JoinRoomResponse(false, RoomErrorCodes.InvalidRoomCode, null, null, null, null);
        }

        var room = GetRoomByCode(roomCode);
        if (room is null)
        {
            return new JoinRoomResponse(false, RoomErrorCodes.RoomNotFound, null, null, null, null);
        }

        var (success, error, player, reconnectToken) = room.TryAddPlayer(
            request.DisplayName,
            connectionId,
            _options.MaxPlayersPerRoom);

        if (!success || player is null)
        {
            return new JoinRoomResponse(false, error, null, null, null, null);
        }

        return new JoinRoomResponse(
            true,
            null,
            player.PlayerId,
            reconnectToken,
            room.GetSnapshot(),
            room.GetPlayerSnapshots(),
            room.GetTeamSnapshots());
    }

    public (ResumePlayerResponse Response, string? PreviousConnectionId) ResumePlayer(ResumePlayerRequest request, string connectionId)
    {
        if (request is null)
            return (new ResumePlayerResponse(false, RoomErrorCodes.InvalidReconnectToken, null, null, null, null, null, null, null), null);

        var room = GetRoomByCode(request.RoomCode);
        if (room is null)
            return (new ResumePlayerResponse(false, RoomErrorCodes.RoomNotFound, null, null, null, null, null, null, null), null);

        var result = room.TryResumePlayer(request.PlayerId, request.ReconnectToken, connectionId,
            TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectGraceSeconds)));
        if (!result.Success || result.Player is null)
            return (new ResumePlayerResponse(false, result.ErrorCode, null, null, null, null, null, null, null), result.PreviousConnectionId);

        var teamState = room.GetTeamGameForPlayer(result.Player.PlayerId)?.GetSnapshot();
        return (new ResumePlayerResponse(
            true,
            null,
            result.Player.PlayerId,
            result.ReconnectToken,
            room.GetSnapshot(),
            room.GetPlayerSnapshots(),
            room.GetTeamSnapshots(),
            teamState,
            room.GetPublicProgressSnapshot(),
            _reservations.GetByOwner(result.Player.PlayerId),
            room.Results), result.PreviousConnectionId);
    }

    public HeartbeatResponse Heartbeat(HeartbeatRequest request, string connectionId)
    {
        if (request is null) return new HeartbeatResponse(false, RoomErrorCodes.PlayerNotFound, DateTimeOffset.UtcNow);
        var room = GetRoomById(request.RoomId);
        if (room is null) return new HeartbeatResponse(false, RoomErrorCodes.RoomNotFound, DateTimeOffset.UtcNow);
        if (!room.IsPlayerConnectionBound(request.PlayerId, connectionId))
            return new HeartbeatResponse(false, RoomErrorCodes.ConnectionNotBound, DateTimeOffset.UtcNow);
        var player = room.GetPlayer(request.PlayerId)!;
        player.LastSeenAt = room.UtcNowProvider();
        return new HeartbeatResponse(true, null, player.LastSeenAt);
    }

    public AdminAddTeamResponse AdminAddTeam(AdminAddTeamRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminAddTeamResponse(false, RoomErrorCodes.InvalidTeamName, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminAddTeamResponse(false, RoomErrorCodes.RoomNotFound, null, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminAddTeamResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, room.Version);
        }

        var (success, error, team) = room.TryAddTeam(
            request.Name,
            request.Color,
            request.Capacity,
            _options.MaxTeamsPerRoom,
            _options.MaxPlayersPerTeam,
            _options.MaxPlayersPerRoom);

        return new AdminAddTeamResponse(success, error, team, room.Version);
    }

    public AdminUpdateTeamResponse AdminUpdateTeam(AdminUpdateTeamRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminUpdateTeamResponse(false, RoomErrorCodes.InvalidTeamName, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminUpdateTeamResponse(false, RoomErrorCodes.RoomNotFound, null, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminUpdateTeamResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, room.Version);
        }

        var (success, error, team) = room.TryUpdateTeam(
            request.TeamId,
            request.Name,
            request.Color,
            request.Capacity,
            _options.MaxPlayersPerTeam,
            _options.MaxPlayersPerRoom);

        return new AdminUpdateTeamResponse(success, error, team, room.Version);
    }

    public AdminReorderTeamsResponse AdminReorderTeams(AdminReorderTeamsRequest request, string? connectionId = null)
    {
        if (request is null || request.OrderedTeamIds is null)
        {
            return new AdminReorderTeamsResponse(false, RoomErrorCodes.InvalidTeamOrder, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminReorderTeamsResponse(false, RoomErrorCodes.RoomNotFound, null, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminReorderTeamsResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, room.Version);
        }

        var (success, error, teams) = room.TryReorderTeams(request.OrderedTeamIds);
        return new AdminReorderTeamsResponse(success, error, teams, room.Version);
    }

    public AdminRemoveTeamResponse AdminRemoveTeam(AdminRemoveTeamRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminRemoveTeamResponse(false, RoomErrorCodes.TeamNotFound, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminRemoveTeamResponse(false, RoomErrorCodes.RoomNotFound, null, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminRemoveTeamResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, room.Version);
        }

        var (success, error) = room.TryRemoveTeam(request.TeamId);
        return new AdminRemoveTeamResponse(success, error, success ? request.TeamId : null, room.Version);
    }

    public (JoinTeamResponse Response, string? OldTeamId) JoinTeam(JoinTeamRequest request)
    {
        if (request is null)
        {
            return (new JoinTeamResponse(false, RoomErrorCodes.InvalidRoomCode, null, 0), null);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return (new JoinTeamResponse(false, RoomErrorCodes.RoomNotFound, null, 0), null);
        }

        var (success, error, teamId, oldTeamId) = room.TryJoinTeam(request.PlayerId, request.TeamId, enforceSelfSelection: true);
        return (new JoinTeamResponse(success, error, teamId, room.Version), oldTeamId);
    }

    public (LeaveTeamResponse Response, string? OldTeamId) LeaveTeam(LeaveTeamRequest request)
    {
        if (request is null)
        {
            return (new LeaveTeamResponse(false, RoomErrorCodes.InvalidRoomCode, 0), null);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return (new LeaveTeamResponse(false, RoomErrorCodes.RoomNotFound, 0), null);
        }

        var (success, error, oldTeamId) = room.TryLeaveTeam(request.PlayerId, enforceSelfSelection: true);
        return (new LeaveTeamResponse(success, error, room.Version), oldTeamId);
    }

    public AdminMovePlayerResponse AdminMovePlayer(AdminMovePlayerRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminMovePlayerResponse(false, RoomErrorCodes.InvalidRoomCode, null, null, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminMovePlayerResponse(false, RoomErrorCodes.RoomNotFound, request.PlayerId, null, null, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminMovePlayerResponse(false, RoomErrorCodes.UnauthorizedAdmin, request.PlayerId, null, null, room.Version);
        }

        var (success, error, oldTeamId, newTeamId) = room.TryAdminMovePlayer(request.PlayerId, request.TargetTeamId);
        return new AdminMovePlayerResponse(success, error, request.PlayerId, oldTeamId, newTeamId, room.Version);
    }

    public (AdminKickPlayerResponse Response, string? OldTeamId, string? ConnectionId) AdminKickPlayer(AdminKickPlayerRequest request, string? adminConnectionId = null)
    {
        if (request is null)
        {
            return (new AdminKickPlayerResponse(false, RoomErrorCodes.InvalidRoomCode, null, 0), null, null);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return (new AdminKickPlayerResponse(false, RoomErrorCodes.RoomNotFound, request.PlayerId, 0), null, null);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (adminConnectionId is not null && !string.Equals(room.AdminConnectionId, adminConnectionId, StringComparison.Ordinal)))
        {
            return (new AdminKickPlayerResponse(false, RoomErrorCodes.UnauthorizedAdmin, request.PlayerId, room.Version), null, null);
        }

        var (success, error, oldTeamId, playerConnectionId) = room.TryKickPlayer(request.PlayerId);
        return (new AdminKickPlayerResponse(success, error, success ? request.PlayerId : null, room.Version), oldTeamId, playerConnectionId);
    }

    public AdminToggleSelfTeamSelectionResponse AdminToggleSelfTeamSelection(AdminToggleSelfTeamSelectionRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminToggleSelfTeamSelectionResponse(false, RoomErrorCodes.InvalidRoomCode, false, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminToggleSelfTeamSelectionResponse(false, RoomErrorCodes.RoomNotFound, false, 0);
        }

        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)))
        {
            return new AdminToggleSelfTeamSelectionResponse(false, RoomErrorCodes.UnauthorizedAdmin, room.AllowSelfTeamSelection, room.Version);
        }

        var (success, error, allow) = room.TryToggleSelfTeamSelection(request.AllowSelfTeamSelection);
        return new AdminToggleSelfTeamSelectionResponse(success, error, allow, room.Version);
    }

    public SelectAvatarResponse SelectAvatar(SelectAvatarRequest request)
    {
        if (request is null)
        {
            return new SelectAvatarResponse(false, RoomErrorCodes.InvalidAvatarId, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new SelectAvatarResponse(false, RoomErrorCodes.RoomNotFound, null, 0);
        }

        var (success, error, avatarId) = room.TrySelectAvatar(request.PlayerId, request.AvatarId);
        return new SelectAvatarResponse(success, error, avatarId, room.Version);
    }

    public SetReadyResponse SetReady(SetReadyRequest request)
    {
        if (request is null)
        {
            return new SetReadyResponse(false, RoomErrorCodes.PlayerNotFound, false, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new SetReadyResponse(false, RoomErrorCodes.RoomNotFound, false, 0);
        }

        var (success, error, isReady) = room.TrySetReady(request.PlayerId, request.IsReady);
        return new SetReadyResponse(success, error, isReady, room.Version);
    }

    public AdminSetJoinLockResponse AdminSetJoinLock(AdminSetJoinLockRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminSetJoinLockResponse(false, RoomErrorCodes.InvalidRoomCode, false, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminSetJoinLockResponse(false, RoomErrorCodes.RoomNotFound, false, 0);
        }

        if (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)) return new AdminSetJoinLockResponse(false, RoomErrorCodes.AdminNotBound, room.IsJoinLocked, room.Version);
        var (success, error, isJoinLocked) = room.TrySetJoinLock(request.AdminToken, request.IsJoinLocked);
        return new AdminSetJoinLockResponse(success, error, isJoinLocked, room.Version);
    }

    public AdminSetRosterLockResponse AdminSetRosterLock(AdminSetRosterLockRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminSetRosterLockResponse(false, RoomErrorCodes.InvalidRoomCode, false, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminSetRosterLockResponse(false, RoomErrorCodes.RoomNotFound, false, 0);
        }

        if (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)) return new AdminSetRosterLockResponse(false, RoomErrorCodes.AdminNotBound, room.IsRosterLocked, room.Version);
        var (success, error, isRosterLocked) = room.TrySetRosterLock(request.AdminToken, request.IsRosterLocked);
        return new AdminSetRosterLockResponse(success, error, isRosterLocked, room.Version);
    }

    public AdminStartMatchResponse AdminStartMatch(AdminStartMatchRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminStartMatchResponse(false, RoomErrorCodes.InvalidRoomCode, null, null, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminStartMatchResponse(false, RoomErrorCodes.RoomNotFound, null, null, 0);
        }

        if (connectionId is not null && !IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId))
            return new AdminStartMatchResponse(false, RoomErrorCodes.AdminNotBound, null, null, room.Version);

        var (success, error, matchId, startTime) = room.TryStartMatch(request.AdminToken, request.CountdownSeconds);
        return new AdminStartMatchResponse(success, error, matchId, startTime, room.Version);
    }

    public AdminEndMatchResponse AdminEndMatch(AdminEndMatchRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId)) return new(false, RoomErrorCodes.InvalidCommandId, null, null);
        var key = $"end:{request.RoomId}:{request.CommandId}";
        if (_adminActionCommands.TryGetValue(key, out var cached) && cached is AdminEndMatchResponse prior) return prior;
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId)) return new(false, RoomErrorCodes.AdminNotBound, null, null);
        if (!room.TryEndMatch(request.MatchId, MatchEndReason.AdminEnded, out var finished) || finished is null)
            return new(false, RoomErrorCodes.MatchCannotBeEnded, room.GetSnapshot(), null);
        var response = new AdminEndMatchResponse(true, null, room.GetSnapshot(), new MatchEndedEvent(request.MatchId, finished.FinishedAtUtc, MatchEndReason.AdminEnded, finished.Results));
        _adminActionCommands.TryAdd(key, response);
        return response;
    }

    public AdminCancelMatchResponse AdminCancelMatch(AdminCancelMatchRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId)) return new(false, RoomErrorCodes.InvalidCommandId, null, null);
        var key = $"cancel:{request.RoomId}:{request.CommandId}";
        if (_adminActionCommands.TryGetValue(key, out var cached) && cached is AdminCancelMatchResponse prior) return prior;
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId)) return new(false, RoomErrorCodes.AdminNotBound, null, null);
        if (room.Status == RoomStatus.Countdown)
        {
            var cancelled = room.TryCancelCountdown(request.AdminToken);
            if (!cancelled.Success) return new(false, RoomErrorCodes.MatchCannotBeCancelled, room.GetSnapshot(), null);
            var response = new AdminCancelMatchResponse(true, null, room.GetSnapshot(), null);
            _adminActionCommands.TryAdd(key, response);
            return response;
        }
        if (request.MatchId is null || !room.TryEndMatch(request.MatchId, MatchEndReason.Cancelled, out var finished) || finished is null)
            return new(false, RoomErrorCodes.MatchCannotBeCancelled, room.GetSnapshot(), null);
        var result = new AdminCancelMatchResponse(true, null, room.GetSnapshot(), new MatchEndedEvent(request.MatchId, finished.FinishedAtUtc, MatchEndReason.Cancelled, finished.Results));
        _adminActionCommands.TryAdd(key, result);
        return result;
    }

    public AdminCloseRoomResponse AdminCloseRoom(AdminCloseRoomRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId)) return new(false, RoomErrorCodes.InvalidCommandId, null, null);
        var key = $"close:{request.RoomId}:{request.CommandId}";
        if (_adminActionCommands.TryGetValue(key, out var cached) && cached is AdminCloseRoomResponse prior) return prior;
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null);
        if (room.Status == RoomStatus.Closed) return new(false, RoomErrorCodes.RoomAlreadyClosed, room.GetSnapshot(), null);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId)) return new(false, RoomErrorCodes.AdminNotBound, null, null);
        if (!room.TryCloseRoom(request.Reason, out var closed, out _ ) || closed is null)
            return new(false, RoomErrorCodes.RoomAlreadyClosed, room.GetSnapshot(), null);
        _reservations.ReleaseByMatch(room.MatchId ?? string.Empty);
        var response = new AdminCloseRoomResponse(true, null, room.GetSnapshot(), closed);
        _adminActionCommands.TryAdd(key, response);
        return response;
    }

    public ResumeAdminResponse ResumeAdmin(ResumeAdminRequest request, string connectionId)
    {
        if (request is null) return new(false, RoomErrorCodes.UnauthorizedAdmin, null, null);
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null);
        var result = room.TryResumeAdmin(request.AdminToken, connectionId);
        if (!result.Success) return new(false, room.Status == RoomStatus.Closed ? RoomErrorCodes.RoomClosed : RoomErrorCodes.UnauthorizedAdmin, null, null);
        if (result.NewToken is not null) _store?.UpdateAdminTokenHash(room.RoomId, PlayerSession.HashToken(result.NewToken));
        var linked = room.LinkedAdminPlayerId;
        var snapshot = new AdminDashboardSnapshot(room.GetSnapshot(), room.GetTeamSnapshots(), room.GetPlayerSnapshots().Select(p => linked == p.PlayerId ? p with { IsAdmin = true } : p).ToList(), room.GetSnapshot().Clock, room.GetPublicProgressSnapshot(), room.Results, linked is null ? Array.Empty<PuzzleReservationState>() : _reservations.GetByOwner(linked), linked);
        return new ResumeAdminResponse(true, null, result.NewToken, snapshot, result.PreviousConnectionId);
    }

    public LateJoinResponse LateJoin(LateJoinRequest request, string connectionId)
    {
        if (request is null) return new(false, RoomErrorCodes.InvalidDisplayName, null, null, null, null, null);
        var room = GetRoomByCode(request.RoomCode);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null, null, null, null);
        var result = room.TryLateJoin(request.DisplayName, connectionId, request.TeamId, request.AvatarId, _options.MaxPlayersPerRoom);
        if (!result.Success || result.Player is null) return new(false, result.ErrorCode, null, null, room.GetSnapshot(), null, room.GetPublicProgressSnapshot());
        return new(true, null, result.Player.PlayerId, result.ReconnectToken, room.GetSnapshot(), room.GetTeamGameForPlayer(result.Player.PlayerId)?.GetSnapshot(), room.GetPublicProgressSnapshot());
    }

    public AdminSetCanPlayResponse AdminSetCanPlay(AdminSetCanPlayRequest request, string connectionId)
    {
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, false, 0);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId)) return new(false, RoomErrorCodes.AdminNotBound, room.AdminCanPlay, room.Version);
        var result = room.TrySetAdminCanPlay(request.AdminToken, request.AdminCanPlay);
        return new(result.Success, result.ErrorCode, result.Value, room.Version);
    }

    public AdminSetEndOnFirstFinishResponse AdminSetEndOnFirstFinish(AdminSetEndOnFirstFinishRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId))
            return new(false, RoomErrorCodes.InvalidCommandId, false, 0);
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, false, 0);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId))
            return new(false, RoomErrorCodes.AdminNotBound, room.EndOnFirstFinish, room.Version);
        var result = room.TrySetEndOnFirstFinish(request.AdminToken, request.EndOnFirstFinish);
        return new(result.Success, result.ErrorCode, result.Value, room.Version);
    }

    public AdminJoinAsPlayerResponse AdminJoinAsPlayer(AdminJoinAsPlayerRequest request, string connectionId)
    {
        var room = GetRoomById(request.RoomId);
        if (room is null) return new(false, RoomErrorCodes.RoomNotFound, null, null, null, null);
        if (!IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId)) return new(false, RoomErrorCodes.AdminNotBound, null, null, null, null);
        var result = room.TryAdminJoinAsPlayer(request.AdminToken, connectionId, request.TeamId, request.DisplayName, request.AvatarId, _options.MaxPlayersPerRoom);
        return new(result.Success, result.ErrorCode, result.Player?.PlayerId, result.ReconnectToken, room.GetSnapshot(), result.Player is null ? null : room.GetTeamGameForPlayer(result.Player.PlayerId)?.GetSnapshot());
    }

    public AdminNewMatchResponse AdminNewMatch(AdminNewMatchRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId))
            return new AdminNewMatchResponse(false, RoomErrorCodes.InvalidCommandId, null, null, null);
        var cacheKey = $"{request.RoomId}:{request.CommandId}";
        if (_newMatchCommands.TryGetValue(cacheKey, out var replay)) return replay;
        var room = GetRoomById(request.RoomId);
        if (room is null) return new AdminNewMatchResponse(false, RoomErrorCodes.RoomNotFound, null, null, null);
        if (!room.VerifyAdmin(request.AdminToken))
            return new AdminNewMatchResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, null, null);
        if (!string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal))
            return new AdminNewMatchResponse(false, RoomErrorCodes.AdminNotBound, null, null, null);
        if (_store is not null && room.Results is null)
            return new AdminNewMatchResponse(false, RoomErrorCodes.PersistenceUnavailable, null, null, null);
        if (!room.TryPrepareNewMatch(out var snapshot))
            return new AdminNewMatchResponse(false, RoomErrorCodes.InvalidRoomStatus, null, null, null);
        var response = new AdminNewMatchResponse(true, null, snapshot, room.GetPlayerSnapshots(), room.GetTeamSnapshots());
        _newMatchCommands.TryAdd(cacheKey, response);
        return response;
    }

    public (bool Success, string? ErrorCode, MatchPausedEvent? Event) AdminPauseMatch(AdminPauseMatchRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId)) return (false, RoomErrorCodes.InvalidCommandId, null);
        var room = GetRoomById(request.RoomId);
        if (room is null) return (false, RoomErrorCodes.RoomNotFound, null);
        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal))) return (false, RoomErrorCodes.AdminNotBound, null);
        if (!string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)) return (false, RoomErrorCodes.AdminNotBound, null);
        if (!room.IsCurrentMatch(request.MatchId)) return (false, RoomErrorCodes.MatchIdMismatch, null);
        if (room.Status != RoomStatus.Playing && room.Status != RoomStatus.Paused)
            return (false, RoomErrorCodes.InvalidRoomStatus, null);
        return room.TryPauseMatch(request.MatchId, request.CommandId, out var paused)
            ? (true, null, paused)
            : (false, RoomErrorCodes.MatchAlreadyPaused, null);
    }

    public (bool Success, string? ErrorCode, MatchResumedEvent? Event) AdminResumeMatch(AdminResumeMatchRequest request, string connectionId)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CommandId)) return (false, RoomErrorCodes.InvalidCommandId, null);
        var room = GetRoomById(request.RoomId);
        if (room is null) return (false, RoomErrorCodes.RoomNotFound, null);
        if (!room.VerifyAdmin(request.AdminToken) || (connectionId is not null && !string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal))) return (false, RoomErrorCodes.AdminNotBound, null);
        if (!string.Equals(room.AdminConnectionId, connectionId, StringComparison.Ordinal)) return (false, RoomErrorCodes.AdminNotBound, null);
        if (!room.IsCurrentMatch(request.MatchId)) return (false, RoomErrorCodes.MatchIdMismatch, null);
        if (room.Status != RoomStatus.Paused && room.Status != RoomStatus.Playing)
            return (false, RoomErrorCodes.MatchNotPaused, null);
        return room.TryResumeMatch(request.MatchId, request.CommandId, out var resumed)
            ? (true, null, resumed)
            : (false, RoomErrorCodes.MatchNotPaused, null);
    }

    public AdminCancelCountdownResponse AdminCancelCountdown(AdminCancelCountdownRequest request, string? connectionId = null)
    {
        if (request is null)
        {
            return new AdminCancelCountdownResponse(false, RoomErrorCodes.InvalidRoomCode, 0);
        }

        var room = GetRoomById(request.RoomId);
        if (room is null)
        {
            return new AdminCancelCountdownResponse(false, RoomErrorCodes.RoomNotFound, 0);
        }

        if (connectionId is not null && !IsAdminConnectionBound(request.RoomId, request.AdminToken, connectionId))
            return new AdminCancelCountdownResponse(false, RoomErrorCodes.AdminNotBound, room.Version);

        var (success, error) = room.TryCancelCountdown(request.AdminToken);
        return new AdminCancelCountdownResponse(success, error, room.Version);
    }

    public bool RemoveRoom(string roomId)
    {
        if (_roomsById.TryRemove(roomId, out var room))
        {
            _roomsByCode.TryRemove(room.RoomCode, out _);
            return true;
        }
        return false;
    }
}
