using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// Lets you press a new shortcut. It does NOT save anything — it only exposes the
/// captured candidate (CandidateMods / CandidateKey). The caller tests whether the
/// combo can actually be registered and only then commits it, so a taken combo
/// never overwrites the working one.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly Database _db;
    private readonly TextBox _capture;

    public string CandidateMods { get; private set; } = "";
    public string CandidateKey  { get; private set; } = "";
    public bool HasCandidate => CandidateMods.Length > 0 && CandidateKey.Length > 0;

    public SettingsForm(Database db)
    {
        _db = db;

        Text            = "Jotlay \u2014 Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(380, 168);

        var lbl = new Label
        {
            Text = "Click the box below, then press the shortcut you want:",
            Left = 16, Top = 16, Width = 348
        };

        _capture = new TextBox
        {
            Left = 16, Top = 44, Width = 348, ReadOnly = true,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Text = CurrentText()
        };
        _capture.KeyDown += Capture_KeyDown;

        var note = new Label
        {
            Text = "Tip: use at least two modifiers (e.g. Ctrl+Alt) so it won't\nclash with shortcuts inside other apps.",
            Left = 16, Top = 78, Width = 348, Height = 34,
            ForeColor = Color.Gray
        };

        var save = new Button
        {
            Text = "Save", Left = 200, Top = 122, Width = 78,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel", Left = 286, Top = 122, Width = 78,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { lbl, _capture, note, save, cancel });
        AcceptButton = save;
        CancelButton = cancel;
    }

    private string CurrentText() =>
        $"{_db.GetSetting("hotkey_mods", "ctrl+alt")}+{_db.GetSetting("hotkey_key", "J")}"
            .ToUpperInvariant();

    private void Capture_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        // Ignore lone modifier taps; wait for a real key.
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)
            return;

        var mods = new List<string>();
        if (e.Control) mods.Add("ctrl");
        if (e.Alt)     mods.Add("alt");
        if (e.Shift)   mods.Add("shift");

        if (mods.Count == 0)
        {
            _capture.Text = "Hold Ctrl / Alt / Shift too\u2026";
            CandidateMods = CandidateKey = "";
            return;
        }

        string keyToken = e.KeyCode == Keys.Space ? "space" : e.KeyCode.ToString();

        // Validate against the same rules the rest of the app uses.
        if (!Hotkey.TryResolveKey(keyToken, out _, out string normalizedKey))
        {
            _capture.Text = "Pick a normal key (letter, digit, F-key\u2026)";
            CandidateMods = CandidateKey = "";
            return;
        }

        string candidateMods = string.Join("+", mods);

        // Refuse combos that would hijack a global editing shortcut (e.g. Ctrl+C).
        if (Hotkey.IsReserved(candidateMods, normalizedKey, out _))
        {
            _capture.Text = $"{candidateMods}+{normalizedKey}".ToUpperInvariant()
                          + " is reserved - add a modifier";
            CandidateMods = CandidateKey = "";
            return;
        }

        CandidateMods = candidateMods;
        CandidateKey  = normalizedKey;
        _capture.Text = $"{CandidateMods}+{CandidateKey}".ToUpperInvariant();
    }
}
