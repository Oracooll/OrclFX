// Orcl File Explorer: Quick Look, a large preview window for the selected file (Space in a file list). Space or Esc
// closes it; the arrow keys step to the next or previous item in the list.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class QuickLook : Form
    {
        readonly MainForm main;
        readonly BrowserTab tab;
        readonly PreviewPane preview = new PreviewPane();
        string path;

        public QuickLook(MainForm main, BrowserTab tab, string path)
        {
            this.main = main;
            this.tab = tab;
            Text = "Quick Look";
            Icon = main.Icon;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = main.Font;
            Rectangle area = Screen.FromControl(main).WorkingArea;
            Size = new Size(area.Width * 3 / 4, area.Height * 4 / 5);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            MinimumSize = new Size(Native.Px(320), Native.Px(240));
            BackColor = Theme.Window;
            Controls.Add(preview);
            preview.ApplyTheme();
            HandleCreated += delegate
            {
                int dark = Theme.Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            };
            FormClosing += delegate { preview.Shutdown(); };
            this.path = path;
            Shown += delegate { ShowFile(this.path); }; // once the window has its size
        }

        public string ShownPath { get { return path; } }

        public void ShowFile(string p)
        {
            path = p;
            Text = Path.GetFileName(p.TrimEnd('\\')) + "  —  Quick Look (Space or Esc closes, arrow keys: next / previous)";
            preview.Show(p);
        }

        // Handled before any control sees the key (arrow keys would otherwise move the focus between controls).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Keys typed into the preview's own Search box (or anything else editable) are left alone.
            TextBox box = ActiveControl as TextBox;
            if (box != null && !box.ReadOnly) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape || keyData == Keys.Space) { Close(); return true; }
            int step = keyData == Keys.Right || keyData == Keys.Down ? 1 : keyData == Keys.Left || keyData == Keys.Up ? -1 : 0;
            // In a text preview the arrows keep moving through the text.
            if (step != 0 && box == null)
            {
                string next = tab.Created ? tab.StepSelection(step) : null;
                if (next != null) ShowFile(next);
                else System.Media.SystemSounds.Beep.Play();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            preview.Dispose();
            main.QuickLookClosed(this);
        }
    }
}
