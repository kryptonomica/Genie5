namespace Genie.Core.Aliases;

public sealed class AliasRule
{
    public AliasRule(string name, string expansion, bool isEnabled = true, string className = "default")
    { Name = name; Expansion = expansion; IsEnabled = isEnabled; ClassName = className; }
    public string Name      { get; }
    public string Expansion { get; }
    public bool   IsEnabled { get; set; }

    /// <summary>
    /// Class this alias belongs to (Genie 4 parity). The alias only fires
    /// when <see cref="ClassEngine.IsActive"/> returns true for this name.
    /// Default is <c>"default"</c>, which the engine always considers active
    /// — so aliases without an explicit class behave exactly as they did
    /// before the Classes-scope wiring was added.
    /// </summary>
    public string ClassName { get; set; } = "default";
}
