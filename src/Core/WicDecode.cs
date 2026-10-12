// OrclFX: decodes and scales pictures with WIC (Windows Imaging Component, through WPF's imaging classes) instead of
// GDI+. GDI+ holds a lock while it decodes or scales a picture that stops every other GDI+ call in the app (the
// windows' own drawing among them) for as long as it takes: a second or two for a big photo, which froze the Space
// Viewer whenever a picture was being read in the background. WIC has no such lock, and decodes JPEGs straight at
// a smaller size (much faster than decoding all of it and scaling down).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrclFileExplorer
{
    static class WicDecode
    {
        // WPF gives each thread that decodes a dispatcher (with a hidden window): a thread that ends lets it go.
        public static void EndThread()
        {
            try
            {
                System.Windows.Threading.Dispatcher d = System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
                if (d != null) d.InvokeShutdown();
            }
            catch { }
        }

        // The picture in bytes decoded at size (its stored orientation's size), then turned as given; null when WIC
        // can't (the caller uses GDI+).
        public static Bitmap Decode(byte[] bytes, System.Drawing.Size size, RotateFlipType turn)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes, false))
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    // As GDI+ did: the embedded colour profile isn't applied.
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; // (IgnoreImageCache needs a file name)
                    bi.StreamSource = ms;
                    bi.DecodePixelWidth = size.Width;
                    bi.DecodePixelHeight = size.Height;
                    bi.EndInit();
                    BitmapSource src = bi;
                    double angle = 0;
                    bool flip = false;
                    switch (turn)
                    {
                        case RotateFlipType.RotateNoneFlipX: flip = true; break;
                        case RotateFlipType.Rotate90FlipNone: angle = 90; break;
                        case RotateFlipType.Rotate180FlipNone: angle = 180; break;
                        case RotateFlipType.Rotate270FlipNone: angle = 270; break;
                        case RotateFlipType.Rotate90FlipX: angle = 90; flip = true; break;
                        case RotateFlipType.Rotate180FlipX: angle = 180; flip = true; break;
                        case RotateFlipType.Rotate270FlipX: angle = 270; flip = true; break;
                    }
                    if (angle != 0) src = new TransformedBitmap(src, new RotateTransform(angle));
                    if (flip) src = new TransformedBitmap(src, new ScaleTransform(-1, 1));
                    src = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
                    int w = src.PixelWidth, h = src.PixelHeight;
                    Bitmap result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    try
                    {
                        BitmapData bits = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, result.PixelFormat);
                        try { src.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), bits.Scan0, bits.Stride * h, bits.Stride); }
                        finally { result.UnlockBits(bits); }
                        return result;
                    }
                    catch { result.Dispose(); throw; }
                }
            }
            catch (Exception ex)
            {
                Program.Trace("wic: " + ex.Message);
                return null;
            }
        }
    }
}
