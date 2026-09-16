using TruyTimDanChu.Shared;

namespace TruyTimDanChu.Server;

/// <summary>
/// Server-authoritative match clock. Monotonic timestamps are used for elapsed/deadline
/// calculations; UTC is only an externally visible event timestamp.
/// </summary>
public sealed class MatchClock
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _limit;
    private long _startedTimestamp;
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset _deadlineUtc;
    private DateTimeOffset? _finishedAtUtc;
    private MatchEndReason? _endReason;
    private long? _pauseStartedTimestamp;
    private TimeSpan _totalPaused;
    private bool _started;

    public MatchClock(TimeSpan limit, TimeProvider? timeProvider = null)
    {
        if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTimeOffset StartedAtUtc { get { lock (_gate) return _startedAtUtc; } }
    public DateTimeOffset DeadlineUtc { get { lock (_gate) return _deadlineUtc; } }
    public DateTimeOffset? FinishedAtUtc { get { lock (_gate) return _finishedAtUtc; } }
    public MatchEndReason? EndReason { get { lock (_gate) return _endReason; } }
    public long TotalPausedMilliseconds { get { lock (_gate) return (long)_totalPaused.TotalMilliseconds; } }
    public bool IsPaused { get { lock (_gate) return _pauseStartedTimestamp.HasValue; } }
    public bool IsStarted { get { lock (_gate) return _started; } }

    public void Start(DateTimeOffset? startedAtUtc = null)
    {
        lock (_gate)
        {
            if (_started) return;
            _startedAtUtc = startedAtUtc ?? _timeProvider.GetUtcNow();
            _deadlineUtc = _startedAtUtc + _limit;
            _startedTimestamp = _timeProvider.GetTimestamp();
            _started = true;
        }
    }

    public bool IsBeforeDeadline()
    {
        lock (_gate)
        {
            if (!_started || _finishedAtUtc.HasValue) return false;
            return GetElapsedCore() < _limit;
        }
    }

    public long GetElapsedMilliseconds()
    {
        lock (_gate) return GetElapsedCore().TotalMilliseconds >= 0
            ? (long)GetElapsedCore().TotalMilliseconds : 0;
    }

    public long GetRemainingMilliseconds()
    {
        lock (_gate)
        {
            if (!_started) return (long)_limit.TotalMilliseconds;
            var remaining = _limit - GetElapsedCore();
            return Math.Max(0, (long)remaining.TotalMilliseconds);
        }
    }

    public bool TryFinish(MatchEndReason reason, out DateTimeOffset finishedAtUtc)
    {
        lock (_gate)
        {
            if (_finishedAtUtc.HasValue)
            {
                finishedAtUtc = _finishedAtUtc.Value;
                return false;
            }

            finishedAtUtc = _timeProvider.GetUtcNow();
            _finishedAtUtc = finishedAtUtc;
            _endReason = reason;
            return true;
        }
    }

    public bool TryPause(out DateTimeOffset pausedAtUtc)
    {
        lock (_gate)
        {
            pausedAtUtc = _timeProvider.GetUtcNow();
            if (!_started || _finishedAtUtc.HasValue || _pauseStartedTimestamp.HasValue) return false;
            _pauseStartedTimestamp = _timeProvider.GetTimestamp();
            return true;
        }
    }

    public bool TryResume(out DateTimeOffset resumedAtUtc)
    {
        lock (_gate)
        {
            resumedAtUtc = _timeProvider.GetUtcNow();
            if (!_started || _finishedAtUtc.HasValue || !_pauseStartedTimestamp.HasValue) return false;
            var paused = _timeProvider.GetElapsedTime(_pauseStartedTimestamp.Value, _timeProvider.GetTimestamp());
            _totalPaused += paused;
            _deadlineUtc += paused;
            _pauseStartedTimestamp = null;
            return true;
        }
    }

    public MatchClockSnapshot GetSnapshot(string matchId, bool isPaused = false)
    {
        lock (_gate)
        {
            var elapsed = _started ? Math.Max(0, (long)GetElapsedCore().TotalMilliseconds) : 0;
            var remaining = _started ? Math.Max(0, (long)(_limit - GetElapsedCore()).TotalMilliseconds) : (long)_limit.TotalMilliseconds;
            if (_finishedAtUtc.HasValue) remaining = 0;
            return new MatchClockSnapshot(
                matchId,
                _startedAtUtc,
                _deadlineUtc,
                _timeProvider.GetUtcNow(),
                elapsed,
                remaining,
                isPaused,
                _finishedAtUtc,
                _endReason);
        }
    }

    private TimeSpan GetElapsedCore()
    {
        if (!_started) return TimeSpan.Zero;
        var endTimestamp = _pauseStartedTimestamp ?? _timeProvider.GetTimestamp();
        return _timeProvider.GetElapsedTime(_startedTimestamp, endTimestamp) - _totalPaused;
    }
}
