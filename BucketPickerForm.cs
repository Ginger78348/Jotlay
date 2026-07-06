using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Jotlay;

/// <summary>
/// A checklist of every bucket (with note counts) so you can export just the
/// ones you want — e.g. tick "idea" and "read", leave "work" alone.
/// </summary>
public sealed class BucketPickerForm : Form
{
    private readonly CheckedListBox _list;
    private readonly List<string> _buckets;

    public List<string> Selected { get; } = new();

    public BucketPickerForm(List<BucketRow> buckets)
    {
        _buckets = buckets.Select(b => b.Bucket).ToList();

        Text            = "Jotlay \u2014 export buckets";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(320, 400);

        var lbl = new Label
        {
            Text = "Tick the buckets to export (one .md file each):",
            Left = 14, Top = 12, Width = 296
        };

        _list = new CheckedListBox
        {
            Left = 14, Top = 38, Width = 292, Height = 280,
            CheckOnClick = true,
            IntegralHeight = false
        };
        foreach (var b in buckets)
            _list.Items.Add($"{b.Bucket}   ({b.Count})");

        var selectAll = new Button { Text = "Select all",  Left = 14,  Top = 328, Width = 90 };
        selectAll.Click += (_, _) => SetAll(true);

        var clear = new Button { Text = "Clear", Left = 110, Top = 328, Width = 70 };
        clear.Click += (_, _) => SetAll(false);

        var export = new Button
        {
            Text = "Export", Left = 138, Top = 362, Width = 80,
            DialogResult = DialogResult.OK
        };
        export.Click += (_, _) => Collect();

        var cancel = new Button
        {
            Text = "Cancel", Left = 224, Top = 362, Width = 82,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { lbl, _list, selectAll, clear, export, cancel });
        AcceptButton = export;
        CancelButton = cancel;
    }

    private void SetAll(bool state)
    {
        for (int i = 0; i < _list.Items.Count; i++)
            _list.SetItemChecked(i, state);
    }

    private void Collect()
    {
        Selected.Clear();
        foreach (int i in _list.CheckedIndices)
            Selected.Add(_buckets[i]);
    }
}
