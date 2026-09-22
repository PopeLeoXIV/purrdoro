using System.Reflection;
using System.Windows.Input;
using Purrdoro.Core.Models;
using Purrdoro.Core.Services;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.ViewModels;

/// <summary>
/// Backs the Settings page. Every change is validated, clamped, saved and
/// broadcast immediately through <see cref="ISettingsService"/>.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    public static readonly Uri GitHubUri = new("https://github.com/PopeLeoXIV");

    private readonly ISettingsService _settingsService;
    private readonly ILinkLauncher _linkLauncher;
    private readonly SynchronizationContext? _syncContext;
    private AppSettings _settings;
    private bool _disposed;

    public SettingsViewModel(ISettingsService settingsService, ILinkLauncher linkLauncher)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _linkLauncher = linkLauncher ?? throw new ArgumentNullException(nameof(linkLauncher));
        _syncContext = SynchronizationContext.Current;
        _settings = settingsService.Current;
        _settingsService.SettingsChanged += OnSettingsChanged;

        OpenGitHubCommand = new AsyncRelayCommand(OpenGitHubAsync);
    }

    public ICommand OpenGitHubCommand { get; }

    // ---- Timer --------------------------------------------------------------------

    public double FocusMinutes
    {
        get => _settings.FocusMinutes;
        set => UpdateWholeNumber(value, nameof(FocusMinutes), (s, v) => s.FocusMinutes = v);
    }

    public double ShortBreakMinutes
    {
        get => _settings.ShortBreakMinutes;
        set => UpdateWholeNumber(value, nameof(ShortBreakMinutes), (s, v) => s.ShortBreakMinutes = v);
    }

    public double LongBreakMinutes
    {
        get => _settings.LongBreakMinutes;
        set => UpdateWholeNumber(value, nameof(LongBreakMinutes), (s, v) => s.LongBreakMinutes = v);
    }

    public double SessionsBeforeLongBreak
    {
        get => _settings.SessionsBeforeLongBreak;
        set => UpdateWholeNumber(value, nameof(SessionsBeforeLongBreak), (s, v) => s.SessionsBeforeLongBreak = v);
    }

    public double FocusMinimum => AppSettings.MinFocusMinutes;

    public double FocusMaximum => AppSettings.MaxFocusMinutes;

    public double ShortBreakMinimum => AppSettings.MinShortBreakMinutes;

    public double ShortBreakMaximum => AppSettings.MaxShortBreakMinutes;

    public double LongBreakMinimum => AppSettings.MinLongBreakMinutes;

    public double LongBreakMaximum => AppSettings.MaxLongBreakMinutes;

    public double SessionsMinimum => AppSettings.MinSessionsBeforeLongBreak;

    public double SessionsMaximum => AppSettings.MaxSessionsBeforeLongBreak;

    public string DurationChangeNote =>
        "New durations apply from the next session, or when you reset the current one.";

    // ---- Automation ---------------------------------------------------------------

    public bool AutoStartBreaks
    {
        get => _settings.AutoStartBreaks;
        set => Update(s => s.AutoStartBreaks = value);
    }

    public bool AutoStartFocus
    {
        get => _settings.AutoStartFocus;
        set => Update(s => s.AutoStartFocus = value);
    }

    // ---- Alerts -------------------------------------------------------------------

    public bool NotificationsEnabled
    {
        get => _settings.NotificationsEnabled;
        set => Update(s => s.NotificationsEnabled = value);
    }

    public bool SoundEnabled
    {
        get => _settings.SoundEnabled;
        set => Update(s => s.SoundEnabled = value);
    }

    // ---- Appearance ---------------------------------------------------------------

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Use system setting", "Light", "Dark" };

    /// <summary>0 = System, 1 = Light, 2 = Dark (matches <see cref="ThemeOptions"/>).</summary>
    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value >= ThemeOptions.Count)
            {
                NotifyLater(nameof(ThemeIndex));
                return;
            }

            Update(s => s.Theme = (ThemePreference)value);
        }
    }

    // ---- About --------------------------------------------------------------------

    public string AppName => "Purrdoro";

    public string AppDescription => "A cozy little Pomodoro timer for focused work and well-earned breaks.";

    public string Credit => "Made with ❤️ by 1uckyday";

    public string VersionText { get; } = "Version " + GetVersion();

    /// <summary>The small cat in the About section is always in a good mood.</summary>
    public CatMood AboutMood => CatMood.Happy;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }

    private Task OpenGitHubAsync() => _linkLauncher.OpenAsync(GitHubUri);

    private void UpdateWholeNumber(double value, string propertyName, Action<AppSettings, int> apply)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            // An empty or nonsensical entry: keep the previous value and tell the view to show it again.
            NotifyLater(propertyName);
            return;
        }

        var rounded = (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        Update(s => apply(s, rounded));

        // If clamping changed what the user typed, make sure the view reflects the stored value.
        if (Math.Abs(GetCurrentValue(propertyName) - value) > double.Epsilon)
        {
            NotifyLater(propertyName);
        }
    }

    private double GetCurrentValue(string propertyName) => propertyName switch
    {
        nameof(FocusMinutes) => FocusMinutes,
        nameof(ShortBreakMinutes) => ShortBreakMinutes,
        nameof(LongBreakMinutes) => LongBreakMinutes,
        nameof(SessionsBeforeLongBreak) => SessionsBeforeLongBreak,
        _ => double.NaN,
    };

    private void Update(Action<AppSettings> change) => _settingsService.Update(change);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        _settings = e.NewSettings;
        OnPropertyChanged(nameof(FocusMinutes));
        OnPropertyChanged(nameof(ShortBreakMinutes));
        OnPropertyChanged(nameof(LongBreakMinutes));
        OnPropertyChanged(nameof(SessionsBeforeLongBreak));
        OnPropertyChanged(nameof(AutoStartBreaks));
        OnPropertyChanged(nameof(AutoStartFocus));
        OnPropertyChanged(nameof(NotificationsEnabled));
        OnPropertyChanged(nameof(SoundEnabled));
        OnPropertyChanged(nameof(ThemeIndex));
    }

    /// <summary>
    /// Raises PropertyChanged after the current binding update has finished, so a
    /// two-way bound control re-reads the corrected value instead of ignoring it.
    /// </summary>
    private void NotifyLater(string propertyName)
    {
        if (_syncContext is null)
        {
            OnPropertyChanged(propertyName);
            return;
        }

        _syncContext.Post(_ => OnPropertyChanged(propertyName), null);
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+commitHash" build metadata.
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
