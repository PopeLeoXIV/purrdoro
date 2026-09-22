using Purrdoro.Core.Models;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.Services;

/// <summary>
/// A periodic "please refresh" signal. It carries no timing information:
/// the timer derives remaining time from a monotonic clock, so a late or
/// skipped tick can never make the countdown drift.
/// </summary>
/// <remarks>
/// Implementations must raise <see cref="Tick"/> on the thread that owns the
/// timer (the UI thread in the app). Calling <see cref="Start"/> while already
/// running must not create a second tick loop.
/// </remarks>
public interface ITickSource : IDisposable
{
    event EventHandler? Tick;

    bool IsRunning { get; }

    void Start(TimeSpan interval);

    void Stop();
}

/// <summary>A drift-free countdown for a single session.</summary>
public interface ITimerService : IDisposable
{
    /// <summary>Raised on every refresh tick while running.</summary>
    event EventHandler? Ticked;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised exactly once per started countdown when it reaches zero.</summary>
    event EventHandler? Completed;

    TimerState State { get; }

    TimeSpan Duration { get; }

    TimeSpan Remaining { get; }

    /// <summary>Fraction of the session already elapsed, 0..1.</summary>
    double Progress { get; }

    /// <summary>Stops any countdown and prepares a new one of <paramref name="duration"/>.</summary>
    void Load(TimeSpan duration);

    /// <summary>Starts the loaded countdown. Returns false if it was not idle.</summary>
    bool Start();

    /// <summary>Freezes the remaining time. Returns false if it was not running.</summary>
    bool Pause();

    /// <summary>Continues from the frozen remaining time. Returns false if it was not paused.</summary>
    bool Resume();

    /// <summary>Returns to idle with the full loaded duration.</summary>
    void Reset();

    /// <summary>Re-evaluates the clock immediately (e.g. after the app is re-activated).</summary>
    void Poll();
}

/// <summary>Loads, validates and persists user preferences.</summary>
public interface ISettingsService
{
    /// <summary>Raised after settings have changed and been saved.</summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>A snapshot of the current, already-normalised settings.</summary>
    AppSettings Current { get; }

    /// <summary>What happened when the settings file was read at startup.</summary>
    SettingsLoadStatus LoadStatus { get; }

    /// <summary>Applies <paramref name="change"/>, normalises, saves and notifies.</summary>
    void Update(Action<AppSettings> change);
}

/// <summary>Shows a native desktop notification.</summary>
public interface INotificationService
{
    void Show(string title, string message);
}

/// <summary>Plays the short completion chime.</summary>
public interface ISoundService
{
    void PlayCompletionSound();
}

/// <summary>Opens a URL in the user's default browser.</summary>
public interface ILinkLauncher
{
    Task<bool> OpenAsync(Uri uri);
}

/// <summary>Stores today's completed-Pomodoro tally.</summary>
public interface IProgressStore
{
    DailyProgress Load();

    void Save(DailyProgress progress);
}

public sealed class SettingsChangedEventArgs(AppSettings oldSettings, AppSettings newSettings) : EventArgs
{
    public AppSettings OldSettings { get; } = oldSettings;

    public AppSettings NewSettings { get; } = newSettings;
}
