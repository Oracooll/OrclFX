// Tests for the text preview (encodings, binary files, size limit, search) and the address bar's path parts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrclFileExplorer.Tests
{
    static class TextPreviewTests
    {
        static string Decode(byte[] b, bool cut = false)
        {
            string text, enc;
            return TextPreview.Decode(b, cut, out text, out enc) ? enc + ":" + text : "binary";
        }

        [Test]
        static void Encodings()
        {
            Assert.Equal("UTF-8:héllo", Decode(Encoding.UTF8.GetBytes("héllo")), "UTF-8");
            Assert.Equal("UTF-8 with BOM:x", Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' }), "BOM");
            byte[] u16 = new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 };
            Assert.Equal("UTF-16:hi", Decode(u16), "UTF-16");
            Assert.True(Decode(new byte[] { (byte)'a', 0xE9, (byte)'b' }).EndsWith(":a\u00E9b") || Decode(new byte[] { (byte)'a', 0xE9, (byte)'b' }).Contains(":a"), "not UTF-8: the system code page");
        }

        [Test]
        static void BinaryIsNotText()
        {
            Assert.Equal("binary", Decode(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0 }), "an exe");
            byte[] noise = new byte[200];
            for (int i = 0; i < noise.Length; i++) noise[i] = (byte)(i % 2 == 0 ? 1 : 'a');
            Assert.Equal("binary", Decode(noise), "control characters");
        }

        [Test]
        static void ACutCharacterIsDropped()
        {
            byte[] b = Encoding.UTF8.GetBytes("ab\u20AC"); // € is 3 bytes
            Array.Resize(ref b, b.Length - 1);
            Assert.Equal("UTF-8:ab", Decode(b, true), "the cut € is left out, not turned into the code page");
        }

        [Test]
        static void ReadsFilesWithLineBreaksForDisplay()
        {
            using (TempDir d = new TempDir())
            {
                string f = d.File("a.json", "{\n  \"x\": 1\n}\n");
                string text, note;
                Assert.True(TextPreview.TryRead(f, out text, out note), "read");
                Assert.Equal("{\r\n  \"x\": 1\r\n}\r\n", text, "line breaks");
                Assert.Equal("UTF-8 · 4 lines", note, "note");
                string big = Path.Combine(d.Path, "big.log");
                File.WriteAllText(big, new string('x', TextPreview.MaxBytes + 10));
                Assert.True(TextPreview.TryRead(big, out text, out note) && text.Length == TextPreview.MaxBytes && note.Contains("first"), "only the start of a big file");
                string bin = Path.Combine(d.Path, "b.dat");
                File.WriteAllBytes(bin, new byte[] { 1, 0, 2, 0 });
                Assert.True(!TextPreview.TryRead(bin, out text, out note), "binary");
            }
        }

        [Test]
        static void FindAllIgnoresCase()
        {
            Assert.Sequence(new string[] { "0", "4", "8" }, Show(TextPreview.FindAll("Abc abc ABC", "abc")), "matches");
            Assert.Sequence(new string[] { "0", "2" }, Show(TextPreview.FindAll("aaaa", "aa")), "not overlapping");
            Assert.Equal(0, TextPreview.FindAll("abc", "").Count, "nothing to find");
        }

        [Test]
        static void PlainTextKinds()
        {
            Assert.True(TextPreview.IsPlainTextKind(@"C:\x\a.JSON"), ".json");
            Assert.True(!TextPreview.IsPlainTextKind(@"C:\x\a.pdf"), ".pdf");
        }

        static List<string> Show(List<int> l) { return l.ConvertAll(delegate(int i) { return i.ToString(); }); }

        static string Parts(string path)
        {
            List<string> r = new List<string>();
            foreach (KeyValuePair<string, string> p in PathParts.Split(path)) r.Add(p.Key + "=" + p.Value);
            return string.Join(" ", r.ToArray());
        }

        [Test]
        static void AddressParts()
        {
            Assert.Equal(@"C:=C:\ Users=C:\Users Me=C:\Users\Me", Parts(@"c:\Users\Me\"), "drive");
            Assert.Equal(@"D:=D:\", Parts(@"D:\"), "drive root");
            Assert.Equal(@"nas=\\nas share=\\nas\share x=\\nas\share\x", Parts(@"\\nas\share\x"), "share");
            Assert.Equal("", Parts("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"), "This PC");
            Assert.Equal("", Parts(""), "nothing");
            Assert.Equal("", Parts("Find results"), "not a path");
        }
    }
}
