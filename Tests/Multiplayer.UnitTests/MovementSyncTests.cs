using TruyTimDanChu.Game;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class MovementSyncTests
{
    [Fact]
    public void BufferCapacity_CappedAtLimit()
    {
        var reconciler = new MovementReconciler();

        // Ghi 100 input liên tiếp
        for (int i = 1; i <= 100; i++)
        {
            reconciler.RecordInput(i, 8, 0.016f, 1000 + i * 16);
        }

        // Buffer không vượt quá MaxBufferSize (60)
        Assert.Equal(MovementReconciler.MaxBufferSize, reconciler.PendingInputCount);
        // Input cũ nhất còn giữ là sequence 41
        Assert.Equal(41, reconciler.PendingInputs[0].Sequence);
        Assert.Equal(100, reconciler.PendingInputs[^1].Sequence);
    }

    [Fact]
    public void Reconcile_DiscardsOlderOrDuplicateAcks()
    {
        var reconciler = new MovementReconciler();
        var player = new PlayerState { X = 500f, Y = 366f };

        reconciler.RecordInput(5, 8, 0.05f, 1000);
        reconciler.RecordInput(6, 8, 0.05f, 1050);

        // Server ACK sequence 6
        var ack6 = new MovementAck(true, null, 6, 507.3f, 366f, 1, 1, 1060);
        reconciler.Reconcile(ack6, player);

        Assert.Equal(6, reconciler.LastAckedSequence);
        Assert.Equal(0, reconciler.PendingInputCount);

        // Gói ACK sequence 5 (đến trễ hoặc lặp) gửi tới sau
        var ack5 = new MovementAck(true, null, 5, 503.65f, 366f, 1, 1, 1070);
        reconciler.Reconcile(ack5, player);

        // Không kéo lùi sequence và không thay đổi LastAckedSequence
        Assert.Equal(6, reconciler.LastAckedSequence);
    }

    [Fact]
    public void Reconcile_PurgesAckedInputs()
    {
        var reconciler = new MovementReconciler();
        var player = new PlayerState { X = 500f, Y = 366f };

        for (int i = 1; i <= 5; i++)
        {
            reconciler.RecordInput(i, 8, 0.05f, 1000 + i * 50);
        }
        Assert.Equal(5, reconciler.PendingInputCount);

        // Server ACK sequence 3
        var ack3 = new MovementAck(true, null, 3, 510.95f, 366f, 1, 1, 1160);
        reconciler.Reconcile(ack3, player);

        // Các input sequence 1, 2, 3 đã được dọn sạch, chỉ còn 4 và 5
        Assert.Equal(2, reconciler.PendingInputCount);
        Assert.Equal(4, reconciler.PendingInputs[0].Sequence);
        Assert.Equal(5, reconciler.PendingInputs[1].Sequence);
    }

    [Fact]
    public void Reconcile_ReplaysUnackedInputsAccurately()
    {
        var reconciler = new MovementReconciler();
        var player = new PlayerState { X = 500f, Y = 366f };

        // Player gửi 4 bước sang phải (mỗi bước 0.05s)
        for (int i = 1; i <= 4; i++)
        {
            reconciler.RecordInput(i, 8, 0.05f, 1000 + i * 50);
        }

        // Server tính toán xác nhận sequence 2 tại x = 507.3f
        var ack2 = new MovementAck(true, null, 2, 507.3f, 366f, 1, 1, 1110);
        reconciler.Reconcile(ack2, player);

        // Sau khi replay seq 3 và 4 từ 507.3f, quãng đường thêm 2 * (73 * 0.05) = 7.3f
        // Vị trí player sau replay phải hội tụ xấp xỉ 514.6f
        Assert.InRange(player.X, 514.5f, 514.7f);
        Assert.Equal(366f, player.Y);
    }

    [Fact]
    public void Reconcile_SnapsOnLargeDifferences()
    {
        var reconciler = new MovementReconciler();
        // Giả lập player bị desync hoặc đâm tường mà client đi quá 30px
        var player = new PlayerState { X = 550f, Y = 366f };

        reconciler.RecordInput(1, 8, 0.05f, 1000);

        // Server chỉ cho phép dừng tại 500f do tường cản (lệch 50px > SnapThreshold 10px)
        var ack1 = new MovementAck(true, null, 1, 500f, 366f, 1, 0, 1020);
        reconciler.Reconcile(ack1, player);

        // Phải snap ngay lập tức về 500f
        Assert.Equal(500f, player.X);
        Assert.Equal(366f, player.Y);
    }

    [Fact]
    public void Reconcile_SoftCorrection_DoesNotSnapOnSmallDifferences()
    {
        var reconciler = new MovementReconciler();
        var player = new PlayerState { X = 507.35f, Y = 366f };

        reconciler.RecordInput(1, 8, 0.05f, 1000);

        // Server trả về 507.30f (chênh lệch 0.05px <= SoftCorrectionThreshold 2.0px)
        var ack1 = new MovementAck(true, null, 1, 507.30f, 366f, 1, 1, 1020);
        reconciler.Reconcile(ack1, player);

        // Sai lệch nhỏ không làm giật vị trí khi đang di chuyển
        Assert.InRange(player.X, 507.29f, 507.36f);
    }

    [Fact]
    public void Reset_ClearsBufferAndState()
    {
        var reconciler = new MovementReconciler();
        reconciler.RecordInput(1, 8, 0.05f, 1000);
        reconciler.Reconcile(new MovementAck(true, null, 1, 503f, 366f, 1, 1, 1020), new PlayerState());

        reconciler.Reset();

        Assert.Equal(0, reconciler.PendingInputCount);
        Assert.Equal(0, reconciler.LastAckedSequence);
    }

    [Fact]
    public void TeammateInterpolator_AddSnapshot_RejectsOutOfOrderPackets()
    {
        var interp = new TeammateInterpolator(500f, 366f);

        // Snapshot seq 10, time 1000
        interp.AddSnapshot(510f, 366f, 1, 1, 10, 1000);
        Assert.Equal(10, interp.LastSequence);
        Assert.Equal(1000, interp.LastTimestampMs);

        // Snapshot seq 9 (out of order)
        interp.AddSnapshot(505f, 366f, 1, 1, 9, 950);
        // Bị bỏ qua
        Assert.Equal(10, interp.LastSequence);
        Assert.Equal(1, interp.SnapshotCount);
    }

    [Fact]
    public void TeammateInterpolator_Update_SmoothsMovementOverTime()
    {
        var interp = new TeammateInterpolator(500f, 366f);
        // Server báo teammate di chuyển tới 520f
        interp.AddSnapshot(520f, 366f, 1, 1, 1, 1000);

        // 1 frame 60 FPS (dt = 0.016s)
        interp.Update(0.016f);

        // Teammate phải di chuyển mượt về phía 520f, không snap thẳng tới 520f ngay
        Assert.True(interp.CurrentX > 500f);
        Assert.True(interp.CurrentX < 520f);
    }

    [Fact]
    public void TeammateInterpolator_Update_ConvergesToTarget()
    {
        var interp = new TeammateInterpolator(500f, 366f);
        interp.AddSnapshot(520f, 366f, 1, 0, 1, 1000);

        // Cập nhật liên tục trong 0.5s (30 frame)
        for (int i = 0; i < 30; i++)
        {
            interp.Update(0.016f);
        }

        // Sau 0.5s, vị trí phải hội tụ hoàn toàn về 520f
        Assert.InRange(interp.CurrentX, 519.9f, 520.01f);
        Assert.Equal(366f, interp.CurrentY);
    }

    [Fact]
    public void MultiplayerSession_PredictionAndReconciliation_Integrated()
    {
        var session = new MultiplayerSession("p1", "Player A", "avatar_01");
        session.ApplySnapshot(new TeamGameStateSnapshot(
            "match1", "team1", "Đội Đỏ", "#E53935",
            [new TeamMemberState("p1", "Player A", "avatar_01", "#E53935", 500f, 366f, 0, 0, true)],
            new TeamProgressSnapshot(Chapter.Lights, new bool[9], new bool[4], new bool[4], 1, 0)
        ));

        // Kiểm tra vị trí ban đầu
        Assert.Equal(500f, session.X);
        Assert.Equal(366f, session.Y);

        // Frame khởi tạo thời gian ban đầu
        session.Tick(1000, 0);

        // Bấm phím phải (8) sau 50ms
        session.Tick(1050, 8);

        // Local prediction: nhân vật di chuyển ngay lập tức trên client
        Assert.True(session.X > 500f);
        Assert.Equal(1, session.Walking);
        Assert.Equal(1, session.Facing);
    }

    [Fact]
    public void MultiplayerSession_AllowsMovement_DuringOpeningChapter()
    {
        // Khi người chơi mới vào trận online, Chapter là Opening
        var session = new MultiplayerSession("p1", "Player A", "avatar_01");
        session.ApplySnapshot(new TeamGameStateSnapshot(
            "match1", "team1", "Đội Đỏ", "#E53935",
            [new TeamMemberState("p1", "Player A", "avatar_01", "#E53935", 500f, 366f, 0, 0, true)],
            new TeamProgressSnapshot(Chapter.Opening, new bool[4], new bool[4], new bool[3], 1, 0)
        ));

        Assert.Equal(Chapter.Opening, session.Progress.Chapter);
        Assert.Equal(500f, session.X);

        // Frame khởi tạo
        session.Tick(1000, 0);

        // Nhấn phím D (Right = 8)
        session.Tick(1050, 8);

        // Nhân vật PHẢI di chuyển được để tiếp cận Trọng / Hòm Dân chủ
        Assert.True(session.X > 500f, $"Kỳ vọng X > 500f nhưng thực tế là {session.X}");
        Assert.Equal(1, session.Walking);
    }

    [Fact]
    public void TeammateActor_ReflectsFacingAndWalking_WhenTeammateMoves()
    {
        var session = new MultiplayerSession("p1", "Player 1", "quang");
        session.ApplySnapshot(new TeamGameStateSnapshot(
            "match1", "team1", "Đội Đỏ", "#E53935",
            [
                new TeamMemberState("p1", "Player 1", "quang", "#E53935", 500f, 366f, 2, 0, true),
                new TeamMemberState("p2", "Player 2", "bao", "#E53935", 518f, 366f, 2, 0, true)
            ],
            new TeamProgressSnapshot(Chapter.Opening, new bool[4], new bool[4], new bool[3], 1, 0)
        ));

        // Ban đầu đồng đội đứng yên quay mặt xuống dưới (Facing = 2, Walking = 0)
        var frame0 = session.Tick(1000, 0);
        var p2Actor0 = frame0.Actors.FirstOrDefault(a => a.Name == "Player 2");
        Assert.NotNull(p2Actor0);
        Assert.Equal(2, p2Actor0.Facing);
        Assert.Equal(0, p2Actor0.Walking);

        // Đồng đội p2 di chuyển lên trên (Facing = 0, Walking = 1)
        session.UpdateTeammatePosition("p2", 518f, 350f, 0, 1, 1, 1050);

        // Frame tiếp theo (dt = 50ms)
        var frame1 = session.Tick(1050, 0);
        var p2Actor1 = frame1.Actors.FirstOrDefault(a => a.Name == "Player 2");
        Assert.NotNull(p2Actor1);
        Assert.Equal(0, p2Actor1.Facing);
        Assert.Equal(1, p2Actor1.Walking);

        // Đồng đội p2 di chuyển sang phải (Facing = 1, Walking = 1)
        session.UpdateTeammatePosition("p2", 530f, 350f, 1, 1, 2, 1100);

        var frame2 = session.Tick(1100, 0);
        var p2Actor2 = frame2.Actors.FirstOrDefault(a => a.Name == "Player 2");
        Assert.NotNull(p2Actor2);
        Assert.Equal(1, p2Actor2.Facing);
        Assert.Equal(1, p2Actor2.Walking);

        // Đồng đội dừng lại (Facing = 1, Walking = 0)
        session.UpdateTeammatePosition("p2", 530f, 350f, 1, 0, 3, 1150);

        var frame3 = session.Tick(1150, 0);
        var p2Actor3 = frame3.Actors.FirstOrDefault(a => a.Name == "Player 2");
        Assert.NotNull(p2Actor3);
        Assert.Equal(1, p2Actor3.Facing);
        Assert.Equal(0, p2Actor3.Walking);
    }
}
