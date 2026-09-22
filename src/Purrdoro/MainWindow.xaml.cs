using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Purrdoro.Views;
using Windows.Graphics;

namespace Purrdoro;

/// <summary>
/// The single application window: Windows 11 title bar, Mica backdrop and a
/// Frame hosting the timer and settings pages. Only window/platform wiring
/// lives here.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Logical (DPI-independent) sizes of a small desktop utility window.
    private const int DefaultWidth = 380;
    private const int DefaultHeight = 620;
    private const int MinimumWidth = 340;
    private const int MinimumHeight = 520;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        ConfigureSizeAndPosition();
        RootFrame.Navigate(typeof(TimerPage));
    }

    /// <summary>The element whose RequestedTheme controls the whole window.</summary>
    public FrameworkElement ThemeRoot => RootGrid;

    /// <summary>Restores (if minimised) and focuses the window.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void ConfigureSizeAndPosition()
    {
        var scale = GetScaleFactor();
        var width = (int)Math.Round(DefaultWidth * scale);
        var height = (int)Math.Round(DefaultHeight * scale);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinimumHeight * scale);
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        var x = workArea.X + ((workArea.Width - width) / 2);
        var y = workArea.Y + ((workArea.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private double GetScaleFactor()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = RootFrame.CanGoBack;
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args)
    {
        if (RootFrame.CanGoBack)
        {
            RootFrame.GoBack();
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
