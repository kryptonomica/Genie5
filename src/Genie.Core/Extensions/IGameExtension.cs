using Genie.Core.Events;

namespace Genie.Core.Extensions;

public interface IGameExtension
{
    string Name        { get; }
    string Version     { get; }
    string Description { get; }
    bool   Enabled     { get; set; }
    void Initialize(IExtensionHost host);
    void OnGameLine(string line);
    /// <summary>A fully-parsed, typed game event (e.g. <see cref="ComponentEvent"/>
    /// for <c>&lt;component id='exp X'&gt;</c>, or a <see cref="TextEvent"/>/
    /// <see cref="ClearStreamEvent"/> on the <c>percWindow</c> active-spells stream).
    /// Preferred over raw text/XML for structured data: the parser has already
    /// reassembled tags split across the connection's chunk boundaries. Most
    /// extensions ignore this.</summary>
    void OnGameEvent(GameEvent ev) { }
    void OnCommandSent(string command);
    void OnPrompt();
    void Shutdown();
    /// <summary>Per-character clean slate: called on a clear-then-load connect (a
    /// character SWITCH), before the next character's data arrives, so an extension
    /// can drop accumulated per-character state (skill tables, spell timers) instead
    /// of letting it bleed across characters. A same-character reconnect does NOT
    /// call this (its accumulated state should survive). Default no-op.</summary>
    void OnReset() { }
    /// <summary>Handle a console <c>/command</c> the user typed. Return true if this
    /// extension owns the command and it should be swallowed (not sent to the game).
    /// Default-style no-op extensions return false.</summary>
    bool OnSlashCommand(string input) => false;
}
