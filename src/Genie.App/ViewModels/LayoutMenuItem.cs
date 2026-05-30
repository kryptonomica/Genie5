using System.Windows.Input;

namespace Genie.App.ViewModels;

/// <summary>Where a saved layout lives.</summary>
public enum LayoutScope
{
    /// <summary>Per-connected-profile layouts ({Config}/Profiles/{guid}/Layouts).</summary>
    Profile,
    /// <summary>Global layouts shared across characters ({AppData}/Genie5/Layouts).</summary>
    Global,
}

/// <summary>
/// Per-entry view-model for the Layout → Load ▶ submenu. Carries the layout's
/// storage <see cref="Scope"/> so <c>LoadLayoutCommand</c> reads from the right
/// store, a <see cref="Display"/> label (global entries are suffixed
/// "(Global)" so duplicate names across scopes stay distinguishable), and a
/// pre-bound <see cref="ICommand"/> so the menu's container style can bind to
/// <c>{Binding Command}</c> directly (avoids the ancestor-cast that crashed
/// Avalonia — see MainWindow.axaml).
/// </summary>
public sealed record LayoutMenuItem(string Display, string Name, LayoutScope Scope, ICommand Command);

/// <summary>Input to the Save Layout dialog: the suggested name plus the
/// existing names in each scope (for the click-to-overwrite list) and whether
/// a per-profile target is even available (false ⇒ Global-only).</summary>
public sealed record LayoutSavePrompt(
    string DefaultName,
    bool ProfileAvailable,
    IReadOnlyList<string> ProfileNames,
    IReadOnlyList<string> GlobalNames);

/// <summary>Result of the Save Layout dialog: the chosen name + target scope.</summary>
public sealed record LayoutSaveResult(string Name, LayoutScope Scope);
