using System.Text.Json;

namespace TruyTimDanChu.Game;

public enum Chapter { Opening, Lights, Draft, River, News, Finale, Complete }
public enum Panel { None, Dialogue, Mirrors, Draft, River, News, Finale, Pause, Sources, Settings, Presenter }

public sealed record Line(string Speaker, string Text);
public sealed record Actor(string Id, string Name, float X, float Y, string Color, string Kind);
public sealed record WorldObject(string Id, float X, float Y, string Kind, string Label, bool Done = false);
public sealed record Frame(float X, float Y, float CameraX, float CameraY, int Facing, int Walking,
    Actor[] Actors, WorldObject[] Objects, string? TargetId, float TargetX, float TargetY, string? TargetLabel,
    string? Save, bool UiDirty);
public sealed record SaveData(int Version, Chapter Chapter, bool[] Spoken, bool[] Lamps, bool[] Clues, bool[] Lore, bool DraftDone, bool RiverDone, bool NewsDone, bool Muted, int TextScale);

public sealed class GameEngine
{
    private static readonly string[] Neighbors = ["Phương", "Dũng", "Bảo", "Nam"];
    private static readonly string[] DraftSteps =
    [
        "Công khai dự thảo", "Người dân góp ý", "Tiếp nhận",
        "Tổng hợp", "Giải trình và chỉnh lý", "Thảo luận và quyết định"
    ];
    private static readonly string[] ClueNames = ["Biên lai", "Bảng quy trình", "Lời kể của Bảo"];
    private static readonly int[] RiverTargets = [1, 2, 3, 0];
    private static readonly string[] FinalOrder =
    [
        "Chủ thể quyền lực", "Cơ chế tham gia",
        "Giám sát và phản ánh", "Phản hồi và kết quả"
    ];

    private readonly Dictionary<string, (float X, float Y, string Kind, string Label)> _places = new()
    {
        ["trong"] = (526, 300, "npc", "Trọng"),
        ["kieu_anh"] = (464, 325, "npc", "Kiều Anh"),
        ["ninh"] = (359, 119, "npc", "Ninh"),
        ["phuong"] = (170, 280, "npc", "Phương"),
        ["dung"] = (205, 338, "npc", "Dũng"),
        ["bao"] = (160, 362, "npc", "Bảo"),
        ["nam"] = (242, 308, "npc", "Nam"),
        ["han"] = (553, 125, "npc", "Hán"),
        ["lamp0"] = (125, 260, "altar", "Bệ đèn của Phương"),
        ["lamp1"] = (267, 260, "altar", "Bệ đèn của Dũng"),
        ["lamp2"] = (125, 388, "altar", "Bệ đèn của Bảo"),
        ["lamp3"] = (267, 388, "altar", "Bệ đèn của Nam"),
        ["mirror_board"] = (195, 323, "board", "Gương trung tâm"),
        ["draft_board"] = (486, 115, "board", "Cỗ máy góp ý"),
        ["river_board"] = (823, 325, "board", "Bản phương án cây cầu"),
        ["clue0"] = (180, 520, "clue", "Biên lai"),
        ["clue1"] = (339, 509, "clue", "Bảng quy trình"),
        ["clue2"] = (286, 552, "clue", "Lời kể"),
        ["news_board"] = (246, 516, "board", "Bảng tin"),
        ["final_board"] = (513, 328, "board", "Hòm Dân chủ"),
        ["lore0"] = (344, 80, "book", "Nguồn gốc"),
        ["lore1"] = (380, 80, "book", "Các mô hình"),
        ["lore2"] = (415, 80, "book", "Hai cách tham gia"),
        ["lore3"] = (450, 80, "book", "Cơ sở pháp lý")
    };

    public Chapter Chapter { get; private set; } = Chapter.Opening;
    public Panel Panel { get; private set; } = Panel.None;
    public float X { get; private set; } = 500;
    public float Y { get; private set; } = 366;
    public int Facing { get; private set; } = 0;
    public int Walking { get; private set; }
    public int ElapsedSeconds { get; private set; }
    public bool[] Spoken { get; } = new bool[4];
    public bool[] Lamps { get; } = new bool[4];
    public bool[] Clues { get; } = new bool[3];
    public bool[] Lore { get; } = new bool[4];
    public int[] Mirrors { get; } = [0, 0, 0, 0];
    public int[] DraftSequence { get; } = [2, 0, 5, 1, 4, 3];
    public int[] RiverSigns { get; } = [0, 0, 0, 0];
    public List<int> FinaleSequence { get; } = [];
    public int ReturnStep { get; private set; }
    public bool DraftDone { get; private set; }
    public bool RiverDone { get; private set; }
    public bool NewsDone { get; private set; }
    public bool Muted { get; set; }
    public int TextScale { get; set; } = 125;
    public void SetMuted(bool value) { Muted = value; SaveDirty = true; UiDirty = true; }
    public void SetTextScale(int value) { TextScale = Math.Clamp(value, 100, 150); SaveDirty = true; UiDirty = true; }
    public int NewsNoise { get; private set; }
    public int DraftFailures { get; private set; }
    public int RiverFailures { get; private set; }
    public string? Toast { get; private set; }
    public string Objective => Chapter switch
    {
        Chapter.Opening => "Gặp chú Trọng ở Hòm Dân chủ",
        Chapter.Lights => !Spoken.All(x => x) ? "Trò chuyện với Phương, Dũng, Bảo và Nam" :
            !Lamps.All(x => x) ? "Đặt bốn ngọn đèn lên các bệ" : "Chỉnh gương tại tâm quảng trường",
        Chapter.Draft => "Tìm Phương ở Xưởng Dự thảo và sắp xếp đường đi của ý kiến",
        Chapter.River => "Gặp Dũng ở khu ven sông và dẫn dòng ý kiến qua bốn trạm",
        Chapter.News => !Clues.All(x => x) ? "Thu thập ba manh mối ở Phố Tin tức" :
            "Trở lại bảng tin để quyết định cách phản ánh",
        Chapter.Finale => "Quay về Hòm Dân chủ và khép vòng Tiếng Nói",
        _ => "Bạn đã tìm thấy điều cần tìm"
    };
    public string Area => X switch
    {
        < 310 when Y > 445 => "Phố Tin tức",
        < 310 => "Quảng trường Ánh sáng",
        > 680 => "Khu dân cư ven sông",
        _ when Y < 215 => "Thư viện · Xưởng Dự thảo",
        _ => "Quảng trường trung tâm"
    };
    public string? Hint => Chapter switch
    {
        Chapter.Lights => !Spoken.All(x => x) ? "Hãy tìm đủ bốn người ở quảng trường phía tây." :
            !Lamps.All(x => x) ? "Mỗi ngọn đèn có một bệ riêng. Lại gần bệ và nhấn E." :
            "Các gương cần hướng ánh sáng về tâm. Mỗi gương có bốn hướng.",
        Chapter.Draft => "Các bước bắt đầu từ công khai dự thảo và kết thúc bằng quyết định.",
        Chapter.River => "Thử bật dòng sáng. Nó sẽ dừng ở trạm đầu tiên bị bỏ qua.",
        Chapter.News => !Clues.All(x => x) ? "Tìm biên lai, bảng quy trình và hỏi Bảo." :
            "Phản ánh cần chứng cứ và sự xác minh, không chỉ một bài đăng.",
        Chapter.Finale => "Xếp bốn mảnh theo hành trình của tiếng nói, rồi đưa câu trả lời trở về.",
        _ => null
    };
    public string NextTarget => FindTarget(ActiveObjects().ToArray())?.Label ?? "Khám phá thị trấn";
    public int Shards => Chapter switch
    {
        Chapter.Opening or Chapter.Lights => 0,
        Chapter.Draft => 1,
        Chapter.River => 2,
        Chapter.News => 3,
        _ => 4
    };
    public int DialogueIndex { get; private set; }
    public IReadOnlyList<Line> Dialogue => _dialogue;
    public Line? CurrentLine => DialogueIndex < _dialogue.Count ? _dialogue[DialogueIndex] : null;
    public IReadOnlyList<string> DraftLabels => DraftSequence.Select(i => DraftSteps[i]).ToArray();
    public IReadOnlyList<string> ClueLabels => ClueNames;
    public IReadOnlyList<string> FinalLabels => FinalOrder;
    public bool UiDirty { get; private set; } = true;
    public bool SaveDirty { get; private set; }

    private List<Line> _dialogue = [];
    private double _lastTime;
    private double _elapsedAccumulator;
    private Action? _afterDialogue;
    private float _lastX = 500;
    private float _lastY = 366;

    public void Begin()
    {
        X = 500; Y = 366;
        ShowDialogue(
        [
            new("Trọng", "Hòm Dân chủ trống không. Nhưng có lẽ từ đầu chúng ta đã tìm sai chỗ."),
            new("Kiều Anh", "Quang, đừng tìm một món đồ. Hãy theo đường đi của tiếng nói."),
            new("Quang", "Vậy mình sẽ hỏi những người đang sống ở thị trấn này.")
        ], () => SetChapter(Chapter.Lights));
    }

    public void StartFromSave()
    {
        Panel = Panel.None;
        UiDirty = true;
    }

    public Frame Tick(double timestampMs, int keys)
    {
        if (_lastTime == 0) _lastTime = timestampMs;
        var delta = Math.Clamp(timestampMs - _lastTime, 0, 50);
        _lastTime = timestampMs;
        if (Panel == Panel.None && Chapter != Chapter.Opening && Chapter != Chapter.Complete)
        {
            _elapsedAccumulator += delta;
            if (delta > 0) Move(keys, (float)(delta / 1000));
            if (_elapsedAccumulator >= 1000)
            {
                ElapsedSeconds += (int)(_elapsedAccumulator / 1000);
                _elapsedAccumulator %= 1000;
            }
        }
        else
        {
            Walking = 0;
        }
        var save = SaveDirty ? JsonSerializer.Serialize(CreateSave()) : null;
        SaveDirty = false;
        var dirty = UiDirty;
        UiDirty = false;
        var objects = ActiveObjects().ToArray();
        var target = FindTarget(objects);
        return new Frame(X, Y,
            Math.Clamp(X - 240, 0, 544), Math.Clamp(Y - 135, 0, 370),
            Facing, Walking, ActiveActors().ToArray(), objects,
            target?.Id, target?.X ?? 0, target?.Y ?? 0, target?.Label,
            save, dirty);
    }

    private WorldObject? FindTarget(WorldObject[] objects)
    {
        string? targetId = Chapter switch
        {
            Chapter.Lights when !Spoken.All(x => x) => new[] { "phuong", "dung", "bao", "nam" }[Array.FindIndex(Spoken, x => !x)],
            Chapter.Lights when !Lamps.All(x => x) => "lamp" + Array.FindIndex(Lamps, x => !x),
            Chapter.Lights => "mirror_board",
            Chapter.Draft => "draft_board",
            Chapter.River => "river_board",
            Chapter.News when !Clues[0] => "clue0",
            Chapter.News when !Clues[1] => "clue1",
            Chapter.News when !Clues[2] => "bao",
            Chapter.News => "news_board",
            Chapter.Finale => "final_board",
            _ => null
        };
        return targetId is null ? null : objects.FirstOrDefault(o => o.Id == targetId);
    }

    public void PauseClock() { _lastTime = 0; Walking = 0; }

    private void Move(int keys, float dt)
    {
        float dx = ((keys & 8) != 0 ? 1 : 0) - ((keys & 4) != 0 ? 1 : 0);
        float dy = ((keys & 2) != 0 ? 1 : 0) - ((keys & 1) != 0 ? 1 : 0);
        if (dx == 0 && dy == 0) { Walking = 0; return; }
        if (dx != 0 && dy != 0) { dx *= 0.7071068f; dy *= 0.7071068f; }
        if (Math.Abs(dx) > Math.Abs(dy)) Facing = dx > 0 ? 1 : 3;
        else Facing = dy > 0 ? 2 : 0;
        var sx = dx * 73 * dt;
        var sy = dy * 73 * dt;
        var substeps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(sx), Math.Abs(sy)) / 4));
        for (var i = 0; i < substeps; i++)
        {
            var nextX = Math.Clamp(X + sx / substeps, 16, 1008);
            if (CanStand(nextX, Y)) X = nextX;
            var nextY = Math.Clamp(Y + sy / substeps, 16, 624);
            if (CanStand(X, nextY)) Y = nextY;
        }
        Walking = 1;
        if (Math.Abs(X - _lastX) + Math.Abs(Y - _lastY) > 15)
        { _lastX = X; _lastY = Y; }
    }

    private bool CanStand(float px, float py)
    {
        // Những khối nhà và mặt nước là AABB cố định; hành lang luôn rộng ít nhất 32 px.
        var bounds = new (float X, float Y, float W, float H)[]
        {
            (306, 34, 158, 48), (474, 36, 168, 50), // tường thư viện và xưởng
            (757, 250, 132, 43), (757, 367, 132, 54), // hai cụm nhà ven sông
            (430, 450, 152, 97), // công viên trung tâm
            (80, 459, 62, 49), (316, 452, 82, 44), // sạp phố tin
            (923, 194, 56, 303) // sông
        };
        foreach (var b in bounds)
            if (px + 5 > b.X && px - 5 < b.X + b.W && py + 7 > b.Y && py - 4 < b.Y + b.H)
                return false;
        return true;
    }

    public void Interact()
    {
        if (Panel != Panel.None) return;
        var target = ActiveObjects()
            .Select(o => (o.Id, o.X, o.Y, o.Kind, o.Label, Distance: MathF.Sqrt((X-o.X)*(X-o.X)+(Y-o.Y)*(Y-o.Y))))
            .Where(o => o.Distance <= 31)
            .OrderBy(o => o.Distance)
            .FirstOrDefault();
        if (target.Id is null)
        {
            Toast = "Hãy lại gần người hoặc vật có dấu !";
            UiDirty = true;
            return;
        }
        Handle(target.Id);
    }

    private void Handle(string id)
    {
        if (id == "trong")
        {
            if (Chapter == Chapter.Finale) OpenPanel(Panel.Finale);
            else if (Chapter == Chapter.Complete) ShowDialogue([new("Trọng", "Hòm vẫn trống, nhưng thị trấn thì không. Mỗi tiếng nói đều có đường đi.")]);
            else ShowDialogue([new("Trọng", "Cứ theo Sổ Tiếng Nói. Khi đủ bốn mảnh, hãy quay lại đây.")]);
            return;
        }
        if (id == "kieu_anh")
        {
            ShowDialogue([
                new("Kiều Anh", Chapter == Chapter.Lights ? "Bốn người ở quảng trường phía tây đều có một ngọn đèn. Hãy lắng nghe họ." : "Nhìn vào cuốn sổ: quyền, tham gia, giám sát và phản hồi phải nối thành một vòng.")
            ]);
            return;
        }
        if (id == "ninh") { ShowDialogue([new("Ninh", "Lịch sử có nhiều mô hình dân chủ, mỗi mô hình đều có giới hạn. Hãy đọc các bảng trong thư viện để hiểu thêm.")]); return; }
        if (id.StartsWith("lore") && int.TryParse(id[^1..], out var loreId))
        {
            Lore[loreId] = true;
            SaveDirty = true;
            ShowDialogue(loreId switch
            {
                0 => [new("Ninh", "Demos là nhân dân, kratos là quyền lực. Ở Athens cổ đại, nhiều người như phụ nữ, nô lệ và ngoại kiều vẫn bị loại trừ.")],
                1 => [new("Ninh", "Không nên đồng nhất dân chủ với một mô hình duy nhất. Mỗi mô hình có đóng góp, giới hạn và điều kiện lịch sử riêng.")],
                2 => [new("Ninh", "Người dân thực hiện quyền lực qua dân chủ trực tiếp và qua người đại diện. Được lắng nghe khác với có quyền quyết định.")],
                _ => [new("Ninh", "Điều 2 xác định chủ thể quyền lực; Điều 6 nói về cách thực hiện; Điều 28 ghi nhận quyền tham gia. Muốn thực chất, cần cả giám sát và trách nhiệm giải trình.")]
            });
            return;
        }
        var neighborIndex = Array.FindIndex(Neighbors, n => n.Equals(_places[id].Label, StringComparison.Ordinal));
        if (Chapter == Chapter.Lights && neighborIndex >= 0 && !Spoken[neighborIndex])
        {
            Spoken[neighborIndex] = true;
            SaveDirty = true;
            UiDirty = true;
            ShowDialogue([
                new(Neighbors[neighborIndex], neighborIndex switch
                {
                    0 => "Mình từng gửi ý kiến, nhưng không rõ ý kiến sẽ đi tới đâu.",
                    1 => "Một phương án mới có thể ảnh hưởng tới nhà mình. Mình muốn được biết và được nói.",
                    2 => "Mình thấy một điều bất thường, nhưng phải kiểm tra trước khi kể với mọi người.",
                    _ => "Mình chuyển lời giúp mọi người. Một lá thư gửi đi cần có thư trả lời."
                }),
                new("Quang", "Tiếng nói của bạn không hề nhỏ. Hãy cho mình mượn ngọn đèn nhé.")
            ]);
            return;
        }
        if (id.StartsWith("lamp") && Chapter == Chapter.Lights && int.TryParse(id[^1..], out var lampId))
        {
            if (!Spoken[lampId]) { Toast = "Hãy trò chuyện với " + Neighbors[lampId] + " trước."; UiDirty = true; return; }
            if (!Lamps[lampId]) { Lamps[lampId] = true; SaveDirty = true; UiDirty = true; Toast = "Đã đặt ngọn đèn của " + Neighbors[lampId] + "."; }
            else { Toast = "Ngọn đèn đã ở đúng bệ."; UiDirty = true; }
            return;
        }
        if (id == "mirror_board" && Chapter == Chapter.Lights)
        {
            if (!Lamps.All(x => x)) { Toast = "Cần đủ bốn ngọn đèn trước khi chỉnh gương."; UiDirty = true; }
            else OpenPanel(Panel.Mirrors);
            return;
        }
        if (id == "phuong" && Chapter == Chapter.Draft)
        {
            ShowDialogue([new("Phương", "Gửi được ý kiến mới chỉ là bước đầu. Bạn giúp mình sắp xếp đường đi của nó nhé?")]);
            return;
        }
        if (id == "han")
        {
            ShowDialogue([new("Hán", Chapter == Chapter.News ? "Thông tin cần được tiếp nhận, xác minh, xử lý theo quy định rồi phản hồi." :
                "Tiếp nhận không phải bước cuối. Còn tổng hợp, giải trình và đưa kết quả trở lại.")]);
            return;
        }
        if (id == "draft_board" && Chapter == Chapter.Draft) { OpenPanel(Panel.Draft); return; }
        if (id == "dung" && Chapter == Chapter.River)
        {
            ShowDialogue([new("Dũng", "Tôi không đòi mọi người phải đồng ý. Tôi muốn phương án được công khai, ý kiến được ghi nhận và có đối thoại.")]);
            return;
        }
        if (id == "nam" && Chapter == Chapter.River)
        {
            ShowDialogue([new("Nam", "Mình có thể mang ý kiến đi, nhưng đường đi phải qua đủ bốn trạm. Hãy thử chỉnh các biển chỉ hướng.")]);
            return;
        }
        if (id == "river_board" && Chapter == Chapter.River) { OpenPanel(Panel.River); return; }
        if (id == "bao" && Chapter == Chapter.News)
        {
            Clues[2] = true; SaveDirty = true; UiDirty = true;
            ShowDialogue([
                new("Bảo", "Mình thấy một người nộp tiền phạt qua tài khoản cá nhân. Đây mới là lời kể của mình, cần đối chiếu thêm."),
                new("Quang", "Mình sẽ tìm biên lai và bảng quy trình trước khi phản ánh.")
            ]);
            return;
        }
        if (id.StartsWith("clue") && Chapter == Chapter.News && int.TryParse(id[^1..], out var clueId))
        {
            Clues[clueId] = true; SaveDirty = true; UiDirty = true;
            ShowDialogue([new("Quang", "Đã ghi vào sổ: " + ClueNames[clueId] + ".")]);
            return;
        }
        if (id == "news_board" && Chapter == Chapter.News) { OpenPanel(Panel.News); return; }
        if (id == "final_board" && Chapter == Chapter.Finale) { OpenPanel(Panel.Finale); return; }
        Toast = "Chưa phải lúc này. Xem mục tiêu trong Sổ Tiếng Nói.";
        UiDirty = true;
    }

    private IEnumerable<Actor> ActiveActors()
    {
        var names = new[] { ("trong", "Trọng", "#deb783"), ("kieu_anh", "Kiều Anh", "#9fd1c4"),
            ("ninh", "Ninh", "#9eb2db"), ("phuong", "Phương", "#e6a7bb"), ("dung", "Dũng", "#d0a873"),
            ("bao", "Bảo", "#e9b569"), ("nam", "Nam", "#94cfaa"), ("han", "Hán", "#bdd0e7") };
        foreach (var (id, name, color) in names)
        {
            var p = _places[id];
            var x = p.X; var y = p.Y;
            if (Chapter > Chapter.Lights)
            {
                if (id == "phuong") { x = 505; y = 154; }
                if (id == "dung") { x = 758; y = 318; }
                if (id == "bao") { x = 259; y = 537; }
                if (id == "nam") { x = Chapter == Chapter.River ? 875 : 541; y = Chapter == Chapter.River ? 333 : 340; }
            }
            if (id == "han" && Chapter >= Chapter.News) { x = 390; y = 543; }
            yield return new Actor(id, name, x, y, color, id);
        }
    }

    private IEnumerable<WorldObject> ActiveObjects()
    {
        foreach (var a in ActiveActors())
        {
            if (Chapter == Chapter.Lights && (a.Id is "phuong" or "dung" or "bao" or "nam"))
                yield return new WorldObject(a.Id, a.X, a.Y, "npc", a.Name, Spoken[Array.IndexOf(Neighbors, a.Name)]);
            else yield return new WorldObject(a.Id, a.X, a.Y, "npc", a.Name);
        }
        foreach (var (id, p) in _places)
        {
            if (p.Kind == "npc") continue;
            if (id.StartsWith("lamp") && Chapter == Chapter.Lights)
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, Lamps[int.Parse(id[^1..])]);
            else if (id == "mirror_board" && Chapter == Chapter.Lights)
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label);
            else if (id == "draft_board" && Chapter == Chapter.Draft)
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, DraftDone);
            else if (id == "river_board" && Chapter == Chapter.River)
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, RiverDone);
            else if (id.StartsWith("clue") && Chapter == Chapter.News && !Clues[int.Parse(id[^1..])])
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label);
            else if (id == "news_board" && Chapter == Chapter.News)
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, NewsDone);
            else if (id == "final_board")
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, Chapter == Chapter.Complete);
            else if (id.StartsWith("lore"))
                yield return new WorldObject(id, p.X, p.Y, p.Kind, p.Label, Lore[int.Parse(id[^1..])]);
        }
    }

    public void ShowDialogue(IEnumerable<Line> lines, Action? after = null)
    {
        _dialogue = lines.ToList();
        DialogueIndex = 0;
        _afterDialogue = after;
        Panel = Panel.Dialogue;
        UiDirty = true;
    }

    public void AdvanceDialogue()
    {
        if (Panel != Panel.Dialogue) return;
        DialogueIndex++;
        if (DialogueIndex >= _dialogue.Count)
        {
            Panel = Panel.None;
            _dialogue = [];
            var callback = _afterDialogue;
            _afterDialogue = null;
            callback?.Invoke();
        }
        UiDirty = true;
    }

    public void OpenPanel(Panel panel) { Panel = panel; Toast = null; UiDirty = true; }
    public void ClosePanel() { if (Panel != Panel.Dialogue) { Panel = Panel.None; Toast = null; UiDirty = true; } }
    public void ClearToast() { Toast = null; UiDirty = true; }

    private void SetChapter(Chapter chapter)
    {
        Chapter = chapter;
        SaveDirty = true;
        UiDirty = true;
        Toast = chapter switch
        {
            Chapter.Draft => "Mảnh 1/4: Chủ thể quyền lực",
            Chapter.River => "Mảnh 2/4: Cơ chế tham gia",
            Chapter.News => "Mảnh 3/4: Phản hồi và kết quả",
            Chapter.Finale => "Mảnh 4/4: Giám sát và phản ánh",
            _ => null
        };
    }

    public void TurnMirror(int index)
    {
        Mirrors[index] = (Mirrors[index] + 1) % 4;
        UiDirty = true;
    }
    public void CheckMirrors()
    {
        if (Mirrors.SequenceEqual(new[] { 1, 2, 3, 0 }))
        {
            Panel = Panel.None;
            ShowDialogue([
                new("Quang", "Không có một ngọn đèn nào tự thắp sáng cả quảng trường."),
                new("Kiều Anh", "Quyền lực thuộc về Nhân dân. Hãy mang mảnh đầu tiên tới Xưởng Dự thảo.")
            ], () => SetChapter(Chapter.Draft));
        }
        else { Toast = "Một vài tia sáng chưa chạm vào tâm."; UiDirty = true; }
    }
    public void MoveDraft(int index, int direction)
    {
        var other = index + direction;
        if (other < 0 || other >= DraftSequence.Length) return;
        (DraftSequence[index], DraftSequence[other]) = (DraftSequence[other], DraftSequence[index]);
        UiDirty = true;
    }
    public void CheckDraft()
    {
        if (DraftSequence.SequenceEqual(Enumerable.Range(0, 6)))
        {
            DraftDone = true;
            Panel = Panel.None;
            ShowDialogue([
                new("Phương", "Gửi được ý kiến mới chỉ là bước đầu. Giờ mình thấy ý kiến được tiếp nhận, giải trình và chỉnh lý như thế nào."),
                new("Nam", "Mình sẽ mang phần phản hồi trở lại với người đã góp ý.")
            ], () => SetChapter(Chapter.River));
        }
        else { DraftFailures++; Toast = DraftFailures >= 2 ? "Bước đầu là công khai dự thảo; bước cuối là quyết định." : "Có bước đang đi trước khi nó được chuẩn bị."; UiDirty = true; }
    }
    public void TurnRiver(int index) { RiverSigns[index] = (RiverSigns[index] + 1) % 4; UiDirty = true; }
    public void CheckRiver()
    {
        if (RiverSigns.SequenceEqual(RiverTargets))
        {
            RiverDone = true;
            Panel = Panel.None;
            ShowDialogue([
                new("Dũng", "Không phải mọi đề nghị đều được chấp nhận. Nhưng chúng tôi đã được biết, được nói và được giải thích."),
                new("Nam", "Phương án cầu đã được điều chỉnh để giảm ảnh hưởng cho khu dân cư.")
            ], () => SetChapter(Chapter.News));
        }
        else
        {
            RiverFailures++;
            var firstWrong = Enumerable.Range(0, RiverSigns.Length).First(i => RiverSigns[i] != RiverTargets[i]);
            Toast = "Dòng ý kiến dừng ở trạm " + (firstWrong + 1) + ". Hãy chỉnh lại biển.";
            UiDirty = true;
        }
    }
    public void ChooseNews(int choice)
    {
        if (choice == 0)
        {
            NewsNoise = Math.Min(3, NewsNoise + 1);
            Toast = "Tin lan nhanh, nhưng chưa được xác minh. Có thể xem lại quyết định.";
            UiDirty = true;
        }
        else if (choice == 1)
        {
            Toast = "Im lặng khiến vấn đề không được xem xét. Có thể xem lại quyết định.";
            UiDirty = true;
        }
        else if (!Clues.All(x => x))
        {
            Toast = "Cần đủ ba manh mối để gửi phản ánh.";
            UiDirty = true;
        }
        else
        {
            NewsNoise = 0;
            NewsDone = true;
            Panel = Panel.None;
            ShowDialogue([
                new("Hán", "Tôi tiếp nhận thông tin, xác minh rồi xử lý theo quy định. Kết quả sẽ được phản hồi."),
                new("Bảo", "Mạng xã hội giúp phát hiện vấn đề, nhưng không thay thế chứng cứ và quá trình xác minh."),
                new("Quang", "Mảnh cuối đã hiện ra. Mình phải quay về chỗ chú Trọng.")
            ], () => SetChapter(Chapter.Finale));
        }
    }
    public void AddFinalePiece(int piece)
    {
        if (FinaleSequence.Contains(piece)) return;
        FinaleSequence.Add(piece);
        UiDirty = true;
        if (FinaleSequence.Count == 4)
        {
            if (FinaleSequence.SequenceEqual(Enumerable.Range(0, 4)))
            {
                Panel = Panel.None;
                ShowDialogue([
                    new("Trọng", "Bốn mảnh đều sáng, nhưng Hòm Dân chủ vẫn chưa mở..."),
                    new("Nam", "Vì câu trả lời chưa quay lại với người dân. Mình đang mang lá thư này."),
                    new("Quang", "Vậy hãy khép nốt vòng Tiếng Nói.")
                ], () => OpenPanel(Panel.Finale));
                FinaleSequence.Add(4); // bước trả thư
            }
            else
            {
                FinaleSequence.Clear();
                Toast = "Hãy xếp theo hành trình của một tiếng nói, từ người dân tới phản hồi.";
            }
        }
    }
    public void CompleteFinale()
    {
        Panel = Panel.None;
        SetChapter(Chapter.Complete);
        ShowDialogue([
            new("Nam", "Tiếp nhận. Giải trình. Điều chỉnh hoặc xử lý. Và cuối cùng: trả kết quả."),
            new("Trọng", "Thứ chúng ta tìm không nằm trong chiếc hòm. Nó nằm trong cách tiếng nói đi tới người có trách nhiệm và câu trả lời quay về với người dân."),
            new("Kiều Anh", "Quyền làm chủ cần được bảo đảm bằng pháp luật, dân chủ trực tiếp và đại diện, khả năng giám sát cùng trách nhiệm giải trình."),
            new("Quang", "Dân chủ thực chất không nằm ở việc một quốc gia giống mô hình nào, mà nằm ở việc tiếng nói của người dân có được lắng nghe, phản hồi và chuyển hóa thành hành động hay không.")
        ], () => OpenPanel(Panel.Sources));
    }

    public void AdvanceReturnStep(int step)
    {
        if (Chapter != Chapter.Finale || Panel != Panel.Finale || !FinaleSequence.Contains(4)) return;
        if (step != ReturnStep) { Toast = "Hãy đi theo thứ tự của lá thư."; UiDirty = true; return; }
        ReturnStep++;
        Toast = step switch
        {
            0 => "Hán đã tiếp nhận lá thư.",
            1 => "Phương nhận được lời giải trình.",
            2 => "Dũng thấy phương án đã được điều chỉnh.",
            _ => "Kết quả đã trở lại với người dân."
        };
        UiDirty = true;
        if (ReturnStep == 4) CompleteFinale();
    }

    public void PresenterJump(Chapter chapter)
    {
        Chapter = chapter;
        Panel = Panel.None;
        FinaleSequence.Clear();
        ReturnStep = 0;
        Array.Fill(Spoken, chapter > Chapter.Lights);
        Array.Fill(Lamps, chapter > Chapter.Lights);
        Array.Fill(Clues, chapter > Chapter.News);
        DraftDone = chapter > Chapter.Draft;
        RiverDone = chapter > Chapter.River;
        NewsDone = chapter > Chapter.News;
        X = chapter switch { Chapter.Lights => 195, Chapter.Draft => 500, Chapter.River => 815, Chapter.News => 250, _ => 500 };
        Y = chapter switch { Chapter.Lights => 320, Chapter.Draft => 166, Chapter.River => 334, Chapter.News => 535, _ => 365 };
        Toast = "Đã chuyển tới " + (chapter switch { Chapter.Lights => "Quảng trường Ánh sáng", Chapter.Draft => "Xưởng Dự thảo", Chapter.River => "Khu ven sông", Chapter.News => "Phố Tin tức", _ => "Hòm Dân chủ" });
        UiDirty = true;
        PauseClock();
    }

    public SaveData CreateSave() => new(1, Chapter, [.. Spoken], [.. Lamps], [.. Clues], [.. Lore], DraftDone, RiverDone, NewsDone, Muted, TextScale);
    public void Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var save = JsonSerializer.Deserialize<SaveData>(json);
            if (save is null || save.Version != 1 || save.Spoken.Length != 4 || save.Lamps.Length != 4 ||
                save.Clues.Length != 3 || save.Lore.Length != 4) return;
            Chapter = save.Chapter;
            Array.Copy(save.Spoken, Spoken, 4);
            Array.Copy(save.Lamps, Lamps, 4);
            Array.Copy(save.Clues, Clues, 3);
            Array.Copy(save.Lore, Lore, 4);
            DraftDone = save.DraftDone; RiverDone = save.RiverDone; NewsDone = save.NewsDone;
            Muted = save.Muted; TextScale = save.TextScale;
            (X, Y) = Chapter switch
            {
                Chapter.Lights => (195, 320),
                Chapter.Draft => (500, 166),
                Chapter.River => (815, 334),
                Chapter.News => (250, 535),
                _ => (500, 365)
            };
            UiDirty = true;
        }
        catch (JsonException) { Toast = "Bản lưu không đọc được; hãy bắt đầu lượt mới."; }
    }
}
