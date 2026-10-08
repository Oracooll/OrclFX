// OrclFX: turning a picture file by 90° without losing quality. JPEG photos get a new EXIF orientation
// (their image data isn't touched); ordinary 24/32-bit PNG and BMP pictures are turned and saved again. Anything
// that re-saving would change (GIF palettes, 16-bit or indexed PNG, TIFF) isn't turned.
// Covered by tests\.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace OrclFileExplorer
{
    static class Rotation
    {
        // Null when done, otherwise why it wasn't.
        public static string Turn(string path, bool cw)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return Path.GetFileName(path) + " is read-only.";
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".jpe" || ext == ".jfif")
                {
                    int r = Exif.TryTurnJpeg(path, cw);
                    if (r == Exif.Turned) return null;
                    if (r == Exif.NotUnderstood) return Path.GetFileName(path) + " isn't a JPEG this app can turn safely; nothing was changed.";
                    return TurnJpegData(path, cw);
                }
                return TurnAndSave(path, cw, ext);
            }
            catch (Exception ex) { return Path.GetFileName(path) + " couldn't be turned: " + ex.Message; }
        }

        // A JPEG with camera data but no orientation entry: GDI+ can turn its image data losslessly when the sides
        // are whole JPEG blocks (multiples of 16 pixels); otherwise it isn't turned, rather than lose quality.
        static string TurnJpegData(string path, bool cw)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms, false, false))
            {
                if (img.Width % 16 != 0 || img.Height % 16 != 0)
                    return Path.GetFileName(path) + " can't be turned without losing quality (its size isn't a multiple of 16 pixels).";
                ImageCodecInfo jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), delegate(ImageCodecInfo c) { return c.FormatID == ImageFormat.Jpeg.Guid; });
                using (EncoderParameters ps = new EncoderParameters(1))
                using (MemoryStream outp = new MemoryStream())
                {
                    ps.Param[0] = new EncoderParameter(Encoder.Transformation, (long)(cw ? EncoderValue.TransformRotate90 : EncoderValue.TransformRotate270));
                    img.Save(outp, jpeg, ps);
                    Util.ReplaceFileSafelyWithBytes(path, outp.ToArray());
                }
            }
            return null;
        }

        static string TurnAndSave(string path, bool cw, string ext)
        {
            ImageFormat format = ext == ".png" ? ImageFormat.Png : ext == ".bmp" || ext == ".dib" ? ImageFormat.Bmp : null;
            if (format == null) return Path.GetFileName(path) + " can't be turned without changing it (only JPEG, PNG and BMP are turned).";
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms, false, false))
            {
                PixelFormat pf = img.PixelFormat;
                if (pf != PixelFormat.Format24bppRgb && pf != PixelFormat.Format32bppRgb && pf != PixelFormat.Format32bppArgb)
                    return Path.GetFileName(path) + " can't be turned without changing it (its colour depth would change).";
                foreach (Guid dim in img.FrameDimensionsList)
                    if (img.GetFrameCount(new FrameDimension(dim)) > 1)
                        return Path.GetFileName(path) + " has several frames or pages and isn't turned.";
                // An exact copy (same pixels, colour depth, alpha and resolution) is turned: nothing is redrawn.
                using (Image b = (Image)img.Clone())
                {
                    b.RotateFlip(cw ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
                    using (MemoryStream outp = new MemoryStream())
                    {
                        b.Save(outp, format);
                        Util.ReplaceFileSafelyWithBytes(path, outp.ToArray());
                    }
                }
            }
            return null;
        }
    }
}
