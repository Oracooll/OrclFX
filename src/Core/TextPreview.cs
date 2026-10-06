// Orcl File Explorer: reading a file as text for the preview pane (encoding, binary detection, size limit). No UI;
// covered by tests\.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrclFileExplorer
{
    static class TextPreview
    {
        public const int MaxBytes = 2 * 1024 * 1024;

        // Plain-text kinds shown as text even when Windows has a preview handler for them (the text view can be
        // searched and follows the theme). Other files are shown as text only when no handler can show them.
        static readonly HashSet<string> plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".txt", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".config", ".json", ".jsonc", ".yml", ".yaml", ".toml",
            ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".sh", ".bash", ".cs", ".vb", ".fs", ".c", ".h", ".cpp", ".hpp", ".cc",
            ".java", ".kt", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".py", ".rb", ".go", ".rs", ".php", ".pl", ".lua", ".sql",
            ".css", ".scss", ".less", ".reg", ".inf", ".gitignore", ".gitattributes", ".editorconfig", ".properties",
            ".csproj", ".vbproj", ".sln", ".props", ".targets", ".manifest", ".nfo", ".srt", ".vtt", ".diff", ".patch" };

        public static bool IsPlainTextKind(string path) { return plain.Contains(Path.GetExtension(path) ?? ""); }

        // Reads the file as text: true with the text (line breaks made \r\n for display) and a short description
        // ("UTF-8 · 120 lines"); false for binary files and for files that are only in the cloud (reading them
        // would download them).
        public static bool TryRead(string path, out string text, out string note)
        {
            text = note = null;
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists) return false;
            const FileAttributes recallOnOpen = (FileAttributes)0x40000, recallOnDataAccess = (FileAttributes)0x400000;
            if ((fi.Attributes & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0) return false;
            long size = fi.Length;
            byte[] bytes;
            using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int n = (int)Math.Min(size, MaxBytes);
                bytes = new byte[n];
                int got = 0, r;
                while (got < n && (r = s.Read(bytes, got, n - got)) > 0) got += r;
                if (got < n) Array.Resize(ref bytes, got);
            }
            bool cut = size > bytes.Length;
            string encoding;
            if (!Decode(bytes, cut, out text, out encoding)) { text = null; return false; }
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            int lines = text.Length == 0 ? 0 : 1;
            foreach (char c in text) if (c == '\n') lines++;
            note = encoding + " · " + lines.ToString("N0") + (lines == 1 ? " line" : " lines") + (cut ? " · first " + Util.FormatBytes(MaxBytes) + " of " + Util.FormatBytes(size) : "");
            if (size == 0) note = "empty file";
            return true;
        }

        // The text in the bytes, or false when they look binary. cut: the bytes are the start of a longer file, so
        // the last character may be incomplete.
        public static bool Decode(byte[] b, bool cut, out string text, out string encoding)
        {
            text = null;
            encoding = null;
            if (b.Length >= 2 && (b[0] == 0xFF && b[1] == 0xFE || b[0] == 0xFE && b[1] == 0xFF))
            {
                bool le = b[0] == 0xFF;
                int len = (b.Length - 2) & ~1;
                text = (le ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(b, 2, len);
                encoding = le ? "UTF-16" : "UTF-16 BE";
                return !HasControlNoise(text);
            }
            int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            int check = Math.Min(b.Length, 8192);
            for (int i = start; i < check; i++) if (b[i] == 0) return false; // NUL bytes: binary
            int end = b.Length;
            if (cut) while (end > start && end > b.Length - 4 && (b[end - 1] & 0xC0) == 0x80) end--;   // continuation bytes
            if (cut && end > start && b[end - 1] >= 0xC0) end--;                                      // a lead byte without them
            try
            {
                text = new UTF8Encoding(false, true).GetString(b, start, end - start);
                encoding = start == 3 ? "UTF-8 with BOM" : "UTF-8";
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Default.GetString(b, start, b.Length - start);
                encoding = Encoding.Default.WebName.ToUpperInvariant();
            }
            return !HasControlNoise(text);
        }

        // Many control characters (other than tab, line breaks and form feed) in the first part: not text.
        static bool HasControlNoise(string s)
        {
            int n = Math.Min(s.Length, 8192), bad = 0;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c < 32 && c != '\t' && c != '\n' && c != '\r' && c != '\f' && c != 27) bad++;
            }
            return n > 0 && bad * 100 > n; // more than 1%
        }

        // Every place the text contains what (ignoring case), as start indexes.
        public static List<int> FindAll(string text, string what)
        {
            List<int> r = new List<int>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(what)) return r;
            for (int i = text.IndexOf(what, StringComparison.CurrentCultureIgnoreCase); i >= 0 && r.Count < 100000;
                i = i + what.Length <= text.Length ? text.IndexOf(what, i + Math.Max(1, what.Length), StringComparison.CurrentCultureIgnoreCase) : -1)
                r.Add(i);
            return r;
        }
    }
}
