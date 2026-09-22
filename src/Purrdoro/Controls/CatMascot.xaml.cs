using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Purrdoro.Core.Models;

namespace Purrdoro.Controls;

/// <summary>
/// The Purrdoro cat. Set <see cref="Mood"/> and the control cross-fades to the
/// matching expression. Purely presentational: it holds no app state.
/// </summary>
/// <remarks>
/// Every mood change explicitly sets the opacity of <em>every</em> expression part
/// (from <see cref="CatExpression.For"/>). Nothing relies on a previous state being
/// undone, so expressions cannot stack, however quickly the mood changes.
/// </remarks>
public sealed partial class CatMascot : UserControl
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood),
        typeof(CatMood),
        typeof(CatMascot),
        new PropertyMetadata(CatMood.Content, OnMoodChanged));

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(180);

    public CatMascot()
    {
        InitializeComponent();

        foreach (var part in AllParts)
        {
            part.OpacityTransition = new ScalarTransition { Duration = FadeDuration };
        }

        ApplyMood(animate: false);

        // Pages are cached and re-attached; snap to the correct face when shown again.
        Loaded += (_, _) => ApplyMood(animate: false);
    }

    public CatMood Mood
    {
        get => (CatMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    private UIElement[] AllParts =>
        [EyesContent, EyesFocused, EyeHighlights, EyesClosed, EyesHappy, MouthSmall, MouthOpen, SleepyZs, Sparkles];

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CatMascot)d).ApplyMood(animate: ((CatMascot)d).IsLoaded);

    private void ApplyMood(bool animate)
    {
        var face = CatExpression.For(Mood);

        SetVisible(EyesContent, face.Eyes == CatEyes.Round, animate);
        SetVisible(EyesFocused, face.Eyes == CatEyes.Attentive, animate);
        SetVisible(EyeHighlights, face.Eyes == CatEyes.Attentive, animate);
        SetVisible(EyesClosed, face.Eyes == CatEyes.Closed, animate);
        SetVisible(EyesHappy, face.Eyes == CatEyes.Smiling, animate);
        SetVisible(MouthSmall, !face.OpenMouth, animate);
        SetVisible(MouthOpen, face.OpenMouth, animate);
        SetVisible(SleepyZs, face.ShowSleepyZs, animate);
        SetVisible(Sparkles, face.ShowSparkles, animate);
    }

    private static void SetVisible(UIElement part, bool visible, bool animate)
    {
        var target = visible ? 1.0 : 0.0;
        if (animate)
        {
            part.Opacity = target;
            return;
        }

        // Snap instantly: suspend the implicit fade for this one assignment.
        var transition = part.OpacityTransition;
        part.OpacityTransition = null;
        part.Opacity = target;
        part.OpacityTransition = transition;
    }
}
