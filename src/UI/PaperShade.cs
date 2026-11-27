// OrclFX: the Paper theme's beige tint over an inactive pane's file list.
// Windows doesn't let a program change the Explorer view's own background, so a see-through layered window lies on
// top of it instead. It takes no clicks, keys or drops: the mouse goes straight through to the list underneath
// (WS_EX_TRANSPARENT and HTTRANSPARENT), so the first click still activates the pane as before.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class PaperShade : Control
    {
        // Theme.Shade laid over the list at this strength shows as the theme's background; the writing stays readable.
        public const byte Alpha = Palettes.ShadeAlpha;

        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);

        public PaperShade()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Dock = DockStyle.Fill;
            Visible = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 /* WS_EX_LAYERED */ | 0x20 /* WS_EX_TRANSPARENT */ | 0x08000000 /* WS_EX_NOACTIVATE */;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetLayeredWindowAttributes(Handle, 0, Alpha, 2 /* LWA_ALPHA */);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Shade)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; } // WM_NCHITTEST: HTTRANSPARENT
            base.WndProc(ref m);
        }
    }
}
