using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Purrdoro.Core.Models;
using Purrdoro.Core.Services;
using Purrdoro.Core.Sessions;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.ViewModels;

/// <summary>One indicator dot on the way to the next long break.</summary>
public sealed record CycleDot(bool IsFilled);

/// <summary>
/// State and commands for the main timer surface.
/// </summary>
/// <remarks>
/// <para><b>Start / Pause / Resume</b> share one primary command whose meaning follows the timer state.</para>
/// <para><b>Reset</b> returns the current session to its full duration (using the latest
/// settings) without changing the cycle or the completed count.</para>
/// <para><b>Skip</b> ends the current session early and moves to the next one exactly as if
/// it had finished, except that a skipped focus session is never counted, and no
/// notification or sound is played. The auto-start settings apply to the next session
/// in the same way as after a natural completion.</para>
/// <para><b>Settings changes</b> never alter a running or paused countdown. New durations
/// take effect when the next session is loaded or when the user presses Reset; an idle
/// (not yet started) session picks up the new duration immediately.</para>
/// </remarks>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    public const string PlayGlyph = "";
    public const string PauseGlyph = "";

    private static readonly TimeSpan CelebrationDuration = TimeSpan.FromSeconds(5);

    private readonly ITimerService _timer;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notifications;
    private readonly ISoundService _sound;
    private readonly DailyTally _tally;
    private readonly TimeProvider _timeProvider;
    private readonly PomodoroCycle _cycle;
    private readonly RelayCommand _resetCommand;

    private AppSettings _settings;
    private SessionType? _lastCompletedSession;
    private bool _awaitingStartAfterCompletion;
    private long? _celebrationStartedAt;
    private bool _disposed;

    private SessionType _currentSession;
    private TimerState _timerState;
    private string _sessionTitle = string.Empty;
    private string _timeText = "25:00";
    private string _message = string.Empty;
    private double _progress;
    private string _primaryActionLabel = "Start";
    private string _primaryActionGlyph = PlayGlyph;
    private string _primaryActionHelp = string.Empty;
    private bool _canReset;
    private bool _isBreak;
    private CatMood _catMood = CatMood.Content;
    private int _completedToday;
    private string _completedTodayText = string.Empty;
    private int _completedInCycle;
    private int _sessionsBeforeLongBreak;
    private string _cycleDescription = string.Empty;
    private string _timerAutomationName = string.Empty;

    public MainViewModel(
        ITimerService timer,
        ISettingsService settingsService,
        INotificationService notifications,
        ISoundService sound,
        IProgressStore progressStore,
        TimeProvider timeProvider)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _tally = new DailyTally(progressStore ?? throw new ArgumentNullException(nameof(progressStore)), timeProvider);

        _settings = settingsService.Current;
        _cycle = new PomodoroCycle(_settings.SessionsBeforeLongBreak);

        PrimaryCommand = new RelayCommand(TogglePrimary);
        _resetCommand = new RelayCommand(Reset, () => CanReset);
        SkipCommand = new RelayCommand(Skip);

        _timer.Ticked += OnTimerTicked;
        _timer.StateChanged += OnTimerStateChanged;
        _timer.Completed += OnTimerCompleted;
        _settingsService.SettingsChanged += OnSettingsChanged;

        _timer.Load(_settings.GetDuration(_cycle.Current));
        RefreshAll();
    }

    public RelayCommand PrimaryCommand { get; }

    public RelayCommand ResetCommand => _resetCommand;

    public RelayCommand SkipCommand { get; }

    public ObservableCollection<CycleDot> CycleDots { get; } = [];

    public SessionType CurrentSession { get => _currentSession; private set => SetProperty(ref _currentSession, value); }

    public TimerState TimerState { get => _timerState; private set => SetProperty(ref _timerState, value); }

    /// <summary>E.g. "Focus session", "Short break", "Session completed".</summary>
    public string SessionTitle { get => _sessionTitle; private set => SetProperty(ref _sessionTitle, value); }

    /// <summary>Remaining time as MM:SS.</summary>
    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }

    /// <summary>Small contextual line under the timer.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>Elapsed fraction of the current session, 0..1.</summary>
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    public string PrimaryActionLabel { get => _primaryActionLabel; private set => SetProperty(ref _primaryActionLabel, value); }

    public string PrimaryActionGlyph { get => _primaryActionGlyph; private set => SetProperty(ref _primaryActionGlyph, value); }

    public string PrimaryActionHelp { get => _primaryActionHelp; private set => SetProperty(ref _primaryActionHelp, value); }

    public bool CanReset
    {
        get => _canReset;
        private set
        {
            if (SetProperty(ref _canReset, value))
            {
                _resetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBreak { get => _isBreak; private set => SetProperty(ref _isBreak, value); }

    public CatMood CatMood { get => _catMood; private set => SetProperty(ref _catMood, value); }

    /// <summary>Genuinely completed focus sessions today.</summary>
    public int CompletedToday { get => _completedToday; private set => SetProperty(ref _completedToday, value); }

    public string CompletedTodayText { get => _completedTodayText; private set => SetProperty(ref _completedTodayText, value); }

    public int CompletedInCycle { get => _completedInCycle; private set => SetProperty(ref _completedInCycle, value); }

    public int SessionsBeforeLongBreak { get => _sessionsBeforeLongBreak; private set => SetProperty(ref _sessionsBeforeLongBreak, value); }

    public string CycleDescription { get => _cycleDescription; private set => SetProperty(ref _cycleDescription, value); }

    /// <summary>Screen-reader friendly description of the timer.</summary>
    public string TimerAutomationName { get => _timerAutomationName; private set => SetProperty(ref _timerAutomationName, value); }

    /// <summary>Re-checks the clock immediately, e.g. when the window is re-activated.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Poll();
        RefreshAll();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Ticked -= OnTimerTicked;
        _timer.StateChanged -= OnTimerStateChanged;
        _timer.Completed -= OnTimerCompleted;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _timer.Dispose();
    }

    private void TogglePrimary()
    {
        switch (_timer.State)
        {
            case TimerState.Running:
                _timer.Pause();
                break;
            case TimerState.Paused:
                _timer.Resume();
                break;
            default:
                ClearCompletionPresentation();
                _timer.Start();
                break;
        }

        RefreshAll();
    }

    private void Reset()
    {
        ClearCompletionPresentation();

        // Reset picks up any duration change made while this session was running.
        _timer.Load(_settings.GetDuration(_cycle.Current));
        RefreshAll();
    }

    private void Skip()
    {
        ClearCompletionPresentation();
        MoveToNextSession(completed: false);
    }

    private void OnTimerCompleted(object? sender, EventArgs e)
    {
        var finished = _cycle.Current;
        if (finished == SessionType.Focus)
        {
            _tally.RecordCompletedPomodoro();
        }

        MoveToNextSession(completed: true);
        Announce(finished, _cycle.Current);
    }

    private void MoveToNextSession(bool completed)
    {
        var finished = _cycle.Current;
        var next = _cycle.Advance(completed);
        _timer.Load(_settings.GetDuration(next));

        if (completed)
        {
            _lastCompletedSession = finished;
            _awaitingStartAfterCompletion = true;
            _celebrationStartedAt = _timeProvider.GetTimestamp();
        }

        var autoStart = next.IsBreak() ? _settings.AutoStartBreaks : _settings.AutoStartFocus;
        if (autoStart)
        {
            _awaitingStartAfterCompletion = false;
            _timer.Start();
        }

        RefreshAll();
    }

    private void Announce(SessionType finished, SessionType next)
    {
        // Notification and sound are deliberately independent of each other.
        if (_settings.NotificationsEnabled)
        {
            var (title, body) = GetCompletionCopy(finished, next);
            try
            {
                _notifications.Show(title, body);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Purrdoro] Notification failed: {ex.Message}");
            }
        }

        if (_settings.SoundEnabled)
        {
            try
            {
                _sound.PlayCompletionSound();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Purrdoro] Sound failed: {ex.Message}");
            }
        }
    }

    public static (string Title, string Body) GetCompletionCopy(SessionType finished, SessionType next) =>
        finished == SessionType.Focus
            ? ("Nice work!", next == SessionType.LongBreak
                ? "Time for a long break. You've earned it."
                : "Time to paws for a break.")
            : ("Break's over", "Ready for another focus session?");

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        _settings = e.NewSettings;
        _cycle.SessionsBeforeLongBreak = _settings.SessionsBeforeLongBreak;

        // Never disturb an active countdown; only an idle session adopts the new length.
        if (_timer.State == TimerState.Idle)
        {
            var duration = _settings.GetDuration(_cycle.Current);
            if (duration != _timer.Duration)
            {
                _timer.Load(duration);
            }
        }

        RefreshAll();
    }

    private void OnTimerTicked(object? sender, EventArgs e) => RefreshTime();

    private void OnTimerStateChanged(object? sender, EventArgs e) => RefreshAll();

    private void ClearCompletionPresentation()
    {
        _awaitingStartAfterCompletion = false;
        _celebrationStartedAt = null;
    }

    private bool IsCelebrating
    {
        get
        {
            if (_awaitingStartAfterCompletion)
            {
                return true;
            }

            if (_celebrationStartedAt is { } started)
            {
                if (_timeProvider.GetElapsedTime(started) < CelebrationDuration)
                {
                    return true;
                }

                _celebrationStartedAt = null;
            }

            return false;
        }
    }

    private void RefreshAll()
    {
        if (_disposed)
        {
            return;
        }

        var state = _timer.State;
        var session = _cycle.Current;

        CurrentSession = session;
        TimerState = state;
        IsBreak = session.IsBreak();
        CanReset = state is TimerState.Running or TimerState.Paused;

        (PrimaryActionLabel, PrimaryActionGlyph, PrimaryActionHelp) = state switch
        {
            TimerState.Running => ("Pause", PauseGlyph, "Pause the timer"),
            TimerState.Paused => ("Resume", PlayGlyph, "Resume the timer"),
            _ => ("Start", PlayGlyph, $"Start the {DescribeSession(session).ToLowerInvariant()}"),
        };

        SessionsBeforeLongBreak = _cycle.SessionsBeforeLongBreak;
        CompletedInCycle = _cycle.CompletedInCycle;
        UpdateCycleDots();

        RefreshTime();
    }

    private void RefreshTime()
    {
        if (_disposed)
        {
            return;
        }

        var remaining = _timer.Remaining;
        var totalSeconds = (int)((remaining.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        TimeText = string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}");
        Progress = _timer.Progress;
        TimerAutomationName = $"{DescribeSession(_cycle.Current)}, {DescribeDuration(minutes, seconds)} remaining";

        var today = _tally.Today;
        CompletedToday = today;
        CompletedTodayText = today switch
        {
            0 => "No pomodoros yet today",
            1 => "1 pomodoro today",
            _ => string.Create(CultureInfo.CurrentCulture, $"{today} pomodoros today"),
        };

        RefreshMoodAndCopy();
    }

    private void RefreshMoodAndCopy()
    {
        var state = _timer.State;
        var session = _cycle.Current;

        if (IsCelebrating && _lastCompletedSession is { } finished)
        {
            CatMood = CatMood.Celebrating;
            if (finished == SessionType.Focus)
            {
                SessionTitle = "Session completed";
                Message = session == SessionType.LongBreak
                    ? "Nice work! A long break is up next."
                    : "Nice work! Time to paws for a break.";
            }
            else
            {
                SessionTitle = "Break's over";
                Message = "Ready for another focus session?";
            }

            return;
        }

        SessionTitle = DescribeSession(session);

        if (state == TimerState.Paused)
        {
            CatMood = CatMood.Sleepy;
            Message = "Paused. Resume when you're ready";
            return;
        }

        var running = state == TimerState.Running;
        (CatMood, Message) = session switch
        {
            SessionType.ShortBreak => (CatMood.Happy, running ? "Paws for a break" : "A short break is ready"),
            SessionType.LongBreak => (CatMood.Happy, running ? "Stretch, sip and unwind" : "You've earned a long break"),
            _ => running ? (CatMood.Focused, "Focus time") : (CatMood.Content, "Ready when you are"),
        };
    }

    private void UpdateCycleDots()
    {
        var total = _cycle.SessionsBeforeLongBreak;
        var filled = Math.Min(_cycle.CompletedInCycle, total);

        CycleDescription = string.Create(
            CultureInfo.CurrentCulture,
            $"{filled} of {total} focus sessions completed before the next long break");

        var matches = CycleDots.Count == total;
        for (var i = 0; matches && i < total; i++)
        {
            matches = CycleDots[i].IsFilled == (i < filled);
        }

        if (matches)
        {
            return;
        }

        CycleDots.Clear();
        for (var i = 0; i < total; i++)
        {
            CycleDots.Add(new CycleDot(i < filled));
        }
    }

    public static string DescribeSession(SessionType session) => session switch
    {
        SessionType.ShortBreak => "Short break",
        SessionType.LongBreak => "Long break",
        _ => "Focus session",
    };

    private static string DescribeDuration(int minutes, int seconds)
    {
        var m = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        var s = seconds == 1 ? "1 second" : $"{seconds} seconds";
        return $"{m} {s}";
    }
}
