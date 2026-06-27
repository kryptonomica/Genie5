using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Genie.App.Docking;

/// <summary>
/// Shared "Copy All" helper for the window right-click menu. Joins a window's
/// buffered lines and writes them to the system clipboard. Reaches the
/// clipboard through the desktop lifetime's MainWindow — the menu commands are
/// built in <see cref="GenieDockFactory"/>, which has no visual of its own to
/// resolve a TopLevel from (mirrors <c>GameTextViewModel.CopyAllToClipboardAsync</c>).
/// </summary>
internal static class WindowClipboard
{
    public static async Task CopyLinesAsync(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        var any = false;
        foreach (var line in lines)
        {
            sb.AppendLine(line);
            any = true;
        }
        if (!any) return;   // nothing to copy — leave the clipboard untouched

        var main = (Application.Current?.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (main?.Clipboard is { } cb)
            await cb.SetTextAsync(sb.ToString());
    }
}
