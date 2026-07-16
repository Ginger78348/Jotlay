using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// One window to browse, search, export, and delete individual notes. Replaces the
/// old bucket-only export picker. Notes are shown in a checkable list; a bucket
/// filter and a search box narrow the view; Export writes the selected notes
/// (one .md per bucket) and Delete removes them after confirmation.
/// </summary>
public sealed class NotesWindow : Form
{
    private readonly Database _db;

    private readonly ComboBox _bucketFilter = new();
    private readonly TextBox _search = new();
    private readonly ListView _list = new();
    private readonly Label _count = new();

    private List<NoteRow> _all = new();     // everything currently loaded
    private List<NoteRow> _view = new();    // what's shown after filter/search

    // palette (matches the capture box)
    private static readonly Color Bg      = Color.FromArgb(24, 26, 31);
    private static readonly Color Panel   = Color.FromArgb(32, 35, 42);
    private static readonly Color Field   = Color.FromArgb(40, 44, 52);
    private static readonly Color Fg      = Color.Gainsboro;
    private static readonly Color Dim     = Color.FromArgb(150, 160, 175);
    private static readonly Color Accent  = Color.FromArgb(0, 200, 220);

    public NotesWindow(Database db)
    {
        _db = db;

        Text          = "Jotlay \u2014 Notes";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize   = new Size(560, 440);
        Size          = new Size(680, 560);
        BackColor     = Bg;
        ForeColor     = Fg;
        Font          = new Font("Segoe UI", 9.5f);

        // ---- top bar: bucket filter + search ----
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, ColumnCount = 3, Padding = new Padding(12, 10, 12, 4),
            BackColor = Bg
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));

        _bucketFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _bucketFilter.FlatStyle     = FlatStyle.Flat;
        _bucketFilter.BackColor     = Field;
        _bucketFilter.ForeColor     = Fg;
        _bucketFilter.Margin        = new Padding(0, 2, 8, 2);
        _bucketFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        _search.BackColor   = Field;
        _search.ForeColor   = Fg;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Margin      = new Padding(0, 2, 0, 2);
        _search.Dock        = DockStyle.Fill;
        SetCue(_search, "Search notes\u2026");
        _search.TextChanged += (_, _) => ApplyFilter();

        top.Controls.Add(_bucketFilter, 0, 0);
        top.Controls.Add(_search, 1, 0);

        // ---- the list ----
        _list.Dock          = DockStyle.Fill;
        _list.View          = View.Details;
        _list.CheckBoxes    = true;
        _list.FullRowSelect = true;
        _list.GridLines     = false;
        _list.BackColor     = Panel;
        _list.ForeColor     = Fg;
        _list.BorderStyle   = BorderStyle.None;
        _list.OwnerDraw     = false;
        _list.Columns.Add("When", 130);
        _list.Columns.Add("Bucket", 110);
        _list.Columns.Add("Note", 400);
        _list.Resize += (_, _) => FitNoteColumn();

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4), BackColor = Bg };
        listHost.Controls.Add(_list);

        // ---- selection helpers row ----
        var mid = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 36), Padding = new Padding(12, 4, 12, 4),
            BackColor = Bg, WrapContents = false
        };
        var selectAll = MakeFlatButton("Select all", 84);
        selectAll.Click += (_, _) => SetAllChecked(true);
        var clear = MakeFlatButton("Clear", 64);
        clear.Click += (_, _) => SetAllChecked(false);
        _count.AutoSize = true;
        _count.ForeColor = Dim;
        _count.Margin = new Padding(12, 8, 0, 0);
        mid.Controls.Add(selectAll);
        mid.Controls.Add(clear);
        mid.Controls.Add(_count);

        // ---- bottom action bar (generously tall so nothing clips) ----
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 60), Padding = new Padding(12, 12, 12, 12),
            BackColor = Panel, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        var close = MakeFlatButton("Close", 88, height: 34);
        close.Click += (_, _) => Close();
        var delete = MakeAccentButton("Delete", 96, Color.FromArgb(200, 70, 70));
        delete.Click += (_, _) => DeleteSelected();
        var export = MakeAccentButton("Export", 96, Accent);
        export.Click += (_, _) => ExportSelected();
        bottom.Controls.Add(close);
        bottom.Controls.Add(delete);
        bottom.Controls.Add(export);

        // order matters: fill first, then docked bars
        Controls.Add(listHost);
        Controls.Add(mid);
        Controls.Add(bottom);
        Controls.Add(top);

        Load += (_, _) => { ReloadAll(); FitNoteColumn(); };
    }

    // ---- data ----

    private void ReloadAll()
    {
        _all = _db.AllNotes();

        // rebuild bucket filter
        string previously = _bucketFilter.SelectedItem as string ?? "All buckets";
        _bucketFilter.Items.Clear();
        _bucketFilter.Items.Add("All buckets");
        foreach (var b in _db.Buckets())
            _bucketFilter.Items.Add(b.Bucket);
        int idx = _bucketFilter.Items.IndexOf(previously);
        _bucketFilter.SelectedIndex = idx >= 0 ? idx : 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string bucket = _bucketFilter.SelectedItem as string ?? "All buckets";
        string q = _search.Text.Trim();
        bool searching = q.Length > 0 && q != "Search notes\u2026";

        IEnumerable<NoteRow> rows = _all;
        if (bucket != "All buckets")
            rows = rows.Where(n => n.Bucket == bucket);
        if (searching)
            rows = rows.Where(n =>
                n.Body.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.Bucket.Contains(q, StringComparison.OrdinalIgnoreCase));

        _view = rows.OrderByDescending(n => n.CreatedUtc).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var n in _view)
        {
            var local = DateTime.Parse(n.CreatedUtc).ToLocalTime();
            var item = new ListViewItem(local.ToString("yyyy-MM-dd HH:mm"))
            {
                Tag = n.Id
            };
            item.SubItems.Add(n.Bucket);
            item.SubItems.Add(OneLine(n.Body));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int shown = _list.Items.Count;
        int sel = _list.CheckedItems.Count;
        _count.Text = sel > 0 ? $"{sel} selected of {shown} shown" : $"{shown} note(s) shown";
    }

    // ---- actions ----

    private List<long> CheckedIds() =>
        _list.CheckedItems.Cast<ListViewItem>()
             .Where(i => i.Tag is long)
             .Select(i => (long)i.Tag!).ToList();

    private void ExportSelected()
    {
        var ids = CheckedIds();
        var notes = _all.Where(n => ids.Contains(n.Id)).ToList();
        if (notes.Count == 0)
        {
            MessageBox.Show(this, "Tick the notes you want to export first.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var fbd = new FolderBrowserDialog { Description = "Choose a folder for the exported .md files" };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;

        int files = 0;
        try
        {
            foreach (var group in notes.GroupBy(n => n.Bucket).OrderBy(g => g.Key))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# {group.Key}");
                sb.AppendLine();
                foreach (var n in group.OrderBy(x => x.CreatedUtc))
                {
                    var local = DateTime.Parse(n.CreatedUtc).ToLocalTime();
                    string body = n.Body.Replace("\r\n", "\n").Replace("\n", "\n  ");
                    sb.AppendLine($"- [{local:yyyy-MM-dd HH:mm}] {body}");
                }
                string safe = string.Join("_", group.Key.Split(Path.GetInvalidFileNameChars()));
                File.WriteAllText(Path.Combine(fbd.SelectedPath, safe + ".md"), sb.ToString());
                files++;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed:\n\n" + ex.Message,
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this, $"Exported {files} file(s).",
            "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        try { System.Diagnostics.Process.Start("explorer.exe", fbd.SelectedPath); } catch { }
    }

    private void DeleteSelected()
    {
        var ids = CheckedIds();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Tick the notes you want to delete first.",
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Permanently delete {ids.Count} note(s)? This can't be undone.",
            "Jotlay \u2014 confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _db.DeleteNotes(ids);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Delete failed:\n\n" + ex.Message,
                "Jotlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ReloadAll();
    }

    // ---- helpers ----

    private void SetAllChecked(bool state)
    {
        _list.BeginUpdate();
        foreach (ListViewItem i in _list.Items) i.Checked = state;
        _list.EndUpdate();
        UpdateCount();
    }

    private void FitNoteColumn()
    {
        if (_list.Columns.Count < 3) return;
        int used = _list.Columns[0].Width + _list.Columns[1].Width;
        int rest = _list.ClientSize.Width - used - 4;
        _list.Columns[2].Width = Math.Max(160, rest);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _list.ItemChecked += (_, _) => UpdateCount();
    }

    private static string OneLine(string s) =>
        s.Replace("\r", " ").Replace("\n", " ").Trim();

    private Button MakeFlatButton(string text, int width, int height = 28)
    {
        var b = new Button
        {
            // AutoSize + MinimumSize: keeps the intended footprint but grows to fit
            // the text, so nothing clips at higher display scaling.
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, height),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Fg,
            Margin = new Padding(0, 0, 8, 0)
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 88);
        return b;
    }

    private Button MakeAccentButton(string text, int width, Color color, int height = 34)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, height),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat, BackColor = color,
            ForeColor = Color.FromArgb(16, 20, 26),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(8, 0, 0, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    // simple cue-banner text for the search box
    private void SetCue(TextBox tb, string cue)
    {
        tb.Text = cue;
        tb.ForeColor = Dim;
        tb.GotFocus += (_, _) => { if (tb.Text == cue) { tb.Text = ""; tb.ForeColor = Fg; } };
        tb.LostFocus += (_, _) => { if (tb.Text.Trim().Length == 0) { tb.Text = cue; tb.ForeColor = Dim; } };
    }
}
