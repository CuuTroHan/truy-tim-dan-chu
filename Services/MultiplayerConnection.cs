using Microsoft.AspNetCore.SignalR.Client;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Services;

public enum OnlineConnectionState { Disconnected, Connecting, Connected, Failed, Incompatible }

public sealed class MultiplayerConnection : IAsyncDisposable
{
    private readonly Uri _hubUri;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private HubConnection? _hub;
    private bool _disposed;
    public OnlineConnectionState State { get; private set; }
    public string Message { get; private set; } = "Chưa kết nối máy chủ.";
    public event Action? Changed;
    public event Action<PlayerSnapshot>? PlayerJoined;
    public event Action<string>? PlayerLeft;
    public event Action<TeamSnapshot>? TeamAdded;
    public event Action<TeamSnapshot>? TeamUpdated;
    public event Action<List<TeamSnapshot>>? TeamsReordered;
    public event Action<string>? TeamRemoved;
    public event Action<PlayerTeamChangedEvent>? PlayerTeamChanged;
    public event Action<PlayerKickedEvent>? KickedFromRoom;
    public event Action<bool>? SelfTeamSelectionToggled;
    public event Action<PlayerAvatarChangedEvent>? PlayerAvatarChanged;
    public event Action<PlayerReadyChangedEvent>? PlayerReadyChanged;
    public event Action<bool>? JoinLockToggled;
    public event Action<bool>? RosterLockToggled;
    public event Action<string?>? ReadyReset;
    public event Action<CountdownStartedEvent>? CountdownStarted;
    public event Action<CountdownCanceledEvent>? CountdownCanceled;
    public event Action<MatchStartedEvent>? MatchStarted;
    public event Action<TeamGameStateSnapshot>? TeamStateUpdated;
    public event Action<PlayerMovedBroadcast>? PlayerMoved;
    public event Action<PuzzleReservationState>? PuzzleReservationChanged;
    public event Action<PublicProgressSnapshot>? PublicProgressUpdated;
    public event Action<MatchFinishedEvent>? MatchFinished;
    public event Action<RoomSnapshot>? RoomStateUpdated;
    public event Action<SessionSupersededEvent>? SessionSuperseded;
    public event Action<MatchPausedEvent>? MatchPaused;
    public event Action<MatchResumedEvent>? MatchResumed;
    public event Action<MatchEndedEvent>? MatchEnded;
    public event Action<RoomClosedEvent>? RoomClosed;
    public event Action<AdminSessionSupersededEvent>? AdminSessionSuperseded;
    public event Action? Reconnected;

    public MultiplayerConnection(Uri hubUri, TimeSpan timeout)
    {
        if (!hubUri.IsAbsoluteUri || (hubUri.Scheme != "http" && hubUri.Scheme != "https"))
            throw new ArgumentException("Hub URL must be HTTP(S).", nameof(hubUri));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _hubUri = hubUri;
        _timeout = timeout;
    }

    public async Task ConnectAsync()
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try
        {
            if (_disposed || State == OnlineConnectionState.Connected) return;
            await ClearHubAsync();
            SetState(OnlineConnectionState.Connecting, "Đang kết nối máy chủ…");
            // Reconnect nghiệp vụ được điều khiển bởi Online.razor để có thể resume PlayerId/token;
            // không tự retry transport rồi vô tình gửi command của phiên cũ.
            var hub = new HubConnectionBuilder().WithUrl(_hubUri).Build();
            hub.ServerTimeout = TimeSpan.FromSeconds(30);
            hub.KeepAliveInterval = TimeSpan.FromSeconds(10);
            _hub = hub;
            hub.Closed += OnClosedAsync;
            hub.Reconnected += _ => { Reconnected?.Invoke(); return Task.CompletedTask; };
            hub.On<PlayerSnapshot>("PlayerJoined", p => PlayerJoined?.Invoke(p));
            hub.On<string>("PlayerLeft", id => PlayerLeft?.Invoke(id));
            hub.On<TeamSnapshot>("TeamAdded", t => TeamAdded?.Invoke(t));
            hub.On<TeamSnapshot>("TeamUpdated", t => TeamUpdated?.Invoke(t));
            hub.On<List<TeamSnapshot>>("TeamsReordered", list => TeamsReordered?.Invoke(list));
            hub.On<string>("TeamRemoved", id => TeamRemoved?.Invoke(id));
            hub.On<PlayerTeamChangedEvent>("PlayerTeamChanged", e => PlayerTeamChanged?.Invoke(e));
            hub.On<PlayerKickedEvent>("KickedFromRoom", e => KickedFromRoom?.Invoke(e));
            hub.On<bool>("SelfTeamSelectionToggled", allow => SelfTeamSelectionToggled?.Invoke(allow));
            hub.On<PlayerAvatarChangedEvent>("PlayerAvatarChanged", e => PlayerAvatarChanged?.Invoke(e));
            hub.On<PlayerReadyChangedEvent>("PlayerReadyChanged", e => PlayerReadyChanged?.Invoke(e));
            hub.On<bool>("JoinLockToggled", locked => JoinLockToggled?.Invoke(locked));
            hub.On<bool>("RosterLockToggled", locked => RosterLockToggled?.Invoke(locked));
            hub.On<string?>("ReadyReset", reason => ReadyReset?.Invoke(reason));
            hub.On<CountdownStartedEvent>("CountdownStarted", e => CountdownStarted?.Invoke(e));
            hub.On<CountdownCanceledEvent>("CountdownCanceled", e => CountdownCanceled?.Invoke(e));
            hub.On<MatchStartedEvent>("MatchStarted", e => MatchStarted?.Invoke(e));
            hub.On<TeamGameStateSnapshot>("TeamStateUpdated", s => TeamStateUpdated?.Invoke(s));
            hub.On<PlayerMovedBroadcast>("PlayerMoved", e => PlayerMoved?.Invoke(e));
            hub.On<PuzzleReservationState>("PuzzleReservationChanged", e => PuzzleReservationChanged?.Invoke(e));
            hub.On<PublicProgressSnapshot>("PublicProgressUpdated", e => PublicProgressUpdated?.Invoke(e));
            hub.On<MatchFinishedEvent>("MatchFinished", e => MatchFinished?.Invoke(e));
            hub.On<RoomSnapshot>("RoomStateUpdated", e => RoomStateUpdated?.Invoke(e));
            hub.On<SessionSupersededEvent>("SessionSuperseded", e => SessionSuperseded?.Invoke(e));
            hub.On<MatchPausedEvent>("MatchPaused", e => MatchPaused?.Invoke(e));
            hub.On<MatchResumedEvent>("MatchResumed", e => MatchResumed?.Invoke(e));
            hub.On<MatchEndedEvent>("MatchEnded", e => MatchEnded?.Invoke(e));
            hub.On<RoomClosedEvent>("RoomClosed", e => RoomClosed?.Invoke(e));
            hub.On<AdminSessionSupersededEvent>("AdminSessionSuperseded", e => AdminSessionSuperseded?.Invoke(e));
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            attempt.CancelAfter(_timeout);
            await hub.StartAsync(attempt.Token);
            var result = await hub.InvokeAsync<HandshakeResponse>("Handshake", ConnectionProtocol.Current, attempt.Token);
            if (!result.Accepted || result.ProtocolVersion != ConnectionProtocol.Version ||
                result.ContentVersion != ConnectionProtocol.ContentVersion)
            {
                await ClearHubAsync();
                SetState(OnlineConnectionState.Incompatible, "Phiên bản game chưa khớp máy chủ. Hãy tải lại trang để cập nhật.");
                return;
            }
            if (hub.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connection closed during handshake.");
            SetState(OnlineConnectionState.Connected, "Đã kết nối máy chủ.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await ClearHubAsync();
            SetState(OnlineConnectionState.Failed, "Không thể kết nối máy chủ. Kiểm tra mạng hoặc máy chủ rồi thử lại.");
        }
        finally { _gate.Release(); }
    }

    public async Task<CreateRoomResponse> CreateRoomAsync(CreateRoomRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new CreateRoomResponse(false, "NOT_CONNECTED", null, null, null, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<CreateRoomResponse>("CreateRoom", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new CreateRoomResponse(false, ex.Message, null, null, null, null);
        }
    }

    public async Task<JoinRoomResponse> JoinRoomAsync(JoinRoomRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new JoinRoomResponse(false, "NOT_CONNECTED", null, null, null, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<JoinRoomResponse>("JoinRoom", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new JoinRoomResponse(false, ex.Message, null, null, null, null);
        }
    }

    public async Task<AdminAddTeamResponse> AdminAddTeamAsync(AdminAddTeamRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminAddTeamResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminAddTeamResponse>("AdminAddTeam", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminAddTeamResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<AdminUpdateTeamResponse> AdminUpdateTeamAsync(AdminUpdateTeamRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminUpdateTeamResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminUpdateTeamResponse>("AdminUpdateTeam", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminUpdateTeamResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<AdminReorderTeamsResponse> AdminReorderTeamsAsync(AdminReorderTeamsRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminReorderTeamsResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminReorderTeamsResponse>("AdminReorderTeams", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminReorderTeamsResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<AdminRemoveTeamResponse> AdminRemoveTeamAsync(AdminRemoveTeamRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminRemoveTeamResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminRemoveTeamResponse>("AdminRemoveTeam", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminRemoveTeamResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<JoinTeamResponse> JoinTeamAsync(JoinTeamRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new JoinTeamResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<JoinTeamResponse>("JoinTeam", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new JoinTeamResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<LeaveTeamResponse> LeaveTeamAsync(LeaveTeamRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new LeaveTeamResponse(false, "NOT_CONNECTED", 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<LeaveTeamResponse>("LeaveTeam", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new LeaveTeamResponse(false, ex.Message, 0);
        }
    }

    public async Task<AdminMovePlayerResponse> AdminMovePlayerAsync(AdminMovePlayerRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminMovePlayerResponse(false, "NOT_CONNECTED", request.PlayerId, null, null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminMovePlayerResponse>("AdminMovePlayer", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminMovePlayerResponse(false, ex.Message, request.PlayerId, null, null, 0);
        }
    }

    public async Task<AdminKickPlayerResponse> AdminKickPlayerAsync(AdminKickPlayerRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminKickPlayerResponse(false, "NOT_CONNECTED", request.PlayerId, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminKickPlayerResponse>("AdminKickPlayer", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminKickPlayerResponse(false, ex.Message, request.PlayerId, 0);
        }
    }

    public async Task<AdminToggleSelfTeamSelectionResponse> AdminToggleSelfTeamSelectionAsync(AdminToggleSelfTeamSelectionRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminToggleSelfTeamSelectionResponse(false, "NOT_CONNECTED", false, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminToggleSelfTeamSelectionResponse>("AdminToggleSelfTeamSelection", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminToggleSelfTeamSelectionResponse(false, ex.Message, false, 0);
        }
    }

    public async Task<SelectAvatarResponse> SelectAvatarAsync(SelectAvatarRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new SelectAvatarResponse(false, "NOT_CONNECTED", null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<SelectAvatarResponse>("SelectAvatar", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new SelectAvatarResponse(false, ex.Message, null, 0);
        }
    }

    public async Task<SetReadyResponse> SetReadyAsync(SetReadyRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new SetReadyResponse(false, "NOT_CONNECTED", false, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<SetReadyResponse>("SetReady", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new SetReadyResponse(false, ex.Message, false, 0);
        }
    }

    public async Task<AdminSetJoinLockResponse> AdminSetJoinLockAsync(AdminSetJoinLockRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminSetJoinLockResponse(false, "NOT_CONNECTED", false, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminSetJoinLockResponse>("AdminSetJoinLock", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminSetJoinLockResponse(false, ex.Message, false, 0);
        }
    }

    public async Task<AdminSetRosterLockResponse> AdminSetRosterLockAsync(AdminSetRosterLockRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminSetRosterLockResponse(false, "NOT_CONNECTED", false, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminSetRosterLockResponse>("AdminSetRosterLock", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminSetRosterLockResponse(false, ex.Message, false, 0);
        }
    }

    public async Task<AdminStartMatchResponse> AdminStartMatchAsync(AdminStartMatchRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminStartMatchResponse(false, "NOT_CONNECTED", null, null, 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminStartMatchResponse>("AdminStartMatch", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminStartMatchResponse(false, ex.Message, null, null, 0);
        }
    }

    public async Task<AdminCancelCountdownResponse> AdminCancelCountdownAsync(AdminCancelCountdownRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminCancelCountdownResponse(false, "NOT_CONNECTED", 0);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<AdminCancelCountdownResponse>("AdminCancelCountdown", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new AdminCancelCountdownResponse(false, ex.Message, 0);
        }
    }

    public async Task<GetTeamStateResponse> GetMyTeamStateAsync(GetTeamStateRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new GetTeamStateResponse(false, "NOT_CONNECTED", null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<GetTeamStateResponse>("GetMyTeamState", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new GetTeamStateResponse(false, ex.Message, null);
        }
    }

    public async Task<MovementAck> SendMovementAsync(PlayerMovementInput input)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new MovementAck(false, "NOT_CONNECTED", input?.Sequence ?? 0, 0, 0, 0, 0, 0);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return await _hub.InvokeAsync<MovementAck>("SendMovement", input, cts.Token);
        }
        catch (Exception ex)
        {
            return new MovementAck(false, ex.Message, input?.Sequence ?? 0, 0, 0, 0, 0, 0);
        }
    }

    public async Task<InteractResponse> InteractAsync(InteractRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new InteractResponse(false, "NOT_CONNECTED", request?.ObjectId, false, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<InteractResponse>("Interact", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new InteractResponse(false, ex.Message, request?.ObjectId, false, null);
        }
    }

    public async Task<AdminEndMatchResponse> AdminEndMatchAsync(AdminEndMatchRequest request) => await InvokeAsync<AdminEndMatchResponse>("AdminEndMatch", request, new(false, "NOT_CONNECTED", null, null));
    public async Task<AdminCancelMatchResponse> AdminCancelMatchAsync(AdminCancelMatchRequest request) => await InvokeAsync<AdminCancelMatchResponse>("AdminCancelMatch", request, new(false, "NOT_CONNECTED", null, null));
    public async Task<AdminCloseRoomResponse> AdminCloseRoomAsync(AdminCloseRoomRequest request) => await InvokeAsync<AdminCloseRoomResponse>("AdminCloseRoom", request, new(false, "NOT_CONNECTED", null, null));
    public async Task<ResumeAdminResponse> ResumeAdminAsync(ResumeAdminRequest request) => await InvokeAsync<ResumeAdminResponse>("ResumeAdmin", request, new(false, "NOT_CONNECTED", null, null));
    public async Task<LateJoinResponse> LateJoinAsync(LateJoinRequest request) => await InvokeAsync<LateJoinResponse>("LateJoin", request, new(false, "NOT_CONNECTED", null, null, null, null, null));
    public async Task<AdminSetCanPlayResponse> AdminSetCanPlayAsync(AdminSetCanPlayRequest request) => await InvokeAsync<AdminSetCanPlayResponse>("AdminSetCanPlay", request, new(false, "NOT_CONNECTED", false, 0));
    public async Task<AdminSetEndOnFirstFinishResponse> AdminSetEndOnFirstFinishAsync(AdminSetEndOnFirstFinishRequest request) => await InvokeAsync<AdminSetEndOnFirstFinishResponse>("AdminSetEndOnFirstFinish", request, new(false, "NOT_CONNECTED", false, 0));
    public async Task<AdminJoinAsPlayerResponse> AdminJoinAsPlayerAsync(AdminJoinAsPlayerRequest request) => await InvokeAsync<AdminJoinAsPlayerResponse>("AdminJoinAsPlayer", request, new(false, "NOT_CONNECTED", null, null, null, null));

    private async Task<T> InvokeAsync<T>(string method, object request, T disconnected)
    {
        if (_hub is null || State != OnlineConnectionState.Connected) return disconnected;
        try { using var cts = new CancellationTokenSource(_timeout); return await _hub.InvokeAsync<T>(method, request, cts.Token); }
        catch (Exception) { return disconnected; }
    }

    public async Task<ResumePlayerResponse> ResumePlayerAsync(ResumePlayerRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new ResumePlayerResponse(false, "NOT_CONNECTED", null, null, null, null, null, null, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<ResumePlayerResponse>("ResumePlayer", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new ResumePlayerResponse(false, ex.Message, null, null, null, null, null, null, null);
        }
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new HeartbeatResponse(false, "NOT_CONNECTED", DateTimeOffset.UtcNow);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<HeartbeatResponse>("Heartbeat", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new HeartbeatResponse(false, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    public async Task<AdminNewMatchResponse> AdminNewMatchAsync(AdminNewMatchRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminNewMatchResponse(false, "NOT_CONNECTED", null, null, null);
        try { using var cts = new CancellationTokenSource(_timeout); return await _hub.InvokeAsync<AdminNewMatchResponse>("AdminNewMatch", request, cts.Token); }
        catch (Exception ex) { return new AdminNewMatchResponse(false, ex.Message, null, null, null); }
    }

    public async Task<AdminPauseMatchResponse> AdminPauseMatchAsync(AdminPauseMatchRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminPauseMatchResponse(false, "NOT_CONNECTED", null);
        try { using var cts = new CancellationTokenSource(_timeout); return await _hub.InvokeAsync<AdminPauseMatchResponse>("AdminPauseMatch", request, cts.Token); }
        catch (Exception ex) { return new AdminPauseMatchResponse(false, ex.Message, null); }
    }

    public async Task<AdminResumeMatchResponse> AdminResumeMatchAsync(AdminResumeMatchRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new AdminResumeMatchResponse(false, "NOT_CONNECTED", null);
        try { using var cts = new CancellationTokenSource(_timeout); return await _hub.InvokeAsync<AdminResumeMatchResponse>("AdminResumeMatch", request, cts.Token); }
        catch (Exception ex) { return new AdminResumeMatchResponse(false, ex.Message, null); }
    }

    public async Task<ReservePuzzleResponse> ReservePuzzleAsync(ReservePuzzleRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new ReservePuzzleResponse(false, "NOT_CONNECTED", null, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<ReservePuzzleResponse>("ReservePuzzle", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new ReservePuzzleResponse(false, ex.Message, null, null);
        }
    }

    public async Task<ReleasePuzzleResponse> ReleasePuzzleAsync(ReleasePuzzleRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new ReleasePuzzleResponse(false, "NOT_CONNECTED", null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<ReleasePuzzleResponse>("ReleasePuzzle", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new ReleasePuzzleResponse(false, ex.Message, null);
        }
    }

    public async Task<ValidatePuzzleSubmissionResponse> ValidatePuzzleSubmissionAsync(ValidatePuzzleSubmissionRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new ValidatePuzzleSubmissionResponse(false, "NOT_CONNECTED");
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<ValidatePuzzleSubmissionResponse>("ValidatePuzzleSubmission", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new ValidatePuzzleSubmissionResponse(false, ex.Message);
        }
    }

    public async Task<SubmitPuzzleResponse> SubmitPuzzleAsync(SubmitPuzzleRequest request)
    {
        if (_hub is null || State != OnlineConnectionState.Connected)
            return new SubmitPuzzleResponse(false, "NOT_CONNECTED", false, false, 0, null);
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            return await _hub.InvokeAsync<SubmitPuzzleResponse>("SubmitPuzzle", request, cts.Token);
        }
        catch (Exception ex)
        {
            return new SubmitPuzzleResponse(false, ex.Message, false, false, 0, null);
        }
    }

    private Task OnClosedAsync(Exception? error)
    {
        SetState(OnlineConnectionState.Disconnected, "Đã mất kết nối máy chủ. Bạn có thể thử lại.");
        return Task.CompletedTask;
    }

    private void SetState(OnlineConnectionState state, string message)
    {
        if (_disposed) return;
        State = state;
        Message = message;
        Changed?.Invoke();
    }

    private async Task ClearHubAsync()
    {
        var hub = _hub;
        _hub = null;
        if (hub is null) return;
        hub.Closed -= OnClosedAsync;
        await hub.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Changed = null;
        _lifetime.Cancel();
        await _gate.WaitAsync();
        try { await ClearHubAsync(); }
        finally { _gate.Release(); _lifetime.Dispose(); }
    }
}
