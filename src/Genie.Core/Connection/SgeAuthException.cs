namespace Genie.Core.Connection;

/// <summary>
/// A <b>non-transient</b> SGE login failure — bad account/password, a server
/// <c>PROBLEM</c> response (billing, already-logged-in, character-in-game,
/// unavailable), or an unparseable login reply. Distinct from network/timeout
/// errors: retrying won't help, so <see cref="GameConnection"/> surfaces the
/// reason and stops immediately instead of burning its reconnect budget on a
/// futile loop.
///
/// <para>
/// The message is already user-facing (e.g. "SGE login refused: Character is
/// currently in game.") — it flows straight to the connection's Error event and
/// the <c>#connect</c> handler's echo.
/// </para>
/// </summary>
public sealed class SgeAuthException : Exception
{
    public SgeAuthException(string message) : base(message) { }
}
