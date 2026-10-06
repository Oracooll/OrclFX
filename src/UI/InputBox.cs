// Orcl File Explorer: a small themed dialog that asks for one line of text.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class InputBox
    {
        // The text typed, or null when cancelled.
        public static string Ask(Form owner, string title, string prompt, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = f.ShowInTaskbar = false;
                f.Font = owner.Font;
                f.BackColor = Theme.Menu;
                f.ForeColor = Theme.Text;
                f.ClientSize = new Size(Native.Px(360), Native.Px(112));
                Label l = new Label();
                l.Text = prompt;
                l.AutoSize = true;
                l.Location = new Point(Native.Px(12), Native.Px(12));
                TextBox box = new TextBox();
                box.Text = initial ?? "";
                box.Location = new Point(Native.Px(12), Native.Px(38));
                box.Width = Native.Px(336);
                box.BackColor = Theme.Input;
                box.ForeColor = Theme.Text;
                Button ok = new Button();
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(Native.Px(190), Native.Px(76)); ok.Width = Native.Px(75);
                Button cancel = new Button();
                cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(Native.Px(273), Native.Px(76)); cancel.Width = Native.Px(75);
                f.Controls.AddRange(new Control[] { l, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.HandleCreated += delegate
                {
                    int dark = Theme.Dark ? 1 : 0;
                    Native.DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
                };
                f.Shown += delegate { box.SelectAll(); box.Focus(); };
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
