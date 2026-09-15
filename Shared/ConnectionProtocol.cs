namespace TruyTimDanChu.Shared;

public sealed record HandshakeRequest(int ProtocolVersion, string ContentVersion);
public sealed record HandshakeResponse(bool Accepted, string? ErrorCode, int ProtocolVersion, string ContentVersion);

public static class ConnectionProtocol
{
    public const int Version = 1;
    public const string ContentVersion = "minh-dang-v1";
    public const string HubPath = "/hubs/room";
    public static HandshakeRequest Current => new(Version, ContentVersion);

    public static HandshakeResponse Validate(HandshakeRequest? request) => new(
        request?.ProtocolVersion == Version && request.ContentVersion == ContentVersion,
        request is null ? "INVALID_HANDSHAKE" : request.ProtocolVersion != Version ? "PROTOCOL_MISMATCH" :
        request.ContentVersion != ContentVersion ? "CONTENT_MISMATCH" : null,
        Version, ContentVersion);
}
