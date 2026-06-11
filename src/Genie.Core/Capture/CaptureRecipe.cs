using System.Text.Json;
using System.Text.Json.Serialization;

namespace Genie.Core.Capture;

/// <summary>
/// A declarative "Capture for Analyst" recipe — the <c>.json</c> half of a
/// recipe pair. The matching <c>.cmd</c> (named by <see cref="Cmd"/>) holds the
/// actual steps and runs through the normal script engine, so it honours the
/// same type-ahead / roundtime gating as any user script.
///
/// <para>
/// This realises the "code prepares, user executes" model: tooling (or an AI assistant)
/// drops a <c>.cmd</c>+<c>.json</c> pair into the recipes directory; the App
/// lists it in the <b>Analyst ▸ Run Capture Recipe</b> menu; the user runs it
/// after a confirmation dialog. Nothing auto-runs — the run is the user pressing
/// a button, which keeps it on the right side of DR's "responsiveness, not
/// automation" line.
/// </para>
/// </summary>
public sealed record CaptureRecipe
{
    /// <summary>Display name shown in the menu and confirmation dialog.</summary>
    public required string Name { get; init; }

    /// <summary>One-line description of what the capture is for.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// What the user must do before running (e.g. "stand outdoors, above
    /// ground"). Surfaced prominently in the confirmation dialog.
    /// </summary>
    public string Precondition { get; init; } = "";

    /// <summary>
    /// The <c>.cmd</c> file (relative to the recipe's own directory) executed
    /// via the script engine when the recipe runs.
    /// </summary>
    public required string Cmd { get; init; }

    /// <summary>
    /// Human-facing list of the commands this recipe sends to the game, shown in
    /// the confirmation dialog so the user sees exactly what will run without
    /// opening the <c>.cmd</c>. Advisory only — the <c>.cmd</c> is the source of
    /// truth for execution.
    /// </summary>
    public IReadOnlyList<string> Sends { get; init; } = [];

    /// <summary>
    /// Streams to redact from the capture. Null/empty means "use the redactor
    /// default" (<see cref="CaptureRedactor.SocialStreams"/>). Set to an explicit
    /// list to tune per-recipe.
    /// </summary>
    public IReadOnlyList<string>? Redact { get; init; }

    /// <summary>Rough runtime in seconds, shown to set expectations.</summary>
    public int EstimatedSeconds { get; init; }

    /// <summary>Absolute path this recipe was loaded from (set by the loader).</summary>
    [JsonIgnore] public string? SourcePath { get; init; }

    /// <summary>Absolute path to the <see cref="Cmd"/> file, resolved against
    /// <see cref="SourcePath"/>'s directory. Null if <see cref="SourcePath"/> is unset.</summary>
    [JsonIgnore]
    public string? CmdPath =>
        SourcePath is null ? null : Path.Combine(Path.GetDirectoryName(SourcePath)!, Cmd);

    /// <summary>Build the redactor this recipe calls for.</summary>
    public CaptureRedactor BuildRedactor() =>
        new(Redact is { Count: > 0 } ? Redact : null);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Load a single recipe from a <c>.json</c> file. Returns null on
    /// read/parse failure or if required fields are missing.</summary>
    public static CaptureRecipe? Load(string jsonPath)
    {
        try
        {
            var recipe = JsonSerializer.Deserialize<CaptureRecipe>(File.ReadAllText(jsonPath), Json);
            return recipe is null ? null : recipe with { SourcePath = Path.GetFullPath(jsonPath) };
        }
        catch
        {
            return null; // malformed recipe — skip rather than crash the menu
        }
    }

    /// <summary>
    /// Load every <c>*.json</c> recipe in <paramref name="dir"/> whose companion
    /// <c>.cmd</c> exists, sorted by name. Missing directory → empty list.
    /// </summary>
    public static IReadOnlyList<CaptureRecipe> LoadAll(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        var list = new List<CaptureRecipe>();
        foreach (var json in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (Load(json) is { } recipe && recipe.CmdPath is { } cmd && File.Exists(cmd))
                list.Add(recipe);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
