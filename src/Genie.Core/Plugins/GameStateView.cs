using Genie.Plugins;

namespace Genie.Core.Plugins;

/// <summary>
/// Read-only <see cref="IGameStateView"/> over the live
/// <see cref="Models.GameState"/>. A thin pass-through — it holds the same
/// instance the engine mutates, so reads are always current, but the interface
/// exposes no setters.
/// </summary>
internal sealed class GameStateView : IGameStateView
{
    private readonly Models.GameState _s;
    public GameStateView(Models.GameState s) => _s = s;

    public string CharacterName => _s.CharacterName;
    public string GuildName     => _s.GuildName;
    public int    Circle        => _s.Circle;

    public int Health        => _s.Vitals.Health;
    public int Mana          => _s.Vitals.Mana;
    public int Spirit        => _s.Vitals.Spirit;
    public int Stamina       => _s.Vitals.StaminaFatigue;
    public int Concentration => _s.Vitals.Concentration;

    public int    RoomId    => _s.Room.RoomId;
    public string RoomTitle => _s.Room.Title;

    public string LeftHand  => _s.Inventory.LeftHand;
    public string RightHand => _s.Inventory.RightHand;

    public IReadOnlyDictionary<string, int> SkillRanks => _s.LiveSkills.Snapshot();
}
