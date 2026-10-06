// Orcl File Explorer: decoding common picture files directly for the preview (faster, sharper and more reliable
// than the shell's thumbnails, which often come back as the file's icon the first time). Covered by tests\.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OrclFileExplorer
{
    static class Pictures
    {
        public const long MaxFileBytes = 150L * 1024 * 1024;
        public const int MaxPixels = 120 * 1000 * 1000; // bigger pictures are left to the shell

        static readonly HashSet<string> kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff" };

        public static bool IsPictureKind(string path) { return kinds.Contains(Path.GetExtension(path) ?? ""); }

        // The picture scaled to fit box (never enlarged), turned the way its camera says (EXIF orientation); null
        // when it can't be decoded here (then the shell's thumbnail is used) or only lives in the cloud.
        public static Bitmap Load(string path, Size box)
        {
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > MaxFileBytes) return null;
            const FileAttributes recallOnOpen = (FileAttributes)0x40000, recallOnDataAccess = (FileAttributes)0x400000;
            if ((fi.Attributes & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0) return null;
            byte[] bytes;
            using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[s.Length];
                int got = 0, r;
                while (got < bytes.Length && (r = s.Read(bytes, got, bytes.Length - got)) > 0) got += r;
                if (got < bytes.Length) return null; // changed while reading
            }
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms, false, false))
            {
                if ((long)img.Width * img.Height > MaxPixels) return null;
                RotateFlipType turn = Orientation(img);
                bool sideways = turn == RotateFlipType.Rotate90FlipNone || turn == RotateFlipType.Rotate270FlipNone ||
                    turn == RotateFlipType.Rotate90FlipX || turn == RotateFlipType.Rotate270FlipX;
                int w = sideways ? img.Height : img.Width, h = sideways ? img.Width : img.Height;
                Size fit = Fit(new Size(w, h), box);
                Bitmap result = new Bitmap(fit.Width, fit.Height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        if (turn == RotateFlipType.RotateNoneFlipNone)
                            using (ImageAttributes ia = new ImageAttributes())
                            {
                                ia.SetWrapMode(WrapMode.TileFlipXY); // no grey fringe at the edges
                                g.DrawImage(img, new Rectangle(0, 0, fit.Width, fit.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                            }
                        else
                        {
                            // Turn a scaled copy (cheaper than turning the full picture).
                            Size pre = sideways ? new Size(fit.Height, fit.Width) : fit;
                            using (Bitmap scaled = new Bitmap(pre.Width, pre.Height, PixelFormat.Format32bppArgb))
                            {
                                using (Graphics sg = Graphics.FromImage(scaled))
                                {
                                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    sg.DrawImage(img, new Rectangle(0, 0, pre.Width, pre.Height));
                                }
                                scaled.RotateFlip(turn);
                                g.DrawImageUnscaled(scaled, 0, 0);
                            }
                        }
                    }
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }

        // The largest size with the picture's proportions that fits box, never bigger than the picture itself.
        public static Size Fit(Size picture, Size box)
        {
            if (picture.Width <= 0 || picture.Height <= 0) return new Size(1, 1);
            double k = Math.Min(1.0, Math.Min((double)Math.Max(1, box.Width) / picture.Width, (double)Math.Max(1, box.Height) / picture.Height));
            return new Size(Math.Max(1, (int)Math.Round(picture.Width * k)), Math.Max(1, (int)Math.Round(picture.Height * k)));
        }

        // EXIF orientation (tag 0x0112) as the turn that shows the picture upright.
        static RotateFlipType Orientation(Image img)
        {
            try
            {
                foreach (int id in img.PropertyIdList)
                    if (id == 0x0112)
                    {
                        PropertyItem p = img.GetPropertyItem(id);
                        int o = p.Value != null && p.Value.Length >= 2 ? BitConverter.ToUInt16(p.Value, 0) : 1;
                        switch (o)
                        {
                            case 2: return RotateFlipType.RotateNoneFlipX;
                            case 3: return RotateFlipType.Rotate180FlipNone;
                            case 4: return RotateFlipType.Rotate180FlipX;
                            case 5: return RotateFlipType.Rotate90FlipX;
                            case 6: return RotateFlipType.Rotate90FlipNone;
                            case 7: return RotateFlipType.Rotate270FlipX;
                            case 8: return RotateFlipType.Rotate270FlipNone;
                        }
                    }
            }
            catch { }
            return RotateFlipType.RotateNoneFlipNone;
        }
    }
}
