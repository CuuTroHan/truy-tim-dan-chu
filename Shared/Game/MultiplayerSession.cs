using System.Collections.Concurrent;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Game;

public sealed class MultiplayerSession : IGameSession
{
    private readonly GameEngine _engine;
    private readonly PlayerState _player = new();
    private readonly TeamProgress _progress = new();
    private readonly LocalUiState _uiState = new();
    private readonly ConcurrentDictionary<string, TeamMemberState> _teammates = new();
    private readonly MovementReconciler _reconciler = new();
    private readonly ConcurrentDictionary<string, TeammateInterpolator> _teammateInterpolators = new();
    private double _lastTime;
    private bool _initialized;
    private int _sequence;
    private double _lastMovementSentTime;
    private int _lastKeys;

    public MovementReconciler Reconciler => _reconciler;
    public IReadOnlyDictionary<string, TeammateInterpolator> TeammateInterpolators => _teammateInterpolators;
    public string RoomId { get; set; } = string.Empty;
    public Func<PlayerMovementInput, Task<MovementAck>>? OnSendMovement { get; set; }
    public Func<InteractRequest, Task<InteractResponse>>? OnInteract { get; set; }
    public string MatchId { get; private set; } = string.Empty;
    public string TeamId { get; private set; } = string.Empty;
    public string TeamName { get; private set; } = string.Empty;
    public string TeamColor { get; private set; } = "#E53935";

    public bool IsMultiplayer => true;
    public PlayerState Player => _player;
    public TeamProgress Progress => _progress;
    public LocalUiState UiState => _uiState;
    public IReadOnlyCollection<TeamMemberState> Teammates => _teammates.Values.ToArray();

    public Chapter Chapter => _engine.Chapter;
    public Panel Panel => _engine.Panel;
    public float X => _player.X;
    public float Y => _player.Y;
    public int Facing => _player.Facing;
    public int Walking => _player.Walking;
    public int ElapsedSeconds => _engine.ElapsedSeconds;
    public int Shards => _engine.Shards;
    public string Objective => _engine.Objective;
    public string Area => _engine.Area;
    public string? Hint => _engine.Hint;
    public string NextTarget => _engine.NextTarget;
    public string? Toast => _engine.Toast;
    public bool Muted { get => _engine.Muted; set => _engine.SetMuted(value); }
    public int TextScale { get => _engine.TextScale; set => _engine.SetTextScale(value); }
    public int DialogueIndex => _engine.DialogueIndex;
    public IReadOnlyList<Line> Dialogue => _engine.Dialogue;
    public Line? CurrentLine => _engine.CurrentLine;
    public IReadOnlyList<string> DraftLabels => _engine.DraftLabels;
    public IReadOnlyList<string> ClueLabels => _engine.ClueLabels;
    public IReadOnlyList<string> FinalLabels => _engine.FinalLabels;
    public bool[] Spoken => _engine.Spoken;
    public bool[] Lamps => _engine.Lamps;
    public bool[] Clues => _engine.Clues;
    public bool[] Lore => _engine.Lore;
    public int[] Mirrors => _engine.Mirrors;
    public int[] DraftSequence => _engine.DraftSequence;
    public int[] RiverSigns => _engine.RiverSigns;
    public List<int> FinaleSequence => _engine.FinaleSequence;
    public int ReturnStep => _engine.ReturnStep;
    public bool DraftDone => _engine.DraftDone;
    public bool RiverDone => _engine.RiverDone;
    public bool NewsDone => _engine.NewsDone;
    public int NewsNoise => _engine.NewsNoise;
    public bool UiDirty => _engine.UiDirty;
    public bool SaveDirty => false;

    public MultiplayerSession(string playerId, string displayName, string avatarId, GameEngine? engine = null)
    {
        _player.PlayerId = playerId;
        _player.DisplayName = displayName;
        _player.AvatarId = avatarId;
        _engine = engine ?? new GameEngine();
    }

    public void ApplySnapshot(TeamGameStateSnapshot snapshot)
    {
        if (snapshot.Progress.Version < _progress.Version && _progress.Version != 0)
        {
            return;
        }

        MatchId = snapshot.MatchId;
        TeamId = snapshot.TeamId;
        TeamName = snapshot.TeamName;
        TeamColor = snapshot.TeamColor;
        _player.AccentColor = snapshot.TeamColor;

        _teammates.Clear();
        foreach (var m in snapshot.Members)
        {
            if (m.PlayerId == _player.PlayerId)
            {
                if (!_initialized)
                {
                    _player.X = m.X;
                    _player.Y = m.Y;
                    _player.Facing = m.Facing;
                    _player.Walking = m.Walking;
                    _initialized = true;
                    _reconciler.Reset();
                }
            }
            else
            {
                _teammates[m.PlayerId] = m;
                var interp = _teammateInterpolators.GetOrAdd(m.PlayerId, _ => new TeammateInterpolator(m.X, m.Y, m.Facing, m.Walking));
                interp.AddSnapshot(m.X, m.Y, m.Facing, m.Walking, 0, 0);
            }
        }

        // Đồng bộ tiến độ nhiệm vụ đội
        _progress.Chapter = snapshot.Progress.Chapter;
        for (int i = 0; i < snapshot.Progress.Spoken.Length && i < _progress.Spoken.Length; i++)
            _progress.Spoken[i] = snapshot.Progress.Spoken[i];
        for (int i = 0; i < snapshot.Progress.Lamps.Length && i < _progress.Lamps.Length; i++)
            _progress.Lamps[i] = snapshot.Progress.Lamps[i];
        for (int i = 0; i < snapshot.Progress.Clues.Length && i < _progress.Clues.Length; i++)
            _progress.Clues[i] = snapshot.Progress.Clues[i];
        _progress.Version = snapshot.Progress.Version;

        if (_engine.Chapter != snapshot.Progress.Chapter)
        {
            _engine.PresenterJump(snapshot.Progress.Chapter);
        }
        for (int i = 0; i < snapshot.Progress.Spoken.Length && i < _engine.Spoken.Length; i++)
            _engine.Spoken[i] = snapshot.Progress.Spoken[i];
        for (int i = 0; i < snapshot.Progress.Lamps.Length && i < _engine.Lamps.Length; i++)
            _engine.Lamps[i] = snapshot.Progress.Lamps[i];
        for (int i = 0; i < snapshot.Progress.Clues.Length && i < _engine.Clues.Length; i++)
            _engine.Clues[i] = snapshot.Progress.Clues[i];
    }

    public void UpdateTeammatePosition(string playerId, float x, float y, int facing, int walking, int sequence = 0, long timestampMs = 0)
    {
        if (_teammates.TryGetValue(playerId, out var member))
        {
            _teammates[playerId] = member with { X = x, Y = y, Facing = facing, Walking = walking };
        }
        var interp = _teammateInterpolators.GetOrAdd(playerId, _ => new TeammateInterpolator(x, y, facing, walking));
        interp.AddSnapshot(x, y, facing, walking, sequence, timestampMs);
    }

    public void ResetMovementSync()
    {
        _reconciler.Reset();
        _teammateInterpolators.Clear();
    }

    public void Begin()
    {
        _engine.Begin();
    }

    public void StartFromSave()
    {
        _engine.StartFromSave();
    }

    public Frame Tick(double timestampMs, int keys)
    {
        if (_lastTime == 0) _lastTime = timestampMs;
        var delta = Math.Clamp(timestampMs - _lastTime, 0, 50);
        var dt = (float)(delta / 1000f);
        _lastTime = timestampMs;

        if (Panel == Panel.None && Chapter != Chapter.Complete)
        {
            if (keys != _lastKeys || (keys != 0 && timestampMs - _lastMovementSentTime >= 66))
            {
                _lastKeys = keys;
                _lastMovementSentTime = timestampMs;
                var seq = ++_sequence;
                _reconciler.RecordInput(seq, keys, dt, timestampMs);
                var input = new PlayerMovementInput(RoomId, _player.PlayerId, keys, seq, (long)timestampMs);
                if (OnSendMovement is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        var ack = await OnSendMovement(input);
                        _reconciler.Reconcile(ack, _player);
                    });
                }
            }

            if (delta > 0)
            {
                TownCollision.SimulateStep(_player.X, _player.Y, keys, dt,
                    out var newX, out var newY, out var facing, out var walking, _player.Facing);
                _player.X = newX;
                _player.Y = newY;
                _player.Facing = facing;
                _player.Walking = walking;
                _engine.SetPosition(_player.X, _player.Y);
            }
        }
        else
        {
            if (_player.Walking != 0 || _lastKeys != 0)
            {
                _lastKeys = 0;
                var seq = ++_sequence;
                _reconciler.RecordInput(seq, 0, dt, timestampMs);
                var input = new PlayerMovementInput(RoomId, _player.PlayerId, 0, seq, (long)timestampMs);
                if (OnSendMovement is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        var ack = await OnSendMovement(input);
                        _reconciler.Reconcile(ack, _player);
                    });
                }
            }
            _player.Walking = 0;
        }

        // Camera theo người mình
        var cameraX = Math.Clamp(_player.X - 240f, 0f, 544f);
        var cameraY = Math.Clamp(_player.Y - 135f, 0f, 370f);

        // Lấy actors: NPC từ engine + teammates
        var engineFrame = _engine.Tick(timestampMs, 0);
        var actorList = new List<Actor>(engineFrame.Actors);

        foreach (var t in _teammates.Values)
        {
            var interp = _teammateInterpolators.GetOrAdd(t.PlayerId, _ => new TeammateInterpolator(t.X, t.Y, t.Facing, t.Walking));
            interp.Update(dt);
            actorList.Add(new Actor(t.AvatarId, t.DisplayName, interp.CurrentX, interp.CurrentY, t.AccentColor, "teammate", interp.CurrentFacing, interp.CurrentWalking));
        }

        return new Frame(
            _player.X, _player.Y,
            cameraX, cameraY,
            _player.Facing, _player.Walking,
            actorList.ToArray(),
            engineFrame.Objects,
            engineFrame.TargetId,
            engineFrame.TargetX,
            engineFrame.TargetY,
            engineFrame.TargetLabel,
            null,
            engineFrame.UiDirty,
            _player.DisplayName,
            _player.AvatarId,
            _player.AccentColor
        );
    }

    public void PauseClock()
    {
        _lastTime = 0;
        _player.Walking = 0;
        _engine.PauseClock();
    }

    public void Interact()
    {
        if (Panel != Panel.None) return;

        var target = TownCollision.Places
            .Select(p => (Id: p.Key, p.Value.X, p.Value.Y, p.Value.Kind, p.Value.Label,
                Distance: MathF.Sqrt((_player.X - p.Value.X) * (_player.X - p.Value.X) + (_player.Y - p.Value.Y) * (_player.Y - p.Value.Y))))
            .Where(o => o.Distance <= TownCollision.MaxInteractionDistance)
            .OrderBy(o => o.Distance)
            .FirstOrDefault();

        if (target.Id is null)
        {
            _engine.SetPosition(_player.X, _player.Y);
            _engine.Interact();
            return;
        }

        _engine.SetPosition(_player.X, _player.Y);
        if (_engine.Chapter == Chapter.Opening && target.Id is "trong" or "final_board" or "kieu_anh")
        {
            _engine.ShowDialogue(
            [
                new("Trọng", "Hòm Dân chủ trống không. Nhưng có lẽ từ đầu chúng ta đã tìm sai chỗ."),
                new("Kiều Anh", "Quang, đừng tìm một món đồ. Hãy theo đường đi của tiếng nói."),
                new("Quang", "Vậy mình sẽ hỏi những người đang sống ở thị trấn này.")
            ], () => PresenterJump(Chapter.Lights));
        }
        else
        {
            _engine.HandleObject(target.Id);
        }

        if (OnInteract is not null && !string.IsNullOrEmpty(RoomId))
        {
            var commandId = Guid.NewGuid().ToString("N");
            var req = new InteractRequest(RoomId, _player.PlayerId, target.Id, commandId);
            _ = Task.Run(async () =>
            {
                var response = await OnInteract(req);
                if (response.Success && response.State is not null && response.State.Progress.Version > _progress.Version)
                {
                    ApplySnapshot(response.State);
                }
            });
        }
    }
    public void ShowDialogue(IEnumerable<Line> lines, Action? after = null) => _engine.ShowDialogue(lines, after);
    public void AdvanceDialogue() => _engine.AdvanceDialogue();
    public void OpenPanel(Panel panel) => _engine.OpenPanel(panel);
    public void ClosePanel() => _engine.ClosePanel();
    public void TurnMirror(int index) => _engine.TurnMirror(index);
    public void CheckMirrors() => _engine.CheckMirrors();
    public void MoveDraft(int index, int direction) => _engine.MoveDraft(index, direction);
    public void CheckDraft() => _engine.CheckDraft();
    public void TurnRiver(int index) => _engine.TurnRiver(index);
    public void CheckRiver() => _engine.CheckRiver();
    public void ChooseNews(int option) => _engine.ChooseNews(option);
    public void AddFinalePiece(int shardIndex) => _engine.AddFinalePiece(shardIndex);
    public void ResetFinaleSequence() => _engine.FinaleSequence.Clear();
    public void AdvanceReturnStep(int stepIndex) => _engine.AdvanceReturnStep(stepIndex);
    public void SetMuted(bool value) => _engine.SetMuted(value);
    public void SetTextScale(int value) => _engine.SetTextScale(value);
    public void PresenterJump(Chapter chapter) => _engine.PresenterJump(chapter);
    public SaveData CreateSave() => _engine.CreateSave();
    public void Load(string? json) => _engine.Load(json);
}
