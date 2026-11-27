// OrclFX: text size and weight (Themes › Text and contrast; the B button switches bold on and off; settings keys
// "textsize" and "textweight", and "bold" from 1.1.055-1.1.057), for easier reading.
// The weight (normal, semibold or bold) applies to every text the app shows: the window's own text (tabs, address
// bar, title, status bar, menus, tooltips, the Space Viewer, Find), the tree, the Shortcuts list, the preview's text
// and, in the light themes, the file lists. The size enlarges all of the window with its text (Native.Px grows with
// it), so nothing is cut off. Windows 11's own file list ignores a program's font (IVisualProperties.SetFont answers
// E_NOTIMPL), so with either one changed the views are the older list control (BrowserTab.OldList), which takes
// WM_SETFONT; dark themes keep Windows' own list and font (the older list can't be made fully dark).
// Like touch mode, both are read before the window is built, and changing them rebuilds the window.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class TextStyle
    {
        public static readonly int[] SizePercents = { 100, 115, 130, 150 };
        public static readonly string[] SizeNames = { "Normal", "Larger (115 %)", "Large (130 %)", "Largest (150 %)" };
        public static readonly string[] WeightNames = { "Normal", "Semibold", "Bold" };
        public const int Normal = 0, Semibold = 1, Bold = 2;

        public static int Size;    // index into SizePercents
        public static int Weight;  // Normal, Semibold or Bold

        public static float Zoom { get { return SizePercents[Size] / 100f; } }
        public static bool Custom { get { return Size > 0 || Weight > 0; } }

        // The settings from the settings file's lines (before the window is built).
        public static void ReadEarly(string[] lines)
        {
            int size, weight;
            Read(lines, out size, out weight);
            Size = size; Weight = weight;
        }

        public static void Read(string[] lines, out int size, out int weight)
        {
            size = 0; weight = 0;
            bool weightSet = false;
            if (lines == null) return;
            int n;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                int eq = l.IndexOf('=');
                if (eq < 0) continue;
                string k = l.Substring(0, eq), v = l.Substring(eq + 1).Trim();
                if (k == "textsize" && int.TryParse(v, out n) && n >= 0 && n < SizePercents.Length) size = n;
                else if (k == "textweight" && int.TryParse(v, out n) && n >= 0 && n <= Bold) { weight = n; weightSet = true; }
                else if (k == "bold" && !weightSet) weight = v == "1" ? Bold : Normal; // 1.1.055-1.1.057
            }
        }

        // The app's main font: Segoe UI 9 pt, in the chosen size and weight.
        public static Font MainFont() { return Ui(new Font("Segoe UI", 9f)); }

        // A font as the app shows it: in the chosen size and weight (a font that's bold already stays bold). Semibold
        // is Segoe UI's own semibold face; other faces, which have none, stay regular.
        public static Font Ui(Font f)
        {
            if (!Custom) return f;
            float size = f.SizeInPoints * Zoom;
            FontStyle style = f.Style;
            string family = f.FontFamily.Name;
            if (!f.Bold && Weight == Bold) style |= FontStyle.Bold;
            else if (!f.Bold && Weight == Semibold && family == "Segoe UI") family = "Segoe UI Semibold";
            return new Font(family, size, style, GraphicsUnit.Point);
        }

        // An icon font's size (the glyphs in buttons grow with the text).
        public static float IconSize(float size) { return size * Zoom; }

        // ---- tooltips: Windows draws them in its own font, so with a changed text they're drawn here

        static Font tipFont;
        static Font TipFont { get { if (tipFont == null) tipFont = Ui(SystemFonts.StatusFont); return tipFont; } }

        public static ToolTip NewTips() { ToolTip t = new ToolTip(); Tips(t); return t; }

        public static void Tips(ToolTip t)
        {
            if (!Custom) return;
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

        // ---- native lists (the tree, the older file list): WM_SETFONT with Windows' list font in the chosen style

        static IntPtr listFont;

        public static void Window(IntPtr hwnd)
        {
            if (!Custom || hwnd == IntPtr.Zero) return;
            // (One font handle for every list, never freed: the windows keep using it.)
            if (listFont == IntPtr.Zero) using (Font f = Ui(SystemFonts.IconTitleFont)) listFont = f.ToHfont();
            IntPtr cur = Native.SendMessage(hwnd, 0x31 /* WM_GETFONT */, IntPtr.Zero, IntPtr.Zero);
            if (cur != listFont) Native.SendMessage(hwnd, 0x30 /* WM_SETFONT */, listFont, (IntPtr)1);
        }
    }
}
