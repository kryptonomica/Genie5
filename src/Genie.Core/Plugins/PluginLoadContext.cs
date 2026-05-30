using System.Reflection;
using System.Runtime.Loader;

namespace Genie.Core.Plugins;

/// <summary>
/// Isolated, collectible load context for one plugin DLL. Each plugin gets its
/// own context so it can be unloaded/reloaded and so two plugins can't clash on
/// dependency versions.
///
/// <para><b>Critical:</b> the shared contract assembly
/// (<c>Genie.Plugins.Abstractions</c>) and the framework are deliberately NOT
/// loaded into this context — <see cref="Load"/> returns null for them so they
/// resolve from the host's default context. That makes the plugin's
/// <c>IGeniePlugin</c> the <i>same</i> type as the host's, so the
/// <c>is IGeniePlugin</c> check and casts actually succeed. Loading a private
/// copy here would create a distinct type identity and every cast would fail.</para>
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base(name: $"plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Unify the shared contract with the host (see class remarks). Returning
        // null lets the default context supply the already-loaded copy.
        if (assemblyName.Name == "Genie.Plugins.Abstractions")
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
