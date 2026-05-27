namespace Genie.Core.Events;

public enum ConnectionEventKind
{
    Connected,
    Disconnected,
    Reconnecting,
    Error
}

public sealed record ConnectionEvent(
    ConnectionEventKind Kind,
    int                 Attempt = 0,
    string?             Message = null);
