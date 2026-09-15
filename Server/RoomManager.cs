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
    private readonly ConcurrentDictionary<string, RoomInstance> _roomsById = new();
    private readonly ConcurrentDictionary<string, RoomInstance> _roomsByCode = new(StringComparer.OrdinalIgnoreCase);

    public int RoomCount => _roomsById.Count;
    public event Action<string, string, DateTimeOffset>? OnMatchStarted;

    public RoomManager(IOptions<RoomServerOptions>? options = null, IRoomCodeGenerator? codeGenerator = null)
    {
        _options = options?.Value ?? new RoomServerOptions();
        _codeGenerator = codeGenerator ?? new DefaultRoomCodeGenerator();
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
            request.EndOnFirstFinish
        )
        {
            AdminConnectionId = connectionId
        };

        room.OnMatchStarted = (mId, sTime) =>
        {
            OnMatchStarted?.Invoke(room.RoomId, mId, sTime);
        };

        if (!_roomsById.TryAdd(roomId, room) || !_roomsByCode.TryAdd(roomCode, room))
        {
            _roomsById.TryRemove(roomId, out _);
            _roomsByCode.TryRemove(roomCode, out _);
            return new CreateRoomResponse(false, RoomErrorCodes.ServerFull, null, null, null, null);
        }

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

    public AdminAddTeamResponse AdminAddTeam(AdminAddTeamRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
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

    public AdminUpdateTeamResponse AdminUpdateTeam(AdminUpdateTeamRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
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

    public AdminReorderTeamsResponse AdminReorderTeams(AdminReorderTeamsRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
        {
            return new AdminReorderTeamsResponse(false, RoomErrorCodes.UnauthorizedAdmin, null, room.Version);
        }

        var (success, error, teams) = room.TryReorderTeams(request.OrderedTeamIds);
        return new AdminReorderTeamsResponse(success, error, teams, room.Version);
    }

    public AdminRemoveTeamResponse AdminRemoveTeam(AdminRemoveTeamRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
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

    public AdminMovePlayerResponse AdminMovePlayer(AdminMovePlayerRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
        {
            return new AdminMovePlayerResponse(false, RoomErrorCodes.UnauthorizedAdmin, request.PlayerId, null, null, room.Version);
        }

        var (success, error, oldTeamId, newTeamId) = room.TryAdminMovePlayer(request.PlayerId, request.TargetTeamId);
        return new AdminMovePlayerResponse(success, error, request.PlayerId, oldTeamId, newTeamId, room.Version);
    }

    public (AdminKickPlayerResponse Response, string? OldTeamId, string? ConnectionId) AdminKickPlayer(AdminKickPlayerRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
        {
            return (new AdminKickPlayerResponse(false, RoomErrorCodes.UnauthorizedAdmin, request.PlayerId, room.Version), null, null);
        }

        var (success, error, oldTeamId, connectionId) = room.TryKickPlayer(request.PlayerId);
        return (new AdminKickPlayerResponse(success, error, success ? request.PlayerId : null, room.Version), oldTeamId, connectionId);
    }

    public AdminToggleSelfTeamSelectionResponse AdminToggleSelfTeamSelection(AdminToggleSelfTeamSelectionRequest request)
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

        if (!room.VerifyAdmin(request.AdminToken))
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

    public AdminSetJoinLockResponse AdminSetJoinLock(AdminSetJoinLockRequest request)
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

        var (success, error, isJoinLocked) = room.TrySetJoinLock(request.AdminToken, request.IsJoinLocked);
        return new AdminSetJoinLockResponse(success, error, isJoinLocked, room.Version);
    }

    public AdminSetRosterLockResponse AdminSetRosterLock(AdminSetRosterLockRequest request)
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

        var (success, error, isRosterLocked) = room.TrySetRosterLock(request.AdminToken, request.IsRosterLocked);
        return new AdminSetRosterLockResponse(success, error, isRosterLocked, room.Version);
    }

    public AdminStartMatchResponse AdminStartMatch(AdminStartMatchRequest request)
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

        var (success, error, matchId, startTime) = room.TryStartMatch(request.AdminToken, request.CountdownSeconds);
        return new AdminStartMatchResponse(success, error, matchId, startTime, room.Version);
    }

    public AdminCancelCountdownResponse AdminCancelCountdown(AdminCancelCountdownRequest request)
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
