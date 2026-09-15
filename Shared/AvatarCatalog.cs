namespace TruyTimDanChu.Shared;

public sealed record AvatarInfo(
    string Id,
    string DisplayName,
    string AssetPath
);

public static class AvatarCatalog
{
    public static readonly IReadOnlyList<AvatarInfo> All =
    [
        new("quang", "Quang", "assets/pipoya/quang.png"),
        new("trong", "Trọng", "assets/pipoya/trong.png"),
        new("kieu_anh", "Kiều Anh", "assets/pipoya/kieu_anh.png"),
        new("ninh", "Ninh", "assets/pipoya/ninh.png"),
        new("phuong", "Phương", "assets/pipoya/phuong.png"),
        new("dung", "Dũng", "assets/pipoya/dung.png"),
        new("bao", "Bảo", "assets/pipoya/bao.png"),
        new("han", "Hân", "assets/pipoya/han.png"),
        new("nam", "Nam", "assets/pipoya/nam.png"),
    ];

    private static readonly HashSet<string> ValidIds = new(All.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? avatarId) =>
        !string.IsNullOrEmpty(avatarId) && ValidIds.Contains(avatarId);

    public static AvatarInfo? Get(string? avatarId) =>
        All.FirstOrDefault(a => string.Equals(a.Id, avatarId, StringComparison.OrdinalIgnoreCase));

    public static string DefaultAvatarId => "quang";

    /// <summary>
    /// Tạo ký hiệu phân biệt ổn định (ví dụ: #1, #2) cho người chơi khi trùng sprite trong cùng phòng
    /// </summary>
    public static string GetDiscriminator(string playerId, IEnumerable<PlayerSnapshot> roomPlayers)
    {
        var player = roomPlayers.FirstOrDefault(p => p.PlayerId == playerId);
        if (player is null) return "";

        var sameAvatarPlayers = roomPlayers
            .Where(p => string.Equals(p.AvatarId, player.AvatarId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PlayerId, StringComparer.Ordinal)
            .ToList();

        if (sameAvatarPlayers.Count <= 1) return "";

        var index = sameAvatarPlayers.FindIndex(p => p.PlayerId == playerId);
        return $"#{index + 1}";
    }
}

