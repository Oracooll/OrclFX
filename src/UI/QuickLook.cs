// Orcl File Explorer: Quick Look, a large preview window for the selected file (Space in a file list). Space or Esc
// closes it; the arrow keys step to the next or previous item in the list. While it's open it gets those keys
// wherever the keyboard focus is in the app (see MainForm.FilterMessage), so the file list behind it never moves
// on its own.
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
        readonly Timer refocus = new Timer();
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
            // Windows' preview handlers (PDF, Office …) run in another process and may take the keyboard focus
            // when they load, and keys sent there never reach this app: take it back shortly after each file.
            refocus.Interval = 400;
            refocus.Tick += delegate { refocus.Stop(); TakeFocus(); };
            preview.Loaded += delegate { TakeFocus(); refocus.Stop(); refocus.Start(); };
            FormClosing += delegate { refocus.Stop(); preview.Shutdown(); };
            this.path = path;
            Shown += delegate { ShowFile(this.path); }; // once the window has its size
        }

        public string ShownPath { get { return path; } }

        public void ShowFile(string p)
        {
            path = p;
            Text = Path.GetFileName(p.TrimEnd('\\')) + "  —  Quick Look (Space or Esc closes, arrow keys: next / previous)";
            preview.Show(p);
            Program.Trace("quick look shows " + p);
        }

        void TakeFocus()
        {
            if (IsDisposed || !Visible || Form.ActiveForm != this) return; // never from another app
            Native.SetFocus(Handle);
        }

        // A key pressed anywhere in the app while Quick Look is open (target: the window it was sent to). True when
        // Quick Look used it: arrows step through the files, Space and Esc close. Typing in the preview's Search box
        // is left alone.
        public bool HandleKey(Keys key, IntPtr target)
        {
            if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0) return false;
            TextBox box = Control.FromHandle(target) as TextBox;
            if (box != null && !box.ReadOnly && box.FindForm() == this) return false;
            bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
            if (key == Keys.Escape || key == Keys.Space && !shift) { Close(); return true; }
            int step = key == Keys.Right || key == Keys.Down ? 1 : key == Keys.Left || key == Keys.Up ? -1 : 0;
            if (step == 0) return false;
            string next = tab.Created ? tab.StepSelection(step) : null;
            if (next != null) ShowFile(next);
            else System.Media.SystemSounds.Beep.Play();
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Normally MainForm's message filter has handled these already; this is the fallback.
            if ((keyData & (Keys.Control | Keys.Alt)) == 0 && HandleKey(keyData & Keys.KeyCode, msg.HWnd)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            refocus.Dispose();
            preview.Dispose();
            main.QuickLookClosed(this);
        }
    }
}
