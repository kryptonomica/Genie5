namespace Genie.Core.Extensions;

public interface IGameExtension
{
    string Name        { get; }
    string Version     { get; }
    string Description { get; }
    bool   Enabled     { get; set; }
    void Initialize(IExtensionHost host);
    void OnGameLine(string line);
    void OnCommandSent(string command);
    void OnPrompt();
    void Shutdown();
}
