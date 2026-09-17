using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Game;

public sealed record MovementInputRecord(
    int Sequence,
    int Keys,
    float Dt,
    double TimestampMs
);

public sealed class MovementReconciler
{
    private readonly List<MovementInputRecord> _unackedInputs = new();
    private readonly object _lock = new();

    public const int MaxBufferSize = 60;
    public const float SoftCorrectionThreshold = 2.0f;
    public const float SnapThreshold = 10.0f;
    private const float CorrectionBlend = 0.18f;

    public int LastAckedSequence { get; private set; }

    public int PendingInputCount
    {
        get
        {
            lock (_lock) return _unackedInputs.Count;
        }
    }

    public IReadOnlyList<MovementInputRecord> PendingInputs
    {
        get
        {
            lock (_lock) return _unackedInputs.ToArray();
        }
    }

    public void RecordInput(int sequence, int keys, float dt, double timestampMs)
    {
        lock (_lock)
        {
            if (_unackedInputs.Count >= MaxBufferSize)
            {
                _unackedInputs.RemoveAt(0);
            }
            _unackedInputs.Add(new MovementInputRecord(sequence, keys, dt, timestampMs));
        }
    }

    public void Reconcile(MovementAck ack, PlayerState player)
    {
        if (!ack.Success) return;

        lock (_lock)
        {
            if (ack.Sequence <= LastAckedSequence)
            {
                // Bỏ qua gói ACK đến trễ hơn hoặc bị lặp
                return;
            }
            LastAckedSequence = ack.Sequence;

            // Xóa tất cả input đã được server xác nhận
            _unackedInputs.RemoveAll(i => i.Sequence <= ack.Sequence);

            // Bắt đầu replay từ vị trí server xác nhận
            float curX = ack.X;
            float curY = ack.Y;
            int curFacing = ack.Facing;
            int curWalking = ack.Walking;

            foreach (var input in _unackedInputs)
            {
                TownCollision.SimulateStep(curX, curY, input.Keys, input.Dt,
                    out curX, out curY, out curFacing, out curWalking, curFacing);
            }

            float dx = curX - player.X;
            float dy = curY - player.Y;
            float distSq = dx * dx + dy * dy;

            if (distSq > SnapThreshold * SnapThreshold)
            {
                // Sai lệch lớn (> 10px): snap ngay lập tức
                player.X = curX;
                player.Y = curY;
                player.Facing = curFacing;
                player.Walking = curWalking;
            }
            else if (distSq > SoftCorrectionThreshold * SoftCorrectionThreshold)
            {
                // Khi đang di chuyển, ACK có thể về muộn hơn input hiện tại.
                // Hiệu chỉnh dần thay vì kéo giật nhân vật về vị trí cũ.
                if (player.Walking != 0 || curWalking != 0)
                {
                    player.X += dx * CorrectionBlend;
                    player.Y += dy * CorrectionBlend;
                }
                else
                {
                    player.X = curX;
                    player.Y = curY;
                }
                player.Facing = curFacing;
                player.Walking = curWalking;
            }
            else
            {
                // Sai lệch nhỏ (<= 2px): coi như lỗi sai số làm tròn float, không giật màn hình
                // Nếu nhân vật đã dừng hẳn, cho đồng bộ chính xác
                if (player.Walking == 0 && curWalking == 0)
                {
                    player.X = curX;
                    player.Y = curY;
                    player.Facing = curFacing;
                    player.Walking = 0;
                }
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _unackedInputs.Clear();
            LastAckedSequence = 0;
        }
    }
}

public sealed record TeammateSnapshot(
    float X,
    float Y,
    int Facing,
    int Walking,
    int Sequence,
    long TimestampMs
);

public sealed class TeammateInterpolator
{
    private readonly List<TeammateSnapshot> _snapshots = new();
    private readonly object _lock = new();

    public const int MaxSnapshotBuffer = 30;

    public int LastSequence { get; private set; }
    public long LastTimestampMs { get; private set; }

    public float CurrentX { get; private set; }
    public float CurrentY { get; private set; }
    public int CurrentFacing { get; private set; }
    public int CurrentWalking { get; private set; }

    public int SnapshotCount
    {
        get
        {
            lock (_lock) return _snapshots.Count;
        }
    }

    public TeammateInterpolator(float initialX, float initialY, int facing = 0, int walking = 0)
    {
        CurrentX = initialX;
        CurrentY = initialY;
        CurrentFacing = facing;
        CurrentWalking = walking;
    }

    public void AddSnapshot(float x, float y, int facing, int walking, int sequence, long timestampMs)
    {
        lock (_lock)
        {
            if (sequence <= LastSequence && sequence != 0)
            {
                // Bỏ qua gói out-of-order
                return;
            }
            if (timestampMs <= LastTimestampMs && LastTimestampMs != 0)
            {
                // Bỏ qua timestamp cũ
                return;
            }

            LastSequence = sequence;
            LastTimestampMs = timestampMs;

            if (_snapshots.Count >= MaxSnapshotBuffer)
            {
                _snapshots.RemoveAt(0);
            }
            _snapshots.Add(new TeammateSnapshot(x, y, facing, walking, sequence, timestampMs));
        }
    }

    public void Update(float dt)
    {
        lock (_lock)
        {
            if (_snapshots.Count == 0) return;

            var latest = _snapshots[^1];
            float dx = latest.X - CurrentX;
            float dy = latest.Y - CurrentY;
            float distSq = dx * dx + dy * dy;

            // Nếu khoảng cách quá lớn (ví dụ spawn lại hoặc teleport > 100px), snap ngay
            if (distSq > 100f * 100f)
            {
                CurrentX = latest.X;
                CurrentY = latest.Y;
                CurrentFacing = latest.Facing;
                CurrentWalking = latest.Walking;
                return;
            }

            // Lerp mượt mà về target snapshot với tốc độ mượt ở 60 FPS
            float lerpFactor = Math.Min(1.0f, dt * 16f);
            CurrentX += dx * lerpFactor;
            CurrentY += dy * lerpFactor;
            CurrentFacing = latest.Facing;
            CurrentWalking = latest.Walking;

            // Nếu rất gần đích (sai số < 0.2px), chốt về vị trí đích
            if (Math.Abs(latest.X - CurrentX) < 0.2f && Math.Abs(latest.Y - CurrentY) < 0.2f)
            {
                CurrentX = latest.X;
                CurrentY = latest.Y;
                if (latest.Walking == 0)
                {
                    CurrentWalking = 0;
                }
            }
        }
    }

    public void Reset(float x, float y, int facing = 0, int walking = 0)
    {
        lock (_lock)
        {
            _snapshots.Clear();
            LastSequence = 0;
            LastTimestampMs = 0;
            CurrentX = x;
            CurrentY = y;
            CurrentFacing = facing;
            CurrentWalking = walking;
        }
    }
}
