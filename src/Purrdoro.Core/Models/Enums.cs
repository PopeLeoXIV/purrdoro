namespace Purrdoro.Core.Models;

/// <summary>The three kinds of session in a Pomodoro cycle.</summary>
public enum SessionType
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>Lifecycle of a single countdown.</summary>
public enum TimerState
{
    /// <summary>Loaded with a full duration but not started.</summary>
    Idle,

    /// <summary>Counting down.</summary>
    Running,

    /// <summary>Stopped part-way; the remaining time is frozen.</summary>
    Paused,

    /// <summary>Reached zero. Transient: the session engine immediately loads the next session.</summary>
    Completed,
}

/// <summary>The mascot's expression, derived from timer state.</summary>
public enum CatMood
{
    /// <summary>Waiting for a focus session to begin: eyes open, soft smile.</summary>
    Content,

    /// <summary>Focus session running: calm and attentive.</summary>
    Focused,

    /// <summary>Timer paused: closed, sleepy eyes.</summary>
    Sleepy,

    /// <summary>On a break: happy, relaxed ^ ^ eyes.</summary>
    Happy,

    /// <summary>A session just finished: cheerful, with a little sparkle.</summary>
    Celebrating,
}

/// <summary>User-selected application theme.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public static class SessionTypeExtensions
{
    public static bool IsBreak(this SessionType session) => session != SessionType.Focus;
}
