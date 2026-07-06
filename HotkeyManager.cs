using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// Owns a hidden window and registers a single system-wide hotkey with Windows.
/// A global hotkey is intercepted by the OS before the focused app sees it, which
/// is exactly why it works from inside any focused application.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY   = 0x0312;
    private const int HOTKEY_ID   = 0xB001; // the live binding
    private const int PROBE_ID    = 0xB002; // temporary, used only to test a candidate

    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // don't fire repeatedly while held

    public event Action? HotkeyPressed;

    // Whether a live hotkey is currently registered under HOTKEY_ID.
    private bool _registered;

    public HotkeyManager()
    {
        // Create a real (hidden) window so Windows has somewhere to post WM_HOTKEY.
        CreateHandle(new CreateParams());
    }

    /// <summary>
    /// Registers the given combo as the live hotkey, but ONLY if it can be claimed.
    /// If the combo is unavailable, any previously-working hotkey is left intact and
    /// this returns false. This is what lets "if the new combo is taken, the old one
    /// stays" actually hold true.
    /// </summary>
    public bool Register(uint modifiers, uint vk)
    {
        uint flags = modifiers | MOD_NOREPEAT;

        // Probe the candidate under a temp ID first, so we never tear down the working
        // hotkey for a combo that turns out to be unavailable.
        if (!RegisterHotKey(Handle, PROBE_ID, flags, vk))
            return false; // candidate unavailable — live hotkey untouched

        // Candidate is valid. Release the probe and the old live binding, then claim
        // the combo under the real ID.
        UnregisterHotKey(Handle, PROBE_ID);
        if (_registered)
            UnregisterHotKey(Handle, HOTKEY_ID);

        _registered = RegisterHotKey(Handle, HOTKEY_ID, flags, vk);
        return _registered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            HotkeyPressed?.Invoke();

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered)
            UnregisterHotKey(Handle, HOTKEY_ID);
        DestroyHandle();
    }
}
