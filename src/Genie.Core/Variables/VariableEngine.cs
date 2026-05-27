using Genie.Core.Commanding;

namespace Genie.Core.Variables;

public sealed class VariableEngine
{
    private readonly VariableStore  _store = new();
    private readonly CommandEngine? _commandEngine;

    /// <summary>
    /// Command engine reference is currently retained only for future
    /// expansion (no method dereferences it yet). Nullable so the
    /// Configuration dialog can create draft instances offline.
    /// </summary>
    public VariableEngine(CommandEngine? commandEngine = null)
    {
        _commandEngine = commandEngine;
    }

    public VariableStore Store => _store;

    public bool TryProcess(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (input.StartsWith("#var ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Substring(5).Split(' ', 2);
            if (parts.Length == 2)
            {
                _store.Set(parts[0], parts[1]);
            }
            return true;
        }

        if (input.StartsWith("#unset ", StringComparison.OrdinalIgnoreCase))
        {
            var name = input.Substring(7).Trim();
            _store.Remove(name);
            return true;
        }

        return false;
    }

    public string Expand(string input)
    {
        return _store.Expand(input);
    }
}
