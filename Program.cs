using System;
using System.Threading;
using System.Windows.Forms;

namespace Jotlay;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Advanced/manual path: "Jotlay.exe --set-hotkey ctrl+alt+j" validates and
        // stores the setting, then exits without launching the tray. Not part of the
        // normal install flow — the installer never calls this.
        if (args.Length >= 2 && args[0].Equals("--set-hotkey", StringComparison.OrdinalIgnoreCase))
        {
            SetHotkeyFromCli(args[1]);
            return;
        }

        // Only allow one copy running at a time.
        using var mutex = new Mutex(true, "Jotlay_SingleInstance_9F3C21", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Jotlay is already running — look for the icon in your system tray.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }

    /// <summary>
    /// Validates a combo like "ctrl+alt+j" and stores it. Rejects invalid input
    /// (unknown keys, modifier-only keys, missing modifiers) and leaves existing
    /// settings untouched rather than saving something the app can't register.
    /// </summary>
    private static void SetHotkeyFromCli(string combo)
    {
        if (!Hotkey.TryParse(combo, out string modsText, out string keyText, out _, out _))
        {
            MessageBox.Show(
                $"'{combo}' isn't a valid hotkey.\n\n" +
                "Use at least one modifier (Ctrl/Alt/Shift) plus one key, e.g. ctrl+alt+j.\n" +
                "Your existing hotkey was left unchanged.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (Hotkey.IsReserved(modsText, keyText, out string reserved))
        {
            MessageBox.Show(
                reserved + "\n\nYour existing hotkey was left unchanged.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var db = new Database();
        db.SetSetting("hotkey_mods", modsText);
        db.SetSetting("hotkey_key", keyText);
    }
}
