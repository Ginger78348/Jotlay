using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// The borderless dark box that appears on the hotkey. Type, Enter to save, Esc
/// to cancel, click away to dismiss. It never truly closes — it hides and waits.
/// </summary>
public sealed class CaptureForm : Form
{
    private readonly Database _db;
    private readonly TextBox _input;
    private readonly Label _hint;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly Color BoxBg   = Color.FromArgb(24, 26, 31);
    private static readonly Color FieldBg = Color.FromArgb(34, 37, 45);
    private static readonly Color Accent  = Color.FromArgb(0, 200, 220);

    public CaptureForm(Database db)
    {
        _db = db;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        Width           = 560;
        Height          = 150;
        BackColor       = Accent;          // thin accent border showing through padding
        Padding         = new Padding(2);

        var panel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BoxBg,
            Padding   = new Padding(14)
        };
        Controls.Add(panel);

        _hint = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 22,
            ForeColor = Color.FromArgb(120, 130, 145),
            Font      = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _input = new TextBox
        {
            Multiline   = true,
            BorderStyle = BorderStyle.None,
            BackColor   = FieldBg,
            ForeColor   = Color.Gainsboro,
            Font        = new Font("Segoe UI", 12.5f),
            Dock        = DockStyle.Fill,
            AcceptsTab  = false
        };
        _input.KeyDown     += Input_KeyDown;
        _input.TextChanged += (_, _) => UpdateHint();

        panel.Controls.Add(_input);   // fill first
        panel.Controls.Add(_hint);    // then dock hint to bottom

        Deactivate += (_, _) => Hide();   // clicking elsewhere dismisses
        UpdateHint();
    }

    private void UpdateHint()
    {
        var (bucket, _) = Router.Parse(_input.Text);
        _hint.Text = $"\u2192 {bucket}        Enter save \u00B7 Shift+Enter newline \u00B7 Esc cancel";
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            _input.Clear();
            Hide();
        }
        else if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;   // stop the newline that Enter would insert
            Save();
        }
        // Shift+Enter is left alone, so it inserts a newline for multi-line notes.
    }

    private void Save()
    {
        string raw = _input.Text.Trim();
        if (raw.Length == 0) { Hide(); return; }

        var (bucket, body) = Router.Parse(raw);
        try
        {
            _db.AddNote(bucket, body, raw);
        }
        catch (Exception ex)
        {
            // Keep the note visible and the box open so the text isn't lost.
            MessageBox.Show(
                "Jotlay couldn't save that note:\n\n" + ex.Message +
                "\n\nYour text is still here — try again, or copy it out.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _input.Focus();
            return;
        }
        _input.Clear();
        Hide();
    }

    public void ShowBox()
    {
        // If the box is already open, a second hotkey press should NOT wipe what's
        // been typed — just bring it back to focus.
        if (Visible)
        {
            BringToFront();
            Activate();
            SetForegroundWindow(Handle);
            _input.Focus();
            return;
        }

        // Center-ish on whichever monitor the mouse is on (upper third reads better).
        var screen = Screen.FromPoint(Cursor.Position);
        Left = screen.WorkingArea.Left + (screen.WorkingArea.Width  - Width)  / 2;
        Top  = screen.WorkingArea.Top  + (screen.WorkingArea.Height - Height) / 3;

        _input.Clear();
        UpdateHint();

        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);  // nudge Windows to actually give us focus
        _input.Focus();
    }

    // The box hides instead of closing, so it's instantly available next time.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            base.OnFormClosing(e);
        }
    }
}
