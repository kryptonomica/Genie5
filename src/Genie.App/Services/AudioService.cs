using System.IO;
using System.Runtime.InteropServices;

namespace Genie.App.Services;

/// <summary>
/// Cross-platform short-sound playback for trigger/highlight SFX (and the
/// <c>#play</c> command). Plays an already-resolved absolute file path,
/// fire-and-forget — a missing file or unavailable backend is swallowed so a
/// bad sound name never disrupts the session.
///
/// <para>The PlaySounds gate and SoundDir/.wav resolution live in
/// <c>GenieCore.PlaySound</c>; this service is the dumb backend that just makes
/// noise on whatever OS we're on:</para>
/// <list type="bullet">
///   <item>Windows — <c>winmm.dll PlaySound</c> (async, native, no process
///         spawn or console flash; WAV only, matching Genie 4).</item>
///   <item>macOS — <c>afplay</c>.</item>
///   <item>Linux — <c>paplay</c> (PulseAudio), falling back to <c>aplay</c>.</item>
/// </list>
/// </summary>
public sealed class AudioService
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC     = 0x0001;
    private const uint SND_FILENAME  = 0x00020000;
    private const uint SND_NODEFAULT = 0x0002;

    /// <summary>Play the sound at <paramref name="fullPath"/> (absolute). No-op
    /// on a blank path or missing file. Never throws.</summary>
    public void Play(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        try
        {
            if (!File.Exists(fullPath))
            {
                Diagnostics.ErrorLog.Log("AudioService.Play", new FileNotFoundException(fullPath));
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                // SND_NODEFAULT: don't fall back to the system "ding" if the
                // file can't be played (e.g. not a WAV).
                PlaySound(fullPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Spawn("afplay", fullPath);
            }
            else // Linux / other Unix
            {
                if (!Spawn("paplay", fullPath))
                    Spawn("aplay", fullPath);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.ErrorLog.Log("AudioService.Play", ex);
        }
    }

    /// <summary>Stop any in-flight Windows playback (winmm). No-op elsewhere —
    /// the shell-out players are short and self-terminating.</summary>
    public void Stop()
    {
        if (OperatingSystem.IsWindows())
            try { PlaySound(null, IntPtr.Zero, 0); } catch { /* best-effort */ }
    }

    private static bool Spawn(string exe, string file)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, "\"" + file + "\"")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch
        {
            return false;   // backend not installed — let the caller try the next one
        }
    }
}
