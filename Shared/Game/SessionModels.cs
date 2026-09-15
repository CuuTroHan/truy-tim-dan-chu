namespace TruyTimDanChu.Game;

/// <summary>
/// Trạng thái người chơi cá nhân (vị trí, hướng nhìn, hoạt ảnh, thông tin hiển thị).
/// </summary>
public sealed class PlayerState
{
    public string PlayerId { get; set; } = "local";
    public string DisplayName { get; set; } = "Quang";
    public string AvatarId { get; set; } = "pipoya_0";
    public string AccentColor { get; set; } = "#deb783";
    public string? TeamId { get; set; }
    public float X { get; set; } = 500;
    public float Y { get; set; } = 366;
    public int Facing { get; set; } = 0;
    public int Walking { get; set; } = 0;
    public bool IsConnected { get; set; } = true;

    public void SetPosition(float x, float y, int facing, int walking)
    {
        X = x;
        Y = y;
        Facing = facing;
        Walking = walking;
    }
}

/// <summary>
/// Tiến độ nhiệm vụ dùng chung cho cả đội (các cờ đối thoại, ngọn đèn, manh mối, câu đố, mảnh Tiếng Nói).
/// </summary>
public sealed class TeamProgress
{
    public Chapter Chapter { get; set; } = Chapter.Opening;
    public bool[] Spoken { get; } = new bool[4];
    public bool[] Lamps { get; } = new bool[4];
    public bool[] Clues { get; } = new bool[3];
    public bool[] Lore { get; } = new bool[4];
    public int[] Mirrors { get; } = [0, 0, 0, 0];
    public int[] DraftSequence { get; } = [2, 0, 5, 1, 4, 3];
    public int[] RiverSigns { get; } = [0, 0, 0, 0];
    public List<int> FinaleSequence { get; } = [];
    public int ReturnStep { get; set; }
    public bool DraftDone { get; set; }
    public bool RiverDone { get; set; }
    public bool NewsDone { get; set; }
    public int NewsNoise { get; set; }
    public int DraftFailures { get; set; }
    public int RiverFailures { get; set; }
    public int Version { get; set; } = 1;

    public int Shards => Chapter switch
    {
        Chapter.Opening or Chapter.Lights => 0,
        Chapter.Draft => 1,
        Chapter.River => 2,
        Chapter.News => 3,
        _ => 4
    };

    public void Reset()
    {
        Chapter = Chapter.Opening;
        Array.Clear(Spoken);
        Array.Clear(Lamps);
        Array.Clear(Clues);
        Array.Clear(Lore);
        Array.Clear(Mirrors);
        new int[] { 2, 0, 5, 1, 4, 3 }.CopyTo(DraftSequence, 0);
        Array.Clear(RiverSigns);
        FinaleSequence.Clear();
        ReturnStep = 0;
        DraftDone = false;
        RiverDone = false;
        NewsDone = false;
        NewsNoise = 0;
        DraftFailures = 0;
        RiverFailures = 0;
        Version = 1;
    }
}

/// <summary>
/// Trạng thái giao diện cục bộ của từng client (không chia sẻ qua mạng cho đồng đội).
/// </summary>
public sealed class LocalUiState
{
    public Panel Panel { get; set; } = Panel.None;
    public int DialogueIndex { get; set; }
    public List<Line> Dialogue { get; set; } = [];
    public Line? CurrentLine => DialogueIndex < Dialogue.Count ? Dialogue[DialogueIndex] : null;
    public string? Toast { get; set; }
    public bool Muted { get; set; }
    public int TextScale { get; set; } = 125;
    public bool UiDirty { get; set; } = true;
    public bool SaveDirty { get; set; }
}

/// <summary>
/// Giao diện phiên chơi thống nhất cho cả Single-player và Multiplayer.
/// </summary>
public interface IGameSession
{
    Chapter Chapter { get; }
    Panel Panel { get; }
    float X { get; }
    float Y { get; }
    int Facing { get; }
    int Walking { get; }
    int ElapsedSeconds { get; }
    int Shards { get; }
    string Objective { get; }
    string Area { get; }
    string? Hint { get; }
    string NextTarget { get; }
    string? Toast { get; }
    bool Muted { get; set; }
    int TextScale { get; set; }
    int DialogueIndex { get; }
    IReadOnlyList<Line> Dialogue { get; }
    Line? CurrentLine { get; }
    IReadOnlyList<string> DraftLabels { get; }
    IReadOnlyList<string> ClueLabels { get; }
    IReadOnlyList<string> FinalLabels { get; }
    bool[] Spoken { get; }
    bool[] Lamps { get; }
    bool[] Clues { get; }
    bool[] Lore { get; }
    int[] Mirrors { get; }
    int[] DraftSequence { get; }
    int[] RiverSigns { get; }
    List<int> FinaleSequence { get; }
    int ReturnStep { get; }
    bool DraftDone { get; }
    bool RiverDone { get; }
    bool NewsDone { get; }
    int NewsNoise { get; }
    bool UiDirty { get; }
    bool SaveDirty { get; }
    bool IsMultiplayer { get; }

    PlayerState Player { get; }
    TeamProgress Progress { get; }
    LocalUiState UiState { get; }

    void Begin();
    void StartFromSave();
    Frame Tick(double timestampMs, int keys);
    void PauseClock();
    void Interact();
    void ShowDialogue(IEnumerable<Line> lines, Action? after = null);
    void AdvanceDialogue();
    void OpenPanel(Panel panel);
    void ClosePanel();
    void TurnMirror(int index);
    void CheckMirrors();
    void MoveDraft(int index, int direction);
    void CheckDraft();
    void TurnRiver(int index);
    void CheckRiver();
    void ChooseNews(int option);
    void AddFinalePiece(int shardIndex);
    void ResetFinaleSequence();
    void AdvanceReturnStep(int stepIndex);
    void SetMuted(bool value);
    void SetTextScale(int value);
    void PresenterJump(Chapter chapter);
    SaveData CreateSave();
    void Load(string? json);
}
