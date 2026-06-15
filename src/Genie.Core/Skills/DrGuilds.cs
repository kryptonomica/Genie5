namespace Genie.Core.Skills;

/// <summary>
/// The eleven DragonRealms player guilds. Backs the Edit Exit dialog's Guild
/// dropdown, which gates guild-restricted routes — Trader caravans, Thief
/// passages, Ranger trails, Moon Mage portals, and the like. Maps to the
/// <c>class=&lt;Guild&gt;</c> clause in <see cref="Genie.Core.Mapper.ExitRequirement"/>.
/// (Guildless characters are "Commoner" — represented as no restriction.)
/// </summary>
public static class DrGuilds
{
    /// <summary>All eleven guild names, alphabetical.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Barbarian", "Bard", "Cleric", "Empath", "Moon Mage", "Necromancer",
        "Paladin", "Ranger", "Thief", "Trader", "Warrior Mage",
    };
}
