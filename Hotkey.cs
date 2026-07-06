using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// Shared parsing and validation for hotkey settings, used by both the settings
/// dialog and the --set-hotkey CLI so the two can never disagree. A valid hotkey
/// is: at least one real modifier, plus exactly one non-modifier key that Windows
/// actually knows.
/// </summary>
public static class Hotkey
{
    /// <summary>
    /// Tries to interpret a combo string like "ctrl+alt+j" or "ctrl alt space".
    /// On success, returns normalized mods text ("ctrl+alt"), key text ("j"),
    /// and the Win32 modifier flags + virtual-key code ready for registration.
    /// </summary>
    public static bool TryParse(string combo, out string modsText, out string keyText,
                                out uint mods, out uint vk)
    {
        modsText = keyText = "";
        mods = 0; vk = 0;

        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.ToLowerInvariant()
            .Replace(" ", "+")
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2) return false; // need at least one modifier + one key

        var modList = new List<string>();
        string keyToken = parts[^1];

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "ctrl":
                case "control": Add(modList, "ctrl"); mods |= HotkeyManager.MOD_CONTROL; break;
                case "alt":     Add(modList, "alt");  mods |= HotkeyManager.MOD_ALT;     break;
                case "shift":   Add(modList, "shift");mods |= HotkeyManager.MOD_SHIFT;   break;
                case "win":     Add(modList, "win");  mods |= HotkeyManager.MOD_WIN;     break;
                default: return false; // unknown modifier token — reject rather than ignore
            }
        }

        if (modList.Count == 0) return false;
        if (!TryResolveKey(keyToken, out vk, out keyText)) return false;

        modsText = string.Join("+", modList);
        return true;
    }

    /// <summary>Resolves a single key token to a virtual-key code, rejecting invalid or modifier-only keys.</summary>
    public static bool TryResolveKey(string token, out uint vk, out string normalized)
    {
        vk = 0; normalized = "";
        token = token.Trim();
        if (token.Length == 0) return false;

        if (token.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            vk = (uint)Keys.Space; normalized = "space"; return true;
        }

        // Single printable letter/digit.
        if (token.Length == 1 && (char.IsLetterOrDigit(token[0])))
        {
            char c = char.ToUpperInvariant(token[0]);
            vk = c; normalized = c.ToString().ToLowerInvariant();
            return true;
        }

        // Named key that maps to a real Keys value (e.g. F5, Insert, Home).
        if (Enum.TryParse<Keys>(token, ignoreCase: true, out var k))
        {
            // Reject modifier keys standing in as the main key.
            if (k is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin
                  or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
                  or Keys.LMenu or Keys.RMenu or Keys.None)
                return false;

            vk = (uint)k;
            normalized = k.ToString().ToLowerInvariant();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Keys that, combined with Ctrl alone, are near-universal editing shortcuts
    /// (copy, paste, cut, undo, redo, select-all, save, print, find, close, new,
    /// open, new-tab). Windows will happily register these as a global hotkey, but
    /// doing so hijacks them everywhere and breaks the shortcut in every other app.
    /// </summary>
    private static readonly HashSet<string> ReservedCtrlKeys =
        new(StringComparer.OrdinalIgnoreCase)
        { "c", "v", "x", "z", "y", "a", "s", "p", "f", "w", "n", "o", "t" };

    /// <summary>
    /// True if the (already-parsed) combo would shadow a common global editing
    /// shortcut such as Ctrl+C. Only a bare Ctrl+&lt;key&gt; collides - adding a
    /// second modifier (Ctrl+Alt+C) is fine. <paramref name="reason"/> explains why,
    /// ready to show the user.
    /// </summary>
    public static bool IsReserved(string modsText, string keyText, out string reason)
    {
        reason = "";
        if (modsText.Equals("ctrl", StringComparison.OrdinalIgnoreCase)
            && ReservedCtrlKeys.Contains(keyText))
        {
            string k = keyText.ToUpperInvariant();
            reason = $"Ctrl+{k} is a common editing shortcut (copy, paste, undo, "
                   + "save). Using it as a global hotkey would break it in every "
                   + $"other app. Add another modifier, e.g. Ctrl+Alt+{k}.";
            return true;
        }
        return false;
    }

    private static void Add(List<string> list, string s)
    {
        if (!list.Contains(s)) list.Add(s);
    }
}
