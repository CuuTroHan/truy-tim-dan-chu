using Microsoft.AspNetCore.SignalR;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class RoomHub : Hub
{
    private readonly RoomManager _roomManager;

    public RoomHub(RoomManager roomManager)
    {
        _roomManager = roomManager;
    }

    public HandshakeResponse Handshake(HandshakeRequest? request) => ConnectionProtocol.Validate(request);

    public async Task<CreateRoomResponse> CreateRoom(CreateRoomRequest request)
    {
        var response = _roomManager.CreateRoom(request, Context.ConnectionId);
        if (response.Success && response.RoomId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.RoomId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"admin_{response.RoomId}");
        }
        return response;
    }

    public async Task<JoinRoomResponse> JoinRoom(JoinRoomRequest request)
    {
        var response = _roomManager.JoinRoom(request, Context.ConnectionId);
        if (response.Success && response.Room is not null && response.PlayerId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.Room.RoomId}");
            var joinedPlayer = response.Players?.FirstOrDefault(p => p.PlayerId == response.PlayerId);
            if (joinedPlayer is not null)
            {
                await Clients.OthersInGroup($"room_{response.Room.RoomId}").SendAsync("PlayerJoined", joinedPlayer);
            }
        }
        return response;
    }

    public async Task<AdminAddTeamResponse> AdminAddTeam(AdminAddTeamRequest request)
    {
        var response = _roomManager.AdminAddTeam(request);
        if (response.Success && response.Team is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamAdded", response.Team);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thiết lập đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminUpdateTeamResponse> AdminUpdateTeam(AdminUpdateTeamRequest request)
    {
        var response = _roomManager.AdminUpdateTeam(request);
        if (response.Success && response.Team is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamUpdated", response.Team);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thiết lập đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminReorderTeamsResponse> AdminReorderTeams(AdminReorderTeamsRequest request)
    {
        var response = _roomManager.AdminReorderTeams(request);
        if (response.Success && response.Teams is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamsReordered", response.Teams);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thứ tự đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminRemoveTeamResponse> AdminRemoveTeam(AdminRemoveTeamRequest request)
    {
        var response = _roomManager.AdminRemoveTeam(request);
        if (response.Success && response.RemovedTeamId is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamRemoved", response.RemovedTeamId);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Danh sách đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<JoinTeamResponse> JoinTeam(JoinTeamRequest request)
    {
        var (response, oldTeamId) = _roomManager.JoinTeam(request);
        if (response.Success && response.TeamId is not null)
        {
            if (!string.IsNullOrEmpty(oldTeamId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"team_{oldTeamId}");
            }
            await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{response.TeamId}");
            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerTeamChanged", new PlayerTeamChangedEvent(request.PlayerId, oldTeamId, response.TeamId));
        }
        return response;
    }

    public async Task<LeaveTeamResponse> LeaveTeam(LeaveTeamRequest request)
    {
        var (response, oldTeamId) = _roomManager.LeaveTeam(request);
        if (response.Success)
        {
            if (!string.IsNullOrEmpty(oldTeamId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"team_{oldTeamId}");
            }
            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerTeamChanged", new PlayerTeamChangedEvent(request.PlayerId, oldTeamId, null));
        }
        return response;
    }

    public async Task<AdminMovePlayerResponse> AdminMovePlayer(AdminMovePlayerRequest request)
    {
        var response = _roomManager.AdminMovePlayer(request);
        if (response.Success)
        {
            var playerConnId = _roomManager.GetRoomById(request.RoomId)?.GetPlayer(request.PlayerId)?.ConnectionId;
            if (!string.IsNullOrEmpty(playerConnId))
            {
                if (!string.IsNullOrEmpty(response.OldTeamId))
                {
                    await Groups.RemoveFromGroupAsync(playerConnId, $"team_{response.OldTeamId}");
                }
                if (!string.IsNullOrEmpty(response.NewTeamId))
                {
                    await Groups.AddToGroupAsync(playerConnId, $"team_{response.NewTeamId}");
                }
            }

            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerTeamChanged",
                new PlayerTeamChangedEvent(request.PlayerId, response.OldTeamId, response.NewTeamId));
        }
        return response;
    }

    public async Task<AdminKickPlayerResponse> AdminKickPlayer(AdminKickPlayerRequest request)
    {
        var (response, oldTeamId, connectionId) = _roomManager.AdminKickPlayer(request);
        if (response.Success)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                await Clients.Client(connectionId).SendAsync("KickedFromRoom", new PlayerKickedEvent(request.PlayerId, request.Reason));
                await Groups.RemoveFromGroupAsync(connectionId, $"room_{request.RoomId}");
                if (!string.IsNullOrEmpty(oldTeamId))
                {
                    await Groups.RemoveFromGroupAsync(connectionId, $"team_{oldTeamId}");
                }
            }

            if (!string.IsNullOrEmpty(oldTeamId))
            {
                await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerTeamChanged",
                    new PlayerTeamChangedEvent(request.PlayerId, oldTeamId, null));
            }

            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerLeft", request.PlayerId);
        }
        return response;
    }

    public async Task<AdminToggleSelfTeamSelectionResponse> AdminToggleSelfTeamSelection(AdminToggleSelfTeamSelectionRequest request)
    {
        var response = _roomManager.AdminToggleSelfTeamSelection(request);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("SelfTeamSelectionToggled", response.AllowSelfTeamSelection);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Chế độ tự chọn đội đã thay đổi.");
        }
        return response;
    }

    public async Task<SelectAvatarResponse> SelectAvatar(SelectAvatarRequest request)
    {
        var response = _roomManager.SelectAvatar(request);
        if (response.Success && response.AvatarId is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerAvatarChanged",
                new PlayerAvatarChangedEvent(request.PlayerId, response.AvatarId));
        }
        return response;
    }

    public async Task<SetReadyResponse> SetReady(SetReadyRequest request)
    {
        var response = _roomManager.SetReady(request);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("PlayerReadyChanged",
                new PlayerReadyChangedEvent(request.PlayerId, response.IsReady));
        }
        return response;
    }

    public async Task<AdminSetJoinLockResponse> AdminSetJoinLock(AdminSetJoinLockRequest request)
    {
        var response = _roomManager.AdminSetJoinLock(request);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("JoinLockToggled", response.IsJoinLocked);
        }
        return response;
    }

    public async Task<AdminSetRosterLockResponse> AdminSetRosterLock(AdminSetRosterLockRequest request)
    {
        var response = _roomManager.AdminSetRosterLock(request);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("RosterLockToggled", response.IsRosterLocked);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Khóa đội hình đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminStartMatchResponse> AdminStartMatch(AdminStartMatchRequest request)
    {
        var response = _roomManager.AdminStartMatch(request);
        if (response.Success && response.MatchId is not null && response.MatchStartTimeUtc is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("CountdownStarted",
                new CountdownStartedEvent(response.MatchId, request.CountdownSeconds, response.MatchStartTimeUtc.Value));
        }
        return response;
    }

    public async Task<AdminCancelCountdownResponse> AdminCancelCountdown(AdminCancelCountdownRequest request)
    {
        var response = _roomManager.AdminCancelCountdown(request);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("CountdownCanceled",
                new CountdownCanceledEvent());
        }
        return response;
    }

    public async Task<GetTeamStateResponse> GetMyTeamState(GetTeamStateRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RoomId) || string.IsNullOrWhiteSpace(request.PlayerId))
        {
            return new GetTeamStateResponse(false, RoomErrorCodes.PlayerNotFound, null);
        }

        var room = _roomManager.GetRoomById(request.RoomId);
        if (room is null)
        {
            return new GetTeamStateResponse(false, RoomErrorCodes.RoomNotFound, null);
        }

        if (room.Status != RoomStatus.Playing)
        {
            return new GetTeamStateResponse(false, RoomErrorCodes.InvalidRoomStatus, null);
        }

        var teamGame = room.GetTeamGameForPlayer(request.PlayerId);
        if (teamGame is null)
        {
            return new GetTeamStateResponse(false, RoomErrorCodes.PlayerNotInTeam, null);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{teamGame.TeamId}");
        return new GetTeamStateResponse(true, null, teamGame.GetSnapshot());
    }

    public async Task<MovementAck> SendMovement(PlayerMovementInput input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.RoomId) || string.IsNullOrWhiteSpace(input.PlayerId))
        {
            return new MovementAck(false, RoomErrorCodes.PlayerNotFound, 0, 0, 0, 0, 0, 0);
        }

        var room = _roomManager.GetRoomById(input.RoomId);
        if (room is null)
        {
            return new MovementAck(false, RoomErrorCodes.RoomNotFound, input.Sequence, 0, 0, 0, 0, 0);
        }

        if (room.Status != RoomStatus.Playing)
        {
            return new MovementAck(false, RoomErrorCodes.InvalidRoomStatus, input.Sequence, 0, 0, 0, 0, 0);
        }

        var teamGame = room.GetTeamGameForPlayer(input.PlayerId);
        if (teamGame is null)
        {
            return new MovementAck(false, RoomErrorCodes.PlayerNotInTeam, input.Sequence, 0, 0, 0, 0, 0);
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var success = teamGame.TryProcessMovement(input.PlayerId, input.Keys, input.Sequence, nowMs,
            out var ack, out var broadcast);

        if (success && broadcast is not null)
        {
            await Clients.OthersInGroup($"team_{teamGame.TeamId}").SendAsync("PlayerMoved", broadcast);
        }

        return ack;
    }

    public async Task<InteractResponse> Interact(InteractRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RoomId) || string.IsNullOrWhiteSpace(request.PlayerId))
        {
            return new InteractResponse(false, RoomErrorCodes.PlayerNotFound, request?.ObjectId, false, null);
        }

        var room = _roomManager.GetRoomById(request.RoomId);
        if (room is null)
        {
            return new InteractResponse(false, RoomErrorCodes.RoomNotFound, request.ObjectId, false, null);
        }

        if (room.Status != RoomStatus.Playing)
        {
            return new InteractResponse(false, RoomErrorCodes.InvalidRoomStatus, request.ObjectId, false, null);
        }

        var teamGame = room.GetTeamGameForPlayer(request.PlayerId);
        if (teamGame is null)
        {
            return new InteractResponse(false, RoomErrorCodes.PlayerNotInTeam, request.ObjectId, false, null);
        }

        var success = teamGame.TryProcessInteract(request.PlayerId, request.ObjectId, request.CommandId, out var response);

        if (success && response.Mutated && response.State is not null)
        {
            await Clients.Group($"team_{teamGame.TeamId}").SendAsync("TeamStateUpdated", response.State);
        }

        return response;
    }
}


