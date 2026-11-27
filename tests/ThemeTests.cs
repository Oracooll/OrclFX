// Tests for the themes: the built-in palettes, own themes and their file, which theme is shown when, the old
// "theme=" setting and the text settings.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class ThemeTests
    {
        [Test]
        static void BuiltInThemesReadWell()
        {
            HashSet<string> ids = new HashSet<string>();
            int light = 0, dark = 0;
            foreach (Palette p in Palettes.BuiltIn)
            {
                Assert.True(ids.Add(p.Id), "one theme per id: " + p.Id);
                if (p.Dark) dark++; else light++;
                Assert.True(Palettes.Contrast(p.Text, p.Window) >= 7, p.Name + ": writing on the background " + Palettes.Contrast(p.Text, p.Window).ToString("0.0"));
                Assert.True(Palettes.Contrast(p.Text, p.Bar) >= 7, p.Name + ": writing on the bars");
                Assert.True(Palettes.Contrast(p.TextDim, p.Window) >= 3, p.Name + ": dimmed writing " + Palettes.Contrast(p.TextDim, p.Window).ToString("0.0"));
                Assert.True(Palettes.Contrast(p.TitleText, p.Title) >= 4.5, p.Name + ": title bar writing " + Palettes.Contrast(p.TitleText, p.Title).ToString("0.0"));
                Assert.True(Palettes.Contrast(p.Text, p.Input) >= 7, p.Name + ": writing in the address box");
                Assert.True(p.Dark == (Palettes.Luminance(p.Window) < 0.2), p.Name + ": light or dark as its background");
            }
            Assert.Equal(6, light, "light themes");
            Assert.Equal(6, dark, "dark themes");
        }

        [Test]
        static void TheOldThemesLookAsBefore()
        {
            Palette light = Palettes.Find("light", null), dark = Palettes.Find("dark", null), paper = Palettes.Find("paper", null);
            Assert.Equal(Color.FromArgb(255, 255, 255).ToArgb(), light.Window.ToArgb(), "light background");
            Assert.Equal(Color.FromArgb(0xf0, 0xf0, 0xf0).ToArgb(), light.Title.ToArgb(), "light title bar is the bar");
            Assert.True(!light.ColouredTitle && !light.Tinted && light.WindowsAccent, "light: plain, untinted, Windows' accent");
            Assert.True(!dark.ColouredTitle && !dark.Tinted && dark.WindowsAccent, "dark: plain, untinted, Windows' accent");
            Assert.Equal(Color.FromArgb(0xee, 0xe9, 0xdf).ToArgb(), paper.Window.ToArgb(), "Paper beige");
            Assert.Equal(Color.FromArgb(0x7a, 0x52, 0x30).ToArgb(), paper.Title.ToArgb(), "Paper brown title bar");
            Assert.True(paper.ColouredTitle && paper.Tinted, "Paper: coloured title bar, tinted inactive panes");
            Color shade = Palettes.ShadeColor(paper);
            Assert.True(Palettes.Distance(shade, Color.FromArgb(187, 167, 127)) <= 1, "Paper's tint as before: " + shade);
        }

        [Test]
        static void TheTintShowsAsTheBackground()
        {
            foreach (Palette p in Palettes.BuiltIn)
            {
                if (!p.Tinted) continue;
                Color s = Palettes.ShadeColor(p), l = p.ListBack;
                double a = Palettes.ShadeAlpha / 255.0;
                Color seen = Color.FromArgb((int)Math.Round(s.R * a + l.R * (1 - a)), (int)Math.Round(s.G * a + l.G * (1 - a)), (int)Math.Round(s.B * a + l.B * (1 - a)));
                Assert.True(Palettes.Distance(seen, p.Window) <= 12, p.Name + ": tinted list " + seen + " near the background " + p.Window);
            }
            Assert.True(!Palettes.Find("contrast", null).Tinted && !Palettes.Find("contrastdark", null).Tinted, "high contrast keeps full contrast");
        }

        [Test]
        static void OwnThemesSavedAndReadBack()
        {
            Palette t = Palettes.Derive(Palettes.MineId("Sea"), "Sea", false, Color.FromArgb(230, 240, 250), Color.FromArgb(10, 20, 40),
                Color.FromArgb(0, 90, 200), Color.FromArgb(0, 60, 140), Color.FromArgb(255, 200, 0));
            t.Mine = true;
            List<Palette> back = ThemeFile.Parse(ThemeFile.Serialize(new List<Palette> { t }).Split('\n'));
            Assert.Equal(1, back.Count, "one theme");
            Palette b = back[0];
            Assert.Equal("Sea", b.Name, "name");
            Assert.Equal("my:Sea", b.Id, "id");
            Assert.True(b.Mine && !b.Dark, "own, light");
            Assert.Equal(t.Window.ToArgb(), b.Window.ToArgb(), "background");
            Assert.Equal(t.Title.ToArgb(), b.Title.ToArgb(), "title bar");
            Assert.Equal(t.TitleLine.ToArgb(), b.TitleLine.ToArgb(), "line");
            Assert.Equal(t.Border.ToArgb(), b.Border.ToArgb(), "worked-out colours too");
            Assert.True(b.SameAs(t) && !b.SameAs(Palettes.Find("paper", null)) && !b.SameAs(null), "read again: the same theme by id");
        }

        [Test]
        static void ADamagedThemeFileStillLoads()
        {
            List<Palette> r = ThemeFile.Parse(new[] {
                "# comment", "window=#ffffff", "[Good]", "dark=1", "window=#101020", "text=#eeeeee", "accent=nonsense", "",
                "[ ]", "window=#000000", "[good]", "window=#ff0000", "[Rest [x]]", "unknown=1" });
            Assert.Equal(2, r.Count, "the one without a name and the second \"good\" are left out");
            Assert.Equal("Good", r[0].Name, "first");
            Assert.True(r[0].Dark, "dark");
            Assert.Equal(Color.FromArgb(0x10, 0x10, 0x20).ToArgb(), r[0].Window.ToArgb(), "its background");
            Assert.Equal(Palettes.Find("dark", null).BaseAccent.ToArgb(), r[0].BaseAccent.ToArgb(), "a bad colour is the dark theme's");
            Assert.Equal("Rest x", r[1].Name, "brackets taken out of a name");
        }

        [Test]
        static void FreeNames()
        {
            List<Palette> mine = ThemeFile.Parse(new[] { "[Sea]", "[Sea (2)]" });
            Assert.Equal("Sea (3)", ThemeFile.FreeName("Sea", mine), "taken twice");
            Assert.Equal("Paper (2)", ThemeFile.FreeName("paper", mine).Replace("paper", "Paper"), "a built-in name");
            Assert.Equal("Lake", ThemeFile.FreeName(" Lake ", mine), "free, trimmed");
            Assert.Equal("My theme", ThemeFile.FreeName("  ", mine), "empty");
        }

        [Test]
        static void OwnThemesFileKeepsOthersChanges()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, @"OneDrive\themes.txt");
                Palette a = Palettes.Derive("my:A", "A", false, Color.White, Color.Black, Color.Blue, Color.White, Color.Gray);
                Palette b = Palettes.Derive("my:B", "B", true, Color.Black, Color.White, Color.Yellow, Color.Black, Color.Gray);
                ThemeFile.Save(f, a, null);
                ThemeFile.Save(f, b, null);
                Palette a2 = Palettes.Derive("my:A2", "A2", false, Color.White, Color.Black, Color.Red, Color.White, Color.Gray);
                List<Palette> all = ThemeFile.Save(f, a2, "A");
                Assert.Equal(2, all.Count, "renamed in place");
                Assert.Equal("A2", all[0].Name, "first is the renamed one");
                all = ThemeFile.Delete(f, "a2");
                Assert.Equal(1, all.Count, "deleted (any case)");
                Assert.Equal("B", ThemeFile.Read(f)[0].Name, "the file holds what's left");
            }
        }

        [Test]
        static void WhichThemeWhen()
        {
            Assert.Equal("mint", ThemeChoice.Pick(ThemeChoice.ByHand, "mint", "light", "dark", true, 3, 7, 18), "by hand");
            Assert.Equal("dark", ThemeChoice.Pick(ThemeChoice.AsWindows, "mint", "light", "dark", true, 12, 7, 18), "Windows dark");
            Assert.Equal("paper", ThemeChoice.Pick(ThemeChoice.AsWindows, "mint", "paper", "dark", false, 12, 7, 18), "Windows light");
            Assert.Equal("paper", ThemeChoice.Pick(ThemeChoice.ByHour, "x", "paper", "plum", true, 7, 7, 18), "7:00 is light");
            Assert.Equal("plum", ThemeChoice.Pick(ThemeChoice.ByHour, "x", "paper", "plum", false, 18, 7, 18), "18:00 is dark");
            Assert.Equal("plum", ThemeChoice.Pick(ThemeChoice.ByHour, "x", "paper", "plum", false, 6, 7, 18), "6:00 is dark");
            Assert.True(ThemeChoice.LightHour(23, 20, 6) && ThemeChoice.LightHour(5, 20, 6) && !ThemeChoice.LightHour(6, 20, 6), "over midnight");
            Assert.True(ThemeChoice.LightHour(13, 9, 9), "from = until: always light");
            int f, t;
            Assert.True(ThemeChoice.TryParseHours("7-18", out f, out t) && f == 7 && t == 18, "hours read");
            Assert.True(!ThemeChoice.TryParseHours("7-24", out f, out t) && !ThemeChoice.TryParseHours("x", out f, out t), "bad hours");
        }

        [Test]
        static void TheOldThemeSetting()
        {
            int auto; string manual;
            ThemeChoice.FromLegacy(0, out auto, out manual);
            Assert.Equal(ThemeChoice.AsWindows, auto, "0: match Windows");
            ThemeChoice.FromLegacy(2, out auto, out manual);
            Assert.True(auto == ThemeChoice.ByHand && manual == "dark", "2: dark");
            ThemeChoice.FromLegacy(3, out auto, out manual);
            Assert.True(auto == ThemeChoice.ByHand && manual == "paper", "3: Paper");
            Assert.Equal(3, ThemeChoice.ToLegacy(ThemeChoice.ByHand, Palettes.Find("paper", null)), "Paper back");
            Assert.Equal(2, ThemeChoice.ToLegacy(ThemeChoice.ByHand, Palettes.Find("midnight", null)), "any dark one: dark");
            Assert.Equal(1, ThemeChoice.ToLegacy(ThemeChoice.ByHand, Palettes.Find("mint", null)), "any light one: light");
            Assert.Equal(0, ThemeChoice.ToLegacy(ThemeChoice.AsWindows, Palettes.Find("mint", null)), "as Windows: match Windows");
            Assert.Equal(1, ThemeChoice.ToLegacy(ThemeChoice.ByHour, Palettes.Find("mint", null)), "by the hour (older versions have none): the one shown");
        }

        [Test]
        static void TextSettings()
        {
            int size, weight;
            TextStyle.Read(new[] { "touch=0", "bold=1" }, out size, out weight);
            Assert.True(size == 0 && weight == TextStyle.Bold, "1.1.055's bold=1");
            TextStyle.Read(new[] { "bold=1", "textsize=2", "textweight=1" }, out size, out weight);
            Assert.True(size == 2 && weight == TextStyle.Semibold, "textweight wins over bold");
            TextStyle.Read(new[] { "textweight=0", "bold=1" }, out size, out weight);
            Assert.Equal(TextStyle.Normal, weight, "textweight wins in any order");
            TextStyle.Read(new[] { "textsize=9", "textweight=7" }, out size, out weight);
            Assert.True(size == 0 && weight == 0, "out of range: normal");
        }
    }
}
