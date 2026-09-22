using Purrdoro.Core.Audio;
using Purrdoro.Core.Models;
using Purrdoro.Core.Sessions;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.Tests;

public static class PomodoroCycleTests
{
    [Test]
    public static void DefaultCycle_FollowsFocusShortFocusShortFocusShortFocusLong()
    {
        var cycle = new PomodoroCycle(4);
        var sequence = new List<SessionType> { cycle.Current };
        for (var i = 0; i < 9; i++)
        {
            sequence.Add(cycle.Advance(completed: true));
        }

        var expected = new[]
        {
            SessionType.Focus, SessionType.ShortBreak, SessionType.Focus, SessionType.ShortBreak,
            SessionType.Focus, SessionType.ShortBreak, SessionType.Focus, SessionType.LongBreak,
            SessionType.Focus, SessionType.ShortBreak,
        };
        Assert.Equal(string.Join(",", expected), string.Join(",", sequence));
    }

    [Test]
    public static void SkippedFocus_IsNotCounted_AndLeadsToShortBreak()
    {
        var cycle = new PomodoroCycle(4);
        Assert.Equal(SessionType.ShortBreak, cycle.Advance(completed: false));
        Assert.Equal(0, cycle.CompletedInCycle);
    }

    [Test]
    public static void LongBreak_RequiresGenuinelyCompletedFocusSessions()
    {
        var cycle = new PomodoroCycle(2);
        cycle.Advance(true);  // focus done -> short
        cycle.Advance(false); // skip short -> focus
        Assert.Equal(SessionType.ShortBreak, cycle.Advance(false)); // skipped focus: still short
        cycle.Advance(true);  // short -> focus
        Assert.Equal(SessionType.LongBreak, cycle.Advance(true));
        Assert.Equal(2, cycle.CompletedInCycle);
    }

    [Test]
    public static void SkippingLongBreak_StartsFreshCycle()
    {
        var cycle = new PomodoroCycle(1);
        Assert.Equal(SessionType.LongBreak, cycle.Advance(true));
        Assert.Equal(SessionType.Focus, cycle.Advance(false));
        Assert.Equal(0, cycle.CompletedInCycle);
    }

    [Test]
    public static void SkippingShortBreak_GoesToFocus()
    {
        var cycle = new PomodoroCycle(4);
        cycle.Advance(true);
        Assert.Equal(SessionType.Focus, cycle.Advance(false));
        Assert.Equal(1, cycle.CompletedInCycle);
    }

    [Test]
    public static void Interval_IsNeverBelowOne()
    {
        var cycle = new PomodoroCycle(0);
        Assert.Equal(1, cycle.SessionsBeforeLongBreak);
    }
}

public static class SettingsTests
{
    [Test]
    public static void MissingFile_UsesDefaults_AndWritesCleanFile()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var service = new SettingsService(path);

        Assert.Equal(SettingsLoadStatus.Defaults, service.LoadStatus);
        var s = service.Current;
        Assert.Equal(25, s.FocusMinutes);
        Assert.Equal(5, s.ShortBreakMinutes);
        Assert.Equal(15, s.LongBreakMinutes);
        Assert.Equal(4, s.SessionsBeforeLongBreak);
        Assert.False(s.AutoStartBreaks);
        Assert.False(s.AutoStartFocus);
        Assert.True(s.NotificationsEnabled);
        Assert.True(s.SoundEnabled);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.True(File.Exists(path));
    }

    [Test]
    public static void MalformedFile_DoesNotThrow_BacksUp_AndUsesDefaults()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{ this is not json ::: ");

        var service = new SettingsService(path);
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, service.LoadStatus);
        Assert.Equal(25, service.Current.FocusMinutes);
        Assert.True(File.Exists(dir.File("settings.corrupt.json")));
    }

    [Test]
    public static void WrongTypes_AndNullDocument_AreTreatedAsCorrupt()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");

        File.WriteAllText(path, """{ "focusMinutes": "twenty", "theme": 99 }""");
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, new SettingsService(path).LoadStatus);

        File.WriteAllText(path, "null");
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, new SettingsService(path).LoadStatus);

        File.WriteAllText(path, "");
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, new SettingsService(path).LoadStatus);
    }

    [Test]
    public static void PartialFile_FromOlderVersion_FillsNewSettingsWithDefaults()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """
            {
              // an older, hand-edited file
              "focusMinutes": 50,
              "theme": "Dark",
            }
            """);

        var s = new SettingsService(path).Current;
        Assert.Equal(50, s.FocusMinutes);
        Assert.Equal(ThemePreference.Dark, s.Theme);
        Assert.Equal(5, s.ShortBreakMinutes);
        Assert.True(s.SoundEnabled);
        Assert.True(s.NotificationsEnabled);
    }

    [Test]
    public static void OutOfRangeValues_AreClamped()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """{ "focusMinutes": 999999, "shortBreakMinutes": -4, "longBreakMinutes": 0, "sessionsBeforeLongBreak": 5000 }""");

        var s = new SettingsService(path).Current;
        Assert.Equal(AppSettings.MaxFocusMinutes, s.FocusMinutes);
        Assert.Equal(AppSettings.MinShortBreakMinutes, s.ShortBreakMinutes);
        Assert.Equal(AppSettings.MinLongBreakMinutes, s.LongBreakMinutes);
        Assert.Equal(AppSettings.MaxSessionsBeforeLongBreak, s.SessionsBeforeLongBreak);
    }

    [Test]
    public static void Update_Persists_AcrossInstances_AndRaisesEventOnce()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var service = new SettingsService(path);
        var events = 0;
        service.SettingsChanged += (_, _) => events++;

        service.Update(s =>
        {
            s.FocusMinutes = 40;
            s.ShortBreakMinutes = 7;
            s.LongBreakMinutes = 20;
            s.SessionsBeforeLongBreak = 3;
            s.AutoStartBreaks = true;
            s.AutoStartFocus = true;
            s.NotificationsEnabled = false;
            s.SoundEnabled = false;
            s.Theme = ThemePreference.Light;
        });
        service.Update(s => s.FocusMinutes = 40); // no change -> no event

        Assert.Equal(1, events);

        var reloaded = new SettingsService(path);
        Assert.Equal(SettingsLoadStatus.Loaded, reloaded.LoadStatus);
        var s = reloaded.Current;
        Assert.Equal(40, s.FocusMinutes);
        Assert.Equal(7, s.ShortBreakMinutes);
        Assert.Equal(20, s.LongBreakMinutes);
        Assert.Equal(3, s.SessionsBeforeLongBreak);
        Assert.True(s.AutoStartBreaks);
        Assert.True(s.AutoStartFocus);
        Assert.False(s.NotificationsEnabled);
        Assert.False(s.SoundEnabled);
        Assert.Equal(ThemePreference.Light, s.Theme);
        Assert.True(File.ReadAllText(path).Contains("\"theme\": \"Light\""), "Theme should be stored as a readable string");
    }

    [Test]
    public static void Current_ReturnsDefensiveCopy()
    {
        using var dir = new TempDir();
        var service = new SettingsService(dir.File("settings.json"));
        service.Current.FocusMinutes = 1;
        Assert.Equal(25, service.Current.FocusMinutes);
    }
}

public static class DailyTallyTests
{
    [Test]
    public static void Tally_PersistsAndRollsOverAtMidnight()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryProgressStore();
        var tally = new DailyTally(store, clock);

        tally.RecordCompletedPomodoro();
        tally.RecordCompletedPomodoro();
        Assert.Equal(2, tally.Today);
        Assert.Equal(2, new DailyTally(store, clock).Today); // survives restart

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, tally.Today);
        tally.RecordCompletedPomodoro();
        Assert.Equal(1, store.Stored.CompletedPomodoros);
    }

    [Test]
    public static void ProgressStore_RoundTripsAndSurvivesCorruption()
    {
        using var dir = new TempDir();
        var path = dir.File("progress.json");
        var store = new ProgressStore(path);
        store.Save(new DailyProgress { Date = new DateOnly(2026, 9, 22), CompletedPomodoros = 3 });
        Assert.Equal(3, new ProgressStore(path).Load().CompletedPomodoros);

        File.WriteAllText(path, "][");
        Assert.Equal(0, new ProgressStore(path).Load().CompletedPomodoros);
    }
}

public static class ChimeTests
{
    [Test]
    public static void Chime_IsShortQuietValidWav()
    {
        var wav = ChimeGenerator.CreateCompletionChimeWav();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));

        var samples = ChimeGenerator.Synthesize();
        var seconds = samples.Length / (double)ChimeGenerator.SampleRate;
        Assert.True(seconds < 1.0, $"Chime lasts {seconds:0.00}s");
        Assert.True(samples.Max(Math.Abs) <= 0.3f, "Chime must stay quiet");
        Assert.True(Math.Abs(samples[0]) < 0.01f && Math.Abs(samples[^1]) < 0.01f, "No clicks at the edges");
    }
}

public static class CatExpressionTests
{
    [Test]
    public static void Moods_MapToTheIntendedExpressions()
    {
        // (eyes, open mouth, sparkles, "z z"). Eyes is a single value, so only one pair can ever show.
        Assert.Equal(new CatExpression(CatEyes.Round, false, false, false), CatExpression.For(CatMood.Content));
        Assert.Equal(new CatExpression(CatEyes.Attentive, false, false, false), CatExpression.For(CatMood.Focused));
        Assert.Equal(new CatExpression(CatEyes.Closed, false, false, true), CatExpression.For(CatMood.Sleepy));
        Assert.Equal(new CatExpression(CatEyes.Smiling, false, false, false), CatExpression.For(CatMood.Happy));
        Assert.Equal(new CatExpression(CatEyes.Smiling, true, true, false), CatExpression.For(CatMood.Celebrating));
    }

    [Test]
    public static void EveryMood_HasADistinctFace()
    {
        var faces = Enum.GetValues<CatMood>().Select(CatExpression.For).ToList();
        Assert.Equal(faces.Count, faces.Distinct().Count());
    }
}
