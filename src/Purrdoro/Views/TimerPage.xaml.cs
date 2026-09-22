using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Purrdoro.Core.ViewModels;

namespace Purrdoro.Views;

/// <summary>
/// The main timer surface. All state lives in <see cref="MainViewModel"/>;
/// this code-behind only handles navigation to Settings.
/// </summary>
public sealed partial class TimerPage : Page
{
    public TimerPage()
    {
        ViewModel = App.Current.Services.Main;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CurrentSourcePageType == typeof(SettingsPage))
        {
            return; // ignore a double click
        }

        Frame.Navigate(
            typeof(SettingsPage),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }
}
