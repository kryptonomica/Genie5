namespace Genie.App.Settings;

/// <summary>
/// Disk-backed store for named <see cref="SavedLayout"/>s. Each layout
/// is one JSON file at <c>{LayoutsDir}/{Name}.json</c>; the dir is
/// usually <c>{AppData}/Genie5/Layouts/</c>.
///
/// <para>
/// Per-character vs global: layouts live at the global Genie5 root so
/// the same set of presets is available to every character — Genie 4
/// muscle memory. If we ever need per-character layouts (e.g. an
/// Empath wants different defaults from a Barbarian), point this at
/// the per-profile config dir instead.
/// </para>
/// </summary>
public sealed class LayoutStore
{
    private readonly string _dir;

    public LayoutStore(string layoutsDir)
    {
        _dir = layoutsDir;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>List all saved layouts (file basename without `.json`).
    /// Sorted alphabetically for stable menu order.</summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_dir, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p) ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Load a saved layout by name. Returns null if not found
    /// or the file is unreadable / malformed.</summary>
    public SavedLayout? Load(string name)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var layout = SavedLayout.FromJson(json);
            // Keep the file name authoritative in case the JSON's Name
            // drifted from disk reality (rename via filesystem).
            if (layout is not null) layout.Name = name;
            return layout;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Write the layout to disk, overwriting any existing file
    /// with the same name. The layout's <see cref="SavedLayout.Name"/>
    /// is sanitised to a filesystem-safe basename.</summary>
    public void Save(SavedLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Name))
            throw new ArgumentException("Layout name cannot be empty.", nameof(layout));

        // Refresh the timestamp so the file mtime + the JSON field stay
        // in sync — useful when sorting layouts by recency later.
        layout.SavedAt = DateTimeOffset.Now.ToString("O");

        var path = ResolvePath(layout.Name);
        File.WriteAllText(path, layout.ToJson());
    }

    /// <summary>Delete the named layout. No-op if it doesn't exist.</summary>
    public bool Delete(string name)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Returns true if a layout with this (sanitised) name already
    /// exists on disk — used by Save As to confirm overwrite.</summary>
    public bool Exists(string name) => File.Exists(ResolvePath(name));

    private string ResolvePath(string name)
    {
        var safe = Sanitize(name);
        return Path.Combine(_dir, safe + ".json");
    }

    /// <summary>Sanitise a user-supplied name to a filesystem-safe
    /// basename. Invalid characters get replaced with underscores;
    /// the result is trimmed. Empty results are NOT defaulted here —
    /// callers should validate before reaching this method.</summary>
    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}
