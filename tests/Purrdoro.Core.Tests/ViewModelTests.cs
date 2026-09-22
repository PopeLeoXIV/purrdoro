using Purrdoro.Core.Models;
using Purrdoro.Core.Settings;
using Purrdoro.Core.Timing;
using Purrdoro.Core.ViewModels;

namespace Purrdoro.Core.Tests;

internal sealed class Harness : IDisposable
{
    private readonly TempDir _dir = new();

    public Harness(Action<AppSettings>? configure = null)
    {
        Settings = new SettingsService(_dir.File("settings.json"));
        if (configure is not null)
        {
            Settings.Update(configure);
        }

        Timer = new TimerService(Clock, Ticks);
        ViewModel = new MainViewModel(Timer, Settings, Notifications, Sound, Progress, Clock);
    }

    public ManualTimeProvider Clock { get; } = new();

    public ManualTickSource Ticks { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeSound Sound { get; } = new();

    public InMemoryProgressStore Progress { get; } = new();

    public SettingsService Settings { get; }

    public TimerService Timer { get; }

    public MainViewModel ViewModel { get; }

    public void Primary() => ViewModel.PrimaryCommand.Execute(null);

    public void Skip() => ViewModel.SkipCommand.Execute(null);

    public void Reset() => ViewModel.ResetCommand.Execute(null);

    /// <summary>Lets <paramref name="duration"/> pass and delivers a (single, late) tick.</summary>
    public void Elapse(TimeSpan duration)
    {
        Clock.Advance(duration);
        Ticks.Fire();
    }

    public void FinishCurrentSession() => Elapse(Timer.Remaining + TimeSpan.FromMilliseconds(150));

    public void Dispose()
    {
        ViewModel.Dispose();
        _dir.Dispose();
    }
}

public static class MainViewModelTests
{
    [Test]
    public static void Initial_State_IsIdleFocus2500()
    {
        using var h = new Harness();
        var vm = h.ViewModel;
        Assert.Equal(SessionType.Focus, vm.CurrentSession);
        Assert.Equal("25:00", vm.TimeText);
        Assert.Equal("Focus session", vm.SessionTitle);
        Assert.Equal("Start", vm.PrimaryActionLabel);
        Assert.Equal(CatMood.Content, vm.CatMood);
        Assert.False(vm.CanReset);
        Assert.Equal(4, vm.CycleDots.Count);
        Assert.Equal("No pomodoros yet today", vm.CompletedTodayText);
    }

    [Test]
    public static void Start_Pause_Resume_Flow()
    {
        using var h = new Harness();
        var vm = h.ViewModel;

        h.Primary();
        Assert.Equal(TimerState.Running, vm.TimerState);
        Assert.Equal("Pause", vm.PrimaryActionLabel);
        Assert.Equal(CatMood.Focused, vm.CatMood);
        Assert.Equal("Focus time", vm.Message);
        Assert.True(vm.CanReset);

        h.Elapse(TimeSpan.FromSeconds(0.4));
        Assert.Equal("25:00", vm.TimeText); // shows whole seconds remaining, rounded up
        h.Elapse(TimeSpan.FromSeconds(0.6));
        Assert.Equal("24:59", vm.TimeText);

        h.Primary();
        Assert.Equal(TimerState.Paused, vm.TimerState);
        Assert.Equal("Resume", vm.PrimaryActionLabel);
        Assert.Equal(CatMood.Sleepy, vm.CatMood);

        h.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("24:59", vm.TimeText);

        h.Primary();
        Assert.Equal(TimerState.Running, vm.TimerState);
        h.Elapse(TimeSpan.FromSeconds(59));
        Assert.Equal("24:00", vm.TimeText);
    }

    [Test]
    public static void FocusCompletion_CountsOnce_NotifiesOnce_AndMovesToShortBreak()
    {
        using var h = new Harness();
        var vm = h.ViewModel;
        h.Primary();
        h.FinishCurrentSession();
        h.Ticks.FireStale();
        h.Ticks.FireStale();
        vm.Refresh();

        Assert.Equal(1, vm.CompletedToday);
        Assert.Equal("1 pomodoro today", vm.CompletedTodayText);
        Assert.Equal(SessionType.ShortBreak, vm.CurrentSession);
        Assert.Equal(TimerState.Idle, vm.TimerState); // auto-start breaks is off by default
        Assert.Equal("05:00", vm.TimeText);
        Assert.Equal("Session completed", vm.SessionTitle);
        Assert.Equal(CatMood.Celebrating, vm.CatMood);
        Assert.Equal(1, h.Notifications.Shown.Count);
        Assert.Equal("Nice work!", h.Notifications.Shown[0].Title);
        Assert.Equal("Time to paws for a break.", h.Notifications.Shown[0].Message);
        Assert.Equal(1, h.Sound.Plays);
        Assert.Equal(1, vm.CompletedInCycle);
    }

    [Test]
    public static void BreakCompletion_AnnouncesBreaksOver_AndDoesNotCount()
    {
        using var h = new Harness();
        h.Primary();
        h.FinishCurrentSession();
        h.Primary(); // start break
        Assert.Equal(CatMood.Happy, h.ViewModel.CatMood);
        Assert.Equal("Paws for a break", h.ViewModel.Message);
        h.FinishCurrentSession();

        Assert.Equal(1, h.ViewModel.CompletedToday);
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);
        Assert.Equal("Break's over", h.Notifications.Shown[^1].Title);
        Assert.Equal("Ready for another focus session?", h.Notifications.Shown[^1].Message);
        Assert.Equal("Break's over", h.ViewModel.SessionTitle);
    }

    [Test]
    public static void FullCycle_WithDefaults_SchedulesLongBreakAfterFourFocusSessions()
    {
        using var h = new Harness();
        var seen = new List<SessionType>();
        for (var i = 0; i < 9; i++)
        {
            seen.Add(h.ViewModel.CurrentSession);
            h.Primary();
            h.FinishCurrentSession();
        }

        Assert.Equal(
            "Focus,ShortBreak,Focus,ShortBreak,Focus,ShortBreak,Focus,LongBreak,Focus",
            string.Join(",", seen));
        Assert.Equal(5, h.ViewModel.CompletedToday);
        Assert.Equal("Time for a long break. You've earned it.", h.Notifications.Shown[6].Message);
    }

    [Test]
    public static void LongBreak_Duration_Is15Minutes()
    {
        using var h = new Harness(s => s.SessionsBeforeLongBreak = 1);
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(SessionType.LongBreak, h.ViewModel.CurrentSession);
        Assert.Equal("15:00", h.ViewModel.TimeText);
        Assert.Equal(1, h.ViewModel.CycleDots.Count); // one dot when interval is 1
    }

    [Test]
    public static void SkipFocus_NeverCounts_NoAlerts_GoesToShortBreak()
    {
        using var h = new Harness();
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(24.9));
        h.Skip();

        Assert.Equal(0, h.ViewModel.CompletedToday);
        Assert.Equal(0, h.ViewModel.CompletedInCycle);
        Assert.Equal(SessionType.ShortBreak, h.ViewModel.CurrentSession);
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
        Assert.Equal(0, h.Notifications.Shown.Count);
        Assert.Equal(0, h.Sound.Plays);
        Assert.Equal("Short break", h.ViewModel.SessionTitle); // no celebration for a skip
    }

    [Test]
    public static void SkipShortBreak_And_SkipLongBreak_ReturnToFocus()
    {
        using var h = new Harness(s => s.SessionsBeforeLongBreak = 2);
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(SessionType.ShortBreak, h.ViewModel.CurrentSession);
        h.Skip();
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);

        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(SessionType.LongBreak, h.ViewModel.CurrentSession);
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(3));
        h.Skip();
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);
        Assert.Equal(0, h.ViewModel.CompletedInCycle);
        Assert.Equal(2, h.ViewModel.CompletedToday);
        Assert.Equal("25:00", h.ViewModel.TimeText);
    }

    [Test]
    public static void SkipWhilePaused_Works()
    {
        using var h = new Harness();
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(1));
        h.Primary(); // pause
        h.Skip();
        Assert.Equal(SessionType.ShortBreak, h.ViewModel.CurrentSession);
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
        Assert.False(h.Ticks.IsRunning);
    }

    [Test]
    public static void AutoStartBreaks_StartsBreakAfterFocus_ButNotFocusAfterBreak()
    {
        using var h = new Harness(s => s.AutoStartBreaks = true);
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(SessionType.ShortBreak, h.ViewModel.CurrentSession);
        Assert.Equal(TimerState.Running, h.ViewModel.TimerState);
        Assert.Equal(CatMood.Celebrating, h.ViewModel.CatMood); // brief celebration

        h.Elapse(TimeSpan.FromSeconds(6));
        Assert.Equal(CatMood.Happy, h.ViewModel.CatMood);      // then relaxes

        h.FinishCurrentSession();
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
    }

    [Test]
    public static void AutoStartFocus_StartsFocusAfterBreak()
    {
        using var h = new Harness(s => s.AutoStartFocus = true);
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState); // breaks not auto-started
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);
        Assert.Equal(TimerState.Running, h.ViewModel.TimerState);
    }

    [Test]
    public static void BothAutoStarts_RunContinuously()
    {
        using var h = new Harness(s => { s.AutoStartBreaks = true; s.AutoStartFocus = true; });
        h.Primary();
        for (var i = 0; i < 8; i++)
        {
            h.FinishCurrentSession();
            Assert.Equal(TimerState.Running, h.ViewModel.TimerState);
        }

        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);
        Assert.Equal(4, h.ViewModel.CompletedToday);
        Assert.Equal(8, h.Notifications.Shown.Count);
    }

    [Test]
    public static void NotificationOff_SoundOn_StillPlaysSound()
    {
        using var h = new Harness(s => { s.NotificationsEnabled = false; s.SoundEnabled = true; });
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(0, h.Notifications.Shown.Count);
        Assert.Equal(1, h.Sound.Plays);
    }

    [Test]
    public static void NotificationOn_SoundOff_StillNotifies()
    {
        using var h = new Harness(s => { s.NotificationsEnabled = true; s.SoundEnabled = false; });
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(1, h.Notifications.Shown.Count);
        Assert.Equal(0, h.Sound.Plays);
    }

    [Test]
    public static void BothAlertsOff_TimerStillWorks()
    {
        using var h = new Harness(s => { s.NotificationsEnabled = false; s.SoundEnabled = false; });
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(0, h.Notifications.Shown.Count);
        Assert.Equal(0, h.Sound.Plays);
        Assert.Equal(1, h.ViewModel.CompletedToday);
    }

    [Test]
    public static void FailingAlertServices_DoNotBreakTheTimer()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.File("s.json"));
        var clock = new ManualTimeProvider();
        var ticks = new ManualTickSource();
        var vm = new MainViewModel(new TimerService(clock, ticks), settings, new ThrowingNotifications(), new ThrowingSound(), new InMemoryProgressStore(), clock);
        vm.PrimaryCommand.Execute(null);
        clock.Advance(TimeSpan.FromMinutes(26));
        ticks.Fire();
        Assert.Equal(SessionType.ShortBreak, vm.CurrentSession);
        Assert.Equal(1, vm.CompletedToday);
    }

    [Test]
    public static void ResetRunning_And_ResetPaused_KeepSessionAndCount()
    {
        using var h = new Harness();
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(5));
        h.Reset();
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
        Assert.Equal("25:00", h.ViewModel.TimeText);
        Assert.Equal(SessionType.Focus, h.ViewModel.CurrentSession);

        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(5));
        h.Primary(); // pause
        h.Reset();
        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
        Assert.Equal("25:00", h.ViewModel.TimeText);
        Assert.Equal(0, h.ViewModel.CompletedToday);
        Assert.False(h.ViewModel.ResetCommand.CanExecute(null));
    }

    [Test]
    public static void RepeatedPresses_AreSafe()
    {
        using var h = new Harness();
        for (var i = 0; i < 11; i++)
        {
            h.Primary(); // start/pause toggles, ending on running (odd count)
        }

        Assert.Equal(TimerState.Running, h.ViewModel.TimerState);
        for (var i = 0; i < 5; i++)
        {
            h.Reset();
        }

        Assert.Equal(TimerState.Idle, h.ViewModel.TimerState);
        Assert.Equal(1, h.Ticks.SubscriberCount);
    }

    [Test]
    public static void ChangingDuration_WhileRunning_DoesNotChangeActiveCountdown()
    {
        using var h = new Harness();
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(5));
        h.Settings.Update(s => s.FocusMinutes = 50);

        Assert.Equal("20:00", h.ViewModel.TimeText);
        Assert.Equal(TimerState.Running, h.ViewModel.TimerState);

        h.Reset(); // reset adopts the new length
        Assert.Equal("50:00", h.ViewModel.TimeText);
    }

    [Test]
    public static void ChangingDuration_WhilePaused_IsAppliedOnlyOnReset()
    {
        using var h = new Harness();
        h.Primary();
        h.Elapse(TimeSpan.FromMinutes(5));
        h.Primary();
        h.Settings.Update(s => s.FocusMinutes = 10);
        Assert.Equal("20:00", h.ViewModel.TimeText);
        Assert.Equal(TimerState.Paused, h.ViewModel.TimerState);
    }

    [Test]
    public static void ChangingDuration_WhileIdle_UpdatesDisplayedTime()
    {
        using var h = new Harness();
        h.Settings.Update(s => s.FocusMinutes = 45);
        Assert.Equal("45:00", h.ViewModel.TimeText);

        h.Settings.Update(s => s.ShortBreakMinutes = 8); // not the current session
        Assert.Equal("45:00", h.ViewModel.TimeText);
    }

    [Test]
    public static void NextSession_UsesUpdatedDurations()
    {
        using var h = new Harness();
        h.Primary();
        h.Settings.Update(s => s.ShortBreakMinutes = 9);
        h.FinishCurrentSession();
        Assert.Equal("09:00", h.ViewModel.TimeText);
    }

    [Test]
    public static void ChangingLongBreakInterval_UpdatesDots()
    {
        using var h = new Harness();
        h.Settings.Update(s => s.SessionsBeforeLongBreak = 6);
        Assert.Equal(6, h.ViewModel.CycleDots.Count);
        Assert.Equal(6, h.ViewModel.SessionsBeforeLongBreak);
    }

    [Test]
    public static void DelayedUi_TimerReachesZeroDuringLongStall_CompletesOnce()
    {
        using var h = new Harness();
        h.Primary();
        // The UI thread stalls for 30 minutes (e.g. machine under heavy load) and then
        // delivers a burst of queued ticks.
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        for (var i = 0; i < 10; i++)
        {
            h.Ticks.FireStale();
        }

        Assert.Equal(1, h.ViewModel.CompletedToday);
        Assert.Equal(1, h.Notifications.Shown.Count);
        Assert.Equal(1, h.Sound.Plays);
        Assert.Equal(SessionType.ShortBreak, h.ViewModel.CurrentSession);
    }

    [Test]
    public static void Refresh_OnActivation_DetectsCompletion()
    {
        using var h = new Harness();
        h.Primary();
        h.Clock.Advance(TimeSpan.FromMinutes(26));
        h.ViewModel.Refresh();
        Assert.Equal(1, h.ViewModel.CompletedToday);
    }

    [Test]
    public static void CatMood_FollowsState()
    {
        using var h = new Harness();
        var vm = h.ViewModel;
        Assert.Equal(CatMood.Content, vm.CatMood);
        h.Primary();
        Assert.Equal(CatMood.Focused, vm.CatMood);
        h.Primary();
        Assert.Equal(CatMood.Sleepy, vm.CatMood);
        h.Primary();
        h.FinishCurrentSession();
        Assert.Equal(CatMood.Celebrating, vm.CatMood);
        h.Primary();
        Assert.Equal(CatMood.Happy, vm.CatMood);
        h.Primary();
        Assert.Equal(CatMood.Sleepy, vm.CatMood);
    }

    [Test]
    public static void Dispose_UnsubscribesEverything()
    {
        var h = new Harness();
        h.Primary();
        h.Dispose();
        Assert.Equal(0, h.Ticks.SubscriberCount);
        Assert.False(h.Ticks.IsRunning);
        h.Settings.Update(s => s.FocusMinutes = 33); // must not throw into a disposed VM
    }

    private sealed class ThrowingNotifications : Services.INotificationService
    {
        public void Show(string title, string message) => throw new InvalidOperationException("no notifications here");
    }

    private sealed class ThrowingSound : Services.ISoundService
    {
        public void PlayCompletionSound() => throw new InvalidOperationException("no audio device");
    }
}

public static class SettingsViewModelTests
{
    [Test]
    public static void InvalidNumbers_AreIgnoredOrClamped()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.File("settings.json"));
        var vm = new SettingsViewModel(settings, new FakeLauncher());

        vm.FocusMinutes = double.NaN;
        Assert.Equal(25.0, vm.FocusMinutes);

        vm.FocusMinutes = 1e12;
        Assert.Equal((double)AppSettings.MaxFocusMinutes, vm.FocusMinutes);

        vm.ShortBreakMinutes = -3;
        Assert.Equal((double)AppSettings.MinShortBreakMinutes, vm.ShortBreakMinutes);

        vm.LongBreakMinutes = 17.6;
        Assert.Equal(18.0, vm.LongBreakMinutes);

        vm.SessionsBeforeLongBreak = double.PositiveInfinity;
        Assert.Equal(4.0, vm.SessionsBeforeLongBreak);
    }

    [Test]
    public static void Toggles_And_Theme_ArePersisted()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var vm = new SettingsViewModel(new SettingsService(path), new FakeLauncher());

        vm.AutoStartBreaks = true;
        vm.AutoStartFocus = true;
        vm.NotificationsEnabled = false;
        vm.SoundEnabled = false;
        vm.ThemeIndex = 2;
        vm.ThemeIndex = 7; // ignored

        var reloaded = new SettingsService(path).Current;
        Assert.True(reloaded.AutoStartBreaks);
        Assert.True(reloaded.AutoStartFocus);
        Assert.False(reloaded.NotificationsEnabled);
        Assert.False(reloaded.SoundEnabled);
        Assert.Equal(ThemePreference.Dark, reloaded.Theme);
    }

    [Test]
    public static void About_ContainsExactCredit_AndGitHubOpensCorrectUrl()
    {
        using var dir = new TempDir();
        var launcher = new FakeLauncher();
        var vm = new SettingsViewModel(new SettingsService(dir.File("settings.json")), launcher);

        Assert.Equal("Made with ❤️ by 1uckyday", vm.Credit);
        Assert.Equal("Purrdoro", vm.AppName);
        Assert.Equal("A cozy little Pomodoro timer for focused work and well-earned breaks.", vm.AppDescription);

        ((AsyncRelayCommand)vm.OpenGitHubCommand).ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(1, launcher.Opened.Count);
        Assert.Equal("https://github.com/PopeLeoXIV", launcher.Opened[0].OriginalString);
    }

    [Test]
    public static void ChangesFromSettingsPage_ReachTheTimerImmediately()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.File("settings.json"));
        var clock = new ManualTimeProvider();
        var main = new MainViewModel(new TimerService(clock, new ManualTickSource()), settings, new FakeNotifications(), new FakeSound(), new InMemoryProgressStore(), clock);
        var page = new SettingsViewModel(settings, new FakeLauncher());

        page.FocusMinutes = 30;
        Assert.Equal("30:00", main.TimeText);
    }
}
