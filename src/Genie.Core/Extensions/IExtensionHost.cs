namespace Genie.Core.Extensions;

public interface IExtensionHost
{
    IDictionary<string, string> Globals { get; }
    void Echo(string text);
    void SendCommand(string command);

    /// <summary>Replace a named dock-panel's entire contents (snapshot-style — the
    /// tracker re-renders its whole list each update). The App surfaces unknown
    /// window names as dock panels; this is the same seam plugins used via
    /// <c>IPluginHost.SetWindow</c>, so the App needs no per-tracker wiring.</summary>
    void SetWindow(string window, string content);

    /// <summary>Absolute path to the active config directory (per-character profile
    /// when one is in effect), so an extension can load its own <c>*.cfg</c>.</summary>
    string ConfigDir { get; }

    /// <summary>Write to the diagnostic log (not the game window).</summary>
    void Log(string message);
}
