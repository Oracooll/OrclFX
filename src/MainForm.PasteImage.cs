// OrclFX: pasting a picture from the clipboard (a screenshot, an image copied in a browser or an editor) into a
// folder as a PNG file. Explorer itself pastes only files, so Ctrl+V with a picture and no files on the clipboard
// is handled here; with files on the clipboard Ctrl+V stays Explorer's own paste.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class MainForm
    {
        // True when the clipboard holds a picture and no files (Explorer would paste the files).
        public static bool ClipboardHasImageOnly()
        {
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null) return false;
                if (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("Shell IDList Array")) return false;
                return d.GetDataPresent("PNG") || d.GetDataPresent("image/png") ||
                       d.GetDataPresent(DataFormats.Bitmap) || d.GetDataPresent(DataFormats.Dib);
            }
            catch { return false; } // another program has the clipboard open
        }

        // Ctrl+V in a file list: saves the clipboard's picture in the tab's folder and lets the user name it.
        // False (nothing done) when the clipboard has no picture, so the key goes on to Explorer's own paste.
        public bool PasteImage(BrowserTab t)
        {
            if (!ClipboardHasImageOnly()) return false;
            if (!CanMakeItemsIn(t))
            {
                SystemSounds.Beep.Play();
                Notice("A picture can be pasted in a folder on a disk or network share" + (t != null ? " (" + t.Title + " isn't one)." : "."));
                return true;
            }
            byte[] png = null;
            Image img = null;
            try
            {
                // The PNG some programs put on the clipboard (browsers, Snipping Tool, Office) keeps transparency;
                // otherwise the plain bitmap.
                IDataObject d = Clipboard.GetDataObject();
                foreach (string f in new[] { "PNG", "image/png" })
                {
                    if (png != null || !d.GetDataPresent(f)) continue;
                    byte[] b = ReadAll(d.GetData(f) as Stream);
                    if (Util.IsPng(b)) png = b;
                }
                if (png == null) img = Clipboard.GetImage();
            }
            catch (Exception ex)
            {
                Notice("⚠ Couldn't read the picture on the clipboard: " + ex.Message);
                return true;
            }
            if (png == null && img == null)
            {
                Notice("⚠ The clipboard's picture is in a format that can't be saved.");
                return true;
            }
            DateTime now = DateTime.Now;
            t.CreateItem(delegate(string dir)
            {
                byte[] data = png;
                if (data == null)
                    using (img)
                    using (MemoryStream ms = new MemoryStream())
                    {
                        img.Save(ms, ImageFormat.Png); // encoded here, off the window's thread: big screenshots take a moment
                        data = ms.ToArray();
                    }
                return Util.SavePastedImage(dir, data, now);
            }, delegate(string error) { Notice("⚠ Couldn't save the picture here: " + error); });
            return true;
        }

        static byte[] ReadAll(Stream s)
        {
            if (s == null) return null;
            using (s)
            using (MemoryStream ms = new MemoryStream())
            {
                if (s.CanSeek) s.Position = 0;
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
