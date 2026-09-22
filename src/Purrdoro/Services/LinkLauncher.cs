using System.Diagnostics;
using Purrdoro.Core.Services;

namespace Purrdoro.Services;

/// <summary>Opens links in the user's default browser (no embedded browser).</summary>
internal sealed class LinkLauncher : ILinkLauncher
{
    public async Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            if (await Windows.System.Launcher.LaunchUriAsync(uri))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Purrdoro] LaunchUriAsync failed: {ex.Message}");
        }

        // Fallback for unusual shell configurations.
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Purrdoro] Could not open {uri}: {ex.Message}");
            return false;
        }
    }
}
