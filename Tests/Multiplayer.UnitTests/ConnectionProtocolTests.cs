using TruyTimDanChu.Shared;
using Xunit;

public class ConnectionProtocolTests
{
    [Fact]
    public void CurrentVersionIsAccepted()
    {
        var result = ConnectionProtocol.Validate(ConnectionProtocol.Current);
        Assert.True(result.Accepted);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(0, "minh-dang-v1", "PROTOCOL_MISMATCH")]
    [InlineData(1, "old-map", "CONTENT_MISMATCH")]
    [InlineData(1, null, "CONTENT_MISMATCH")]
    public void IncompatibleVersionsAreRejected(int version, string? content, string code)
    {
        var result = ConnectionProtocol.Validate(new(version, content!));
        Assert.False(result.Accepted);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(ConnectionProtocol.Version, result.ProtocolVersion);
    }

    [Fact]
    public void MissingHandshakeIsRejected() => Assert.Equal("INVALID_HANDSHAKE", ConnectionProtocol.Validate(null).ErrorCode);
}
