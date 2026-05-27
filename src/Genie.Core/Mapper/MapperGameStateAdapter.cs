using Genie.Core.Events;

namespace Genie.Core.Mapper;

/// <summary>
/// Adapts our <see cref="Models.GameState"/> + the parser's
/// <see cref="IObservable{GameEvent}"/> stream to the <see cref="IMapperGameState"/>
/// interface that <see cref="AutoMapperEngine"/> consumes.
///
/// Field mapping:
/// <list type="bullet">
///   <item><c>RoomTitle</c>        ← <c>GameState.Room.Title</c></item>
///   <item><c>RoomDescription</c>  ← <c>GameState.Room.Description</c></item>
///   <item><c>Exits</c>            ← <c>GameState.Room.CompassExits</c> split on whitespace</item>
///   <item><c>ServerRoomId</c>     ← <c>GameState.Room.RoomId</c> (int) converted to string;
///                                   empty when unknown</item>
/// </list>
/// <see cref="StateChanged"/> fires whenever any of the underlying game events
/// that touch the room arrives (<see cref="NavEvent"/>, <see cref="CompassEvent"/>,
/// or a "room title" / "room desc" <see cref="ComponentEvent"/>). The engine
/// internally rate-limits by tracking title + exit-set deltas, so over-firing
/// is harmless.
/// </summary>
public sealed class MapperGameStateAdapter : IMapperGameState, IDisposable
{
    private static readonly char[] WhitespaceSeparators = { ' ', '\t', '\r', '\n' };

    private readonly Models.GameState _state;
    private readonly IDisposable      _subscription;

    /// <summary>
    /// Room title, normalised for engine matching. DR wraps the live room
    /// name in square brackets — both in the text stream ("[Garden Rooftop,
    /// Medical Pavilion]") and as the content of the <c>&lt;component
    /// id="room name"&gt;</c> tag — but the Genie 4 XML maps store titles
    /// bracket-free (<c>name="Garden Rooftop, Medical Pavilion"</c>). Without
    /// stripping here, EVERY matching tier (server-id, fingerprint, desc,
    /// title+exits-overlap) fails because they all use
    /// <c>Title.Equals(...)</c>. Display surfaces still see the bracketed
    /// form via <see cref="Models.RoomState.Title"/>.
    /// </summary>
    public string RoomTitle
    {
        get
        {
            var t = _state.Room.Title;
            if (string.IsNullOrEmpty(t)) return string.Empty;
            if (t.Length >= 2 && t[0] == '[' && t[^1] == ']') return t[1..^1].Trim();
            return t.Trim();
        }
    }

    public string RoomDescription => _state.Room.Description;

    public IReadOnlyCollection<string> Exits =>
        string.IsNullOrWhiteSpace(_state.Room.CompassExits)
            ? Array.Empty<string>()
            : _state.Room.CompassExits.Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);

    public string ServerRoomId =>
        _state.Room.RoomId > 0 ? _state.Room.RoomId.ToString() : string.Empty;

    public event Action? StateChanged;

    public MapperGameStateAdapter(Models.GameState state, IObservable<GameEvent> events)
    {
        _state = state;

        // Fire StateChanged on every event that could have updated Room.*.
        // GameStateEngine writes the fields BEFORE the next event reaches our
        // subscriber, so by the time we read _state.Room here, the new value
        // is already present.
        _subscription = events.Subscribe(e =>
        {
            switch (e)
            {
                case NavEvent:
                case CompassEvent:
                    StateChanged?.Invoke();
                    break;
                case ComponentEvent ce
                    when ce.ComponentId.Equals("room title", StringComparison.OrdinalIgnoreCase)
                      || ce.ComponentId.Equals("room desc",  StringComparison.OrdinalIgnoreCase):
                    StateChanged?.Invoke();
                    break;
            }
        });
    }

    public void Dispose() => _subscription.Dispose();
}
