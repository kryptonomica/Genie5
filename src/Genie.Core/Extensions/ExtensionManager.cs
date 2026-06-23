namespace Genie.Core.Extensions;

public sealed class ExtensionManager
{
    private readonly List<IGameExtension> _extensions = new();
    private readonly IExtensionHost       _host;

    public ExtensionManager(IExtensionHost host) { _host = host; }
    public IReadOnlyList<IGameExtension> Extensions => _extensions;

    public void Register(IGameExtension ext)
    {
        if (_extensions.Any(e => e.Name.Equals(ext.Name, StringComparison.OrdinalIgnoreCase))) return;
        _extensions.Add(ext);
        try { ext.Initialize(_host); }
        catch (Exception ex) { _host.Echo($"[ext] {ext.Name}: init failed — {ex.Message}"); }
    }

    public void Shutdown()
    {
        foreach (var e in _extensions) { try { e.Shutdown(); } catch { } }
        _extensions.Clear();
    }

    public void DispatchGameLine(string line)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i]; if (!e.Enabled) continue;
            try { e.OnGameLine(line); } catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    public void DispatchCommand(string command)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i]; if (!e.Enabled) continue;
            try { e.OnCommandSent(command); } catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    public void DispatchPrompt()
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i]; if (!e.Enabled) continue;
            try { e.OnPrompt(); } catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    public void DispatchGameEvent(Genie.Core.Events.GameEvent ev)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i]; if (!e.Enabled) continue;
            try { e.OnGameEvent(ev); } catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    /// <summary>Per-character clean slate (character switch). Called regardless of
    /// Enabled — a disabled extension must still drop stale state so it doesn't
    /// reappear if re-enabled mid-session.</summary>
    public void DispatchReset()
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            try { _extensions[i].OnReset(); } catch (Exception ex) { _host.Echo($"[ext] {_extensions[i].Name}: {ex.Message}"); }
        }
    }

    /// <summary>Offer a typed <c>/command</c> to each extension in turn; the first to
    /// claim it (return true) swallows it. Returns true if any extension handled it.</summary>
    public bool DispatchSlashCommand(string input)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i]; if (!e.Enabled) continue;
            try { if (e.OnSlashCommand(input)) return true; }
            catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
        return false;
    }
}
