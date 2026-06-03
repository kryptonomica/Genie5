using Avalonia.Input;

namespace Genie.App.Controls;

/// <summary>
/// Shared helper for converting an Avalonia <see cref="Key"/> + <see cref="KeyModifiers"/>
/// pair into Genie 4's macro key vocabulary, and vice-versa where needed.
///
/// Key string examples:
/// <list type="bullet">
/// <item><c>F1</c> .. <c>F12</c> — function keys (firing macros from these
///   does not require any modifier).</item>
/// <item><c>ctrl+h</c>, <c>alt+x</c>, <c>ctrl+shift+a</c> — letter keys with
///   at least one of Ctrl/Alt. Shift alone is treated as ordinary typing.</item>
/// <item><c>ctrl+1</c>, <c>alt+0</c> — number-row digits with modifier.</item>
/// <item><c>ctrl+num5</c>, <c>alt+num0</c> — numpad digits with modifier.</item>
/// </list>
/// Returns <c>null</c> for keystrokes that should never fire a macro or be
/// captured into a macro-key field — plain letters, plain digits, navigation
/// keys without modifiers, Tab/Enter, modifier keys themselves.
/// </summary>
public static class MacroKeyConverter
{
    public static string? ToMacroKey(Key key, KeyModifiers mods)
    {
        // Modifier keys alone are not macros.
        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or
                   Key.LWin or Key.RWin) return null;

        // Function keys fire regardless of modifier (F1, ctrl+F2, alt+shift+F5, …).
        if (key >= Key.F1 && key <= Key.F12)
            return BuildKeyName(key.ToString().ToLowerInvariant(), mods);

        // Letters only with Ctrl or Alt — Shift alone is normal typing.
        if (key >= Key.A && key <= Key.Z &&
            (mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Alt)))
            return BuildKeyName(key.ToString().ToLowerInvariant(), mods);

        // Number-row digits with Ctrl or Alt.
        if (key >= Key.D0 && key <= Key.D9 &&
            (mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Alt)))
            return BuildKeyName(key.ToString()[1..], mods);   // strip "D" prefix

        // Numpad digits fire with OR without a modifier — Genie 4 parity: the
        // numpad is the classic movement pad (num8 → north, etc.). A plain
        // numpad press only ever gets swallowed when a macro is actually bound
        // to it (see MainWindow.OnGlobalKeyDown), so unbound numpad keys still
        // type normally. (Requires NumLock on, so the OS reports NumPadN
        // rather than the navigation keys.)
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return BuildKeyName("num" + key.ToString()[6..], mods);

        return null;
    }

    private static string BuildKeyName(string keyName, KeyModifiers mods)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("ctrl");
        if (mods.HasFlag(KeyModifiers.Alt))     parts.Add("alt");
        if (mods.HasFlag(KeyModifiers.Shift))   parts.Add("shift");
        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
