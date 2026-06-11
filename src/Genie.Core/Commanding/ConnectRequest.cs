namespace Genie.Core.Commanding;

/// <summary>
/// A parsed <c>#connect</c> / <c>#reconnect</c> / <c>#lichconnect</c> request,
/// forwarded from <see cref="CommandEngine"/> through <see cref="ICommandHost.Connect"/>
/// to the App layer (which owns the connection lifecycle, profiles, and dialogs).
///
/// <para>
/// <see cref="Args"/> are the tokens AFTER the verb, already <c>$variable</c>-expanded
/// by <see cref="CommandEngine.ProcessInput"/>. Genie 4 grammar
/// (<c>_refs/Genie4/Core/Command.cs</c> Connect()):
/// <list type="bullet">
///   <item><b>0 args</b> → reconnect the last session.</item>
///   <item><b>1 arg</b> → connect a saved profile looked up by name.</item>
///   <item><b>4 args</b> → explicit <c>account password character game</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="IsLich"/> is set by the <c>#lichconnect</c> verb — the same forms,
/// but routed through a running Lich proxy (maps to
/// <see cref="Connection.ConnectionMode.LichProxy"/>).
/// </para>
/// </summary>
public sealed record ConnectRequest(IReadOnlyList<string> Args, bool IsLich);
