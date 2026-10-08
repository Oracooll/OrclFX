// OrclFX: turning JPEG photos without re-encoding them, by changing the EXIF orientation tag (the way
// cameras record a turned photo), and reading the tag. No UI; covered by tests\.
using System;
using System.IO;

namespace OrclFileExplorer
{
    static class Exif
    {
        // The orientation after turning a picture 90° clockwise (or counter-clockwise), for each EXIF orientation 1-8.
        static readonly int[] clockwise = { 0, 6, 7, 8, 5, 2, 3, 4, 1 };
        static readonly int[] counter = { 0, 8, 5, 6, 7, 4, 1, 2, 3 };

        public static int Turn(int orientation, bool cw)
        {
            if (orientation < 1 || orientation > 8) orientation = 1;
            return cw ? clockwise[orientation] : counter[orientation];
        }

        // Where the orientation value sits in a JPEG file: offset of its 2 bytes, and whether they are big-endian.
        // -1 when the file has EXIF data without the tag; -2 when it has no EXIF data; -3 when its structure isn't
        // understood (then nothing is changed).
        public static long FindOrientation(byte[] b, out bool bigEndian, out int app1At)
        {
            bigEndian = false;
            app1At = -1;
            if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return -3;
            int i = 2;
            while (i + 4 <= b.Length && b[i] == 0xFF)
            {
                if (b[i + 1] == 0xFF) { i++; continue; } // fill byte before a marker
                int marker = b[i + 1];
                if (marker == 0xD8 || marker >= 0xD0 && marker <= 0xD7 || marker == 0x01) { i += 2; continue; }
                if (marker == 0xDA || marker == 0xD9) break; // image data: no more header segments
                int len = (b[i + 2] << 8) | b[i + 3];
                if (len < 2 || i + 2 + len > b.Length) return -3;
                if (marker == 0xE1 && len >= 16 && b[i + 4] == 'E' && b[i + 5] == 'x' && b[i + 6] == 'i' && b[i + 7] == 'f' && b[i + 8] == 0 && b[i + 9] == 0)
                {
                    app1At = i;
                    int tiff = i + 10;
                    bigEndian = b[tiff] == 'M';
                    if (!bigEndian && b[tiff] != 'I') return -3;
                    long ifd = tiff + U32(b, tiff + 4, bigEndian);
                    if (ifd + 2 > i + 2 + len) return -1;
                    int count = U16(b, (int)ifd, bigEndian);
                    for (int k = 0; k < count; k++)
                    {
                        int e = (int)ifd + 2 + k * 12;
                        if (e + 12 > i + 2 + len) break;
                        if (U16(b, e, bigEndian) == 0x0112 && U16(b, e + 2, bigEndian) == 3) return e + 8;
                    }
                    return -1;
                }
                i += 2 + len;
            }
            return i + 1 < b.Length && b[i] == 0xFF && (b[i + 1] == 0xDA || b[i + 1] == 0xD9) ? -2 : -3;
        }

        static int U16(byte[] b, int at, bool be) { return be ? (b[at] << 8) | b[at + 1] : b[at] | (b[at + 1] << 8); }
        static long U32(byte[] b, int at, bool be)
        {
            return be ? ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3]
                      : b[at] | ((long)b[at + 1] << 8) | ((long)b[at + 2] << 16) | ((long)b[at + 3] << 24);
        }

        public static int ReadOrientation(byte[] b)
        {
            bool be; int app1;
            long at = FindOrientation(b, out be, out app1);
            if (at < 0) return 1;
            int o = U16(b, (int)at, be);
            return o >= 1 && o <= 8 ? o : 1;
        }

        public const int Turned = 0, NoTag = 1, NotUnderstood = 2;

        // Turns a JPEG by 90° without touching its image data: Turned; NoTag when it has EXIF data without an
        // orientation entry (adding one would mean rewriting the EXIF block); NotUnderstood when its structure isn't
        // recognised (nothing is changed then).
        public static int TryTurnJpeg(string path, bool cw)
        {
            byte[] b;
            // Read and (when the tag is there) write through one handle no one else can change meanwhile.
            using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                b = new byte[s.Length];
                int got = 0, r;
                while (got < b.Length && (r = s.Read(b, got, b.Length - got)) > 0) got += r;
                if (got < b.Length) return NotUnderstood;
                bool be; int app1;
                long at = FindOrientation(b, out be, out app1);
                if (at == -1) return NoTag;
                if (at == -3) return NotUnderstood;
                if (at >= 0)
                {
                    int o = Turn(U16(b, (int)at, be), cw);
                    // Two bytes changed in place: nothing else in the file moves.
                    s.Position = at;
                    if (be) { s.WriteByte(0); s.WriteByte((byte)o); } else { s.WriteByte((byte)o); s.WriteByte(0); }
                    return Turned;
                }
            }
            // No EXIF data at all: add a minimal block with just the orientation, after the JFIF header if there is one.
            int insert = 2;
            if (b.Length > 6 && b[2] == 0xFF && b[3] == 0xE0) insert = 4 + ((b[4] << 8) | b[5]);
            byte[] seg = MinimalExif(Turn(1, cw));
            byte[] result = new byte[b.Length + seg.Length];
            Buffer.BlockCopy(b, 0, result, 0, insert);
            Buffer.BlockCopy(seg, 0, result, insert, seg.Length);
            Buffer.BlockCopy(b, insert, result, insert + seg.Length, b.Length - insert);
            Util.ReplaceFileSafelyWithBytes(path, result);
            return Turned;
        }

        // APP1 "Exif" segment holding one IFD with one entry: Orientation (little-endian TIFF).
        static byte[] MinimalExif(int orientation)
        {
            byte[] s = new byte[2 + 2 + 6 + 8 + 2 + 12 + 4];
            int len = s.Length - 2;
            s[0] = 0xFF; s[1] = 0xE1; s[2] = (byte)(len >> 8); s[3] = (byte)len;
            s[4] = (byte)'E'; s[5] = (byte)'x'; s[6] = (byte)'i'; s[7] = (byte)'f';
            int t = 10;                                      // TIFF header
            s[t] = (byte)'I'; s[t + 1] = (byte)'I'; s[t + 2] = 42; s[t + 4] = 8;
            s[t + 8] = 1;                                    // one entry
            s[t + 10] = 0x12; s[t + 11] = 0x01;              // tag 0x0112
            s[t + 12] = 3;                                   // SHORT
            s[t + 14] = 1;                                   // count 1
            s[t + 18] = (byte)orientation;                   // value
            return s;                                        // next IFD offset: 0
        }
    }
}
