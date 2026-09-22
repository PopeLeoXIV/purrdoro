using System.Diagnostics;
using System.Runtime.InteropServices;
using Purrdoro.Core.Audio;
using Purrdoro.Core.Services;

namespace Purrdoro.Services;

/// <summary>
/// Plays Purrdoro's own synthesised chime (see <see cref="ChimeGenerator"/>)
/// with the Win32 <c>PlaySound</c> API. Playback is asynchronous, so the UI
/// thread is never blocked, and no audio files or packages are needed.
/// </summary>
internal sealed class SoundService : ISoundService, IDisposable
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    // PlaySound reads the buffer while playing asynchronously, so it must never
    // move: allocate it on the pinned object heap and keep it for the app's lifetime.
    private readonly byte[] _chime;
    private bool _disposed;

    public SoundService()
    {
        var wav = ChimeGenerator.CreateCompletionChimeWav();
        _chime = GC.AllocateUninitializedArray<byte>(wav.Length, pinned: true);
        wav.CopyTo(_chime, 0);
    }

    public void PlayCompletionSound()
    {
        if (_disposed)
        {
            return;
        }

        var pointer = Marshal.UnsafeAddrOfPinnedArrayElement(_chime, 0);
        if (!PlaySound(pointer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
        {
            Debug.WriteLine("[Purrdoro] PlaySound failed (no audio device?).");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop any sound that may still be reading the buffer.
        PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);
}
