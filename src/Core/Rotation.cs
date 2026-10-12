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
            // More after the picture (a phone's extra pictures or motion-photo video): re-encoding would drop it.
            if (DataAfterImage(bytes))
                return Path.GetFileName(path) + " holds more after its picture (extra pictures or a motion-photo video) and isn't turned, so nothing is lost.";
            // An orientation recorded in XMP (shown by Windows when EXIF has none) would turn it twice ("1", upright,
            // as Photoshop writes, is fine).
            if (XmpTurned(System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 256 * 1024))))
                return Path.GetFileName(path) + " records its orientation in XMP data; it isn't turned, so it doesn't end up turned twice.";
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
                    // GDI+ writes back its JFIF and EXIF blocks (the EXIF thumbnail turned too) but drops the others
                    // (XMP ratings and keywords, comments, a colour profile …): those are added back unchanged.
                    byte[] turned = WithMetadataOf(bytes, outp.ToArray());
                    if (turned == null) return Path.GetFileName(path) + " can't be turned keeping all its data (camera data, colour profile …); nothing was changed.";
                    Util.ReplaceFileSafelyWithBytes(path, turned);
                }
            }
            return null;
        }

        // The JPEG "turned" (GDI+'s output) with the metadata blocks of "original" it lacks added back: its own JFIF
        // and EXIF blocks stay (the EXIF one holds the turned thumbnail), the original's others follow (XMP, colour
        // profile, IPTC, comments …) except its JFIF, EXIF and multi-picture index (whose offsets would be wrong).
        // Null when either isn't understood.
        internal static byte[] WithMetadataOf(byte[] original, byte[] turned)
        {
            int origEnd, turnedFrom;
            System.Collections.Generic.List<int[]> meta = MetaSegments(original, out origEnd);
            System.Collections.Generic.List<int[]> theirs = MetaSegments(turned, out turnedFrom);
            if (meta == null || theirs == null) return null;
            using (MemoryStream ms = new MemoryStream(turned.Length + original.Length / 4))
            {
                ms.WriteByte(0xFF); ms.WriteByte(0xD8);
                bool hasJfif = false, hasExif = false, hasIcc = false;
                foreach (int[] s in theirs)
                {
                    ms.Write(turned, s[0], s[1]);
                    if (turned[s[0] + 1] == 0xE0) hasJfif = true;
                    if (turned[s[0] + 1] == 0xE1 && Starts(turned, s[0] + 4, "Exif\0")) hasExif = true;
                    if (turned[s[0] + 1] == 0xE2 && Starts(turned, s[0] + 4, "ICC_PROFILE\0")) hasIcc = true;
                }
                foreach (int[] s in meta)
                {
                    int marker = original[s[0] + 1];
                    bool exif = marker == 0xE1 && Starts(original, s[0] + 4, "Exif\0");
                    // GDI+ leaves the EXIF block out when another block comes before it: the original's would keep the
                    // old thumbnail and sizes, so such a file isn't turned.
                    if (exif && !hasExif) return null;
                    if (marker == 0xE0 && hasJfif) continue;                                          // JFIF
                    if (exif) continue;                                                               // EXIF (GDI+'s)
                    if (marker == 0xE2 && hasIcc && Starts(original, s[0] + 4, "ICC_PROFILE\0")) continue; // colour profile (GDI+ kept it)
                    if (marker == 0xE2 && Starts(original, s[0] + 4, "MPF\0")) continue;       // multi-picture index
                    ms.Write(original, s[0], s[1]);
                }
                ms.Write(turned, turnedFrom, turned.Length - turnedFrom);
                return ms.ToArray();
            }
        }

        // Whether something other than padding follows the main picture's end (the first end-of-image marker after
        // its image data: inside the data, 0xFF bytes are always followed by 0x00 or a restart/table marker).
        internal static bool DataAfterImage(byte[] b)
        {
            int i = 2;
            while (i + 4 <= b.Length && b[i] == 0xFF)
            {
                int marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0xDA) break;
                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }
            for (; i + 1 < b.Length; i++)
                if (b[i] == 0xFF && b[i + 1] == 0xD9)
                {
                    for (int k = i + 2; k < b.Length; k++) if (b[k] != 0 && b[k] != 0xFF) return true;
                    return false;
                }
            return false;
        }

        static bool XmpTurned(string text)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"tiff:Orientation\s*(=\s*[""']|>)\s*(\d)");
            return text.Contains("tiff:Orientation") && (!m.Success || m.Groups[2].Value != "1");
        }

        static bool Starts(byte[] b, int at, string s)
        {
            if (at + s.Length > b.Length) return false;
            for (int i = 0; i < s.Length; i++) if (b[at + i] != s[i]) return false;
            return true;
        }

        // The metadata segments at the start of a JPEG ({offset, length} with their markers), and where the first
        // other segment (the tables, the frame, the image data) starts. Null when the structure isn't understood.
        static System.Collections.Generic.List<int[]> MetaSegments(byte[] b, out int rest)
        {
            rest = -1;
            System.Collections.Generic.List<int[]> list = new System.Collections.Generic.List<int[]>();
            if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
            int i = 2;
            while (i + 4 <= b.Length && b[i] == 0xFF)
            {
                int marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                bool isMeta = marker >= 0xE0 && marker <= 0xEF || marker == 0xFE;
                if (!isMeta) { rest = i; return list; }
                int len = (b[i + 2] << 8) | b[i + 3];
                if (len < 2 || i + 2 + len > b.Length) return null;
                list.Add(new int[] { i, 2 + len });
                i += 2 + len;
            }
            return null;
        }

        // A PNG made only of the parts Windows writes back when saving (so re-saving keeps everything): the image
        // header and data, sRGB, gamma and resolution. Anything else (an animation, a colour profile, text, EXIF ...)
        // would be dropped.
        internal static bool PlainPng(byte[] b)
        {
            if (b.Length < 8 || b[0] != 0x89 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;
            int i = 8;
            while (i + 8 <= b.Length)
            {
                long len = ((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | b[i + 3];
                string type = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);
                if (type != "IHDR" && type != "IDAT" && type != "IEND" && type != "sRGB" && type != "gAMA" && type != "pHYs") return false;
                if (type == "IEND") return true;
                if (len > b.Length - i - 12) return false; // a damaged length
                i += 12 + (int)len;
            }
            return false;
        }

        static string TurnAndSave(string path, bool cw, string ext)
        {
            ImageFormat format = ext == ".png" ? ImageFormat.Png : ext == ".bmp" || ext == ".dib" ? ImageFormat.Bmp : null;
            if (format == null) return Path.GetFileName(path) + " can't be turned without changing it (only JPEG, PNG and BMP are turned).";
            byte[] bytes = File.ReadAllBytes(path);
            if (format == ImageFormat.Png && !PlainPng(bytes))
                return Path.GetFileName(path) + " can't be turned without changing it (it holds an animation, a colour profile or other data that saving it again would drop).";
            // Only 8-bit colour PNGs (RGB or RGBA): GDI+ reads 16-bit and grey ones as 8-bit colour, and saving would
            // lose the extra precision or change the kind of file.
            if (format == ImageFormat.Png && (bytes.Length < 26 || bytes[24] != 8 || bytes[25] != 2 && bytes[25] != 6))
                return Path.GetFileName(path) + " can't be turned without changing it (16-bit, grey or palette PNGs aren't turned).";
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
