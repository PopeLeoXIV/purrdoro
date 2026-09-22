using Purrdoro.Core.Models;
using Purrdoro.Core.Timing;

namespace Purrdoro.Core.Tests;

public static class TimerServiceTests
{
    private static (TimerService Timer, ManualTimeProvider Clock, ManualTickSource Ticks) Create(int minutes = 25)
    {
        var clock = new ManualTimeProvider();
        var ticks = new ManualTickSource();
        var timer = new TimerService(clock, ticks);
        timer.Load(TimeSpan.FromMinutes(minutes));
        return (timer, clock, ticks);
    }

    [Test]
    public static void Load_StartsIdleWithFullDuration()
    {
        var (timer, _, ticks) = Create();
        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
        Assert.Equal(0.0, timer.Progress);
        Assert.False(ticks.IsRunning);
    }

    [Test]
    public static void Remaining_IsDerivedFromElapsedTime_NotFromTickCount()
    {
        var (timer, clock, ticks) = Create();
        timer.Start();

        // Ten minutes pass but the UI thread was so busy that only ONE tick was delivered.
        clock.Advance(TimeSpan.FromMinutes(10));
        ticks.Fire();

        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining);
        Assert.Near(0.4, timer.Progress, 1e-9);
    }

    [Test]
    public static void IrregularAndDelayedTicks_DoNotDrift()
    {
        var (timer, clock, ticks) = Create();
        timer.Start();
        var random = new Random(42);
        var elapsed = TimeSpan.Zero;

        for (var i = 0; i < 500; i++)
        {
            // Ticks arrive anywhere between 50 ms and 2.5 s apart.
            var gap = TimeSpan.FromMilliseconds(random.Next(50, 2500));
            clock.Advance(gap);
            elapsed += gap;
            if (elapsed >= TimeSpan.FromMinutes(25))
            {
                break;
            }

            ticks.Fire();
            Assert.Equal(TimeSpan.FromMinutes(25) - elapsed, timer.Remaining);
        }
    }

    [Test]
    public static void WallClockChanges_DoNotAffectCountdown()
    {
        var (timer, clock, _) = Create();
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(1));
        clock.JumpWallClock(TimeSpan.FromHours(-3)); // user changes the system clock
        Assert.Equal(TimeSpan.FromMinutes(24), timer.Remaining);
    }

    [Test]
    public static void Pause_CapturesExactRemaining_AndResumeContinuesFromIt()
    {
        var (timer, clock, ticks) = Create();
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(83.25));
        Assert.True(timer.Pause());
        Assert.Equal(TimerState.Paused, timer.State);
        Assert.False(ticks.IsRunning);

        var frozen = TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(83.25);
        Assert.Equal(frozen, timer.Remaining);

        // Time passing while paused must not count.
        clock.Advance(TimeSpan.FromMinutes(7));
        Assert.Equal(frozen, timer.Remaining);

        Assert.True(timer.Resume());
        Assert.True(ticks.IsRunning);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(frozen - TimeSpan.FromSeconds(10), timer.Remaining);
    }

    [Test]
    public static void Completion_FiresExactlyOnce_EvenWithManyTicksAtZero()
    {
        var (timer, clock, ticks) = Create(minutes: 1);
        var completions = 0;
        timer.Completed += (_, _) => completions++;
        timer.Start();

        clock.Advance(TimeSpan.FromSeconds(61));
        ticks.Fire();
        ticks.FireStale();
        ticks.FireStale();
        timer.Poll();
        clock.Advance(TimeSpan.FromSeconds(5));
        ticks.FireStale();

        Assert.Equal(1, completions);
        Assert.Equal(TimerState.Completed, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.Equal(1.0, timer.Progress);
        Assert.False(ticks.IsRunning);
    }

    [Test]
    public static void Completion_DoesNotFireEarly()
    {
        var (timer, clock, ticks) = Create(minutes: 1);
        var completions = 0;
        timer.Completed += (_, _) => completions++;
        timer.Start();

        clock.Advance(TimeSpan.FromSeconds(59.9));
        ticks.Fire();
        Assert.Equal(0, completions);
        Assert.Equal(TimerState.Running, timer.State);
    }

    [Test]
    public static void PauseAfterZeroButBeforeTick_CompletesInsteadOfPausing()
    {
        var (timer, clock, _) = Create(minutes: 1);
        var completions = 0;
        timer.Completed += (_, _) => completions++;
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(75));

        Assert.False(timer.Pause());
        Assert.Equal(1, completions);
        Assert.Equal(TimerState.Completed, timer.State);
    }

    [Test]
    public static void CompletedHandler_CanStartNextSession_WithoutDoubleCompletion()
    {
        var (timer, clock, ticks) = Create(minutes: 1);
        var completions = 0;
        timer.Completed += (_, _) =>
        {
            completions++;
            timer.Load(TimeSpan.FromMinutes(5));
            timer.Start();
        };

        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(60));
        ticks.Fire();
        ticks.FireStale(); // a stale tick queued before the transition

        Assert.Equal(1, completions);
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining);
    }

    [Test]
    public static void RepeatedStart_DoesNotCreateSecondLoop_OrRestart()
    {
        var (timer, clock, ticks) = Create();
        Assert.True(timer.Start());
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(timer.Start());
        Assert.False(timer.Start());
        Assert.False(timer.Resume());
        Assert.Equal(TimeSpan.FromMinutes(23), timer.Remaining);
        Assert.Equal(1, ticks.SubscriberCount);
    }

    [Test]
    public static void RepeatedPause_IsHarmless()
    {
        var (timer, clock, _) = Create();
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(timer.Pause());
        Assert.False(timer.Pause());
        Assert.Equal(TimeSpan.FromMinutes(24), timer.Remaining);
    }

    [Test]
    public static void Reset_FromRunning_AndFromPaused_ReturnsToFullIdle()
    {
        var (timer, clock, ticks) = Create();
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(3));
        timer.Reset();
        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
        Assert.False(ticks.IsRunning);

        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(3));
        timer.Pause();
        timer.Reset();
        Assert.Equal(TimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
    }

    [Test]
    public static void Dispose_StopsTicksAndUnsubscribes()
    {
        var (timer, _, ticks) = Create();
        timer.Start();
        timer.Dispose();
        Assert.False(ticks.IsRunning);
        Assert.Equal(0, ticks.SubscriberCount);
        timer.Poll(); // must not throw after dispose
    }
}
