// OrclFX: the Space Viewer's settings (the OrclFX icon at the top left of the viewer opens them): what the mouse,
// the arrows and full screen do, and how pictures are drawn. Kept in the settings file as sv* keys.
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class ViewerSettings
    {
        // Full screen
        public static bool FullScreenClicks = true;   // middle click or double left click switches full screen on and off
        public static int Enlarge = 1;                // small pictures enlarged to fit: 0 never, 1 in full screen, 2 always
        public static bool ExitMiddle = true;         // middle click leaves full screen
        public static bool ConfirmExit;               // ask before leaving full screen
        public static bool HideCursor = true;         // full screen: the pointer hides when it's still
        // Mouse
        public const int WheelStep = 0, WheelZoomCentre = 1, WheelZoomPointer = 2;
        public static int Wheel = WheelZoomPointer;
        public static MouseButtons DragButtons = MouseButtons.Left;
        public static bool ClickSteps;                // left click: previous picture, right click: next
        // Display
        public static bool Smooth = true;             // zoom glides instead of jumping
        public static int ZoomStep = 1;               // 0: 10 %, 1: 25 %, 2: 50 % a step
        public static bool Gpu = true;                // drawn by the graphics card (Direct2D) when it can be
        public static int Background;                 // 0 theme, 1 black, 2 dark grey, 3 grey, 4 white
        public static bool Wrap;                      // after the last file comes the first

        public static readonly string[] EnlargeNames = { "Never", "In full screen only", "Always" };
        public static readonly string[] WheelNames = { "Previous / next picture", "Zoom in / out at the centre of the picture", "Zoom in / out at the mouse pointer" };
        public static readonly string[] ZoomStepNames = { "Fine (10 %)", "Normal (25 %)", "Coarse (50 %)" };
        public static readonly string[] BackgroundNames = { "Same as the theme", "Black", "Dark grey", "Grey", "White" };

        public static float ZoomFactor { get { return ZoomStep == 0 ? 1.1f : ZoomStep == 2 ? 1.5f : 1.25f; } }

        public static Color BackColor
        {
            get
            {
                switch (Background)
                {
                    case 1: return Color.Black;
                    case 2: return Color.FromArgb(32, 32, 32);
                    case 3: return Color.FromArgb(128, 128, 128);
                    case 4: return Color.White;
                    default: return Theme.Dark ? Color.FromArgb(12, 12, 12) : Color.FromArgb(235, 235, 235);
                }
            }
        }

        public static void Write(StringBuilder sb)
        {
            sb.AppendLine("svfsclicks=" + B(FullScreenClicks));
            sb.AppendLine("svenlarge=" + Enlarge);
            sb.AppendLine("svexitmiddle=" + B(ExitMiddle));
            sb.AppendLine("svconfirmexit=" + B(ConfirmExit));
            sb.AppendLine("svhidecursor=" + B(HideCursor));
            sb.AppendLine("svwheel=" + Wheel);
            sb.AppendLine("svdrag=" + ((DragButtons & MouseButtons.Left) != 0 ? "L" : "") + ((DragButtons & MouseButtons.Middle) != 0 ? "M" : "") + ((DragButtons & MouseButtons.Right) != 0 ? "R" : ""));
            sb.AppendLine("svsmooth=" + B(Smooth));
            sb.AppendLine("svzoomstep=" + ZoomStep);
            sb.AppendLine("svgpu=" + B(Gpu));
            sb.AppendLine("svbackground=" + Background);
            sb.AppendLine("svwrap=" + B(Wrap));
            sb.AppendLine("svclicksteps=" + B(ClickSteps));
        }

        static string B(bool b) { return b ? "1" : "0"; }

        // True when the key was one of these settings.
        public static bool Read(string k, string v)
        {
            int n;
            bool num = int.TryParse(v, out n);
            switch (k)
            {
                case "svfsclicks": FullScreenClicks = v != "0"; return true;
                case "svenlarge": if (num && n >= 0 && n <= 2) Enlarge = n; return true;
                case "svexitmiddle": ExitMiddle = v != "0"; return true;
                case "svconfirmexit": ConfirmExit = v == "1"; return true;
                case "svhidecursor": HideCursor = v != "0"; return true;
                case "svwheel": if (num && n >= 0 && n <= 2) Wheel = n; return true;
                case "svdrag":
                    DragButtons = MouseButtons.None;
                    if (v.IndexOf('L') >= 0) DragButtons |= MouseButtons.Left;
                    if (v.IndexOf('M') >= 0) DragButtons |= MouseButtons.Middle;
                    if (v.IndexOf('R') >= 0) DragButtons |= MouseButtons.Right;
                    return true;
                case "svsmooth": Smooth = v != "0"; return true;
                case "svzoomstep": if (num && n >= 0 && n <= 2) ZoomStep = n; return true;
                case "svgpu": Gpu = v != "0"; return true;
                case "svbackground": if (num && n >= 0 && n <= 4) Background = n; return true;
                case "svwrap": Wrap = v == "1"; return true;
                case "svclicksteps": ClickSteps = v == "1"; return true;
            }
            return false;
        }

        // ---- the settings screen

        // True when OK was pressed (the settings are then changed).
        public static bool ShowDialog(Form owner, string drawingStatus)
        {
            using (Form f = new Form())
            {
                f.Text = "Space Viewer settings";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = f.ShowInTaskbar = false;
                f.Font = owner.Font;
                f.BackColor = Theme.Menu;
                f.ForeColor = Theme.Text;
                f.ShowIcon = false;
                int x = Native.Px(16), y = Native.Px(12), w = Native.Px(570), indent = Native.Px(16);

                Font bold = new Font(f.Font, FontStyle.Bold);
                f.Disposed += delegate { bold.Dispose(); };
                Func<string, Label> heading = delegate(string text)
                {
                    if (y > Native.Px(20)) y += Native.Px(10);
                    Label l = new Label();
                    l.Text = text;
                    l.AutoSize = true;
                    l.Font = bold;
                    l.ForeColor = Theme.Accent;
                    l.Location = new Point(x, y);
                    f.Controls.Add(l);
                    y += Native.Px(24);
                    return l;
                };
                Func<string, bool, CheckBox> check = delegate(string text, bool on)
                {
                    CheckBox c = new CheckBox();
                    c.Text = text;
                    c.Checked = on;
                    c.AutoSize = true;
                    c.Location = new Point(x + indent, y);
                    f.Controls.Add(c);
                    y += Native.Px(26);
                    return c;
                };
                Func<string, Label> note = delegate(string text)
                {
                    Label l = new Label();
                    l.Text = text;
                    l.AutoSize = true;
                    l.ForeColor = Theme.TextDim;
                    l.Location = new Point(x + indent, y + Native.Px(2));
                    f.Controls.Add(l);
                    y += Native.Px(24);
                    return l;
                };
                Func<string, string[], int, ComboBox> choice = delegate(string text, string[] items, int at)
                {
                    Label l = new Label();
                    l.Text = text;
                    l.AutoSize = true;
                    l.Location = new Point(x + indent, y + Native.Px(4));
                    f.Controls.Add(l);
                    ComboBox c = new ComboBox();
                    c.DropDownStyle = ComboBoxStyle.DropDownList;
                    c.FlatStyle = FlatStyle.Flat;
                    c.BackColor = Theme.Input;
                    c.ForeColor = Theme.Text;
                    c.Items.AddRange(items);
                    c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, at));
                    c.Location = new Point(x + Native.Px(190), y);
                    c.Width = w - Native.Px(190);
                    f.Controls.Add(c);
                    y += Native.Px(30);
                    return c;
                };

                heading("Full screen");
                CheckBox fsClicks = check("Middle click or double left click switches full screen on and off", FullScreenClicks);
                note("Full screen fills the screen with the picture alone (no thumbnails), fitted to its width or height.");
                ComboBox enlarge = choice("Enlarge small pictures to fit:", EnlargeNames, Enlarge);
                note("Leave full screen: Esc (always), and");
                CheckBox exitMiddle = check("Middle click", ExitMiddle);
                CheckBox confirm = check("Ask before leaving full screen", ConfirmExit);
                CheckBox hide = check("Hide the mouse pointer while it's still", HideCursor);

                heading("Arrow keys");
                note("Left / Right: previous / next picture");
                note("Up / Down: zoom in / out at the centre of the picture");

                heading("Mouse");
                ComboBox wheel = choice("Mouse wheel:", WheelNames, Wheel);
                note("Ctrl+mouse wheel always zooms at the mouse pointer.");
                CheckBox clickSteps = check("Mouse buttons step through the pictures: left click previous, right click next", ClickSteps);
                Label dragLabel = note("Move a zoomed-in picture by dragging it with:");
                dragLabel.ForeColor = Theme.Text;
                CheckBox dragL = check("Left button", (DragButtons & MouseButtons.Left) != 0);
                CheckBox dragM = check("Middle button", (DragButtons & MouseButtons.Middle) != 0);
                CheckBox dragR = check("Right button", (DragButtons & MouseButtons.Right) != 0);

                heading("Display");
                CheckBox smooth = check("Smooth zoom (the picture glides to its new size)", Smooth);
                ComboBox step = choice("Zoom step:", ZoomStepNames, ZoomStep);
                CheckBox gpu = check("Draw with the graphics card (smoothest; the processor takes over if it can't)", Gpu);
                note("Now: " + drawingStatus);
                ComboBox back = choice("Background:", BackgroundNames, Background);
                CheckBox wrap = check("After the last file comes the first (and before the first, the last)", Wrap);

                y += Native.Px(10);
                Button ok = new Button();
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
                ok.Width = Native.Px(80);
                ok.Location = new Point(x + w - Native.Px(168), y);
                Button cancel = new Button();
                cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel;
                cancel.Width = Native.Px(80);
                cancel.Location = new Point(x + w - Native.Px(80), y);
                foreach (Button b in new[] { ok, cancel })
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Theme.Input;
                    b.ForeColor = Theme.Text;
                    b.FlatAppearance.BorderColor = Theme.Border;
                    b.Height = Native.Px(30);
                }
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.ClientSize = new Size(x * 2 + w, y + ok.Height + Native.Px(14));
                foreach (Control c in f.Controls) { CheckBox cb = c as CheckBox; if (cb != null) cb.FlatStyle = FlatStyle.Standard; }
                f.HandleCreated += delegate
                {
                    int dark = Theme.Dark ? 1 : 0;
                    Native.DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
                };
                if (f.ShowDialog(owner) != DialogResult.OK) return false;

                FullScreenClicks = fsClicks.Checked;
                Enlarge = enlarge.SelectedIndex;
                ExitMiddle = exitMiddle.Checked;
                ConfirmExit = confirm.Checked;
                HideCursor = hide.Checked;
                Wheel = wheel.SelectedIndex;
                DragButtons = (dragL.Checked ? MouseButtons.Left : 0) | (dragM.Checked ? MouseButtons.Middle : 0) | (dragR.Checked ? MouseButtons.Right : 0);
                Smooth = smooth.Checked;
                ZoomStep = step.SelectedIndex;
                Gpu = gpu.Checked;
                Background = back.SelectedIndex;
                Wrap = wrap.Checked;
                ClickSteps = clickSteps.Checked;
                return true;
            }
        }
    }
}
