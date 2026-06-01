using System;
using System.Reactive;
using ReactiveUI;

namespace Genie.App.ViewModels;

/// <summary>
/// One entry in the Window → Plugin Windows submenu: a plugin-created window's
/// title plus a checkbox that shows/hides its dock panel. Rebuilt each time the
/// Window menu opens, so the list and check states reflect live dock state.
/// </summary>
public sealed class PluginWindowMenuItem : ReactiveObject
{
    public string Title { get; }

    /// <summary>Whether the panel is currently in the dock tree. Snapshot at
    /// build time (the menu rebuilds on open, so it stays fresh).</summary>
    public bool IsVisible { get; }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public PluginWindowMenuItem(string title, bool isVisible, Action toggle)
    {
        Title         = title;
        IsVisible     = isVisible;
        ToggleCommand = ReactiveCommand.Create(toggle);
    }
}
