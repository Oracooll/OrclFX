// OrclFX: the themes' colours, with no UI (covered by tests\): the built-in themes, the colours worked out from the
// few that make one of your own themes, the file that keeps your own themes (shared between computers through
// OneDrive, like the layouts), and which theme is shown when (chosen by hand, as Windows, or by the hour).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrclFileExplorer
{
    class Palette
    {
        public string Id, Name;
        public bool Dark;
        public bool Mine;           // one of your own themes (Id "my:" + name)
        public bool WindowsAccent;  // the accent is Windows' own (the Light and Dark themes)
        public bool NoTint;         // inactive panes are never tinted (the high-contrast themes keep full contrast)
        public Color Window, Bar, TabHover, Text, TextDim, Border, Hover, Input, Menu, Accent, Lock;
        public Color Title, TitleText, TitleTextDim, TitleHover, TitleDown, TitleLine, TitleAccent;
        // What the theme is made from (and what your own themes keep in the file); the rest is worked out.
        public Color BaseWindow, BaseText, BaseAccent, BaseTitle, BaseLine;

        public Palette Clone() { return (Palette)MemberwiseClone(); }

        // The same theme (your own themes are read again from their file, as new objects).
        public bool SameAs(Palette other) { return other != null && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase); }

        // The title bar has colours of its own (not the bars' grey): it's drawn in them, with a thicker line under it.
        public bool ColouredTitle { get { return Palettes.Distance(Title, Bar) > 24; } }

        // The background Windows gives the Explorer file lists in this theme's mode.
        public Color ListBack { get { return Dark ? Palettes.DarkListBack : Color.White; } }

        // Inactive panes get a see-through tint so their list looks like the theme's background (Windows doesn't let a
        // program colour the list itself): only when that background is far enough from the list's own.
        public bool Tinted { get { return !NoTint && Palettes.Distance(Window, ListBack) > 12; } }
    }

    static class Palettes
    {
        public static readonly Color DarkListBack = Color.FromArgb(25, 25, 25);
        public const byte ShadeAlpha = 64;   // the tint's strength over an inactive pane's list (of 255)

        public static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // a moved towards b by t (0 = a, 1 = b).
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // The biggest difference of the three channels.
        public static int Distance(Color a, Color b)
        {
            return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
        }

        // WCAG relative luminance (0 black - 1 white) and contrast ratio (1 - 21).
        public static double Luminance(Color c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }
        static double Lin(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        public static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }

        // The colour that, laid over the list's background at ShadeAlpha, shows as the theme's background
        // (as near as the strength allows).
        public static Color ShadeColor(Palette p)
        {
            double a = ShadeAlpha / 255.0;
            Color w = p.Window, l = p.ListBack;
            return Color.FromArgb(Solve(w.R, l.R, a), Solve(w.G, l.G, a), Solve(w.B, l.B, a));
        }
        static int Solve(int want, int under, double a)
        {
            double v = (want - under * (1 - a)) / a;
            return (int)Math.Round(Math.Max(0, Math.Min(255, v)));
        }

        // A whole theme from its background, writing, accent, title bar and the line under the title bar.
        public static Palette Derive(string id, string name, bool dark, Color window, Color text, Color accent, Color title, Color line)
        {
            Palette p = new Palette();
            p.Id = id; p.Name = name; p.Dark = dark;
            p.BaseWindow = window; p.BaseText = text; p.BaseAccent = accent; p.BaseTitle = title; p.BaseLine = line;
            p.Window = window;
            p.Text = text;
            p.Bar = Mix(window, text, 0.07);
            p.TabHover = Mix(window, text, 0.13);
            p.Hover = Mix(window, text, 0.15);
            p.Border = Mix(window, text, 0.22);
            p.TextDim = Mix(text, window, 0.40);
            p.Input = dark ? Mix(window, text, 0.06) : Mix(window, Color.White, 0.6);
            p.Menu = dark ? Mix(window, text, 0.05) : Mix(window, Color.White, 0.4);
            p.Accent = accent;
            p.Lock = dark ? C(0xf0c050) : C(0x9a6510);
            p.Title = title;
            if (!p.ColouredTitle)
            {
                p.Title = p.Bar; p.TitleText = p.Text; p.TitleTextDim = p.TextDim; p.TitleHover = p.Hover; p.TitleDown = p.Border;
                p.TitleLine = p.Border; p.TitleAccent = p.Accent;
            }
            else
            {
                bool darkTitle = Luminance(title) < 0.25;
                p.TitleText = darkTitle ? Mix(Color.White, title, 0.04) : Mix(Color.Black, title, 0.12);
                p.TitleTextDim = Mix(p.TitleText, title, 0.30);
                p.TitleHover = Mix(title, p.TitleText, 0.12);
                p.TitleDown = Mix(title, p.TitleText, 0.24);
                p.TitleLine = line;
                p.TitleAccent = Mix(line, p.TitleText, 0.25);
            }
            return p;
        }

        // ---- the built-in themes

        static List<Palette> builtIn;
        public static List<Palette> BuiltIn { get { if (builtIn == null) builtIn = MakeBuiltIn(); return builtIn; } }

        static List<Palette> MakeBuiltIn()
        {
            List<Palette> r = new List<Palette>();

            // Light and Dark: Windows' own look with its accent colour (as OrclFX has always been).
            Palette light = Derive("light", "Light", false, C(0xffffff), C(0x1a1a1a), C(0x0078d4), C(0xf0f0f0), C(0xd4d4d4));
            light.Bar = C(0xf0f0f0); light.TabHover = C(0xe2e2e2); light.TextDim = C(0x606060); light.Border = C(0xd4d4d4);
            light.Hover = C(0xdedede); light.Input = C(0xffffff); light.Menu = C(0xf9f9f9); light.Lock = C(0xb07d00);
            PlainTitle(light);
            light.WindowsAccent = true;
            r.Add(light);

            Palette paper = Derive("paper", "Paper", false, C(0xeee9df), C(0x2b2118), C(0x8a5a2b), C(0x7a5230), C(0xe07b24));
            paper.Bar = C(0xe2dbcc); paper.TabHover = C(0xd8cfbd); paper.TextDim = C(0x6e6150); paper.Border = C(0xcdc3af);
            paper.Hover = C(0xdcd2bf); paper.Input = C(0xf5f1e9); paper.Menu = C(0xefeae0); paper.Lock = C(0x9a6510);
            paper.TitleText = C(0xf7efe2); paper.TitleTextDim = C(0xd6c6ae); paper.TitleHover = C(0x8e6640);
            paper.TitleDown = C(0xa27a52); paper.TitleLine = C(0xe07b24); paper.TitleAccent = C(0xf0a040);
            r.Add(paper);

            r.Add(Derive("mint", "Mint", false, C(0xeaf4ef), C(0x17302a), C(0x1e7a5a), C(0x1f6e52), C(0x3fbf8f)));
            r.Add(Derive("terracotta", "Terracotta", false, C(0xf6ece6), C(0x331a12), C(0xb5502c), C(0xa1442a), C(0xf08a4b)));
            r.Add(Derive("lavender", "Lavender", false, C(0xefedf7), C(0x241c3a), C(0x6a4fb3), C(0x5b45a0), C(0xb39cf0)));

            Palette contrast = Derive("contrast", "High contrast", false, C(0xffffff), C(0x000000), C(0x0037a6), C(0x0b2a6f), C(0xffd400));
            contrast.TextDim = C(0x2e2e2e); contrast.Border = C(0x000000); contrast.Bar = C(0xf0f0f0); contrast.Hover = C(0xcfd8ee);
            contrast.TabHover = C(0xdde3f2); contrast.Lock = C(0x7a5000);
            contrast.TitleText = C(0xffffff); contrast.TitleTextDim = C(0xdfe6ff); contrast.TitleLine = C(0xffd400); contrast.TitleAccent = C(0xffd400);
            contrast.NoTint = true;
            r.Add(contrast);

            Palette dark = Derive("dark", "Dark", true, C(0x191919), C(0xffffff), C(0x0078d4), C(0x202020), C(0x3a3a3a));
            dark.Bar = C(0x202020); dark.TabHover = C(0x2d2d2d); dark.TextDim = C(0xa0a0a0); dark.Border = C(0x3a3a3a);
            dark.Hover = C(0x383838); dark.Input = C(0x2b2b2b); dark.Menu = C(0x2b2b2b); dark.Lock = C(0xf0c050);
            PlainTitle(dark);
            dark.WindowsAccent = true;
            r.Add(dark);

            r.Add(Derive("graphite", "Graphite", true, C(0x1d2025), C(0xe8eaed), C(0x6ea8fe), C(0x2a2e35), C(0x3d434c)));
            r.Add(Derive("midnight", "Midnight", true, C(0x121a33), C(0xe6ebff), C(0x7b9cff), C(0x1d2c63), C(0x5b7cfa)));
            r.Add(Derive("forest", "Forest", true, C(0x122019), C(0xe3f2ea), C(0x4cc38a), C(0x21503a), C(0x3fbf8f)));
            r.Add(Derive("plum", "Plum", true, C(0x1f1626), C(0xf0e6f6), C(0xc38bdc), C(0x4a2858), C(0xb46bd6)));

            Palette contrastDark = Derive("contrastdark", "High contrast dark", true, C(0x000000), C(0xffffff), C(0xffd400), C(0x000000), C(0xffd400));
            contrastDark.TextDim = C(0xd8d8d8); contrastDark.Border = C(0xffffff); contrastDark.Bar = C(0x0d0d0d); contrastDark.Hover = C(0x3a3300);
            contrastDark.TabHover = C(0x262200); contrastDark.Input = C(0x000000); contrastDark.Menu = C(0x0d0d0d); contrastDark.Lock = C(0xffd400);
            PlainTitle(contrastDark);
            contrastDark.TitleLine = C(0xffffff);
            contrastDark.NoTint = true;
            r.Add(contrastDark);
            return r;
        }

        // The title bar in the bars' own colours (after the bar colours were set by hand).
        static void PlainTitle(Palette p)
        {
            p.Title = p.Bar; p.TitleText = p.Text; p.TitleTextDim = p.TextDim; p.TitleHover = p.Hover; p.TitleDown = p.Border;
            p.TitleLine = p.Border; p.TitleAccent = p.Accent;
        }

        // A built-in theme or one of yours, or null.
        public static Palette Find(string id, List<Palette> mine)
        {
            if (id == null) return null;
            foreach (Palette p in BuiltIn) if (p.Id == id) return p;
            if (mine != null) foreach (Palette p in mine) if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public static string MineId(string name) { return "my:" + name; }
    }

    // Which theme is shown: one chosen by hand, the light or the dark one as Windows is set, or by the hour.
    static class ThemeChoice
    {
        public const int ByHand = 0, AsWindows = 1, ByHour = 2;

        public static string Pick(int auto, string manual, string light, string dark, bool windowsDark, int hour, int lightFrom, int lightTo)
        {
            if (auto == AsWindows) return windowsDark ? dark : light;
            if (auto == ByHour) return LightHour(hour, lightFrom, lightTo) ? light : dark;
            return manual;
        }

        // Light from lightFrom:00 until lightTo:00 (over midnight when lightTo is earlier; always when they're equal).
        public static bool LightHour(int hour, int from, int to)
        {
            if (from == to) return true;
            return from < to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        // The settings file's "theme=" from versions up to 1.1.057: 0 match Windows, 1 light, 2 dark, 3 Paper.
        public static void FromLegacy(int n, out int auto, out string manual)
        {
            auto = n == 0 ? AsWindows : ByHand;
            manual = n == 2 ? "dark" : n == 3 ? "paper" : "light";
        }

        // ... and the other way round, so an older version started again still finds about the same theme.
        public static int ToLegacy(int auto, Palette shown)
        {
            if (auto == AsWindows) return 0;
            if (shown != null && shown.Id == "paper") return 3;
            return shown != null && shown.Dark ? 2 : 1;
        }

        // "7-18"
        public static bool TryParseHours(string v, out int from, out int to)
        {
            from = to = 0;
            string[] p = (v ?? "").Split('-');
            return p.Length == 2 && int.TryParse(p[0], out from) && int.TryParse(p[1], out to) && from >= 0 && from <= 23 && to >= 0 && to <= 23;
        }
    }

    // Your own themes: a [name] line, then its colours. The file sits next to the shortcuts and layouts (OneDrive).
    static class ThemeFile
    {
        public const string Header = "# OrclFX themes: a [name] line, then its colours. Shared between computers through OneDrive.";

        public static List<Palette> Parse(string[] lines)
        {
            List<Palette> r = new List<Palette>();
            string name = null;
            Dictionary<string, string> keys = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    Add(r, name, keys);
                    name = CleanName(line.Substring(1, line.Length - 2));
                    keys = new Dictionary<string, string>();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (keys == null || eq < 0) continue;
                keys[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
            }
            Add(r, name, keys);
            return r;
        }

        static void Add(List<Palette> r, string name, Dictionary<string, string> k)
        {
            if (string.IsNullOrEmpty(name) || k == null) return;
            foreach (Palette p in r) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return; // the first of a name wins
            string d;
            bool dark = k.TryGetValue("dark", out d) && d == "1";
            Palette basis = Palettes.Find(dark ? "dark" : "light", null);
            Palette p2 = Palettes.Derive(Palettes.MineId(name), name, dark,
                Get(k, "window", basis.BaseWindow), Get(k, "text", basis.BaseText), Get(k, "accent", basis.BaseAccent),
                Get(k, "title", basis.BaseTitle), Get(k, "line", basis.BaseLine));
            p2.Mine = true;
            r.Add(p2);
        }

        static Color Get(Dictionary<string, string> k, string key, Color fallback)
        {
            string v;
            Color c;
            return k.TryGetValue(key, out v) && TryParseColor(v, out c) ? c : fallback;
        }

        public static bool TryParseColor(string v, out Color c)
        {
            c = Color.Empty;
            v = (v ?? "").Trim();
            if (v.StartsWith("#")) v = v.Substring(1);
            int n;
            if (v.Length != 6 || !int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return false;
            c = Palettes.C(n);
            return true;
        }

        public static string FormatColor(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        public static string Serialize(List<Palette> themes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (Palette p in themes)
            {
                sb.AppendLine();
                sb.AppendLine("[" + p.Name + "]");
                sb.AppendLine("dark=" + (p.Dark ? "1" : "0"));
                sb.AppendLine("window=" + FormatColor(p.BaseWindow));
                sb.AppendLine("text=" + FormatColor(p.BaseText));
                sb.AppendLine("accent=" + FormatColor(p.BaseAccent));
                sb.AppendLine("title=" + FormatColor(p.BaseTitle));
                sb.AppendLine("line=" + FormatColor(p.BaseLine));
            }
            return sb.ToString();
        }

        // A name as it can be stored: one line, no brackets, not empty, at most 40 characters.
        public static string CleanName(string name)
        {
            string s = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace("[", "").Replace("]", "").Trim();
            return s.Length > 40 ? s.Substring(0, 40).Trim() : s;
        }

        // name, or "name (2)", "name (3)" ... when a built-in or own theme has it already.
        public static string FreeName(string name, List<Palette> mine)
        {
            name = CleanName(name);
            if (name.Length == 0) name = "My theme";
            string n = name;
            for (int i = 2; Taken(n, mine); i++) n = name + " (" + i + ")";
            return n;
        }

        static bool Taken(string n, List<Palette> mine)
        {
            foreach (Palette p in Palettes.BuiltIn) if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (Palette p in mine) if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<Palette> Read(string file)
        {
            if (!File.Exists(file)) return new List<Palette>();
            return Parse(ShortcutList.ReadLines(file)); // a damaged file is read from its backup
        }

        // Adds the theme, or replaces the one called oldName (or by its own name), re-reading the file under its lock
        // so another computer's or window's themes saved meanwhile are kept. Returns the themes now in the file.
        public static List<Palette> Save(string file, Palette theme, string oldName)
        {
            return Change(file, delegate(List<Palette> all)
            {
                int i = all.FindIndex(delegate(Palette p) { return string.Equals(p.Name, oldName ?? theme.Name, StringComparison.OrdinalIgnoreCase); });
                if (i >= 0) all[i] = theme; else all.Add(theme);
            });
        }

        public static List<Palette> Delete(string file, string name)
        {
            return Change(file, delegate(List<Palette> all) { all.RemoveAll(delegate(Palette p) { return string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase); }); });
        }

        static List<Palette> Change(string file, Action<List<Palette>> change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            return Util.WithFileLock(file, delegate
            {
                List<Palette> all = Read(file);
                change(all);
                Util.WriteAllTextAtomic(file, Serialize(all));
                return all;
            });
        }
    }
}
