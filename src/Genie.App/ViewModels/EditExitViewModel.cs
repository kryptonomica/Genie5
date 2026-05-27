using System.Collections.ObjectModel;
using System.Reactive;
using Genie.Core.Mapper;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Editable wrapper around a single <see cref="MapExit"/>. Backs the
/// Edit Exit dialog. Save serializes the structured fields back into
/// the <see cref="MapExit.Requires"/> string + the new RT/wait/notes
/// fields, then the caller persists the zone XML.
///
/// <para>
/// The dialog walks the user through:
/// (1) inspecting the from-room + verb (read-only),
/// (2) editing skill requirements as a list of (skill, min-rank) rows,
/// (3) class restriction + min level,
/// (4) RT cost + wait window for boats / scheduled transit,
/// (5) free-form notes for prerequisites the engine can't model.
/// </para>
/// </summary>
public sealed class EditExitViewModel : ReactiveObject
{
    private readonly MapExit _exit;

    // ── Read-only context fields shown at the top of the dialog ──────────

    public string FromRoomTitle { get; }
    public string ExitVerb      { get; }
    public string ToRoomTitle   { get; }

    // ── Editable fields ──────────────────────────────────────────────────

    /// <summary>One row per skill requirement (Climbing → 50).</summary>
    public ObservableCollection<SkillRequirementRow> SkillRequirements { get; }
        = new();

    [Reactive] public string RequiredClass { get; set; } = "";
    [Reactive] public int?   MinLevel      { get; set; }
    [Reactive] public int?   RtCost        { get; set; }
    [Reactive] public int?   WaitMin       { get; set; }
    [Reactive] public int?   WaitMax       { get; set; }
    [Reactive] public string Notes         { get; set; } = "";

    // ── Commands ──────────────────────────────────────────────────────────

    public ReactiveCommand<Unit, bool>  OkCommand     { get; }
    public ReactiveCommand<Unit, bool>  CancelCommand { get; }
    public ReactiveCommand<Unit, Unit>  AddSkillCommand { get; }
    public ReactiveCommand<SkillRequirementRow, Unit> RemoveSkillCommand { get; }

    public EditExitViewModel(MapExit exit, string fromRoomTitle, string toRoomTitle)
    {
        _exit         = exit;
        FromRoomTitle = fromRoomTitle;
        ToRoomTitle   = toRoomTitle;
        ExitVerb      = !string.IsNullOrEmpty(exit.MoveCommand)
            ? exit.MoveCommand
            : exit.Direction.ToString().ToLowerInvariant();

        // Parse the existing Requires string into structured rows. Future
        // edits flow back through Commit() into a fresh Requires string,
        // so we don't accumulate stale free-form text across edits.
        var req = ExitRequirement.Parse(exit.Requires);
        foreach (var (skill, min) in req.MinRanks)
            SkillRequirements.Add(new SkillRequirementRow { Skill = skill, MinRank = min });
        RequiredClass = req.RequiredClass ?? "";
        MinLevel      = req.MinLevel;
        RtCost        = exit.RtCost;
        WaitMin       = exit.WaitMin;
        WaitMax       = exit.WaitMax;
        Notes         = exit.Notes;

        AddSkillCommand    = ReactiveCommand.Create(() =>
            SkillRequirements.Add(new SkillRequirementRow { Skill = "", MinRank = 0 }));
        RemoveSkillCommand = ReactiveCommand.Create<SkillRequirementRow>(row =>
            SkillRequirements.Remove(row));

        OkCommand     = ReactiveCommand.Create(Commit);
        CancelCommand = ReactiveCommand.Create(() => false);
    }

    /// <summary>
    /// Push edits back into the wrapped <see cref="MapExit"/>. Returns
    /// true so the dialog closes with "ok" — the caller persists the
    /// zone XML on a true result.
    /// </summary>
    private bool Commit()
    {
        // Rebuild the Requires string from the structured rows. Empty
        // skills are skipped. Pieces are comma-separated; this round-trips
        // through ExitRequirement.Parse cleanly.
        var pieces = new List<string>();
        foreach (var row in SkillRequirements)
        {
            if (string.IsNullOrWhiteSpace(row.Skill) || row.MinRank <= 0) continue;
            pieces.Add($"{row.Skill.Trim()}>={row.MinRank}");
        }
        if (!string.IsNullOrWhiteSpace(RequiredClass))
            pieces.Add($"class={RequiredClass.Trim()}");
        if (MinLevel.HasValue && MinLevel.Value > 0)
            pieces.Add($"level>={MinLevel.Value}");

        _exit.Requires = string.Join(", ", pieces);
        _exit.RtCost   = RtCost;
        _exit.WaitMin  = WaitMin;
        _exit.WaitMax  = WaitMax;
        _exit.Notes    = (Notes ?? "").Trim();

        return true;
    }
}

/// <summary>One row in the SkillRequirements grid: pair of skill name + min rank.</summary>
public sealed class SkillRequirementRow : ReactiveObject
{
    [Reactive] public string Skill   { get; set; } = "";
    [Reactive] public int    MinRank { get; set; }
}
