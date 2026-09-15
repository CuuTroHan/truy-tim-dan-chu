using TruyTimDanChu.Game;
using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class MovementTests
{
    [Fact]
    public void OneSecondStraightMovement_TravelsExactly73Pixels_WithinTolerance()
    {
        float startX = 500f, startY = 366f;
        int rightKey = 8; // Bitmask for Right

        // Simulate 1 second in 20 steps of 50ms each
        float currentX = startX;
        float currentY = startY;
        for (int i = 0; i < 20; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, rightKey, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }

        var distance = currentX - startX;
        // Speed is 73 px/s -> distance must equal 73px ± 0.05px arithmetic precision
        Assert.InRange(distance, 72.95f, 73.05f);
        Assert.Equal(startY, currentY);
    }

    [Fact]
    public void DiagonalMovement_IsNormalized_TravelsExpectedSpeed()
    {
        float startX = 500f, startY = 366f;
        int downRightKeys = 2 | 8; // Down (2) + Right (8)

        // 1 second (20 steps of 0.05s)
        float currentX = startX;
        float currentY = startY;
        for (int i = 0; i < 20; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, downRightKeys, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }

        var dx = currentX - startX;
        var dy = currentY - startY;
        var totalDistance = MathF.Sqrt(dx * dx + dy * dy);

        // Normalized speed is 73 px/s total, dx and dy are 73 * 0.7071068 ≈ 51.6188
        Assert.InRange(totalDistance, 72.95f, 73.05f);
        Assert.InRange(dx, 51.5f, 51.7f);
        Assert.InRange(dy, 51.5f, 51.7f);
    }

    [Fact]
    public void WallAndRiverCollisions_BlockPlayerFromPassing()
    {
        // River is at X: 923..979, Y: 194..497
        // Start just to the left of the river: X = 915, Y = 250, try walking Right (key 8)
        float currentX = 915f;
        float currentY = 250f;

        for (int i = 0; i < 50; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, 8, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }

        // Cannot penetrate into river (b.X = 923, collision px + 5 > 923 -> px cannot reach 918)
        Assert.True(currentX < 919f);

        // Library wall: X: 306..464, Y: 34..82
        // Start at X = 350, Y = 95, try walking Up (key 1)
        currentX = 350f;
        currentY = 95f;
        for (int i = 0; i < 50; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, 1, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }

        // Blocked below the library wall
        Assert.True(currentY > 85f);
    }

    [Fact]
    public void MapBorders_ClampMovementWithinBounds()
    {
        // Walk Up towards Y = 0
        float currentX = 500f, currentY = 30f;
        for (int i = 0; i < 50; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, 1, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }
        Assert.Equal(TownCollision.MinY, currentY);

        // Walk Left towards X = 0
        currentX = 30f; currentY = 366f;
        for (int i = 0; i < 50; i++)
        {
            TownCollision.SimulateStep(currentX, currentY, 4, 0.05f,
                out var newX, out var newY, out _, out _);
            currentX = newX;
            currentY = newY;
        }
        Assert.Equal(TownCollision.MinX, currentX);
    }

    [Fact]
    public void SpammingPackets10x_DoesNotIncreaseSpeed_ServerClampsDt()
    {
        var session = new TeamMemberSession("p1", "Test", "bao", "#E53935", 500f, 366f);
        session.LastSimulatedTime = 1000;

        // Normal: 10 packets over 1000ms (every 100ms)
        // Spam: 100 packets sent in rapid succession (e.g. within 1000ms with tiny dt)
        long now = 1000;
        int seq = 1;
        for (int i = 0; i < 100; i++)
        {
            now += 10; // 10ms intervals
            session.ProcessMovement(8, seq++, now, out _);
        }

        var spamX = session.X;
        var traveled = spamX - 500f;

        // In 1000ms, player should travel at most 73px, not 730px!
        Assert.InRange(traveled, 72f, 74f);
    }

    [Fact]
    public void OutdatedSequence_IsRejected_DoesNotRollbackPosition()
    {
        var session = new TeamMemberSession("p1", "Test", "bao", "#E53935", 500f, 366f);
        session.LastSimulatedTime = 1000;

        // Move to sequence 5
        session.ProcessMovement(8, 5, 1050, out var ack1);
        Assert.True(ack1.Success);
        var advancedX = session.X;

        // Receive late / outdated sequence 3
        var successOld = session.ProcessMovement(4, 3, 1100, out var ackOld);
        Assert.False(successOld);
        Assert.Equal("OUTDATED_SEQUENCE", ackOld.ErrorCode);

        // Position must NOT have rolled back
        Assert.Equal(advancedX, session.X);
    }

    [Fact]
    public void Inactivity_ExceedingTTL_StopsWalkingAnimation()
    {
        var session = new TeamMemberSession("p1", "Test", "bao", "#E53935", 500f, 366f);
        session.LastSimulatedTime = 1000;
        session.ProcessMovement(8, 1, 1050, out _);
        Assert.Equal(1, session.Walking);

        // Time advances 200ms (< 400ms TTL) -> still walking
        Assert.False(session.CheckTtl(1200));
        Assert.Equal(1, session.Walking);

        // Time advances 500ms (> 400ms TTL) without new inputs -> stops
        Assert.True(session.CheckTtl(1600));
        Assert.Equal(0, session.Walking);
    }
}

