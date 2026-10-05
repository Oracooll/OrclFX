// Orcl File Explorer: where the window reopens. No UI; covered by tests\.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace OrclFileExplorer
{
    static class WindowPlacement
    {
        // The saved window rectangle, adjusted so its title bar (the top titleHeight pixels, the only place to
        // drag it and the caption buttons) is on a screen: unchanged when enough of the title bar is visible,
        // otherwise moved (and shrunk if needed) onto the screen it overlaps most, or the first one. A monitor
        // that was unplugged or rearranged can otherwise leave the window with only a sliver on screen.
        public static Rectangle Fit(Rectangle saved, IList<Rectangle> workAreas, int titleHeight, int minVisible)
        {
            if (workAreas.Count == 0) return saved;
            Rectangle title = new Rectangle(saved.X, saved.Y, saved.Width, titleHeight);
            foreach (Rectangle wa in workAreas)
            {
                Rectangle seen = Rectangle.Intersect(wa, title);
                if (seen.Width >= minVisible && seen.Height >= titleHeight / 2) return saved;
            }
            Rectangle best = workAreas[0];
            long bestArea = -1;
            foreach (Rectangle wa in workAreas)
            {
                Rectangle i = Rectangle.Intersect(wa, saved);
                long area = (long)i.Width * i.Height;
                if (area > bestArea) { bestArea = area; best = wa; }
            }
            int w = Math.Min(saved.Width, best.Width), h = Math.Min(saved.Height, best.Height);
            int x = Math.Max(best.Left, Math.Min(saved.X, best.Right - w));
            int y = Math.Max(best.Top, Math.Min(saved.Y, best.Bottom - h));
            return new Rectangle(x, y, w, h);
        }
    }
}
