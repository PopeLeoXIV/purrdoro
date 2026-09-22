using Purrdoro.Core.Models;
using Purrdoro.Core.Services;

namespace Purrdoro.Core.Timing;

/// <summary>
/// Countdown that derives the remaining time from a monotonic clock instead of
/// decrementing a counter on every tick.
/// </summary>
/// <remarks>
/// <para>
/// When a countdown starts (or resumes) the service records the monotonic
/// timestamp from <see cref="TimeProvider.GetTimestamp"/> (backed by
/// <c>Stopwatch</c>) and the remaining time at that moment. At any later point:
/// </para>
/// <code>remaining = remainingAtStart - (now - startTimestamp)</code>
/// <para>
/// Ticks only ask the UI to refresh. If the UI thread is busy for three seconds,
/// the next tick simply observes that three seconds have passed – nothing is
/// lost and nothing drifts. Wall-clock changes (DST, NTP, the user changing the
/// time) have no effect because the timestamp is monotonic.
/// </para>
/// <para>
/// Completion is raised from a single place, guarded by the state machine, so
/// it fires exactly once per countdown even if several ticks arrive at zero.
/// </para>
/// This class is not thread-safe by design: it lives on the UI thread and its
/// tick source must raise ticks on that same thread.
/// </remarks>
public sealed class TimerService : ITimerService
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider _timeProvider;
    private readonly ITickSource _tickSource;
    private readonly TimeSpan _tickInterval;

    private TimeSpan _duration;
    private TimeSpan _remainingAtAnchor;
    private long _anchorTimestamp;
    private TimerState _state = TimerState.Idle;
    private bool _disposed;

    public TimerService(TimeProvider timeProvider, ITickSource tickSource, TimeSpan? tickInterval = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));
        _tickInterval = tickInterval ?? DefaultTickInterval;
        _tickSource.Tick += OnTick;
    }

    public event EventHandler? Ticked;

    public event EventHandler? StateChanged;

    public event EventHandler? Completed;

    public TimerState State => _state;

    public TimeSpan Duration => _duration;

    public TimeSpan Remaining => _state switch
    {
        TimerState.Running => ClampToZero(_remainingAtAnchor - _timeProvider.GetElapsedTime(_anchorTimestamp)),
        TimerState.Completed => TimeSpan.Zero,
        _ => _remainingAtAnchor,
    };

    public double Progress
    {
        get
        {
            if (_duration <= TimeSpan.Zero)
            {
                return 0;
            }

            var fraction = 1.0 - (Remaining.Ticks / (double)_duration.Ticks);
            return Math.Clamp(fraction, 0.0, 1.0);
        }
    }

    public void Load(TimeSpan duration)
    {
        ThrowIfDisposed();
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        _tickSource.Stop();
        _duration = duration;
        _remainingAtAnchor = duration;
        SetState(TimerState.Idle);
    }

    public bool Start()
    {
        ThrowIfDisposed();
        if (_state is not (TimerState.Idle or TimerState.Completed) || _duration <= TimeSpan.Zero)
        {
            return false;
        }

        _remainingAtAnchor = _duration;
        BeginRunning();
        return true;
    }

    public bool Pause()
    {
        ThrowIfDisposed();
        if (_state != TimerState.Running)
        {
            return false;
        }

        // Capture the exact remaining time *before* changing state.
        var remaining = Remaining;
        _tickSource.Stop();

        if (remaining <= TimeSpan.Zero)
        {
            // The countdown actually finished between ticks: complete instead of pausing at 00:00.
            Complete();
            return false;
        }

        _remainingAtAnchor = remaining;
        SetState(TimerState.Paused);
        return true;
    }

    public bool Resume()
    {
        ThrowIfDisposed();
        if (_state != TimerState.Paused)
        {
            return false;
        }

        BeginRunning();
        return true;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        _tickSource.Stop();
        _remainingAtAnchor = _duration;
        SetState(TimerState.Idle);
    }

    public void Poll()
    {
        if (_disposed || _state != TimerState.Running)
        {
            return;
        }

        if (Remaining <= TimeSpan.Zero)
        {
            Complete();
        }
        else
        {
            Ticked?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tickSource.Tick -= OnTick;
        _tickSource.Stop();
        Ticked = null;
        StateChanged = null;
        Completed = null;
    }

    private void OnTick(object? sender, EventArgs e) => Poll();

    private void BeginRunning()
    {
        _anchorTimestamp = _timeProvider.GetTimestamp();
        SetState(TimerState.Running);

        // ITickSource.Start is idempotent, so a single loop exists no matter how often this runs.
        _tickSource.Start(_tickInterval);
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void Complete()
    {
        // The state check is the single gate that guarantees exactly-once completion.
        if (_state is not (TimerState.Running or TimerState.Paused))
        {
            return;
        }

        _tickSource.Stop();
        _remainingAtAnchor = TimeSpan.Zero;
        SetState(TimerState.Completed);

        // Handlers may synchronously Load()/Start() the next session; that is safe
        // because the state has already left Running.
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(TimerState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan ClampToZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
