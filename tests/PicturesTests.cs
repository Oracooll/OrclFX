// Tests for decoding pictures for the preview: scaling to fit, camera rotation, files that aren't pictures.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class PicturesTests
    {
        static string Save(TempDir d, string name, int w, int h, ImageFormat format, int orientation = 0)
        {
            string path = Path.Combine(d.Path, name);
            using (Bitmap b = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Red); g.FillRectangle(Brushes.Blue, 0, 0, w / 2, h); }
                if (orientation != 0)
                {
                    // PropertyItem has no public constructor.
                    PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    p.Id = 0x0112; p.Type = 3; p.Len = 2; p.Value = BitConverter.GetBytes((ushort)orientation);
                    b.SetPropertyItem(p);
                }
                b.Save(path, format);
            }
            return path;
        }

        [Test]
        static void ScaledToFitButNeverEnlarged()
        {
            using (TempDir d = new TempDir())
            {
                using (Bitmap b = Pictures.Load(Save(d, "wide.png", 400, 200, ImageFormat.Png), new Size(100, 100)))
                    Assert.Equal("100x50", b.Width + "x" + b.Height, "fits the box");
                using (Bitmap b = Pictures.Load(Save(d, "small.jpg", 40, 30, ImageFormat.Jpeg), new Size(1000, 1000)))
                    Assert.Equal("40x30", b.Width + "x" + b.Height, "not enlarged");
            }
        }

        [Test]
        static void CameraRotationIsApplied()
        {
            using (TempDir d = new TempDir())
            using (Bitmap b = Pictures.Load(Save(d, "turned.jpg", 400, 200, ImageFormat.Jpeg, 6), new Size(1000, 1000)))
            {
                Assert.Equal("200x400", b.Width + "x" + b.Height, "upright");
                // Orientation 6: turn 90° clockwise, so the blue left half ends up at the top.
                Color top = b.GetPixel(100, 20), bottom = b.GetPixel(100, 380);
                Assert.True(top.B > 150 && top.R < 100, "blue at the top");
                Assert.True(bottom.R > 150 && bottom.B < 100, "red at the bottom");
            }
        }

        // Every camera orientation (1-8) comes out as GDI+'s RotateFlip makes it (the pictures are decoded with WIC).
        [Test]
        static void EveryOrientationMatchesRotateFlip()
        {
            RotateFlipType[] turns = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone,
                RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
            using (TempDir d = new TempDir())
                for (int o = 1; o <= 8; o++)
                {
                    string path = Path.Combine(d.Path, "o" + o + ".jpg");
                    using (Bitmap b = Quadrants())
                    {
                        PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                        p.Id = 0x0112; p.Type = 3; p.Len = 2; p.Value = BitConverter.GetBytes((ushort)o);
                        b.SetPropertyItem(p);
                        b.Save(path, ImageFormat.Jpeg);
                    }
                    using (Bitmap want = Quadrants())
                    using (Bitmap got = Pictures.Load(path, new Size(1000, 1000)))
                    {
                        want.RotateFlip(turns[o - 1]);
                        Assert.Equal(want.Width + "x" + want.Height, got.Width + "x" + got.Height, "size, orientation " + o);
                        foreach (Point at in new[] { new Point(10, 10), new Point(want.Width - 10, 10), new Point(10, want.Height - 10), new Point(want.Width - 10, want.Height - 10) })
                        {
                            Color w = want.GetPixel(at.X, at.Y), g = got.GetPixel(at.X, at.Y);
                            // (JPEG shifts colours a little.)
                            Assert.True(Math.Abs(w.R - g.R) < 40 && Math.Abs(w.G - g.G) < 40 && Math.Abs(w.B - g.B) < 40, "orientation " + o + " at " + at + ": expected " + w + ", got " + g);
                        }
                    }
                }
        }

        // Four colours in the four corners, so every turn and flip looks different.
        static Bitmap Quadrants()
        {
            Bitmap b = new Bitmap(120, 80);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.FillRectangle(Brushes.Red, 0, 0, 60, 40);
                g.FillRectangle(Brushes.Lime, 60, 0, 60, 40);
                g.FillRectangle(Brushes.Blue, 0, 40, 60, 40);
                g.FillRectangle(Brushes.Yellow, 60, 40, 60, 40);
            }
            return b;
        }

        [Test]
        static void HalfSizeForTheViewer()
        {
            using (TempDir d = new TempDir())
            {
                Pictures.PictureData p = Pictures.LoadEx(Save(d, "big.jpg", 800, 400, ImageFormat.Jpeg, 6), new Size(1000, 1000), false, new Size(500, 500));
                try
                {
                    Assert.Equal("400x800", p.Bitmap.Width + "x" + p.Bitmap.Height, "full, upright");
                    Assert.True(p.Half != null, "half made");
                    Assert.Equal("250x500", p.Half.Width + "x" + p.Half.Height, "half, upright");
                    Assert.Equal(PixelFormat.Format32bppPArgb, p.Bitmap.PixelFormat, "fast format");
                }
                finally { p.Bitmap.Dispose(); if (p.Half != null) p.Half.Dispose(); }
            }
        }

        [Test]
        static void NotAPictureIsLeftToTheShell()
        {
            using (TempDir d = new TempDir())
            {
                string fake = d.File("fake.jpg", "this is not a picture");
                bool threw = false;
                try { Pictures.Load(fake, new Size(100, 100)); } catch { threw = true; }
                Assert.True(threw, "fails, so the worker falls back to the shell's thumbnail");
                Assert.True(Pictures.IsPictureKind(@"C:\a\B.JPEG") && !Pictures.IsPictureKind(@"C:\a\b.heic"), "kinds");
                Assert.Equal("3x1", Pictures.Fit(new Size(3000, 1000), new Size(3, 3)).Width + "x" + Pictures.Fit(new Size(3000, 1000), new Size(3, 3)).Height, "fit");
            }
        }
    }
}
