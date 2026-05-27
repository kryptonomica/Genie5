namespace Genie.Core.Variables;

public sealed class VariableStore
{
    private readonly Dictionary<string, VariableValue> _variables = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, VariableScope scope = VariableScope.User)
    {
        if (_variables.ContainsKey(name))
            _variables[name].Value = value;
        else
            _variables[name] = new VariableValue(name, value, scope);
    }

    public string? Get(string name)
        => _variables.TryGetValue(name, out var v) ? v.Value : null;

    public bool Remove(string name) => _variables.Remove(name);

    public void ClearUserVariables()
    {
        var keys = _variables.Where(kv => kv.Value.Scope == VariableScope.User)
                             .Select(kv => kv.Key).ToList();
        foreach (var k in keys) _variables.Remove(k);
    }

    public IReadOnlyDictionary<string, VariableValue> GetAll() => _variables;

    public string Expand(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = input;
        foreach (var kvp in _variables)
            result = result.Replace("$" + kvp.Key, kvp.Value.Value);
        return result;
    }
}
