using Microsoft.UI;
using Microsoft.UI.Xaml;
using Purrdoro.Core.Models;
using Windows.UI;

namespace Purrdoro.Services;

/// <summary>
/// Applies the System / Light / Dark preference instantly by setting
/// <see cref="FrameworkElement.RequestedTheme"/> on the window's root element,
/// and keeps the caption buttons (minimise/close) readable in either theme.
/// </summary>
internal sealed class ThemeService : IDisposable
{
    private readonly MainWindow _window;
    private readonly FrameworkElement _root;

    public ThemeService(MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _root = window.ThemeRoot;

        // Follows Windows' own light/dark switch while "System" is selected.
        _root.ActualThemeChanged += OnActualThemeChanged;
    }

    public void Apply(ThemePreference preference)
    {
        _root.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        UpdateCaptionButtons();
    }

    public void Dispose() => _root.ActualThemeChanged -= OnActualThemeChanged;

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateCaptionButtons();

    private void UpdateCaptionButtons()
    {
        var titleBar = _window.AppWindow.TitleBar;
        var dark = _root.ActualTheme == ElementTheme.Dark;

        Color foreground = dark ? ColorHelper.FromArgb(0xFF, 0xF3, 0xEC, 0xE4) : ColorHelper.FromArgb(0xFF, 0x3B, 0x30, 0x2A);
        Color inactive = dark ? ColorHelper.FromArgb(0xFF, 0x8F, 0x84, 0x7A) : ColorHelper.FromArgb(0xFF, 0x9A, 0x8C, 0x80);
        Color hover = dark ? ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x14, 0x3B, 0x30, 0x2A);
        Color pressed = dark ? ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x0C, 0x3B, 0x30, 0x2A);

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
