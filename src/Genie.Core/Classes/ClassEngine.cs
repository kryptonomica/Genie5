namespace Genie.Core.Classes;

public sealed class ClassEngine
{
    private readonly Dictionary<string, bool> _classes = new(StringComparer.OrdinalIgnoreCase);

    public ClassEngine() { _classes["default"] = true; }

    public IReadOnlyCollection<string> Names => _classes.Keys;

    /// <summary>
    /// Fires after any state-changing operation (Set / Remove / Clear /
    /// ActivateAll / DeactivateAll). Genie 4 raises an <c>EventClassChange</c>
    /// after #class commands so the UI and rule engines can refresh their
    /// class-gated state. We surface the same hook here.
    /// </summary>
    public event Action? Changed;

    public bool IsActive(string? className)
    {
        if (string.IsNullOrEmpty(className)) return true;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return true;
        return _classes.TryGetValue(className, out var v) && v;
    }

    public bool Ensure(string className, bool defaultActive = true)
    {
        if (string.IsNullOrEmpty(className)) return true;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return true;
        if (!_classes.TryGetValue(className, out var v))
        {
            _classes[className] = defaultActive;
            Changed?.Invoke();
            return defaultActive;
        }
        return v;
    }

    public void Set(string className, bool active)
    {
        if (string.IsNullOrEmpty(className)) return;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) { _classes["default"] = true; return; }
        _classes[className] = active;
        Changed?.Invoke();
    }

    public bool Remove(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return false;
        var removed = _classes.Remove(className);
        if (removed) Changed?.Invoke();
        return removed;
    }

    public void Clear()
    {
        _classes.Clear();
        _classes["default"] = true;
        Changed?.Invoke();
    }

    public void ActivateAll()
    {
        foreach (var k in _classes.Keys.ToList()) _classes[k] = true;
        Changed?.Invoke();
    }

    public void DeactivateAll()
    {
        foreach (var k in _classes.Keys.ToList())
            if (!k.Equals("default", StringComparison.OrdinalIgnoreCase)) _classes[k] = false;
        Changed?.Invoke();
    }

    public IReadOnlyDictionary<string, bool> GetAll() => _classes;
}
