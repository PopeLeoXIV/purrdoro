using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Purrdoro.Core.Services;

namespace Purrdoro.Services;

/// <summary>
/// Native Windows notifications through the Windows App SDK
/// (<see cref="AppNotificationManager"/>). Works offline, needs no service.
/// </summary>
/// <remarks>
/// Notifications are shown silently (<c>MuteAudio</c>): the completion chime is
/// owned by <see cref="SoundService"/> so the two settings stay independent.
/// Each new notification replaces the previous one (same tag/group), so the
/// Action Center never fills up with stale timer alerts. If registration fails
/// (for example, notifications are blocked by policy) the app keeps working and
/// <see cref="Show"/> quietly does nothing.
/// </remarks>
internal sealed class NotificationService : INotificationService, IDisposable
{
    private const string Tag = "session-complete";
    private const string Group = "purrdoro";

    private bool _registered;

    /// <summary>Raised (on a background thread) when the user clicks a notification.</summary>
    public event EventHandler? NotificationClicked;

    public void Initialize()
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                Debug.WriteLine("[Purrdoro] App notifications are not supported on this system.");
                return;
            }

            var manager = AppNotificationManager.Default;

            // The handler must be attached before Register().
            manager.NotificationInvoked += OnNotificationInvoked;

            try
            {
                // Unpackaged (dotnet run / published exe): give Windows a display name and icon.
                var icon = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"));
                manager.Register("Purrdoro", icon);
            }
            catch (Exception)
            {
                // Packaged (MSIX) builds take their identity from the package manifest.
                manager.Register();
            }

            _registered = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Purrdoro] Notifications unavailable: {ex.Message}");
            _registered = false;
        }
    }

    public void Show(string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .MuteAudio()
                .BuildNotification();

            notification.Tag = Tag;
            notification.Group = Group;

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Purrdoro] Could not show notification: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Purrdoro] Notification cleanup failed: {ex.Message}");
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        NotificationClicked?.Invoke(this, EventArgs.Empty);
}
