using System.Collections.Concurrent;

namespace Genie.Core.Models;

// ── DR domain enumerations (from Elanthipedia) ───────────────────────────────

public enum DrRace
{
    Human, Elf, Elothean, Dwarf, Gnome, Halfling, GorTog,
    Kaldar, Prydaen, Rakash, SKraMur, Unknown
}

public enum DrGuild
{
    Barbarian, Bard, Cleric, Empath, MoonMage, Paladin,
    Ranger, Thief, Trader, WarriorMage, Necromancer,
    Unknown
}

public enum Stance { Offensive, Advance, Forward, Neutral, Guarded, Defensive, Unknown }

public enum CharacterStatus
{
    Normal, Kneeling, Prone, Sitting, Stunned, Webbed,
    Bleeding, Poisoned, Diseased, Hidden, Dead
}

// ── Core character attributes (8 DR stats from Elanthipedia) ────────────────

public sealed class Attributes
{
    public int Strength    { get; set; }
    public int Reflex      { get; set; }
    public int Agility     { get; set; }
    public int Charisma    { get; set; }
    public int Discipline  { get; set; }
    public int Wisdom      { get; set; }
    public int Intelligence{ get; set; }
    public int Stamina     { get; set; }
}

// ── Vitals (percentage bars from progressBar XML) ────────────────────────────

public sealed class Vitals
{
    public int Health         { get; set; } = 100;  // 0–100
    public int Mana           { get; set; } = 100;
    public int Spirit         { get; set; } = 100;
    public int StaminaFatigue { get; set; } = 100;  // "stamina" bar
    public int Concentration  { get; set; } = 100;  // spell prep bar
    public int Encumbrance    { get; set; } = 0;    // 0 = unencumbered
}

// ── Skill rank record ────────────────────────────────────────────────────────

public sealed class SkillRank
{
    public string Name      { get; set; } = "";
    public int    Rank      { get; set; }
    public int    LearningPct { get; set; }  // 0–34 (DR learning states)
}

// ── Room state ───────────────────────────────────────────────────────────────

public sealed class RoomState
{
    public string   Title       { get; set; } = "";
    public string   Description { get; set; } = "";
    public string   Exits       { get; set; } = "";
    public string   Objects     { get; set; } = "";
    public string   Players     { get; set; } = "";
    /// <summary>Mapper room ID (from GenieClient/Maps data or &lt;nav rm="..."/&gt;).</summary>
    public int      RoomId      { get; set; }
    /// <summary>Space-separated exit directions from &lt;compass&gt; (e.g. "north south east").</summary>
    public string   CompassExits { get; set; } = "";
}

// ── Combat state ─────────────────────────────────────────────────────────────

public sealed class CombatState
{
    public string           Target      { get; set; } = "";
    public DateTimeOffset   RoundTimeEnd{ get; set; }
    public DateTimeOffset   CastTimeEnd { get; set; }
    public string           PreparedSpell { get; set; } = "";
    public Stance           Stance      { get; set; } = Stance.Neutral;
    public bool             InRoundTime => DateTimeOffset.UtcNow < RoundTimeEnd;
    public bool             InCastTime  => DateTimeOffset.UtcNow < CastTimeEnd;
    public double           RoundTimeRemaining =>
        Math.Max(0, (RoundTimeEnd - DateTimeOffset.UtcNow).TotalSeconds);
}

// ── Inventory ────────────────────────────────────────────────────────────────

public sealed class InventoryState
{
    public string LeftHand  { get; set; } = "";
    public string RightHand { get; set; } = "";
    public string LeftExistId  { get; set; } = "";
    public string RightExistId { get; set; } = "";
}

// ── Full character/world snapshot ────────────────────────────────────────────

/// <summary>
/// The canonical live game state.  Updated by <see cref="GameStateEngine"/> as
/// parsed events arrive.  Thread-safe for reading from UI / AI threads.
/// </summary>
public sealed class GameState
{
    // Character identity
    public string    CharacterName { get; set; } = "";
    public DrRace    Race          { get; set; } = DrRace.Unknown;
    public DrGuild   Guild         { get; set; } = DrGuild.Unknown;
    public int       Circle        { get; set; }  // character circle (level equivalent)

    // Core DR stats
    public Attributes  Attributes { get; } = new();
    public Vitals      Vitals     { get; } = new();
    public SkillRank[] Skills     { get; set; } = [];

    /// <summary>
    /// Live skill ranks indexed by name — populated by the parser hook
    /// for &lt;component id='exp X'&gt; events. Used by the AutoMapper's
    /// weighted Dijkstra to filter exits the character can't take.
    /// Separate from <see cref="Skills"/> (which is a one-shot snapshot
    /// from `info`/`skills` parsing) — this is the live stream that
    /// updates per-skill as the engine sees rank changes.
    /// </summary>
    public Skills.SkillStore LiveSkills { get; } = new();

    // World
    public RoomState    Room      { get; } = new();
    public CombatState  Combat    { get; } = new();
    public InventoryState Inventory { get; } = new();

    // Status flags (from indicators)
    public HashSet<CharacterStatus> ActiveStatuses { get; } = [];

    // Misc UI state
    public string ActiveStream   { get; set; } = "main";
    public DateTimeOffset LastPrompt { get; set; }

    // Raw component text (all received component IDs → latest content)
    public ConcurrentDictionary<string, string> Components { get; } = new();
}
