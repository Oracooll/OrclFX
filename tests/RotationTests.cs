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

        // A JPEG with camera data but no orientation entry has its image data turned; its other blocks (XMP ratings
        // and keywords, comments) must come through unchanged.
        [Test]
        static void JpegDataTurnKeepsTheOtherBlocks()
        {
            using (TempDir d = new TempDir())
            {
                string path = Path.Combine(d.Path, "tagged.jpg");
                using (Bitmap bmp = new Bitmap(64, 48))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Red); g.FillRectangle(Brushes.Blue, 0, 0, 32, 48); }
                    PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    p.Id = 0x010F; p.Type = 2; p.Value = System.Text.Encoding.ASCII.GetBytes("TestCam\0"); p.Len = p.Value.Length;
                    bmp.SetPropertyItem(p);
                    bmp.Save(path, ImageFormat.Jpeg);
                }
                // An XMP block, a colour profile and a comment after the camera data (where editors put them).
                byte[] all = InsertAfterExif(File.ReadAllBytes(path),
                    Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta tiff:Orientation=\"1\">Rating 5, keyword Holiday</x:xmpmeta>"),
                    Segment(0xE2, "ICC_PROFILE\0\u0001\u0001fake profile data"),
                    Segment(0xFE, "A comment to keep"));
                File.WriteAllBytes(path, all);
                Assert.Equal(1, Exif.ReadOrientation(all), "no orientation entry");
                Assert.Equal(null, Rotation.Turn(path, true), "turned");
                string text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));
                Assert.True(text.Contains("Rating 5, keyword Holiday"), "XMP kept");
                Assert.True(text.Contains("A comment to keep"), "comment kept");
                Assert.True(text.Contains("TestCam"), "camera data kept");
                int icc = 0;
                for (int at = text.IndexOf("ICC_PROFILE"); at >= 0; at = text.IndexOf("ICC_PROFILE", at + 1)) icc++;
                Assert.Equal(1, icc, "one colour profile");
                using (Image img = Image.FromFile(path)) Assert.Equal("48x64", img.Width + "x" + img.Height, "turned");
            }
        }

        // An XMP block before the camera data: GDI+ would drop the camera data, so the file isn't turned.
        [Test]
        static void JpegWithXmpBeforeExifIsLeftAlone()
        {
            using (TempDir d = new TempDir())
            {
                string path = Path.Combine(d.Path, "xmpfirst.jpg");
                using (Bitmap bmp = new Bitmap(64, 48))
                {
                    PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    p.Id = 0x010F; p.Type = 2; p.Value = System.Text.Encoding.ASCII.GetBytes("TestCam\0"); p.Len = p.Value.Length;
                    bmp.SetPropertyItem(p);
                    bmp.Save(path, ImageFormat.Jpeg);
                }
                byte[] b = File.ReadAllBytes(path);
                byte[] xmp = Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>Rating 5</x:xmpmeta>");
                byte[] all = new byte[b.Length + xmp.Length];
                Array.Copy(b, 0, all, 0, 2);
                Array.Copy(xmp, 0, all, 2, xmp.Length);
                Array.Copy(b, 2, all, 2 + xmp.Length, b.Length - 2);
                File.WriteAllBytes(path, all);
                Assert.True(Rotation.Turn(path, true) != null, "not turned");
                Assert.Equal(all.Length, File.ReadAllBytes(path).Length, "file unchanged");
            }
        }

        // A photo with more after its picture (a phone's motion-photo video): not turned through its image data.
        [Test]
        static void JpegWithDataAfterThePictureIsLeftAlone()
        {
            using (TempDir d = new TempDir())
            {
                string path = Path.Combine(d.Path, "motion.jpg");
                using (Bitmap bmp = new Bitmap(64, 48))
                {
                    PropertyItem p = (PropertyItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    p.Id = 0x010F; p.Type = 2; p.Value = System.Text.Encoding.ASCII.GetBytes("TestCam\0"); p.Len = p.Value.Length;
                    bmp.SetPropertyItem(p);
                    bmp.Save(path, ImageFormat.Jpeg);
                }
                byte[] b = File.ReadAllBytes(path);
                Assert.True(!Rotation.DataAfterImage(b), "a plain JPEG ends with its picture");
                byte[] video = System.Text.Encoding.ASCII.GetBytes("....ftypmp42 video data");
                byte[] all = new byte[b.Length + video.Length];
                Array.Copy(b, all, b.Length);
                Array.Copy(video, 0, all, b.Length, video.Length);
                File.WriteAllBytes(path, all);
                Assert.True(Rotation.DataAfterImage(all), "found");
                Assert.True(Rotation.Turn(path, true) != null, "not turned");
                Assert.Equal(all.Length, File.ReadAllBytes(path).Length, "file unchanged");
            }
        }

        // The JPEG with these segments put right after its EXIF block.
        static byte[] InsertAfterExif(byte[] b, params byte[][] segments)
        {
            int i = 2;
            while (i + 4 <= b.Length && b[i] == 0xFF)
            {
                int len = (b[i + 2] << 8) | b[i + 3];
                bool exif = b[i + 1] == 0xE1 && b[i + 4] == 'E' && b[i + 5] == 'x';
                i += 2 + len;
                if (exif) break;
            }
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(b, 0, i);
                foreach (byte[] s in segments) ms.Write(s, 0, s.Length);
                ms.Write(b, i, b.Length - i);
                return ms.ToArray();
            }
        }

        static byte[] Segment(int marker, string content)
        {
            byte[] c = System.Text.Encoding.ASCII.GetBytes(content);
            byte[] s = new byte[4 + c.Length];
            s[0] = 0xFF; s[1] = (byte)marker; s[2] = (byte)((c.Length + 2) >> 8); s[3] = (byte)((c.Length + 2) & 0xFF);
            Array.Copy(c, 0, s, 4, c.Length);
            return s;
        }

        // 16-bit PNGs come into GDI+ as 8-bit colour: turning would lose precision, so they're left alone.
        [Test]
        static void SixteenBitPngIsLeftAlone()
        {
            using (TempDir d = new TempDir())
            {
                string path = Path.Combine(d.Path, "deep.png");
                byte[] px = new byte[8 * 4 * 6];
                for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 7);
                System.Windows.Media.Imaging.BitmapSource src = System.Windows.Media.Imaging.BitmapSource.Create(8, 4, 96, 96, System.Windows.Media.PixelFormats.Rgb48, null, px, 8 * 6);
                System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
                using (FileStream fs = File.Create(path)) enc.Save(fs);
                byte[] before = File.ReadAllBytes(path);
                Assert.Equal(16, (int)before[24], "a 16-bit PNG");
                string problem = Rotation.Turn(path, true);
                Assert.True(problem != null, "not turned: " + problem);
                Assert.True(before.Length == File.ReadAllBytes(path).Length, "file unchanged");
            }
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
        static void PngsWithMoreThanThePictureAreNotTurned()
        {
            using (TempDir d = new TempDir())
            {
                string f = Picture(d, "plain.png", 40, 20, ImageFormat.Png);
                Assert.True(Rotation.PlainPng(File.ReadAllBytes(f)), "a PNG as Windows saves it");
                // Insert a text chunk (tEXt) after the header: re-saving would drop it.
                byte[] b = File.ReadAllBytes(f);
                byte[] text = new byte[] { 0, 0, 0, 4, (byte)'t', (byte)'E', (byte)'X', (byte)'t', (byte)'a', 0, (byte)'b', (byte)'c', 0, 0, 0, 0 };
                byte[] withText = new byte[b.Length + text.Length];
                Array.Copy(b, 0, withText, 0, 33);                 // signature + IHDR
                Array.Copy(text, 0, withText, 33, text.Length);
                Array.Copy(b, 33, withText, 33 + text.Length, b.Length - 33);
                File.WriteAllBytes(f, withText);
                Assert.True(!Rotation.PlainPng(withText), "a PNG with text");
                Assert.True(Rotation.Turn(f, true) != null, "not turned");
                Assert.True(Convert.ToBase64String(withText) == Convert.ToBase64String(File.ReadAllBytes(f)), "unchanged");
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
