namespace Genie.Core.Layout;

public sealed class WindowSettings
{
    public string  Id           { get; init; } = "";
    public string  DefaultTitle { get; init; } = "";
    public string  DisplayTitle { get; set; } = "";
    public string  FontFamily   { get; set; } = "Cascadia Mono,Consolas,Courier New,monospace";
    public double  FontSize     { get; set; } = 13;
    public string  Foreground   { get; set; } = "Default";
    public string  Background   { get; set; } = "";
    public bool    Timestamp    { get; set; } = false;
    public string? IfClosed     { get; set; }
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
