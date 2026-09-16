using Microsoft.AspNetCore.SignalR;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class RoomHub : Hub
{
    private readonly RoomManager _roomManager;
    private readonly ReservationService _reservations;

    public RoomHub(RoomManager roomManager, ReservationService reservations)
    {
        _roomManager = roomManager;
        _reservations = reservations;
    }

    private bool AdminBound(string roomId, string token) => _roomManager.IsAdminConnectionBound(roomId, token, Context.ConnectionId);

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
        var response = _roomManager.AdminAddTeam(request, Context.ConnectionId);
        if (response.Success && response.Team is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamAdded", response.Team);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thiết lập đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminUpdateTeamResponse> AdminUpdateTeam(AdminUpdateTeamRequest request)
    {
        var response = _roomManager.AdminUpdateTeam(request, Context.ConnectionId);
        if (response.Success && response.Team is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamUpdated", response.Team);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thiết lập đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminReorderTeamsResponse> AdminReorderTeams(AdminReorderTeamsRequest request)
    {
        var response = _roomManager.AdminReorderTeams(request, Context.ConnectionId);
        if (response.Success && response.Teams is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamsReordered", response.Teams);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Thứ tự đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminRemoveTeamResponse> AdminRemoveTeam(AdminRemoveTeamRequest request)
    {
        var response = _roomManager.AdminRemoveTeam(request, Context.ConnectionId);
        if (response.Success && response.RemovedTeamId is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("TeamRemoved", response.RemovedTeamId);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Danh sách đội thi đấu đã thay đổi.");
        }
        return response;
    }

    public async Task<JoinTeamResponse> JoinTeam(JoinTeamRequest request)
    {
        if (request is null) return new JoinTeamResponse(false, RoomErrorCodes.PlayerNotFound, null, 0);
        var boundRoom = _roomManager.GetRoomById(request.RoomId);
        if (boundRoom is null || !boundRoom.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
            return new JoinTeamResponse(false, boundRoom is null ? RoomErrorCodes.RoomNotFound : RoomErrorCodes.ConnectionNotBound, null, boundRoom?.Version ?? 0);
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
        if (request is null) return new LeaveTeamResponse(false, RoomErrorCodes.PlayerNotFound, 0);
        var boundRoom = _roomManager.GetRoomById(request.RoomId);
        if (boundRoom is null || !boundRoom.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
            return new LeaveTeamResponse(false, boundRoom is null ? RoomErrorCodes.RoomNotFound : RoomErrorCodes.ConnectionNotBound, boundRoom?.Version ?? 0);
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
        var response = _roomManager.AdminMovePlayer(request, Context.ConnectionId);
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
        var (response, oldTeamId, connectionId) = _roomManager.AdminKickPlayer(request, Context.ConnectionId);
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
        var response = _roomManager.AdminToggleSelfTeamSelection(request, Context.ConnectionId);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("SelfTeamSelectionToggled", response.AllowSelfTeamSelection);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Chế độ tự chọn đội đã thay đổi.");
        }
        return response;
    }

    public async Task<SelectAvatarResponse> SelectAvatar(SelectAvatarRequest request)
    {
        if (request is null) return new SelectAvatarResponse(false, RoomErrorCodes.PlayerNotFound, null, 0);
        var boundRoom = _roomManager.GetRoomById(request.RoomId);
        if (boundRoom is null || !boundRoom.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
            return new SelectAvatarResponse(false, boundRoom is null ? RoomErrorCodes.RoomNotFound : RoomErrorCodes.ConnectionNotBound, null, boundRoom?.Version ?? 0);
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
        if (request is null) return new SetReadyResponse(false, RoomErrorCodes.PlayerNotFound, false, 0);
        var boundRoom = _roomManager.GetRoomById(request.RoomId);
        if (boundRoom is null || !boundRoom.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
            return new SetReadyResponse(false, boundRoom is null ? RoomErrorCodes.RoomNotFound : RoomErrorCodes.ConnectionNotBound, false, boundRoom?.Version ?? 0);
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
        var response = _roomManager.AdminSetJoinLock(request, Context.ConnectionId);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("JoinLockToggled", response.IsJoinLocked);
        }
        return response;
    }

    public async Task<AdminSetRosterLockResponse> AdminSetRosterLock(AdminSetRosterLockRequest request)
    {
        var response = _roomManager.AdminSetRosterLock(request, Context.ConnectionId);
        if (response.Success)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("RosterLockToggled", response.IsRosterLocked);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Khóa đội hình đã thay đổi.");
        }
        return response;
    }

    public async Task<AdminStartMatchResponse> AdminStartMatch(AdminStartMatchRequest request)
    {
        var response = _roomManager.AdminStartMatch(request, Context.ConnectionId);
        if (response.Success && response.MatchId is not null && response.MatchStartTimeUtc is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("CountdownStarted",
                new CountdownStartedEvent(response.MatchId, request.CountdownSeconds, response.MatchStartTimeUtc.Value));
        }
        return response;
    }

    public async Task<AdminCancelCountdownResponse> AdminCancelCountdown(AdminCancelCountdownRequest request)
    {
        var response = _roomManager.AdminCancelCountdown(request, Context.ConnectionId);
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
            return new GetTeamStateResponse(false, room.Status == RoomStatus.Paused ? RoomErrorCodes.MatchPaused : RoomErrorCodes.InvalidRoomStatus, null);
        }

        if (!room.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
        {
            return new GetTeamStateResponse(false, RoomErrorCodes.ConnectionNotBound, null);
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
            var error = room.Status == RoomStatus.Closed ? RoomErrorCodes.RoomClosed : room.Status == RoomStatus.Paused ? RoomErrorCodes.MatchPaused : RoomErrorCodes.InvalidRoomStatus;
            return new MovementAck(false, error, input.Sequence, 0, 0, 0, 0, 0);
        }

        if (!room.IsMatchAcceptingCommands())
        {
            return new MovementAck(false, RoomErrorCodes.MatchTimedOut, input.Sequence, 0, 0, 0, 0, 0);
        }

        if (input.MatchId is not null && !room.IsCurrentMatch(input.MatchId))
            return new MovementAck(false, RoomErrorCodes.MatchIdMismatch, input.Sequence, 0, 0, 0, 0, 0);

        if (!room.IsPlayerConnectionBound(input.PlayerId, Context.ConnectionId))
        {
            return new MovementAck(false, RoomErrorCodes.ConnectionNotBound, input.Sequence, 0, 0, 0, 0, 0);
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
            var error = room.Status == RoomStatus.Closed ? RoomErrorCodes.RoomClosed : room.Status == RoomStatus.Paused ? RoomErrorCodes.MatchPaused : RoomErrorCodes.InvalidRoomStatus;
            return new InteractResponse(false, error, request.ObjectId, false, null);
        }

        if (!room.IsMatchAcceptingCommands())
        {
            return new InteractResponse(false, RoomErrorCodes.MatchTimedOut, request.ObjectId, false, null);
        }

        if (request.MatchId is not null && !room.IsCurrentMatch(request.MatchId))
            return new InteractResponse(false, RoomErrorCodes.MatchIdMismatch, request.ObjectId, false, null);

        if (!room.IsPlayerConnectionBound(request.PlayerId, Context.ConnectionId))
        {
            return new InteractResponse(false, RoomErrorCodes.ConnectionNotBound, request.ObjectId, false, null);
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
            if (room is not null)
            {
                var publicProgress = room.GetPublicProgressSnapshot();
                if (publicProgress is not null && publicProgress.IsVisible)
                    await Clients.Group($"room_{room.RoomId}").SendAsync("PublicProgressUpdated", publicProgress);
            }
        }

        return response;
    }

    public async Task<AdminNewMatchResponse> AdminNewMatch(AdminNewMatchRequest request)
    {
        var response = _roomManager.AdminNewMatch(request, Context.ConnectionId);
        if (response.Success && response.Room is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", response.Room);
            if (response.Teams is not null)
                await Clients.Group($"room_{request.RoomId}").SendAsync("TeamsReordered", response.Teams);
            await Clients.Group($"room_{request.RoomId}").SendAsync("ReadyReset", "Lượt thi mới đã sẵn sàng.");
        }
        return response;
    }

    public async Task<AdminPauseMatchResponse> AdminPauseMatch(AdminPauseMatchRequest request)
    {
        var result = _roomManager.AdminPauseMatch(request, Context.ConnectionId);
        if (!result.Success || result.Event is null)
            return new AdminPauseMatchResponse(false, result.ErrorCode, null);
        var room = _roomManager.GetRoomById(request.RoomId);
        if (room is not null)
        {
            foreach (var released in _reservations.ReleaseByMatch(request.MatchId))
                await Clients.Group($"team_{released.TeamId}").SendAsync("PuzzleReservationChanged", released);
            await Clients.Group($"room_{request.RoomId}").SendAsync("MatchPaused", result.Event);
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", room.GetSnapshot());
        }
        return new AdminPauseMatchResponse(true, null, result.Event);
    }

    public async Task<AdminResumeMatchResponse> AdminResumeMatch(AdminResumeMatchRequest request)
    {
        var result = _roomManager.AdminResumeMatch(request, Context.ConnectionId);
        if (!result.Success || result.Event is null)
            return new AdminResumeMatchResponse(false, result.ErrorCode, null);
        var room = _roomManager.GetRoomById(request.RoomId);
        if (room is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("MatchResumed", result.Event);
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", room.GetSnapshot());
        }
        return new AdminResumeMatchResponse(true, null, result.Event);
    }

    public async Task<AdminEndMatchResponse> AdminEndMatch(AdminEndMatchRequest request)
    {
        var response = _roomManager.AdminEndMatch(request, Context.ConnectionId);
        if (response.Success && response.Event is not null)
        {
            _reservations.ReleaseByMatch(request.MatchId);
            await Clients.Group($"room_{request.RoomId}").SendAsync("MatchEnded", response.Event);
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", response.Room);
        }
        return response;
    }

    public async Task<AdminCancelMatchResponse> AdminCancelMatch(AdminCancelMatchRequest request)
    {
        var response = _roomManager.AdminCancelMatch(request, Context.ConnectionId);
        if (response.Success)
        {
            if (response.Event is not null && request.MatchId is not null)
                _reservations.ReleaseByMatch(request.MatchId);
            await Clients.Group($"room_{request.RoomId}").SendAsync("MatchCancelled", response.Event);
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", response.Room);
        }
        return response;
    }

    public async Task<AdminCloseRoomResponse> AdminCloseRoom(AdminCloseRoomRequest request)
    {
        var response = _roomManager.AdminCloseRoom(request, Context.ConnectionId);
        if (response.Success && response.Event is not null)
        {
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomClosed", response.Event);
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", response.Room);
        }
        return response;
    }

    public async Task<ResumeAdminResponse> ResumeAdmin(ResumeAdminRequest request)
    {
        var response = _roomManager.ResumeAdmin(request, Context.ConnectionId);
        if (!response.Success || response.Snapshot is null) return response;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{request.RoomId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"admin_{request.RoomId}");
        if (!string.IsNullOrWhiteSpace(response.Snapshot.LinkedPlayerId))
        {
            var linkedTeam = response.Snapshot.Players.FirstOrDefault(p => p.PlayerId == response.Snapshot.LinkedPlayerId)?.TeamId;
            if (!string.IsNullOrWhiteSpace(linkedTeam)) await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{linkedTeam}");
        }
        if (!string.IsNullOrWhiteSpace(response.PreviousConnectionId) && response.PreviousConnectionId != Context.ConnectionId)
            await Clients.Client(response.PreviousConnectionId).SendAsync("AdminSessionSuperseded", new AdminSessionSupersededEvent(request.RoomId));
        return response;
    }

    public async Task<LateJoinResponse> LateJoin(LateJoinRequest request)
    {
        var response = _roomManager.LateJoin(request, Context.ConnectionId);
        if (!response.Success || response.Room is null || response.PlayerId is null) return response;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.Room.RoomId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{request.TeamId}");
        await Clients.OthersInGroup($"room_{response.Room.RoomId}").SendAsync("PlayerJoined", new PlayerSnapshot(response.PlayerId, request.DisplayName, request.TeamId, request.AvatarId, "#deb783", false, true));
        return response;
    }

    public async Task<AdminSetCanPlayResponse> AdminSetCanPlay(AdminSetCanPlayRequest request)
    {
        var response = _roomManager.AdminSetCanPlay(request, Context.ConnectionId);
        if (response.Success) await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", _roomManager.GetRoomById(request.RoomId)?.GetSnapshot());
        return response;
    }

    public async Task<AdminSetEndOnFirstFinishResponse> AdminSetEndOnFirstFinish(AdminSetEndOnFirstFinishRequest request)
    {
        var response = _roomManager.AdminSetEndOnFirstFinish(request, Context.ConnectionId);
        if (response.Success)
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", _roomManager.GetRoomById(request.RoomId)?.GetSnapshot());
        return response;
    }

    public async Task<AdminJoinAsPlayerResponse> AdminJoinAsPlayer(AdminJoinAsPlayerRequest request)
    {
        var response = _roomManager.AdminJoinAsPlayer(request, Context.ConnectionId);
        if (response.Success && response.PlayerId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{request.TeamId}");
            await Clients.Group($"room_{request.RoomId}").SendAsync("RoomStateUpdated", response.Room);
        }
        return response;
    }

    public async Task<ResumePlayerResponse> ResumePlayer(ResumePlayerRequest request)
    {
        var (response, previousConnectionId) = _roomManager.ResumePlayer(request, Context.ConnectionId);
        if (!response.Success || response.Room is null || response.PlayerId is null)
            return response;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.Room.RoomId}");
        var player = response.Players?.FirstOrDefault(p => p.PlayerId == response.PlayerId);
        if (!string.IsNullOrWhiteSpace(player?.TeamId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"team_{player.TeamId}");

        if (!string.IsNullOrWhiteSpace(previousConnectionId) && previousConnectionId != Context.ConnectionId)
            await Clients.Client(previousConnectionId).SendAsync("SessionSuperseded", new SessionSupersededEvent(response.PlayerId));
        if (player is not null)
            await Clients.OthersInGroup($"room_{response.Room.RoomId}").SendAsync("PlayerJoined", player);
        return response;
    }

    public Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request) =>
        Task.FromResult(_roomManager.Heartbeat(request, Context.ConnectionId));

    public async Task<ReservePuzzleResponse> ReservePuzzle(ReservePuzzleRequest request)
    {
        if (!TryResolvePuzzleRequest(request?.RoomId, request?.PlayerId, request?.PuzzleId,
                request?.MatchId,
                out var room, out var player, out var teamGame, out var error))
            return new ReservePuzzleResponse(false, error, null, null);

        if (!room!.IsMatchAcceptingCommands())
            return new ReservePuzzleResponse(false, RoomErrorCodes.MatchTimedOut, null, null);

        if (!teamGame!.CanReservePuzzle(player!.PlayerId, request!.PuzzleId, out error))
            return new ReservePuzzleResponse(false, error, null, null);

        var key = new PuzzleReservationKey(teamGame.MatchId, teamGame.TeamId, request.PuzzleId);
        var response = _reservations.TryReserve(key, player.PlayerId, player.DisplayName);
        if (response.Success && response.Reservation is not null)
            await Clients.Group($"team_{teamGame.TeamId}").SendAsync("PuzzleReservationChanged", response.Reservation);
        return response;
    }

    public async Task<ReleasePuzzleResponse> ReleasePuzzle(ReleasePuzzleRequest request)
    {
        if (!TryResolvePuzzleRequest(request?.RoomId, request?.PlayerId, request?.PuzzleId,
                request?.MatchId,
                out _, out var player, out var teamGame, out var error))
            return new ReleasePuzzleResponse(false, error, null);

        var key = new PuzzleReservationKey(teamGame!.MatchId, teamGame.TeamId, request!.PuzzleId);
        var response = _reservations.TryRelease(key, player!.PlayerId, request.ReservationToken);
        if (response.Success && response.Reservation is not null)
            await Clients.Group($"team_{teamGame.TeamId}").SendAsync("PuzzleReservationChanged", response.Reservation);
        return response;
    }

    public Task<ValidatePuzzleSubmissionResponse> ValidatePuzzleSubmission(ValidatePuzzleSubmissionRequest request)
    {
        if (!TryResolvePuzzleRequest(request?.RoomId, request?.PlayerId, request?.PuzzleId,
                request?.MatchId,
                out _, out var player, out var teamGame, out var error))
            return Task.FromResult(new ValidatePuzzleSubmissionResponse(false, error));

        var key = new PuzzleReservationKey(teamGame!.MatchId, teamGame.TeamId, request!.PuzzleId);
        return Task.FromResult(_reservations.ValidateSubmission(key, player!.PlayerId, request.ReservationToken));
    }

    public async Task<SubmitPuzzleResponse> SubmitPuzzle(SubmitPuzzleRequest request)
    {
        if (!TryResolvePuzzleRequest(request?.RoomId, request?.PlayerId, request?.PuzzleId,
                request?.MatchId,
                out var room, out var player, out var teamGame, out var error))
            return new SubmitPuzzleResponse(false, error, false, false, 0, null);

        if (!room!.IsMatchAcceptingCommands())
            return new SubmitPuzzleResponse(false, RoomErrorCodes.MatchTimedOut, false, false,
                teamGame?.Progress.WrongAnswerCount ?? 0, teamGame?.GetSnapshot());

        // Replay của đúng CommandId phải trả cùng kết quả ngay cả khi lần submit đúng đã trả khóa.
        if (teamGame!.TryGetPuzzleCommandReplay(player!.PlayerId, request!.PuzzleId, request.CommandId, out var replay))
            return replay;

        var key = new PuzzleReservationKey(teamGame.MatchId, teamGame.TeamId, request.PuzzleId);
        var authorization = _reservations.ValidateSubmission(key, player.PlayerId, request.ReservationToken);
        if (!authorization.Success)
        {
            // Chặn race giữa hai gói trùng: gói đầu có thể vừa hoàn thành và trả khóa.
            if (teamGame.TryGetPuzzleCommandReplay(player.PlayerId, request.PuzzleId, request.CommandId, out replay))
                return replay;
            return new SubmitPuzzleResponse(false, authorization.ErrorCode, false, false,
                teamGame.Progress.WrongAnswerCount, teamGame.GetSnapshot());
        }

        SubmitPuzzleResponse response;
        if (request.PuzzleId == PuzzleIds.Mirrors)
        {
            teamGame.TrySubmitMirrors(player.PlayerId, request.Answer, request.CommandId, out response);
        }
        else if (request.PuzzleId == PuzzleIds.Draft)
        {
            teamGame.TrySubmitDraft(player.PlayerId, request.Answer, request.CommandId, out response);
        }
        else if (request.PuzzleId == PuzzleIds.River)
        {
            teamGame.TrySubmitRiver(player.PlayerId, request.Answer, request.CommandId, out response);
        }
        else if (request.PuzzleId == PuzzleIds.News)
        {
            teamGame.TrySubmitNews(player.PlayerId, request.Answer, request.CommandId, out response);
        }
        else if (request.PuzzleId == PuzzleIds.Finale)
        {
            teamGame.TrySubmitFinale(player.PlayerId, request.Answer, request.CommandId, out response);
        }
        else
        {
            response = new SubmitPuzzleResponse(false, PuzzleReservationErrorCodes.InvalidPuzzle, false, false,
                teamGame.Progress.WrongAnswerCount, teamGame.GetSnapshot());
        }

        if (room is not null && response.State?.Progress.FinishedAtUtc is not null && response.Mutated)
        {
            if (room.TryCommitTeamFinished(teamGame.MatchId, teamGame.TeamId, response.State.Progress.FinishedAtUtc.Value, out var updatedTeamSnapshot, out _) && updatedTeamSnapshot is not null)
            {
                await Clients.Group($"room_{room.RoomId}").SendAsync("TeamUpdated", updatedTeamSnapshot);
            }
        }

        if (response.Mutated && response.State is not null)
        {
            await Clients.Group($"team_{teamGame.TeamId}").SendAsync("TeamStateUpdated", response.State);
            if (room is not null)
            {
                var publicProgress = room.GetPublicProgressSnapshot();
                if (publicProgress is not null && publicProgress.IsVisible)
                    await Clients.Group($"room_{room.RoomId}").SendAsync("PublicProgressUpdated", publicProgress);
            }
        }

        if (response.Success && response.Correct)
        {
            if (request.PuzzleId != PuzzleIds.Finale || teamGame.Progress.FinaleDone)
            {
                var released = _reservations.TryRelease(key, player.PlayerId, request.ReservationToken);
                if (released.Success && released.Reservation is not null)
                    await Clients.Group($"team_{teamGame.TeamId}").SendAsync("PuzzleReservationChanged", released.Reservation);
            }
        }

        return response;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var room in _roomManager.GetRooms())
        {
            if (string.Equals(room.AdminConnectionId, Context.ConnectionId, StringComparison.Ordinal))
                room.AdminConnectionId = null;
        }
        foreach (var (room, player) in _roomManager.FindPlayersByConnection(Context.ConnectionId))
        {
            room.TryMarkPlayerDisconnected(Context.ConnectionId, out _);
            room.GetTeamGameForPlayer(player.PlayerId)?.SetMemberConnection(player.PlayerId, false);
            await Clients.OthersInGroup($"room_{room.RoomId}").SendAsync("PlayerJoined", player.GetSnapshot());
            foreach (var released in _reservations.ReleaseByOwner(player.PlayerId))
                await Clients.Group($"team_{released.TeamId}").SendAsync("PuzzleReservationChanged", released);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private bool TryResolvePuzzleRequest(
        string? roomId,
        string? playerId,
        string? puzzleId,
        string? expectedMatchId,
        out RoomInstance? room,
        out PlayerSession? player,
        out TeamGameInstance? teamGame,
        out string? errorCode)
    {
        room = _roomManager.GetRoomById(roomId);
        player = null;
        teamGame = null;
        if (room is null) { errorCode = RoomErrorCodes.RoomNotFound; return false; }
        if (expectedMatchId is not null && !room.IsCurrentMatch(expectedMatchId)) { errorCode = RoomErrorCodes.MatchIdMismatch; return false; }
        if (room.Status == RoomStatus.Closed) { errorCode = RoomErrorCodes.RoomClosed; return false; }
        if (room.Status == RoomStatus.Paused) { errorCode = RoomErrorCodes.MatchPaused; return false; }
        if (room.Status != RoomStatus.Playing) { errorCode = RoomErrorCodes.InvalidRoomStatus; return false; }
        if (!room.IsMatchAcceptingCommands()) { errorCode = RoomErrorCodes.MatchTimedOut; return false; }
        if (!PuzzleIds.IsKnown(puzzleId)) { errorCode = PuzzleReservationErrorCodes.InvalidPuzzle; return false; }
        player = room.GetPlayer(playerId);
        if (player is null || player.ConnectionId != Context.ConnectionId)
        {
            errorCode = RoomErrorCodes.PlayerNotFound;
            return false;
        }
        teamGame = room.GetTeamGameForPlayer(player.PlayerId);
        if (teamGame is null) { errorCode = RoomErrorCodes.PlayerNotInTeam; return false; }
        errorCode = null;
        return true;
    }
}


