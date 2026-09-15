using System.Security.Cryptography;
using System.Text;
using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

public sealed class PlayerSession
{
    public string PlayerId { get; }
    public string RoomId { get; }
    public string DisplayName { get; private set; }
    public string NormalizedName { get; }
    public string ConnectionId { get; set; }
    public string ReconnectTokenHash { get; }
    public string? TeamId { get; set; }
    public string AvatarId { get; set; } = AvatarCatalog.DefaultAvatarId;
    public string AccentColor { get; set; } = "#deb783";
    public bool IsReady { get; set; }
    public bool IsConnected { get; set; } = true;
    public DateTimeOffset JoinedAt { get; }
    public DateTimeOffset LastSeenAt { get; set; }

    public PlayerSession(
        string playerId,
        string roomId,
        string displayName,
        string connectionId,
        string reconnectToken)
    {
        PlayerId = playerId;
        RoomId = roomId;
        DisplayName = displayName;
        NormalizedName = displayName.Trim().ToUpperInvariant();
        ConnectionId = connectionId;
        ReconnectTokenHash = HashToken(reconnectToken);
        JoinedAt = DateTimeOffset.UtcNow;
        LastSeenAt = JoinedAt;
    }

    public bool VerifyReconnectToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var hash = HashToken(token);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(ReconnectTokenHash),
            Encoding.UTF8.GetBytes(hash));
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public PlayerSnapshot GetSnapshot() => new(
        PlayerId,
        DisplayName,
        TeamId,
        AvatarId,
        AccentColor,
        IsReady,
        IsConnected
    );
}

