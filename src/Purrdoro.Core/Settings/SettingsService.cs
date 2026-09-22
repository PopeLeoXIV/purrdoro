using Purrdoro.Core.Services;

namespace Purrdoro.Core.Settings;

/// <summary>Where Purrdoro keeps its small data files.</summary>
public static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\Purrdoro</c> on Windows.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Purrdoro");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ProgressFile => Path.Combine(DataDirectory, "progress.json");
}

/// <summary>
/// JSON-backed <see cref="ISettingsService"/>. Missing, partial or malformed
/// files never stop the app from starting: values are defaulted and clamped.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly JsonFileStore<AppSettings> _store;
    private AppSettings _current;

    public SettingsService(string filePath)
    {
        _store = new JsonFileStore<AppSettings>(filePath, PurrdoroJsonContext.Default.AppSettings);

        var loaded = _store.Load(out var outcome);
        LoadStatus = outcome switch
        {
            JsonLoadOutcome.Loaded => SettingsLoadStatus.Loaded,
            JsonLoadOutcome.Corrupt => SettingsLoadStatus.RecoveredFromCorruption,
            _ => SettingsLoadStatus.Defaults,
        };

        _current = (loaded ?? new AppSettings()).Normalized();

        if (LoadStatus != SettingsLoadStatus.Loaded)
        {
            // Write a clean file so the user has something valid to look at.
            _store.Save(_current);
        }
    }

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public AppSettings Current => _current.Clone();

    public SettingsLoadStatus LoadStatus { get; }

    public string FilePath => _store.FilePath;

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var old = _current;
        var draft = old.Clone();
        change(draft);
        var updated = draft.Normalized();

        if (AreEqual(old, updated))
        {
            return;
        }

        _current = updated;
        _store.Save(updated);
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(old.Clone(), updated.Clone()));
    }

    private static bool AreEqual(AppSettings a, AppSettings b) =>
        a.FocusMinutes == b.FocusMinutes
        && a.ShortBreakMinutes == b.ShortBreakMinutes
        && a.LongBreakMinutes == b.LongBreakMinutes
        && a.SessionsBeforeLongBreak == b.SessionsBeforeLongBreak
        && a.AutoStartBreaks == b.AutoStartBreaks
        && a.AutoStartFocus == b.AutoStartFocus
        && a.NotificationsEnabled == b.NotificationsEnabled
        && a.SoundEnabled == b.SoundEnabled
        && a.Theme == b.Theme;
}

/// <summary>JSON-backed <see cref="IProgressStore"/>.</summary>
public sealed class ProgressStore(string filePath) : IProgressStore
{
    private readonly JsonFileStore<DailyProgress> _store = new(filePath, PurrdoroJsonContext.Default.DailyProgress);

    public DailyProgress Load()
    {
        var progress = _store.Load(out _) ?? new DailyProgress();
        progress.CompletedPomodoros = Math.Max(0, progress.CompletedPomodoros);
        return progress;
    }

    public void Save(DailyProgress progress) => _store.Save(progress);
}
