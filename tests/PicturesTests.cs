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
