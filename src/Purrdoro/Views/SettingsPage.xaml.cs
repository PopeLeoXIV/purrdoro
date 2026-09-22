using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Purrdoro.Core.ViewModels;
using Windows.Globalization.NumberFormatting;

namespace Purrdoro.Views;

/// <summary>
/// Settings and About. Values are bound two-way to <see cref="SettingsViewModel"/>,
/// which validates, persists and broadcasts each change immediately. Opening this
/// page never touches the running timer.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Current.Services.Settings;
        InitializeComponent();

        // Whole numbers only (e.g. "25", never "25.00").
        foreach (var box in new[] { FocusBox, ShortBreakBox, LongBreakBox, IntervalBox })
        {
            box.NumberFormatter = CreateWholeNumberFormatter();
        }
    }

    public SettingsViewModel ViewModel { get; }

    private static DecimalFormatter CreateWholeNumberFormatter() => new()
    {
        IntegerDigits = 1,
        FractionDigits = 0,
        IsGrouped = false,
        NumberRounder = new IncrementNumberRounder
        {
            Increment = 1,
            RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
        },
    };

    private void OnBackAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            args.Handled = true;
        }
    }
}
