using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Purrdoro.Core.Services;
using Purrdoro.Core.Settings;
using Purrdoro.Core.Timing;
using Purrdoro.Core.ViewModels;
using Purrdoro.Services;

namespace Purrdoro;

/// <summary>The long-lived objects shared by the window and its pages.</summary>
public sealed record AppServices(MainViewModel Main, SettingsViewModel Settings);

/// <summary>
/// Application entry point and composition root. Objects are created by hand
/// here (no DI container needed at this size) and cleaned up when the window closes.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private SettingsService? _settingsService;
    private NotificationService? _notifications;
    private SoundService? _sound;
    private ThemeService? _themeService;
    private AppServices? _services;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static new App Current => (App)Application.Current;

    public AppServices Services =>
        _services ?? throw new InvalidOperationException("Services are created in OnLaunched.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Persistence: %LOCALAPPDATA%\Purrdoro\settings.json and progress.json.
        _settingsService = new SettingsService(AppPaths.SettingsFile);
        var progressStore = new ProgressStore(AppPaths.ProgressFile);

        // Platform services.
        _notifications = new NotificationService();
        _notifications.Initialize();
        _notifications.NotificationClicked += OnNotificationClicked;
        _sound = new SoundService();

        // Timer: monotonic clock + UI-thread refresh ticks.
        var timer = new TimerService(TimeProvider.System, new DispatcherTickSource(dispatcherQueue));

        _services = new AppServices(
            new MainViewModel(timer, _settingsService, _notifications, _sound, progressStore, TimeProvider.System),
            new SettingsViewModel(_settingsService, new LinkLauncher()));

        _window = new MainWindow();
        _themeService = new ThemeService(_window);
        _themeService.Apply(_settingsService.Current.Theme);
        _settingsService.SettingsChanged += OnSettingsChanged;

        _window.Activated += OnWindowActivated;
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Theme != e.NewSettings.Theme)
        {
            _themeService?.Apply(e.NewSettings.Theme);
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Catch up instantly when the user comes back to the window.
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _services?.Main.Refresh();
        }
    }

    private void OnNotificationClicked(object? sender, EventArgs e)
    {
        // Raised on a background thread: hop to the UI thread before touching the window.
        _window?.DispatcherQueue.TryEnqueue(() => _window?.BringToFront());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Closed -= OnWindowClosed;
        }

        if (_notifications is not null)
        {
            _notifications.NotificationClicked -= OnNotificationClicked;
            _notifications.Dispose();
        }

        _services?.Main.Dispose();      // stops the tick loop and unsubscribes from settings
        _services?.Settings.Dispose();
        _themeService?.Dispose();
        _sound?.Dispose();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep a small timer app running rather than crashing on an unexpected UI error.
        Debug.WriteLine($"[Purrdoro] Unhandled exception: {e.Exception}");
        e.Handled = true;
    }
}
