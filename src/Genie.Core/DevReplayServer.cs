using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Genie.Core.Connection;

/// <summary>
/// Listens on a local TCP port and streams a recorded raw XML session file
/// to any connecting client — letting GameConnection run in DevReplay mode
/// against real game data with no live connection and no API cost.
///
/// Usage:
///   1. Record a session:  dotnet run -- DR ... (produces raw_session_*.xml)
///   2. Replay it:         dotnet run -- REPLAY raw_session_20260521_120000.xml
///
/// Speed control (ReplaySpeed in ConnectionConfig):
///   0.0 = as fast as possible (good for parser stress-testing)
///   1.0 = ~real-time (20 ms between 256-byte chunks, approximates network pacing)
///   2.0 = twice real-time
/// </summary>
public sealed class DevReplayServer : IAsyncDisposable
{
    private readonly string                   _filePath;
    private readonly double                   _speed;
    private readonly int                      _port;
    private readonly bool                     _hangAfterStream;
    private readonly ILogger<DevReplayServer> _log;
    private readonly TcpListener              _listener;
    private readonly CancellationTokenSource  _cts = new();
    private Task?                             _acceptLoop;

    public DevReplayServer(
        string                   filePath,
        int                      port   = 8000,
        double                   speed  = 0.0,
        bool                     hangAfterStream = false,
        ILogger<DevReplayServer>? log   = null)
    {
        _filePath        = filePath;
        _port            = port;
        _speed           = speed;
        _hangAfterStream = hangAfterStream;
        _log             = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DevReplayServer>.Instance;
        _listener = new TcpListener(IPAddress.Loopback, port);
        // Allow rapid rebind after the previous replay exits (avoids TIME_WAIT bind failures in dev loops).
        _listener.Server.ExclusiveAddressUse = false;
    }

    /// <summary>Starts listening. Returns immediately; serving happens on background tasks.</summary>
    public void Start()
    {
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Replay file not found: {_filePath}");

        var size = new FileInfo(_filePath).Length;
        _listener.Start();
        _log.LogInformation("DevReplay server listening on localhost:{Port} — file: {File} ({Size:N0} bytes)",
            _port, Path.GetFileName(_filePath), size);

        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    // ── Accept loop ──────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _log.LogInformation("DevReplay: client connected from {EP}", client.Client.RemoteEndPoint);
                _ = StreamFileAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "DevReplay accept loop error");
        }
    }

    // ── Stream one session to one client ─────────────────────────────────────

    private async Task StreamFileAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var networkStream = client.GetStream();
            try
            {
                await using var file = File.OpenRead(_filePath);
                var buf          = new byte[256];   // small chunks match real network packets
                long totalBytes  = 0;
                int  n;

                while ((n = await file.ReadAsync(buf, ct)) > 0)
                {
                    await networkStream.WriteAsync(buf.AsMemory(0, n), ct);
                    await networkStream.FlushAsync(ct);
                    totalBytes += n;

                    // Pace the stream so the parser sees natural chunk boundaries.
                    // At speed=1.0: ~50 chunks/sec  ≈  ~12 KB/sec, similar to a live game.
                    // At speed=0:   no delay, stream as fast as TCP allows.
                    if (_speed > 0)
                    {
                        var delayMs = (int)(20.0 / _speed);
                        if (delayMs > 0)
                            await Task.Delay(delayMs, ct);
                    }
                }

                _log.LogInformation("DevReplay: finished streaming {Bytes:N0} bytes", totalBytes);

                // Watchdog repro: hold the socket open but go completely silent
                // (no FIN, no further bytes) so the client's read blocks. This is
                // the half-open shape the server-activity watchdog exists to catch
                // — without it, a clean FIN would be detected by the 0-byte path
                // instead. The connection is closed when the server is disposed.
                if (_hangAfterStream)
                {
                    _log.LogInformation("DevReplay: holding connection open and silent (watchdog repro)");
                    try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
                    return;
                }

                // Shut down our send side first (sends FIN to client).
                // Then drain any game commands the client sent us before closing,
                // which prevents the OS sending RST due to unread data in our receive buffer.
                try
                {
                    client.Client.Shutdown(SocketShutdown.Send);
                    var drain = new byte[256];
                    while (await networkStream.ReadAsync(drain, ct) > 0) { }
                }
                catch { /* client already closed — fine */ }
                _log.LogInformation("DevReplay: clean close");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "DevReplay: error streaming to client");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop; } catch { }
        _cts.Dispose();
    }
}
