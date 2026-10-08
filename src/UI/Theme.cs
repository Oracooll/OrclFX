// OrclFX: Light/dark colours and the themed menu renderer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OrclFileExplorer
{
    static class Theme
    {
        public static int Mode;          // 0 = match Windows, 1 = light, 2 = dark
        public static bool Dark;
        public static Color Window, Bar, TabHover, Text, TextDim, Border, Hover, Input, Menu, Accent, Lock;
        public static readonly Font IconFont = MakeIconFont(10f), SmallIconFont = MakeIconFont(7f);

        static Font MakeIconFont(float size)
        {
            Font f = new Font("Segoe Fluent Icons", size);
            if (f.Name == "Segoe Fluent Icons") return f;
            return new Font("Segoe MDL2 Assets", size);
        }

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // Tab colour labels (1-6; 0 = none), the same in both themes.
        public static readonly string[] TabColorNames = { "None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple" };
        static readonly int[] tabColors = { 0, 0xe74856, 0xf7630c, 0xffb900, 0x16c60c, 0x0078d4, 0x8764b8 };
        public static Color TabColor(int i) { return C(tabColors[i >= 1 && i < tabColors.Length ? i : 0]); }

        // A small square of the colour, for the menu (made once, kept for the session).
        static readonly Bitmap[] swatches = new Bitmap[7];
        public static Image TabColorSwatch(int i)
        {
            if (i < 1 || i >= swatches.Length) return null;
            if (swatches[i] == null)
            {
                int s = Native.Px(12);
                Bitmap b = new Bitmap(s, s);
                using (Graphics g = Graphics.FromImage(b))
                using (SolidBrush br = new SolidBrush(TabColor(i)))
                    g.FillRectangle(br, 0, 0, s, s);
                swatches[i] = b;
            }
            return swatches[i];
        }

        public static bool WindowsPrefersDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        static Color ReadAccent()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int) { int c = (int)v; return Color.FromArgb(c & 255, (c >> 8) & 255, (c >> 16) & 255); }
            }
            catch { }
            return C(0x0078d4);
        }

        public static void Update()
        {
            Dark = Mode == 2 || (Mode == 0 && WindowsPrefersDark());
            Accent = ReadAccent();
            if (Dark)
            {
                Window = C(0x191919); Bar = C(0x202020); TabHover = C(0x2d2d2d); Text = C(0xffffff); TextDim = C(0xa0a0a0);
                Border = C(0x3a3a3a); Hover = C(0x383838); Input = C(0x2b2b2b); Menu = C(0x2b2b2b); Lock = C(0xf0c050);
            }
            else
            {
                Window = C(0xffffff); Bar = C(0xf0f0f0); TabHover = C(0xe2e2e2); Text = C(0x1a1a1a); TextDim = C(0x606060);
                Border = C(0xd4d4d4); Hover = C(0xdedede); Input = C(0xffffff); Menu = C(0xf9f9f9); Lock = C(0xb07d00);
            }
            // Tell Windows which mode this app wants and drop its cached decision, so Explorer views
            // created after a live switch pick up the new mode (not just the ones created at startup).
            try { Native.SetPreferredAppMode(Dark ? 2 : 3); Native.RefreshImmersiveColorPolicyState(); Native.FlushMenuThemes(); } catch { }
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Menu; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Menu; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Menu; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Menu; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Menu; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Hover; } }
        public override Color CheckBackground { get { return Theme.Menu; } }
        public override Color CheckSelectedBackground { get { return Theme.Hover; } }
        public override Color CheckPressedBackground { get { return Theme.Hover; } }
    }

    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, "", Theme.IconFont, e.ImageRectangle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
