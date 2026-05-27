namespace Genie.Core.Mapper;

/// <summary>
/// Adapter interface between AutoMapperEngine and whatever game-state
/// implementation is in use (GslGameState in the reference impl,
/// GameState + GameStateEngine in our implementation).
/// </summary>
public interface IMapperGameState
{
    string RoomTitle       { get; }
    string RoomDescription { get; }
    IReadOnlyCollection<string> Exits { get; }
    string ServerRoomId    { get; }
    event Action? StateChanged;
}
