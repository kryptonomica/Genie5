using Genie.Core.Persistence;

namespace Genie.Core.Layout;

public sealed class WindowSettingsStore
{
    private readonly Dictionary<string, WindowSettings> _settings = new();
    public IReadOnlyDictionary<string, WindowSettings> All => _settings;

    public WindowSettings Get(string id) => _settings.TryGetValue(id, out var s) ? s : Fallback;

    private static readonly WindowSettings Fallback = new()
    {
        Id = "", DefaultTitle = "", DisplayTitle = "",
        FontFamily = "Cascadia Mono,Consolas,Courier New,monospace",
        FontSize = 13, Foreground = "Default", Background = "", Timestamp = false, IfClosed = null,
    };

    private static readonly Dictionary<string, string?> DefaultIfClosed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["activespells"] = "", ["arrivals"] = "", ["assess"] = null,
            ["atmospherics"] = "main", ["chatter"] = null, ["combat"] = "main",
            ["conversation"] = "log", ["deaths"] = "", ["debug"] = null,
            ["expmods"] = null, ["familiar"] = null, ["game"] = "",
            ["group"] = "", ["inv"] = null, ["inventory"] = null,
            ["itemlog"] = null, ["log"] = null, ["ooc"] = "",
            ["portrait"] = null, ["raw"] = "", ["room"] = null,
            ["talk"] = "conversation", ["thoughts"] = null, ["whispers"] = "conversation",
        };

    public WindowSettings Register(string id, string defaultTitle)
    {
        DefaultIfClosed.TryGetValue(id, out var defIfClosed);
        var s = new WindowSettings
        {
            Id = id, DefaultTitle = defaultTitle, DisplayTitle = defaultTitle,
            FontFamily = "Cascadia Mono,Consolas,Courier New,monospace",
            FontSize = 13, Foreground = "Default", Background = "",
            Timestamp = false, IfClosed = defIfClosed,
        };
        _settings[id] = s;
        return s;
    }

    public void Apply(WindowSettingsPersistenceModel m)
    {
        if (!_settings.TryGetValue(m.Id, out var s)) return;
        s.DisplayTitle = string.IsNullOrEmpty(m.DisplayTitle) ? s.DefaultTitle : m.DisplayTitle;
        s.FontFamily   = string.IsNullOrEmpty(m.FontFamily)   ? s.FontFamily   : m.FontFamily;
        s.FontSize     = m.FontSize > 0 ? m.FontSize : s.FontSize;
        s.Foreground   = string.IsNullOrEmpty(m.Foreground)   ? s.Foreground   : m.Foreground;
        s.Background   = m.Background;
        s.Timestamp    = m.Timestamp;
        if (m.HasIfClosed) s.IfClosed = m.IfClosed;
    }
}
