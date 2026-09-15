namespace TruyTimDanChu.Game;

public static class TownCollision
{
    public const float MinX = 16f;
    public const float MaxX = 1008f;
    public const float MinY = 16f;
    public const float MaxY = 624f;
    public const float Speed = 73f;
    public const float DiagonalFactor = 0.7071068f;

    public const float MaxInteractionDistance = 31f;

    public static readonly string[] Neighbors = ["Phương", "Dũng", "Bảo", "Nam"];
    public static readonly string[] NeighborIds = ["phuong", "dung", "bao", "nam"];

    public static readonly Dictionary<string, (float X, float Y, string Kind, string Label)> Places = new()
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

    public static readonly (float X, float Y, float W, float H)[] Bounds =
    [
        (306f, 34f, 158f, 48f),   // tường thư viện
        (474f, 36f, 168f, 50f),   // tường xưởng
        (757f, 250f, 132f, 43f),  // cụm nhà ven sông 1
        (757f, 367f, 132f, 54f),  // cụm nhà ven sông 2
        (430f, 450f, 152f, 97f),  // công viên trung tâm
        (80f, 459f, 62f, 49f),    // sạp phố tin 1
        (316f, 452f, 82f, 44f),   // sạp phố tin 2
        (923f, 194f, 56f, 303f)   // sông
    ];

    public static bool CanStand(float px, float py)
    {
        if (px < MinX || px > MaxX || py < MinY || py > MaxY)
            return false;

        foreach (var b in Bounds)
        {
            if (px + 5f > b.X && px - 5f < b.X + b.W && py + 7f > b.Y && py - 4f < b.Y + b.H)
                return false;
        }
        return true;
    }

    public static void SimulateStep(float currentX, float currentY, int keys, float dt,
        out float newX, out float newY, out int facing, out int walking)
    {
        float dx = ((keys & 8) != 0 ? 1 : 0) - ((keys & 4) != 0 ? 1 : 0);
        float dy = ((keys & 2) != 0 ? 1 : 0) - ((keys & 1) != 0 ? 1 : 0);

        if (dx == 0 && dy == 0)
        {
            newX = currentX;
            newY = currentY;
            facing = 0;
            walking = 0;
            return;
        }

        if (dx != 0 && dy != 0)
        {
            dx *= DiagonalFactor;
            dy *= DiagonalFactor;
        }

        if (Math.Abs(dx) > Math.Abs(dy))
            facing = dx > 0 ? 1 : 3;
        else
            facing = dy > 0 ? 2 : 0;

        var sx = dx * Speed * dt;
        var sy = dy * Speed * dt;
        var substeps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(sx), Math.Abs(sy)) / 4f));

        var x = currentX;
        var y = currentY;

        for (var i = 0; i < substeps; i++)
        {
            var nextX = Math.Clamp(x + sx / substeps, MinX, MaxX);
            if (CanStand(nextX, y)) x = nextX;

            var nextY = Math.Clamp(y + sy / substeps, MinY, MaxY);
            if (CanStand(x, nextY)) y = nextY;
        }

        newX = x;
        newY = y;
        walking = 1;
    }
}

