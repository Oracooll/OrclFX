// Orcl File Explorer: the Find box that drops down from the magnifier in the title bar.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class FindBox : Form
    {
        readonly Label where = new Label(), hint = new Label();
        readonly ComboBox text = new ComboBox();
        readonly CheckBox contents = new CheckBox();
        readonly Button find = new Button(), stop = new Button();
        public event Action<string, bool> FindClicked;
        public event Action StopClicked;

        public FindBox(string folder, List<string> history, bool searchContents, bool searching)
        {
            // Laid out in pixels below (Native.Px handles the screen's scaling): no extra font-based scaling.
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Theme.Menu;
            ForeColor = Theme.Text;
            Padding = new Padding(Native.Px(12));
            int w = Native.Px(420), x = Native.Px(12), inner = w - 2 * x, y = Native.Px(10);

            where.SetBounds(x, y, inner, Native.Px(20));
            where.AutoEllipsis = true;
            where.UseMnemonic = false;
            where.ForeColor = Theme.TextDim;
            // A long path is shortened in the middle (C:\Users\...\Projects) so "and its subfolders" stays visible.
            string prefix = "Find in  ", suffix = "  and its subfolders", shown = string.Copy(folder);
            int room = inner - TextRenderer.MeasureText(prefix + suffix, Font).Width - Native.Px(8);
            TextRenderer.MeasureText(shown, Font, new Size(Math.Max(room, Native.Px(80)), 0), TextFormatFlags.PathEllipsis | TextFormatFlags.ModifyString);
            int end = shown.IndexOf('\0');
            where.Text = prefix + (end >= 0 ? shown.Substring(0, end) : shown) + suffix;
            y += Native.Px(24);

            text.SetBounds(x, y, inner, Native.Px(26));
            text.DropDownStyle = ComboBoxStyle.DropDown;
            text.FlatStyle = FlatStyle.Flat;
            text.BackColor = Theme.Input;
            text.ForeColor = Theme.Text;
            foreach (string h in history) text.Items.Add(h);
            if (history.Count > 0) text.Text = history[0];
            text.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            text.AutoCompleteSource = AutoCompleteSource.ListItems;
            y += text.Height + Native.Px(4);

            hint.SetBounds(x, y, inner, Native.Px(34));
            hint.ForeColor = Theme.TextDim;
            hint.UseMnemonic = false;
            hint.Text = "Part of a name (report), or patterns such as *.pdf;*.docx. Folders that match are found too.";
            y += hint.Height + Native.Px(4);

            contents.SetBounds(x, y, inner, Native.Px(24));
            contents.Text = "Also search inside files (uses Windows Search, like File Explorer)";
            contents.Checked = searchContents;
            contents.ForeColor = Theme.Text;
            contents.CheckedChanged += delegate { UpdateHint(); };
            y += contents.Height + Native.Px(10);

            int bw = Native.Px(84), bh = Native.Px(28);
            find.SetBounds(w - x - bw, y, bw, bh);
            find.Text = "Find";
            stop.SetBounds(w - x - 2 * bw - Native.Px(8), y, bw, bh);
            stop.Text = "Stop";
            stop.Visible = searching;
            foreach (Button b in new Button[] { find, stop })
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Theme.Border;
                b.BackColor = Theme.Input;
                b.ForeColor = Theme.Text;
            }
            find.FlatAppearance.BorderColor = Theme.Accent;
            find.Click += delegate
            {
                string t = text.Text.Trim();
                if (t.Length == 0) { text.Focus(); return; }
                Close();
                if (FindClicked != null) FindClicked(t, contents.Checked);
            };
            stop.Click += delegate { Close(); if (StopClicked != null) StopClicked(); };
            AcceptButton = find;
            y += bh + Native.Px(12);

            Controls.AddRange(new Control[] { where, text, hint, contents, stop, find });
            ClientSize = new Size(w, y);
            UpdateHint();
            Deactivate += delegate { BeginInvoke((MethodInvoker)Close); };
            Shown += delegate { text.Focus(); text.SelectAll(); };
        }

        void UpdateHint()
        {
            hint.Text = contents.Checked
                ? "Words in names or inside files, the way File Explorer's search box works. Fast in folders Windows indexes, slower elsewhere."
                : "Part of a name (report), or patterns such as *.pdf;*.docx. Folders that match are found too.";
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && !text.DroppedDown) { e.Handled = true; Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // A drop shadow like a menu's.
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }
    }
}
