// Tests for turning pictures without losing quality (EXIF orientation for JPEG, re-saving lossless formats).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class RotationTests
    {
        static string Picture(TempDir d, string name, int w, int h, ImageFormat f, int orientation = 0)
        {
            string path = Path.Combine(d.Path, name);
            using (Bitmap b = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Red); g.FillRectangle(Brushes.Blue, 0, 0, w / 2, h); }
                if (orientation != 0)
                {
                    PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    p.Id = 0x0112; p.Type = 3; p.Len = 2; p.Value = BitConverter.GetBytes((ushort)orientation);
                    b.SetPropertyItem(p);
                }
                b.Save(path, f);
            }
            return path;
        }

        static byte[] ImageData(string jpeg)
        {
            // Everything from the start-of-scan marker on: the compressed picture itself.
            byte[] b = File.ReadAllBytes(jpeg);
            for (int i = 2; i + 1 < b.Length; i++) if (b[i] == 0xFF && b[i + 1] == 0xDA) { byte[] r = new byte[b.Length - i]; Array.Copy(b, i, r, 0, r.Length); return r; }
            return b;
        }

        [Test]
        static void OrientationCycles()
        {
            int o = 1;
            for (int i = 0; i < 4; i++) o = Exif.Turn(o, true);
            Assert.Equal(1, o, "four right turns");
            Assert.Equal(6, Exif.Turn(1, true), "one right turn");
            Assert.Equal(8, Exif.Turn(1, false), "one left turn");
            for (int k = 1; k <= 8; k++) Assert.Equal(k, Exif.Turn(Exif.Turn(k, true), false), "right then left from " + k);
        }

        [Test]
        static void JpegWithOrientationIsTurnedInPlace()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "a.jpg", 400, 200, ImageFormat.Jpeg, 1);
                byte[] before = ImageData(f);
                Assert.Equal(null, Rotation.Turn(f, true), "turned");
                Assert.Equal(6, Exif.ReadOrientation(File.ReadAllBytes(f)), "orientation now 6");
                Assert.True(Convert.ToBase64String(before) == Convert.ToBase64String(ImageData(f)), "picture data untouched");
                using (Bitmap b = Pictures.Load(f, new Size(1000, 1000))) Assert.Equal("200x400", b.Width + "x" + b.Height, "shown upright");
            }
        }

        [Test]
        static void JpegWithoutExifGetsAnOrientation()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "b.jpg", 400, 200, ImageFormat.Jpeg);
                Assert.True(Exif.FindOrientation(File.ReadAllBytes(f), out _be, out _a) == -2 || Exif.ReadOrientation(File.ReadAllBytes(f)) == 1, "starts without a turn");
                byte[] before = ImageData(f);
                Assert.Equal(null, Rotation.Turn(f, false), "turned");
                Assert.Equal(8, Exif.ReadOrientation(File.ReadAllBytes(f)), "orientation 8 (left)");
                Assert.True(Convert.ToBase64String(before) == Convert.ToBase64String(ImageData(f)), "picture data untouched");
                using (Bitmap b = Pictures.Load(f, new Size(1000, 1000)))
                {
                    Assert.Equal("200x400", b.Width + "x" + b.Height, "shown upright");
                    Color top = b.GetPixel(100, 380);
                    Assert.True(top.B > 150, "turned left: the blue left half is at the bottom");
                }
            }
        }
        static bool _be; static int _a;

        [Test]
        static void PngIsTurnedAndSaved()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "c.png", 40, 20, ImageFormat.Png);
                Assert.Equal(null, Rotation.Turn(f, true), "turned");
                using (Bitmap b = new Bitmap(f))
                {
                    Assert.Equal("20x40", b.Width + "x" + b.Height, "size");
                    Assert.True(b.GetPixel(10, 5).B > 150 && b.GetPixel(10, 35).R > 150, "turned right: blue at the top");
                }
            }
        }

        [Test]
        static void PngWithAnotherResolutionKeepsItsPixels()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "dpi.png");
                using (Bitmap b = new Bitmap(40, 20, PixelFormat.Format32bppArgb))
                {
                    b.SetResolution(72, 72);
                    using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(128, 255, 0, 0)); g.FillRectangle(Brushes.Blue, 0, 0, 20, 20); }
                    b.Save(f, ImageFormat.Png);
                }
                Assert.Equal(null, Rotation.Turn(f, true), "turned");
                using (Bitmap b = new Bitmap(f))
                {
                    Assert.Equal("20x40", b.Width + "x" + b.Height, "same pixels, turned (not rescaled by the 72 dpi)");
                    Assert.Equal(72, (int)Math.Round(b.HorizontalResolution), "resolution kept");
                    Assert.Equal(128, (int)b.GetPixel(10, 35).A, "half-transparent pixels unchanged");
                    Assert.True(b.GetPixel(10, 5).B > 200, "blue at the top");
                }
            }
        }

        [Test]
        static void WhatCantBeTurnedSafelyIsLeftAlone()
        {
            using (TempDir d = new TempDir())
            {
                string gif = Picture(d, "e.gif", 40, 20, ImageFormat.Gif);
                string bad = d.File("f.jpg", "ÿØ not really a jpeg");
                byte[] gifBefore = File.ReadAllBytes(gif), badBefore = File.ReadAllBytes(bad);
                Assert.True(Rotation.Turn(gif, true) != null, "a GIF is refused (its palette would change)");
                Assert.True(Rotation.Turn(bad, true) != null, "a JPEG that can't be understood is refused");
                Assert.True(Convert.ToBase64String(gifBefore) == Convert.ToBase64String(File.ReadAllBytes(gif)), "the GIF is unchanged");
                Assert.True(Convert.ToBase64String(badBefore) == Convert.ToBase64String(File.ReadAllBytes(bad)), "the broken JPEG is unchanged");
                Assert.Equal(0, Directory.GetFiles(d.Path, "*.tmp").Length + Directory.GetFiles(d.Path, "*.bak").Length, "no leftovers");
            }
        }

        [Test]
        static void FillBytesBeforeAMarkerAreSkipped()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "g.jpg", 64, 32, ImageFormat.Jpeg, 1);
                byte[] b = File.ReadAllBytes(f);
                byte[] padded = new byte[b.Length + 2];
                padded[0] = 0xFF; padded[1] = 0xD8; padded[2] = 0xFF; padded[3] = 0xFF; // two fill bytes after SOI
                Array.Copy(b, 2, padded, 4, b.Length - 2);
                File.WriteAllBytes(f, padded);
                Assert.Equal(null, Rotation.Turn(f, true), "turned");
                Assert.Equal(6, Exif.ReadOrientation(File.ReadAllBytes(f)), "the existing tag was found");
            }
        }

        [Test]
        static void DetailsForTheInfoBox()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "d.jpg", 300, 200, ImageFormat.Jpeg);
                Pictures.PictureData p = Pictures.LoadEx(f, new Size(100, 100), true);
                Assert.Equal("300x200", p.Original.Width + "x" + p.Original.Height, "original size");
                Assert.Equal("d.jpg", p.Info[0], "name");
                Assert.True(p.Info[1].StartsWith("300 × 200 pixels"), "dimensions: " + p.Info[1]);
                p.Bitmap.Dispose();
            }
        }
    }
}
