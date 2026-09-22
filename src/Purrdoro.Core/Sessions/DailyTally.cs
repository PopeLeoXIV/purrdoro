using Purrdoro.Core.Services;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.Sessions;

/// <summary>
/// Counts Pomodoros completed today and rolls over at local midnight.
/// The count is persisted so "3 pomodoros today" survives an app restart.
/// </summary>
public sealed class DailyTally
{
    private readonly IProgressStore _store;
    private readonly TimeProvider _timeProvider;
    private DailyProgress _progress;

    public DailyTally(IProgressStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _progress = store.Load();
        EnsureToday();
    }

    /// <summary>Completed Pomodoros for the current local date.</summary>
    public int Today
    {
        get
        {
            EnsureToday();
            return _progress.CompletedPomodoros;
        }
    }

    public void RecordCompletedPomodoro()
    {
        EnsureToday();
        _progress = new DailyProgress
        {
            Date = _progress.Date,
            CompletedPomodoros = _progress.CompletedPomodoros + 1,
        };
        _store.Save(_progress);
    }

    private void EnsureToday()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (_progress.Date != today)
        {
            _progress = new DailyProgress { Date = today, CompletedPomodoros = 0 };
        }
    }
}
