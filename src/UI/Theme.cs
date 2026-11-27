// OrclFX: the theme shown (its colours, from Core\Palettes.cs) and the themed menu renderer.
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
        // ---- what's chosen (the settings file's theme* keys; Themes window, title bar, menu › Theme)
        public static int Auto = ThemeChoice.AsWindows;      // ThemeChoice.ByHand, AsWindows or ByHour
        public static int AutoKind = ThemeChoice.AsWindows;  // the automatic kind the title bar's first button turns on
        public static string Manual = "light";               // the theme chosen by hand
        public static string LightId = "light", DarkId = "dark"; // the light and the dark one, for the automatic kinds
        public static int LightFrom = 7, LightTo = 18;       // by the hour: light from, until (hours, 0-23)
        public static int Borders = 1;                       // border contrast: 0 soft, 1 normal, 2 strong
        public static readonly string[] BorderNames = { "Soft", "Normal", "Strong" };
        public static List<Palette> Mine = new List<Palette>(); // your own themes (themes.txt in OneDrive)

        // ---- what's shown
        public static Palette Current;
        public static bool Dark;
        // Tinted: the theme's background differs from the Explorer file lists' (white, or Windows' dark grey), which
        // a program can't colour: inactive panes get a see-through tint of it laid over them (PaperShade), and the
        // active pane's list stays as Windows draws it.
        public static bool Tinted;
        public static bool ColouredTitle;   // the title bar has colours of its own, with light or dark writing
        public static Color Window, Bar, TabHover, Text, TextDim, Border, Hover, Input, Menu, Accent, Lock;
        public static Color Title, TitleText, TitleTextDim, TitleHover, TitleDown, TitleLine, TitleAccent;
        public static Color ListBack, Shade;

        static Font smallIconFont, iconFont;
        public static Font SmallIconFont { get { if (smallIconFont == null) smallIconFont = MakeIconFont(TextStyle.IconSize(7f)); return smallIconFont; } }
        // (A little bigger in touch mode, and with a bigger text size.)
        public static Font IconFont { get { if (iconFont == null) iconFont = MakeIconFont(TextStyle.IconSize(TouchMode.On ? 12f : 10f)); return iconFont; } }

        static Font MakeIconFont(float size)
        {
            Font f = new Font("Segoe Fluent Icons", size);
            if (f.Name == "Segoe Fluent Icons") return f;
            return new Font("Segoe MDL2 Assets", size);
        }

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // Tab colour labels (1-6; 0 = none), the same in every theme.
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

        public static Color WindowsAccent()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int) { int c = (int)v; return Color.FromArgb(c & 255, (c >> 8) & 255, (c >> 16) & 255); }
            }
            catch { }
            return C(0x0078d4);
        }

        // A built-in theme or one of yours, or null.
        public static Palette Find(string id) { return Palettes.Find(id, Mine); }

        // Every theme: the built-in ones, then yours.
        public static List<Palette> All()
        {
            List<Palette> r = new List<Palette>(Palettes.BuiltIn);
            r.AddRange(Mine);
            return r;
        }

        // The theme the settings ask for now. One that's gone (an own theme deleted on another computer) is Light
        // or Dark, as its place asks.
        public static Palette Wanted()
        {
            string id = ThemeChoice.Pick(Auto, Manual, LightId, DarkId, WindowsPrefersDark(), DateTime.Now.Hour, LightFrom, LightTo);
            Palette p = Find(id);
            if (p == null) p = Find(Auto != ThemeChoice.ByHand && id == DarkId ? "dark" : "light");
            return p;
        }

        // Border contrast: the lines between things moved towards the writing (strong) or the background (soft).
        public static Color BorderFor(Palette p, int borders)
        {
            if (borders == 2) return Palettes.Mix(p.Border, p.Text, 0.35);
            if (borders == 0) return Palettes.Mix(p.Border, p.Window, 0.45);
            return p.Border;
        }

        public static void Update()
        {
            Palette p = Wanted();
            Current = p;
            Dark = p.Dark;
            Window = p.Window; Bar = p.Bar; TabHover = p.TabHover; Text = p.Text; TextDim = p.TextDim; Border = p.Border;
            Hover = p.Hover; Input = p.Input; Menu = p.Menu; Accent = p.Accent; Lock = p.Lock;
            Title = p.Title; TitleText = p.TitleText; TitleTextDim = p.TitleTextDim; TitleHover = p.TitleHover;
            TitleDown = p.TitleDown; TitleLine = p.TitleLine; TitleAccent = p.TitleAccent;
            ColouredTitle = p.ColouredTitle;
            if (p.WindowsAccent) { Accent = WindowsAccent(); if (!ColouredTitle) TitleAccent = Accent; }
            Border = BorderFor(p, Borders);
            if (!ColouredTitle) { TitleLine = Border; TitleDown = Border; }
            Tinted = p.Tinted;
            ListBack = p.ListBack;
            Shade = Palettes.ShadeColor(p);
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
            // (Touch mode's taller items: the text in the middle, not at the top.)
            if (TouchMode.On && e.Item.IsOnDropDown) { Rectangle r = e.TextRectangle; r.Y = (e.Item.Height - r.Height) / 2; e.TextRectangle = r; }
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            if (TouchMode.On && e.Item != null && e.Item.IsOnDropDown) { Rectangle r = e.ArrowRectangle; r.Y = (e.Item.Height - r.Height) / 2; e.ArrowRectangle = r; }
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (TouchMode.On && e.Item.IsOnDropDown && e.Image != null)
            {
                Rectangle r = e.ImageRectangle;
                e.Graphics.DrawImage(e.Image, new Rectangle(r.X, (e.Item.Height - r.Height) / 2, r.Width, r.Height));
                return;
            }
            base.OnRenderItemImage(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle at = e.ImageRectangle;
            if (TouchMode.On) at = new Rectangle(at.X, 0, at.Width, e.Item.Height);
            TextRenderer.DrawText(e.Graphics, "", Theme.IconFont, at, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
