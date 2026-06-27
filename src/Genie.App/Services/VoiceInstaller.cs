using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Genie.App.Diagnostics;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Genie.App.Services;

/// <summary>
/// Downloads + installs sherpa-onnx Piper voice models on demand (the
/// <c>#tts install</c> command). The voice ships as a <c>.tar.bz2</c>; we stream
/// it to a temp file with percentage progress, then extract into
/// <c>&lt;TtsVoiceDir&gt;/{voiceId}/</c> via SharpCompress (.NET has no built-in
/// bzip2). All status flows through a caller-supplied reporter the caller
/// marshals to the UI thread.
///
/// <para>One install at a time (a re-entrancy guard); a voice already present is
/// a no-op unless <c>force</c> is set.</para>
/// </summary>
public sealed class VoiceInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private int _busy;   // 0/1 re-entrancy guard (Interlocked)

    public bool IsInstalling => Volatile.Read(ref _busy) == 1;

    /// <summary>Install <paramref name="voice"/> into <paramref name="voiceDir"/>.
    /// Returns true on success (or when already present). Never throws.</summary>
    public async Task<bool> InstallAsync(
        VoiceInfo voice, string voiceDir, Action<string> report, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            report("[tts] a voice install is already running.");
            return false;
        }

        try
        {
            string targetDir = Path.Combine(voiceDir, voice.Id);
            if (IsInstalled(targetDir))
            {
                report($"[tts] {voice.DisplayName} is already installed.");
                return true;
            }

            Directory.CreateDirectory(voiceDir);
            string tmp = Path.Combine(Path.GetTempPath(), $"genie_voice_{voice.Id}.tar.bz2");

            report($"[tts] downloading {voice.DisplayName} (~{voice.ApproxMb} MB)…");
            await DownloadAsync(voice.Url, tmp, report, ct);

            report("[tts] extracting…");
            Extract(tmp, voiceDir);
            try { File.Delete(tmp); } catch { /* temp cleanup best-effort */ }

            if (!IsInstalled(targetDir))
            {
                report("[tts] install finished but the voice files look incomplete.");
                return false;
            }

            report($"[tts] installed {voice.DisplayName}. Try: #speak hello");
            return true;
        }
        catch (OperationCanceledException)
        {
            report("[tts] install canceled.");
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.Log("VoiceInstaller.Install", ex);
            report($"[tts] install failed: {ex.Message}");
            return false;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static async Task DownloadAsync(string url, string dest, Action<string> report, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);

        var buffer = new byte[81920];
        long read = 0;
        int lastReported = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                int pct = (int)(read * 100 / total);
                if (pct >= lastReported + 20)   // report at ~20% steps
                {
                    lastReported = pct;
                    report($"[tts] downloading… {pct}%");
                }
            }
        }
    }

    private static void Extract(string archivePath, string destDir)
    {
        var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            reader.WriteEntryToDirectory(destDir, opts);
        }
    }

    /// <summary>A voice folder is "installed" once it holds the three pieces the
    /// engine needs.</summary>
    public static bool IsInstalled(string dir) =>
        Directory.Exists(dir) &&
        Directory.EnumerateFiles(dir, "*.onnx").Any() &&
        File.Exists(Path.Combine(dir, "tokens.txt")) &&
        Directory.Exists(Path.Combine(dir, "espeak-ng-data"));
}
