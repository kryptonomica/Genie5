using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs a <b>plugin-created</b> dock panel. Unlike <see cref="ExperienceViewModel"/>
/// (which is hard-wired to the "Experience" window), this VM is generic: one
/// instance per distinct window name a plugin writes to via
/// <c>IPluginHost.SetWindow(name, …)</c> / <c>EchoToWindow(name, …)</c>. The
/// host's dock factory creates a panel on demand and binds it here, so any
/// plugin can surface its own window without the App knowing about it in advance.
///
/// <para>This is the generalisation of the named-window seam: the App no longer
/// needs a bespoke ViewModel + Tool + DataTemplate per plugin window — one
/// generic trio serves them all, keyed by name.</para>
/// </summary>
public class PluginWindowViewModel : ReactiveObject
{
    public PluginWindowViewModel(string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Plugin" : title;
    }

    /// <summary>Display title — drives the dock tab caption. A plugin may rename
    /// its window by writing under a new name (a new VM) or by the factory
    /// updating this when it reuses the VM.</summary>
    [Reactive] public string Title { get; set; }

    /// <summary>The full rendered contents of the panel. Snapshot-style: a
    /// <see cref="SetContent"/> replaces it wholesale (matching how the
    /// Experience plugin re-renders), while <see cref="AppendLine"/> supports the
    /// append/log style used by <c>EchoToWindow</c> / <c>#echo &gt;Name</c>.</summary>
    [Reactive] public string Content { get; private set; } = "";

    /// <summary>Replace the whole panel (plugin SetWindow semantics).</summary>
    public void SetContent(string content) => Content = content ?? "";

    /// <summary>Append a line (EchoToWindow semantics). Keeps the panel growing
    /// like a log.</summary>
    public void AppendLine(string text)
    {
        text ??= "";
        Content = Content.Length == 0 ? text : Content + "\n" + text;
    }

    /// <summary>Empty the panel (used when the owning plugin is disabled).</summary>
    public void Clear() => Content = "";
}
