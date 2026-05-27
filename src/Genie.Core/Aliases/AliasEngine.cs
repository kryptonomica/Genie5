using Genie.Core.Classes;
using Genie.Core.Commanding;

namespace Genie.Core.Aliases;

public sealed class AliasEngine
{
    private readonly List<AliasRule>  _aliases = new();
    private readonly CommandEngine?   _commandEngine;

    /// <summary>
    /// Command engine is only used when an alias fires (<see cref="TryProcess"/>).
    /// Offline / draft instances for the Configuration dialog can pass null.
    /// </summary>
    public AliasEngine(CommandEngine? commandEngine = null) { _commandEngine = commandEngine; }

    /// <summary>
    /// Optional class-scope filter — set by <see cref="GenieCore"/> at startup.
    /// When non-null, aliases only fire if their <see cref="AliasRule.ClassName"/>
    /// is active. When null (e.g. offline draft instances in the Configuration
    /// dialog), every enabled alias fires regardless of class state.
    /// </summary>
    public ClassEngine? Classes { get; set; }

    public IReadOnlyList<AliasRule> Aliases => _aliases;

    public AliasRule AddAlias(string name, string expansion, bool isEnabled = true, string className = "default")
    { var a = new AliasRule(name, expansion, isEnabled, className); _aliases.Add(a); return a; }

    public bool RemoveAlias(string name) => _aliases.RemoveAll(a => a.Name == name) > 0;
    public void Clear() => _aliases.Clear();

    public bool SetEnabled(string name, bool enabled)
    {
        var alias = _aliases.FirstOrDefault(a => a.Name == name);
        if (alias == null) return false;
        alias.IsEnabled = enabled;
        return true;
    }

    public bool TryProcess(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var parts = input.Split(' ', 2);
        var alias = _aliases.FirstOrDefault(a =>
            a.IsEnabled
            && (Classes?.IsActive(a.ClassName) ?? true)
            && a.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (alias == null) return false;
        var args     = parts.Length > 1 ? parts[1] : string.Empty;
        var expanded = alias.Expansion.Replace("$*", args);
        _commandEngine?.ProcessInput(expanded);
        return true;
    }
}
