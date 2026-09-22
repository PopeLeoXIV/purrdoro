namespace Purrdoro.Core.Models;

/// <summary>Which pair of eyes the cat shows. Exactly one is visible at a time.</summary>
public enum CatEyes
{
    /// <summary>Round dot eyes.</summary>
    Round,

    /// <summary>Slightly taller eyes with a small highlight.</summary>
    Attentive,

    /// <summary>Closed, sleepy curves.</summary>
    Closed,

    /// <summary>Relaxed ^ ^ curves.</summary>
    Smiling,
}

/// <summary>
/// The complete visible state of the mascot for one <see cref="CatMood"/>.
/// Every part is specified for every mood, so switching moods can never
/// leave parts of the previous expression visible.
/// </summary>
public readonly record struct CatExpression(CatEyes Eyes, bool OpenMouth, bool ShowSparkles, bool ShowSleepyZs)
{
    public static CatExpression For(CatMood mood) => mood switch
    {
        CatMood.Focused => new(CatEyes.Attentive, OpenMouth: false, ShowSparkles: false, ShowSleepyZs: false),
        CatMood.Sleepy => new(CatEyes.Closed, OpenMouth: false, ShowSparkles: false, ShowSleepyZs: true),
        CatMood.Happy => new(CatEyes.Smiling, OpenMouth: false, ShowSparkles: false, ShowSleepyZs: false),
        CatMood.Celebrating => new(CatEyes.Smiling, OpenMouth: true, ShowSparkles: true, ShowSleepyZs: false),
        _ => new(CatEyes.Round, OpenMouth: false, ShowSparkles: false, ShowSleepyZs: false),
    };
}
