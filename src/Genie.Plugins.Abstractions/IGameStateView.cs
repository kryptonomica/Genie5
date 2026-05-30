namespace Genie.Plugins;

/// <summary>
/// Read-only projection of the host's live game state handed to plugins.
/// Deliberately a narrow, immutable-by-contract view — plugins read game state
/// but cannot mutate it, preserving the host's one-way data flow
/// (TCP → parser → state → UI). Extend this surface as plugins prove they need
/// more; never expose the mutable game-state object directly.
/// </summary>
public interface IGameStateView
{
    string CharacterName { get; }
    string GuildName     { get; }   // raw display name, e.g. "Moon Mage" / "Commoner"
    int    Circle        { get; }

    // Vitals (0–100 percentages)
    int Health        { get; }
    int Mana          { get; }
    int Spirit        { get; }
    int Stamina       { get; }
    int Concentration { get; }

    // Room
    int    RoomId    { get; }   // server room id, 0 if unknown
    string RoomTitle { get; }

    // Hands
    string LeftHand  { get; }
    string RightHand { get; }

    /// <summary>Snapshot of known skill ranks (skill name → rank). Empty until
    /// the character's exp data has been seen this session.</summary>
    IReadOnlyDictionary<string, int> SkillRanks { get; }
}
