using System.Reflection;
using System.Runtime.CompilerServices;
using Purrdoro.Core.Services;
using Purrdoro.Core.Settings;

namespace Purrdoro.Core.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
        {
            throw new AssertionException(message ?? $"Expected true: {expression}");
        }
    }

    public static void False(bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
        => True(!condition, message, "!" + expression);

    public static void Equal<T>(T expected, T actual, [CallerArgumentExpression(nameof(actual))] string? expression = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException($"{expression}: expected <{expected}> but was <{actual}>");
        }
    }

    public static void Near(double expected, double actual, double tolerance, [CallerArgumentExpression(nameof(actual))] string? expression = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new AssertionException($"{expression}: expected {expected}±{tolerance} but was {actual}");
        }
    }
}

/// <summary>Deterministic clock: time only moves when a test says so.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _utcNow = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by)
    {
        _ticks += by.Ticks;
        _utcNow += by;
    }

    /// <summary>Moves only the wall clock, e.g. the user changing the system time.</summary>
    public void JumpWallClock(TimeSpan by) => _utcNow += by;
}

/// <summary>A tick source the test fires by hand, standing in for DispatcherQueueTimer.</summary>
public sealed class ManualTickSource : ITickSource
{
    public event EventHandler? Tick;

    public bool IsRunning { get; private set; }

    public int StartCalls { get; private set; }

    public int SubscriberCount => Tick?.GetInvocationList().Length ?? 0;

    public void Start(TimeSpan interval)
    {
        StartCalls++;
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    /// <summary>Delivers a tick if the source is running (like a real timer).</summary>
    public void Fire()
    {
        if (IsRunning)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Delivers a tick regardless – simulates a late tick queued before Stop().</summary>
    public void FireStale() => Tick?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Stop();
}

public sealed class FakeNotifications : INotificationService
{
    public List<(string Title, string Message)> Shown { get; } = [];

    public void Show(string title, string message) => Shown.Add((title, message));
}

public sealed class FakeSound : ISoundService
{
    public int Plays { get; private set; }

    public void PlayCompletionSound() => Plays++;
}

public sealed class FakeLauncher : ILinkLauncher
{
    public List<Uri> Opened { get; } = [];

    public Task<bool> OpenAsync(Uri uri)
    {
        Opened.Add(uri);
        return Task.FromResult(true);
    }
}

public sealed class InMemoryProgressStore : IProgressStore
{
    public DailyProgress Stored { get; set; } = new();

    public int Saves { get; private set; }

    public DailyProgress Load() => new() { Date = Stored.Date, CompletedPomodoros = Stored.CompletedPomodoros };

    public void Save(DailyProgress progress)
    {
        Saves++;
        Stored = new DailyProgress { Date = progress.Date, CompletedPomodoros = progress.CompletedPomodoros };
    }
}

/// <summary>A temporary directory deleted at the end of a test.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "purrdoro-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        var filter = args.FirstOrDefault();
        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .Where(m => filter is null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.MetadataToken)
            .ToList();

        var failed = 0;
        foreach (var test in tests)
        {
            var name = $"{test.DeclaringType!.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"  PASS  {name}");
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {tests.Count} tests passed."
            : $"{failed} of {tests.Count} tests FAILED.");
        return failed == 0 ? 0 : 1;
    }
}
