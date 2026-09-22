using Microsoft.UI.Dispatching;
using Purrdoro.Core.Services;

namespace Purrdoro.Services;

/// <summary>
/// <see cref="ITickSource"/> backed by a single <see cref="DispatcherQueueTimer"/>.
/// Ticks are raised on the UI thread, never block it, and because the one timer
/// instance is reused, a second tick loop can never be created.
/// </summary>
internal sealed class DispatcherTickSource : ITickSource
{
    private readonly DispatcherQueueTimer _timer;
    private bool _disposed;

    public DispatcherTickSource(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _timer = dispatcherQueue.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler? Tick;

    public bool IsRunning => !_disposed && _timer.IsRunning;

    public void Start(TimeSpan interval)
    {
        if (_disposed)
        {
            return;
        }

        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }

        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    public void Stop()
    {
        if (!_disposed)
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _disposed = true;
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args) => Tick?.Invoke(this, EventArgs.Empty);
}
