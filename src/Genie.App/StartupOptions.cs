using Genie.Core.Connection;

namespace Genie.App;

/// <summary>
/// Command-line launch options parsed from the process arguments. Lets an
/// external launcher (e.g. Lich Launcher) start Genie pre-pointed at a
/// connection without touching the Connect dialog:
/// <code>
///   genie.exe --host=127.0.0.1 --port=8001 --profile=TysongDRF
/// </code>
/// Resolution rules (applied in <c>MainWindowViewModel</c>):
/// <list type="bullet">
///   <item><c>--profile=NAME</c> loads a saved profile and supplies its
///   defaults (mode, host/port, account, character).</item>
///   <item><c>--host</c>/<c>--port</c> override whatever the profile (or the
///   defaults) provide.</item>
///   <item>When a host/port is given with no explicit <c>--mode</c> and no
///   profile, the connection is treated as <see cref="ConnectionMode.LichProxy"/>
///   — direct SGE needs a password, which is intentionally never accepted on
///   the command line.</item>
/// </list>
/// </summary>
public sealed class StartupOptions
{
    public string?         Host    { get; init; }
    public int?            Port    { get; init; }
    public string?         Profile { get; init; }
    public ConnectionMode? Mode    { get; init; }

    /// <summary>True when the user supplied anything that should drive an
    /// automatic connection at launch.</summary>
    public bool HasConnectIntent
        => !string.IsNullOrWhiteSpace(Host) || !string.IsNullOrWhiteSpace(Profile);

    /// <summary>
    /// Parse <c>--key=value</c> and <c>--key value</c> forms (case-insensitive
    /// keys, leading <c>--</c> or <c>-</c> or <c>/</c> accepted). Unknown flags
    /// are ignored so Avalonia / Velopack switches pass through harmlessly.
    /// </summary>
    public static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        string? host = null, profile = null, modeRaw = null;
        int?    port = null;

        if (args is not null)
        {
            for (var i = 0; i < args.Count; i++)
            {
                var (key, inlineValue) = SplitFlag(args[i]);
                if (key is null) continue;

                // Resolve the value: inline (--k=v) wins, else consume the next
                // token (--k v) when it isn't itself a flag.
                string? Value()
                {
                    if (inlineValue is not null) return inlineValue;
                    if (i + 1 < args.Count && SplitFlag(args[i + 1]).key is null)
                        return args[++i];
                    return null;
                }

                switch (key.ToLowerInvariant())
                {
                    case "host":    host    = Value(); break;
                    case "port":    if (int.TryParse(Value(), out var p)) port = p; break;
                    case "profile": profile = Value(); break;
                    case "mode":    modeRaw = Value(); break;
                }
            }
        }

        return new StartupOptions
        {
            Host    = string.IsNullOrWhiteSpace(host)    ? null : host!.Trim(),
            Port    = port,
            Profile = string.IsNullOrWhiteSpace(profile) ? null : profile!.Trim(),
            Mode    = ParseMode(modeRaw),
        };
    }

    /// <summary>Splits a token into (key, inlineValue). Returns (null, null)
    /// for non-flag tokens. <c>--host=x</c> → ("host","x"); <c>--host</c> →
    /// ("host", null); <c>foo</c> → (null, null).</summary>
    private static (string? key, string? value) SplitFlag(string token)
    {
        if (string.IsNullOrEmpty(token)) return (null, null);

        var t = token;
        if      (t.StartsWith("--")) t = t[2..];
        else if (t.StartsWith('-'))  t = t[1..];
        else if (t.StartsWith('/'))  t = t[1..];
        else return (null, null);   // bare value, not a flag

        if (t.Length == 0) return (null, null);

        var eq = t.IndexOf('=');
        return eq >= 0 ? (t[..eq], t[(eq + 1)..]) : (t, null);
    }

    private static ConnectionMode? ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "lich" or "lichproxy" or "proxy" => ConnectionMode.LichProxy,
            "sge"  or "direct" or "directsge" => ConnectionMode.DirectSGE,
            _ => null,
        };
    }
}
