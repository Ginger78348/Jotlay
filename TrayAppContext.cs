using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Jotlay;

/// <summary>
/// The living heart of the app: a tray icon, the global hotkey, and the popup.
/// There is no main window — the tray icon keeps the message loop alive.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly Database      _db;
    private readonly CaptureForm   _capture;
    private readonly HotkeyManager _hotkeys;
    private readonly NotifyIcon    _tray;

    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Jotlay";

    public TrayAppContext()
    {
        _db      = new Database();
        _capture = new CaptureForm(_db);

        _hotkeys = new HotkeyManager();
        _hotkeys.HotkeyPressed += () => _capture.ShowBox();

        _tray = new NotifyIcon
        {
            Icon    = BuildTrayIcon(),
            Visible = true,
            Text    = "Jotlay \u2014 quick capture"
        };
        _tray.DoubleClick += (_, _) => _capture.ShowBox();
        RefreshMenu();

        RegisterConfiguredHotkey();
    }

    // ---- hotkey ----------------------------------------------------------

    private void RegisterConfiguredHotkey()
    {
        string combo = $"{_db.GetSetting("hotkey_mods", "ctrl+alt")}+{_db.GetSetting("hotkey_key", "j")}";

        if (Hotkey.TryParse(combo, out _, out _, out uint mods, out uint vk)
            && _hotkeys.Register(mods, vk))
            return;

        _tray.ShowBalloonTip(4500, "Jotlay",
            "Couldn't grab that hotkey \u2014 it may be in use. " +
            "Right-click the tray icon \u2192 Change hotkey.", ToolTipIcon.Warning);
    }

    private void OpenSettings()
    {
        using var f = new SettingsForm(_db);
        if (f.ShowDialog() != DialogResult.OK || !f.HasCandidate) return;

        string combo = $"{f.CandidateMods}+{f.CandidateKey}";
        if (!Hotkey.TryParse(combo, out string modsText, out string keyText, out uint mods, out uint vk))
        {
            _tray.ShowBalloonTip(4000, "Jotlay",
                "That shortcut isn't valid. Your hotkey was left unchanged.",
                ToolTipIcon.Warning);
            return;
        }

        // Belt-and-braces: never register a reserved editing shortcut, even if one
        // reaches here through some other path than the settings dialog.
        if (Hotkey.IsReserved(modsText, keyText, out string reserved))
        {
            _tray.ShowBalloonTip(5000, "Jotlay", reserved, ToolTipIcon.Warning);
            return;
        }

        // Test the combo FIRST. Register() leaves the old hotkey intact on failure,
        // so we only overwrite the saved setting once the new combo actually works.
        if (_hotkeys.Register(mods, vk))
        {
            _db.SetSetting("hotkey_mods", modsText);
            _db.SetSetting("hotkey_key", keyText);
            RefreshMenu();
            _tray.ShowBalloonTip(2000, "Jotlay",
                $"Hotkey set to {CurrentHotkeyText()}.", ToolTipIcon.Info);
        }
        else
        {
            _tray.ShowBalloonTip(4000, "Jotlay",
                "That shortcut is already in use by something else. " +
                "Your existing hotkey still works \u2014 try another combo.",
                ToolTipIcon.Warning);
        }
    }

    private string CurrentHotkeyText() =>
        $"{_db.GetSetting("hotkey_mods", "ctrl+alt")}+{_db.GetSetting("hotkey_key", "j")}"
            .ToUpperInvariant();

    // ---- tray menu -------------------------------------------------------

    private void RefreshMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Capture now", null, (_, _) => _capture.ShowBox());
        menu.Items.Add(new ToolStripSeparator());

        var hk = menu.Items.Add($"Hotkey:  {CurrentHotkeyText()}");
        hk.Enabled = false;
        menu.Items.Add("Change hotkey\u2026", null, (_, _) => OpenSettings());

        var auto = new ToolStripMenuItem("Start with Windows")
        {
            Checked = IsAutostart(),
            CheckOnClick = false
        };
        auto.Click += (_, _) => ToggleAutostart();
        menu.Items.Add(auto);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Browse notes\u2026", null, (_, _) => OpenNotes());
        menu.Items.Add("Show database file", null, (_, _) =>
            Process.Start("explorer.exe", $"/select,\"{_db.DbPath}\""));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    // ---- start with windows ---------------------------------------------

    private static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) != null;
    }

    private void ToggleAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;

        if (IsAutostart()) k.DeleteValue(RunName, throwOnMissingValue: false);
        else               k.SetValue(RunName, $"\"{Application.ExecutablePath}\"");

        RefreshMenu();
    }

    // ---- notes window ----------------------------------------------------

    private NotesWindow? _notesWindow;

    private void OpenNotes()
    {
        // Reuse a single window instance; bring it forward if already open.
        if (_notesWindow is { IsDisposed: false })
        {
            if (_notesWindow.WindowState == FormWindowState.Minimized)
                _notesWindow.WindowState = FormWindowState.Normal;
            _notesWindow.Activate();
            _notesWindow.BringToFront();
            return;
        }

        _notesWindow = new NotesWindow(_db);
        _notesWindow.FormClosed += (_, _) => _notesWindow = null;
        _notesWindow.Show();
        _notesWindow.Activate();
    }

    // ---- icon + exit -----------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon BuildTrayIcon()
    {
        // Drawn in code so there's no separate .ico asset to ship.
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(0, 200, 220));
            g.FillRectangle(bg, 2, 2, 28, 28);

            using var f  = new Font("Segoe UI", 15f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.FromArgb(16, 20, 26));
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("J", f, tb, new RectangleF(0, -1, 32, 32), sf);
        }

        // GetHicon() creates an unmanaged icon handle that Icon.FromHandle won't own.
        // Clone into a managed icon, then destroy the raw handle so it doesn't leak.
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        _hotkeys.Dispose();
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _capture.Dispose();
        ExitThread();
    }
}
