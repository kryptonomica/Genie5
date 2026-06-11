using Genie.Core.Parsing;

namespace Genie.Core.Commanding;

/// <summary>
/// Masks the password in a <c>#connect</c> / <c>#lichconnect</c> command-bar line
/// for any display / history use, so an explicit
/// <c>#connect account password character game</c> (or a <c>$pw</c>-expanded
/// variant) never leaves a recoverable plaintext password in the command history.
///
/// <para>
/// Masks by <b>argument position</b> (the 2nd argument = the password) rather than
/// by matching a literal token, so it covers both an inline password and a
/// <c>$variable</c> reference. Lines that aren't a connect command with the full
/// 4-argument form (i.e. the reconnect and saved-profile forms, which carry no
/// secret) are returned unchanged.
/// </para>
///
/// <para>
/// Only the <b>display/history</b> copy is masked; the live command keeps its real
/// tokens, which flow only into <c>ConnectionConfig.AccountPassword</c> (never
/// echoed). The safe form for scripts is <c>#connect &lt;profile&gt;</c>, which has
/// no inline secret at all.
/// </para>
/// </summary>
public static class ConnectCommandMask
{
    public const string Masked = "********";

    public static string Mask(string commandLine, char commandChar = '#')
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return commandLine;

        var trimmed = commandLine.TrimStart();
        if (trimmed[0] != commandChar) return commandLine;

        var parts = ArgumentParser.ParseArgs(trimmed[1..]);
        // verb + 4 args. The reconnect (0-arg) and profile (1-arg) forms carry
        // no password, so there is nothing to mask.
        if (parts.Count < 5) return commandLine;

        var verb = parts[0].ToLowerInvariant();
        if (verb is not ("connect" or "lichconnect")) return commandLine;

        var masked = parts.ToArray();
        masked[2] = Masked;   // 0 = verb, 1 = account, 2 = password
        return commandChar + string.Join(" ", masked);
    }
}
