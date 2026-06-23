using Genie.Core.Config;
using Genie.Core.Events;
using Genie.Core.Models;
using Microsoft.Extensions.Logging;

namespace Genie.Core.GameState;

/// <summary>
/// Subscribes to the <see cref="Parser.DrXmlParser"/> event stream and updates
/// the live <see cref="Models.GameState"/> singleton.
///
/// This is the single source of truth for "what is the character doing right now."
/// The AI context buffer reads from here; scripts read from here; the UI binds to here.
/// </summary>
public sealed class GameStateEngine : IDisposable
{
    private readonly Models.GameState        _state;
    private readonly ILogger<GameStateEngine> _log;
    private readonly IDisposable             _subscription;

    public Models.GameState State => _state;

    /// <summary>
    /// Optional live config, wired by <c>GenieCore</c>. Supplies
    /// <see cref="GenieConfig.RoundTimeOffset"/> — a client-side timing margin
    /// added to each roundtime so scripts (and the RT display) treat RT as
    /// lasting a little longer, guarding against server/network lag. Read live,
    /// so a <c>#config roundtimeoffset</c> change applies to the next RT.
    /// </summary>
    public GenieConfig? Config { get; set; }

    public GameStateEngine(
        IObservable<GameEvent> gameEvents,
        Models.GameState       state,
        ILogger<GameStateEngine> log)
    {
        _state = state;
        _log   = log;
        _subscription = gameEvents.Subscribe(Apply);
    }

    private void Apply(GameEvent evt)
    {
        switch (evt)
        {
            // ── Vitals ────────────────────────────────────────────────────
            case ProgressBarEvent pb:
                ApplyProgressBar(pb);
                break;

            case ResourceEvent res:
                _log.LogTrace("Resource {Id}={Value}", res.ResourceId, res.Value);
                break;

            case RoomImageEvent img:
                // "0" = no artwork → store empty so consumers read "no image".
                _state.Room.ImageId = img.PictureId == "0" ? "" : img.PictureId;
                break;

            // ── Named components (room, status text strings) ──────────────
            case ComponentEvent comp:
                _state.Components[comp.ComponentId] = comp.Content;
                ApplyComponent(comp);
                break;

            // ── Round / cast time ─────────────────────────────────────────
            case RoundTimeEvent rt:
                // RoundTimeOffset (Genie 4 parity): extend the RT end by the
                // configured seconds so RT-gating waits a safety margin past the
                // server's stated end. Default 0 → no change.
                var rtOffset = Config?.RoundTimeOffset ?? 0;
                _state.Combat.RoundTimeEnd =
                    rtOffset != 0 ? rt.ExpiresAt.AddSeconds(rtOffset) : rt.ExpiresAt;
                break;

            case CastTimeEvent ct:
                _state.Combat.CastTimeEnd = ct.ExpiresAt;
                break;

            // ── Indicators ────────────────────────────────────────────────
            case IndicatorEvent ind:
                ApplyIndicator(ind);
                break;

            // ── Held items ────────────────────────────────────────────────
            case HeldItemEvent held:
                if (held.Hand == Hand.Left)
                {
                    _state.Inventory.LeftHand    = held.Noun;
                    _state.Inventory.LeftExistId = held.ExistId;
                }
                else
                {
                    _state.Inventory.RightHand    = held.Noun;
                    _state.Inventory.RightExistId = held.ExistId;
                }
                break;

            // ── Spell ─────────────────────────────────────────────────────
            case SpellEvent spell:
            {
                var newName = spell.SpellName ?? string.Empty;
                var isNone  = newName.Trim().Length == 0
                              || newName.Equals("None", StringComparison.OrdinalIgnoreCase);
                var changed = !string.Equals(newName, _state.Combat.PreparedSpell,
                                             StringComparison.OrdinalIgnoreCase);
                _state.Combat.PreparedSpell = newName;
                // Stamp the prep-time start on a new/changed spell (Genie 4
                // SetSpellTime); clear it when nothing is prepared. A duplicate
                // refresh of the same spell keeps the original start.
                if (isNone)
                    _state.Combat.SpellTimeStart = null;
                else if (changed || _state.Combat.SpellTimeStart is null)
                    _state.Combat.SpellTimeStart = DateTimeOffset.UtcNow;
                break;
            }

            // ── Navigation ────────────────────────────────────────────────
            case NavEvent nav:
                if (int.TryParse(nav.RoomId, out var rid))
                    _state.Room.RoomId = rid;
                break;

            // ── Guild (from `info` verb) ──────────────────────────────────
            case GuildEvent guild:
                _state.GuildName = guild.Guild;
                _state.Guild     = MapGuild(guild.Guild);
                break;

            // ── Compass exits ──────────────────────────────────────────────
            case CompassEvent compass:
                _state.Room.CompassExits = compass.RawXml;
                break;

            // ── Prompt ────────────────────────────────────────────────────
            case PromptEvent prompt:
                _state.LastPrompt = prompt.ServerTime;
                break;

            // ── Unknown tags → logged for AI training analysis ────────────
            case UnknownTagEvent unk:
                _log.LogDebug("UnknownTag [{Name}]: {Raw}", unk.TagName, unk.RawXml);
                break;
        }
    }

    // ── ProgressBar → Vitals ─────────────────────────────────────────────────

    private void ApplyProgressBar(ProgressBarEvent pb)
    {
        switch (pb.BarId.ToLowerInvariant())
        {
            case "health":        _state.Vitals.Health         = pb.Value; break;
            case "mana":          _state.Vitals.Mana           = pb.Value; break;
            case "spirit":        _state.Vitals.Spirit         = pb.Value; break;
            case "stamina":       _state.Vitals.StaminaFatigue = pb.Value; break;
            case "concentration": _state.Vitals.Concentration  = pb.Value; break;
            case "encumbrance":   _state.Vitals.Encumbrance    = pb.Value; break;
            default:
                _log.LogDebug("Unknown progressBar id: {Id}={Value}", pb.BarId, pb.Value);
                break;
        }
    }

    /// <summary>Map a raw guild display name (from the <c>info</c> verb) to the
    /// <see cref="DrGuild"/> enum used by skill-gated mapper logic. Un-guilded
    /// ("Commoner") and anything unrecognised map to <see cref="DrGuild.Unknown"/>.</summary>
    private static DrGuild MapGuild(string raw)
        => raw.Replace(" ", "").Replace("'", "").Trim().ToLowerInvariant() switch
        {
            "barbarian"   => DrGuild.Barbarian,
            "bard"        => DrGuild.Bard,
            "cleric"      => DrGuild.Cleric,
            "empath"      => DrGuild.Empath,
            "moonmage"    => DrGuild.MoonMage,
            "paladin"     => DrGuild.Paladin,
            "ranger"      => DrGuild.Ranger,
            "thief"       => DrGuild.Thief,
            "trader"      => DrGuild.Trader,
            "warriormage" => DrGuild.WarriorMage,
            "necromancer" => DrGuild.Necromancer,
            _             => DrGuild.Unknown,
        };

    // ── Component → Room state ────────────────────────────────────────────────

    private void ApplyComponent(ComponentEvent comp)
    {
        var idLower = comp.ComponentId.ToLowerInvariant();

        // ── exp <SkillName> — skill rank → SkillStore ──────────────────────
        // DR emits these whenever a skill ticks: the component ID is
        // `exp Climbing`, the content is "Climbing: 100 33% (3/34)".
        // We parse the rank int out of the content and feed SkillStore
        // for the AutoMapper's weighted pathfinder. Format documented in
        // test_results/naper_session_findings.md.
        if (idLower.StartsWith("exp ", StringComparison.Ordinal))
        {
            ParseAndStoreSkillRank(idLower.Substring(4), comp.Content);
            return;
        }

        switch (idLower)
        {
            case "room title":   _state.Room.Title       = comp.Content; break;
            case "room desc":    _state.Room.Description = comp.Content; break;
            case "room exits":   _state.Room.Exits       = comp.Content; break;
            case "room objs":
                _state.Room.Objects = comp.Content;
                // Monster count: filter the bold creature phrases through the
                // ignore list (Genie 4 default "appears dead|(dead)"). Recomputes
                // every room update — empty when nothing is bold.
                _state.Room.Creatures    = FilterCreatures(comp.BoldNames, Config?.IgnoreMonsterList);
                _state.Room.MonsterCount = _state.Room.Creatures.Count;
                break;
            case "room players": _state.Room.Players     = comp.Content; break;

            // Stance
            case "pc stance":
                _state.Combat.Stance = ParseStance(comp.Content);
                break;

            // Character identity (received on login)
            case "pc name":
                _state.CharacterName = comp.Content.Trim();
                break;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex SkillRankRegex =
        new(@"\b(?<rank>\d+)\s+\d+%", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Extract the integer rank from a skill-component content string and
    /// push it into <see cref="GameState.LiveSkills"/>. Resilient to
    /// formatting variations: looks for the first "NNN MM%" pair, where
    /// NNN is the rank and MM is the percentage toward next rank.
    /// </summary>
    private void ParseAndStoreSkillRank(string skillName, string content)
    {
        if (string.IsNullOrWhiteSpace(skillName) || string.IsNullOrWhiteSpace(content))
            return;

        var match = SkillRankRegex.Match(content);
        if (!match.Success) return;
        if (!int.TryParse(match.Groups["rank"].Value, out var rank)) return;

        // Skill names in DR's component IDs are lowercased ("exp climbing").
        // Title-case the first letter so the UI / pathfinder see
        // "Climbing" rather than "climbing" — minor cosmetic.
        var displayName = char.ToUpperInvariant(skillName[0]) + skillName.Substring(1);
        _state.LiveSkills.SetRank(displayName, rank);
    }

    // ── Indicator → Status flags ──────────────────────────────────────────────

    private void ApplyIndicator(IndicatorEvent ind)
    {
        var status = ind.IndicatorId.ToUpperInvariant() switch
        {
            "ICONKNEELING"  => CharacterStatus.Kneeling,
            "ICONPRONE"     => CharacterStatus.Prone,
            "ICONSITTING"   => CharacterStatus.Sitting,
            "ICONSTUNNED"   => CharacterStatus.Stunned,
            "ICONWEBBED"    => CharacterStatus.Webbed,
            "ICONBLEEDING"  => CharacterStatus.Bleeding,
            "ICONPOISONED"  => CharacterStatus.Poisoned,
            "ICONDISEASED"  => CharacterStatus.Diseased,
            "ICONHIDDEN"    => CharacterStatus.Hidden,
            "ICONINVISIBLE" => CharacterStatus.Invisible,
            "ICONJOINED"    => CharacterStatus.Joined,
            "ICONDEAD"      => CharacterStatus.Dead,
            _               => (CharacterStatus?)null
        };

        if (status is null) return;

        if (ind.Visible)
            _state.ActiveStatuses.Add(status.Value);
        else
            _state.ActiveStatuses.Remove(status.Value);
    }

    private static Stance ParseStance(string text) =>
        text.ToLowerInvariant() switch
        {
            "offensive" => Stance.Offensive,
            "advance"   => Stance.Advance,
            "forward"   => Stance.Forward,
            "neutral"   => Stance.Neutral,
            "guarded"   => Stance.Guarded,
            "defensive" => Stance.Defensive,
            _           => Stance.Unknown
        };

    /// <summary>
    /// Filter the room's bold creature phrases through the monster-count ignore
    /// list (a Genie 4 pipe-delimited regex, default "appears dead|(dead)"). A
    /// phrase is excluded when it matches. An empty list filters nothing; a
    /// malformed pattern is ignored (no exclusion) rather than throwing.
    /// </summary>
    private static IReadOnlyList<string> FilterCreatures(
        IReadOnlyList<string>? boldNames, string? ignoreList)
    {
        if (boldNames is null || boldNames.Count == 0) return System.Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(ignoreList)) return boldNames;

        System.Text.RegularExpressions.Regex? rx = null;
        try { rx = new System.Text.RegularExpressions.Regex(ignoreList,
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        catch { return boldNames; }   // bad pattern → don't drop anything

        var kept = new List<string>(boldNames.Count);
        foreach (var name in boldNames)
            if (!rx.IsMatch(name)) kept.Add(name);
        return kept;
    }

    public void Dispose() => _subscription.Dispose();
}
