using Purrdoro.Core.Models;

namespace Purrdoro.Core.Settings;

/// <summary>
/// User preferences persisted to <c>%LOCALAPPDATA%\Purrdoro\settings.json</c>.
/// </summary>
/// <remarks>
/// Every property has a default initialiser, so a settings file written by an
/// older version (missing newer properties) loads with sensible values.
/// Always pass instances through <see cref="Normalized"/> before use.
/// </remarks>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MinFocusMinutes = 1;
    public const int MaxFocusMinutes = 120;
    public const int MinShortBreakMinutes = 1;
    public const int MaxShortBreakMinutes = 30;
    public const int MinLongBreakMinutes = 1;
    public const int MaxLongBreakMinutes = 60;
    public const int MinSessionsBeforeLongBreak = 1;
    public const int MaxSessionsBeforeLongBreak = 10;

    public const int DefaultFocusMinutes = 25;
    public const int DefaultShortBreakMinutes = 5;
    public const int DefaultLongBreakMinutes = 15;
    public const int DefaultSessionsBeforeLongBreak = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int FocusMinutes { get; set; } = DefaultFocusMinutes;

    public int ShortBreakMinutes { get; set; } = DefaultShortBreakMinutes;

    public int LongBreakMinutes { get; set; } = DefaultLongBreakMinutes;

    public int SessionsBeforeLongBreak { get; set; } = DefaultSessionsBeforeLongBreak;

    public bool AutoStartBreaks { get; set; }

    public bool AutoStartFocus { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    public bool SoundEnabled { get; set; } = true;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public TimeSpan GetDuration(SessionType session) => TimeSpan.FromMinutes(session switch
    {
        SessionType.ShortBreak => ShortBreakMinutes,
        SessionType.LongBreak => LongBreakMinutes,
        _ => FocusMinutes,
    });

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized()
    {
        var copy = Clone();
        copy.SchemaVersion = CurrentSchemaVersion;
        copy.FocusMinutes = Math.Clamp(FocusMinutes, MinFocusMinutes, MaxFocusMinutes);
        copy.ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, MinShortBreakMinutes, MaxShortBreakMinutes);
        copy.LongBreakMinutes = Math.Clamp(LongBreakMinutes, MinLongBreakMinutes, MaxLongBreakMinutes);
        copy.SessionsBeforeLongBreak = Math.Clamp(SessionsBeforeLongBreak, MinSessionsBeforeLongBreak, MaxSessionsBeforeLongBreak);
        if (!Enum.IsDefined(Theme))
        {
            copy.Theme = ThemePreference.System;
        }

        return copy;
    }
}

/// <summary>Today's completed-Pomodoro tally, persisted to <c>progress.json</c>.</summary>
public sealed class DailyProgress
{
    public DateOnly Date { get; set; }

    public int CompletedPomodoros { get; set; }
}

public enum SettingsLoadStatus
{
    /// <summary>The settings file was read successfully.</summary>
    Loaded,

    /// <summary>No settings file existed; defaults are in use.</summary>
    Defaults,

    /// <summary>The file was unreadable; it was backed up and defaults are in use.</summary>
    RecoveredFromCorruption,
}
