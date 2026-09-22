using Purrdoro.Core.Models;

namespace Purrdoro.Core.Sessions;

/// <summary>
/// Pure state machine for the Focus → Break → Focus … sequence.
/// </summary>
/// <remarks>
/// Rules (with the default interval of 4):
/// <list type="bullet">
/// <item>A <b>completed</b> focus session adds one to <see cref="CompletedInCycle"/>.
/// If that reaches <see cref="SessionsBeforeLongBreak"/> the next session is a long
/// break, otherwise a short break.</item>
/// <item>A <b>skipped</b> focus session earns no credit. It is followed by a short
/// break (or a long break only if one was already due).</item>
/// <item>Any break – completed or skipped – is followed by focus. Leaving a long
/// break starts a fresh cycle.</item>
/// </list>
/// Result: Focus, Short, Focus, Short, Focus, Short, Focus, Long, Focus …
/// </remarks>
public sealed class PomodoroCycle
{
    private int _sessionsBeforeLongBreak;

    public PomodoroCycle(int sessionsBeforeLongBreak)
    {
        SessionsBeforeLongBreak = sessionsBeforeLongBreak;
    }

    public SessionType Current { get; private set; } = SessionType.Focus;

    /// <summary>Focus sessions genuinely completed since the last long break.</summary>
    public int CompletedInCycle { get; private set; }

    public int SessionsBeforeLongBreak
    {
        get => _sessionsBeforeLongBreak;
        set => _sessionsBeforeLongBreak = Math.Max(1, value);
    }

    /// <summary>
    /// Ends the current session and moves to the next one.
    /// </summary>
    /// <param name="completed">
    /// True when the countdown reached zero; false when the user skipped it.
    /// </param>
    /// <returns>The new current session.</returns>
    public SessionType Advance(bool completed)
    {
        switch (Current)
        {
            case SessionType.Focus:
                if (completed)
                {
                    CompletedInCycle++;
                }

                Current = CompletedInCycle >= SessionsBeforeLongBreak
                    ? SessionType.LongBreak
                    : SessionType.ShortBreak;
                break;

            case SessionType.LongBreak:
                CompletedInCycle = 0;
                Current = SessionType.Focus;
                break;

            default:
                Current = SessionType.Focus;
                break;
        }

        return Current;
    }
}
