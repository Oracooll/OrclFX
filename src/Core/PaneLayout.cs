// Orcl File Explorer: width arithmetic for the 1-4 side-by-side file panes. No UI; covered by tests\.
using System;

namespace OrclFileExplorer
{
    static class PaneLayout
    {
        // Splits avail pixels between the panes in proportion to their weights. The widths always add up to
        // avail: the last pane takes whatever rounding leaves over.
        public static int[] Split(int avail, float[] weights)
        {
            int n = weights.Length;
            int[] w = new int[n];
            if (n == 0) return w;
            float sum = 0;
            foreach (float x in weights) sum += x;
            int used = 0;
            for (int i = 0; i < n - 1; i++)
            {
                w[i] = (int)Math.Round(avail * weights[i] / sum);
                used += w[i];
            }
            w[n - 1] = avail - used;
            return w;
        }

        // The widths after divider i (the right edge of pane i) has moved by d pixels, starting from the
        // widths at the moment the divider was grabbed. Only this divider moves: pane i changes by exactly d,
        // panes to its left stay put, and the panes to its right share the difference equally. d is limited
        // so no pane gets narrower than minWidth. With two panes this is an ordinary split.
        public static float[] Drag(float[] start, int i, float d, float minWidth)
        {
            int n = start.Length;
            float[] w = (float[])start.Clone();
            if (i < 0 || i + 1 >= n) return w;
            int right = n - 1 - i;
            float lo = minWidth - start[i], hi = float.MaxValue;
            for (int k = i + 1; k < n; k++) hi = Math.Min(hi, (start[k] - minWidth) * right);
            d = Math.Max(lo, Math.Min(hi, d));
            for (int k = i; k < n; k++) w[k] = k == i ? start[k] + d : start[k] - d / right;
            return w;
        }
    }
}
