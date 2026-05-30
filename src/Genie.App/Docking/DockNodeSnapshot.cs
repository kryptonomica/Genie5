namespace Genie.App.Docking;

/// <summary>
/// Serializable snapshot of one node in the Dock.Avalonia layout tree.
/// Captures the container structure (proportional splits, tool/document
/// docks, splitters) plus the proportions and active-tab selections, so a
/// saved layout can round-trip the *arrangement* — not just which tools are
/// visible.
///
/// <para>Leaf tools/documents are stored by <see cref="Id"/> only (Kind
/// "leaf"). On rebuild the live instance is pulled from the factory's tool
/// registry, so each panel keeps its already-wired view-model rather than
/// being reconstructed. This is why we don't need the heavier
/// <c>Dock.Serializer</c> package + context relocation.</para>
/// </summary>
public sealed class DockNodeSnapshot
{
    /// <summary>"proportional" | "tooldock" | "documentdock" | "splitter" | "leaf".</summary>
    public string Kind { get; set; } = "";

    /// <summary>Dock/dockable id. For leaves this is the registry key used to
    /// resolve the live instance.</summary>
    public string? Id { get; set; }

    /// <summary>"Horizontal" | "Vertical" — proportional docks only.</summary>
    public string? Orientation { get; set; }

    /// <summary>"Left" | "Right" | "Top" | "Bottom" | "Unset" — tool/document docks only.</summary>
    public string? Alignment { get; set; }

    /// <summary>Split proportion (0..1) or NaN for auto.</summary>
    public double Proportion { get; set; } = double.NaN;

    /// <summary>Id of the active child dockable (selected tab), if any.</summary>
    public string? ActiveId { get; set; }

    public List<DockNodeSnapshot> Children { get; set; } = new();
}
