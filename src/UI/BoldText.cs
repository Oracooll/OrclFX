// OrclFX: bold text (the B button in the title bar; settings key "bold"): every text the app shows is drawn in bold,
// easier to read for weaker eyes: the window's own text (tabs, address bar, title, status bar, menus, tooltips, the
// Space Viewer, Find), the tree, the Shortcuts list, the preview's text and the file lists: Windows 11's own list
// ignores a program's font (IVisualProperties.SetFont answers E_NOTIMPL), so in bold mode the views are the older
// list control (BrowserTab.OldList), which takes WM_SETFONT. Like touch mode, it is read before the window is built
// and switching it rebuilds the window (tabs and layout kept).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class BoldText
    {
        public static bool On;

        // The setting from the settings file's lines (before the window is built).
        public static void ReadEarly(string[] lines)
        {
            if (lines == null) return;
            foreach (string l in lines)
                if (l.StartsWith("bold=", StringComparison.Ordinal)) On = l.Substring(5).Trim() == "1";
        }

        // The font as the app shows it: bold in bold mode (a font that's bold already stays as it is).
        public static Font Ui(Font f)
        {
            if (!On || f.Bold) return f;
            return new Font(f, f.Style | FontStyle.Bold);
        }

        public static FontStyle Style { get { return On ? FontStyle.Bold : FontStyle.Regular; } }

        // ---- tooltips: Windows draws them in its own font, so in bold mode they're drawn here

        static Font tipFont;
        static Font TipFont { get { if (tipFont == null) tipFont = new Font(SystemFonts.StatusFont, FontStyle.Bold); return tipFont; } }

        public static ToolTip NewTips() { ToolTip t = new ToolTip(); Tips(t); return t; }

        public static void Tips(ToolTip t)
        {
            if (!On) return;
            t.OwnerDraw = true;
            t.Popup += delegate(object s, PopupEventArgs e)
            {
                Size z = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl), TipFont);
                e.ToolTipSize = new Size(z.Width + Native.Px(10), z.Height + Native.Px(8));
            };
            t.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                e.DrawBackground();
                e.DrawBorder();
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, TipFont, e.Bounds, SystemColors.InfoText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
        }

        // ---- native windows (the tree's list): WM_SETFONT with a bold copy of the font they have

        public static void Window(IntPtr hwnd)
        {
            if (!On || hwnd == IntPtr.Zero) return;
            IntPtr cur = Native.SendMessage(hwnd, 0x31 /* WM_GETFONT */, IntPtr.Zero, IntPtr.Zero);
            Font f = cur == IntPtr.Zero ? SystemFonts.IconTitleFont : Font.FromHfont(cur);
            if (f.Bold) return;
            // (The font handle is never freed: the window keeps using it, and there's one per tree.)
            using (Font b = new Font(f, FontStyle.Bold)) Native.SendMessage(hwnd, 0x30 /* WM_SETFONT */, b.ToHfont(), (IntPtr)1);
        }
    }
}
