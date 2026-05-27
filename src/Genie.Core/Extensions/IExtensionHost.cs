namespace Genie.Core.Extensions;

public interface IExtensionHost
{
    IDictionary<string, string> Globals { get; }
    void Echo(string text);
    void SendCommand(string command);
}
