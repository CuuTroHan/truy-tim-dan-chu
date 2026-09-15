namespace TruyTimDanChu.Game;

public sealed class SinglePlayerSession : IGameSession
{
    private readonly GameEngine _engine;
    private readonly PlayerState _player = new();
    private readonly TeamProgress _progress = new();
    private readonly LocalUiState _uiState = new();

    public SinglePlayerSession(GameEngine? engine = null)
    {
        _engine = engine ?? new GameEngine();
        SyncModels();
    }

    public bool IsMultiplayer => false;
    public PlayerState Player => _player;
    public TeamProgress Progress => _progress;
    public LocalUiState UiState => _uiState;

    public Chapter Chapter => _engine.Chapter;
    public Panel Panel => _engine.Panel;
    public float X => _engine.X;
    public float Y => _engine.Y;
    public int Facing => _engine.Facing;
    public int Walking => _engine.Walking;
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
    public bool SaveDirty => _engine.SaveDirty;

    public void Begin()
    {
        _engine.Begin();
        SyncModels();
    }

    public void StartFromSave()
    {
        _engine.StartFromSave();
        SyncModels();
    }

    public Frame Tick(double timestampMs, int keys)
    {
        var frame = _engine.Tick(timestampMs, keys);
        SyncModels();
        return frame;
    }

    public void PauseClock() => _engine.PauseClock();

    public void Interact()
    {
        _engine.Interact();
        SyncModels();
    }

    public void ShowDialogue(IEnumerable<Line> lines, Action? after = null)
    {
        _engine.ShowDialogue(lines, after);
        SyncModels();
    }

    public void AdvanceDialogue()
    {
        _engine.AdvanceDialogue();
        SyncModels();
    }

    public void OpenPanel(Panel panel)
    {
        _engine.OpenPanel(panel);
        SyncModels();
    }

    public void ClosePanel()
    {
        _engine.ClosePanel();
        SyncModels();
    }

    public void TurnMirror(int index)
    {
        _engine.TurnMirror(index);
        SyncModels();
    }

    public void CheckMirrors()
    {
        _engine.CheckMirrors();
        SyncModels();
    }

    public void MoveDraft(int fromIndex, int toIndex)
    {
        _engine.MoveDraft(fromIndex, toIndex);
        SyncModels();
    }

    public void CheckDraft()
    {
        _engine.CheckDraft();
        SyncModels();
    }

    public void TurnRiver(int index)
    {
        _engine.TurnRiver(index);
        SyncModels();
    }

    public void CheckRiver()
    {
        _engine.CheckRiver();
        SyncModels();
    }

    public void ChooseNews(int option)
    {
        _engine.ChooseNews(option);
        SyncModels();
    }

    public void AddFinalePiece(int shardIndex)
    {
        _engine.AddFinalePiece(shardIndex);
        SyncModels();
    }

    public void ResetFinaleSequence()
    {
        _engine.FinaleSequence.Clear();
        SyncModels();
    }

    public void AdvanceReturnStep(int stepIndex)
    {
        _engine.AdvanceReturnStep(stepIndex);
        SyncModels();
    }

    public void SetMuted(bool value)
    {
        _engine.SetMuted(value);
        SyncModels();
    }

    public void SetTextScale(int value)
    {
        _engine.SetTextScale(value);
        SyncModels();
    }

    public void PresenterJump(Chapter chapter)
    {
        _engine.PresenterJump(chapter);
        SyncModels();
    }

    public SaveData CreateSave() => _engine.CreateSave();

    public void Load(string? json)
    {
        _engine.Load(json);
        SyncModels();
    }

    private void SyncModels()
    {
        _player.SetPosition(_engine.X, _engine.Y, _engine.Facing, _engine.Walking);
        _progress.Chapter = _engine.Chapter;
        Array.Copy(_engine.Spoken, _progress.Spoken, 4);
        Array.Copy(_engine.Lamps, _progress.Lamps, 4);
        Array.Copy(_engine.Clues, _progress.Clues, 3);
        Array.Copy(_engine.Lore, _progress.Lore, 4);
        Array.Copy(_engine.Mirrors, _progress.Mirrors, 4);
        Array.Copy(_engine.DraftSequence, _progress.DraftSequence, 6);
        Array.Copy(_engine.RiverSigns, _progress.RiverSigns, 4);
        _progress.FinaleSequence.Clear();
        _progress.FinaleSequence.AddRange(_engine.FinaleSequence);
        _progress.ReturnStep = _engine.ReturnStep;
        _progress.DraftDone = _engine.DraftDone;
        _progress.RiverDone = _engine.RiverDone;
        _progress.NewsDone = _engine.NewsDone;
        _progress.NewsNoise = _engine.NewsNoise;
        _progress.DraftFailures = _engine.DraftFailures;
        _progress.RiverFailures = _engine.RiverFailures;

        _uiState.Panel = _engine.Panel;
        _uiState.DialogueIndex = _engine.DialogueIndex;
        _uiState.Dialogue = _engine.Dialogue.ToList();
        _uiState.Toast = _engine.Toast;
        _uiState.Muted = _engine.Muted;
        _uiState.TextScale = _engine.TextScale;
        _uiState.UiDirty = _engine.UiDirty;
        _uiState.SaveDirty = _engine.SaveDirty;
    }
}
