// Orcl File Explorer: Lays out 1-4 file panes side by side with draggable dividers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OrclFileExplorer
{
    // Lays out the visible file panes left to right, with a draggable divider between neighbours
    // that resizes them live. Each pane keeps a relative width (weight) even while it is hidden.
    class PaneRow : Panel
    {
        readonly Pane[] panes;
        readonly List<Pane> shown = new List<Pane>();
        readonly List<PaneBar> bars = new List<PaneBar>();
        public readonly float[] Weights = { 1f, 1f, 1f, 1f };
        public event EventHandler WeightsChanged;

        public PaneRow(Pane[] panes)
        {
            this.panes = panes;
            Dock = DockStyle.Fill;
            foreach (Pane p in panes)
            {
                p.Dock = DockStyle.None;
                p.Visible = false;
                Controls.Add(p);
            }
        }

        public int BarWidth { get { return Native.Px(5); } }
        public int MinPaneWidth { get { return Native.Px(160); } }

        public void ShowPanes(List<Pane> visible)
        {
            SuspendLayout();
            shown.Clear();
            shown.AddRange(visible);
            while (bars.Count < Math.Max(0, shown.Count - 1))
            {
                PaneBar b = new PaneBar(this, bars.Count);
                b.BackColor = Theme.Border;
                bars.Add(b);
                Controls.Add(b);
            }
            for (int i = 0; i < bars.Count; i++) bars[i].Visible = i < shown.Count - 1;
            foreach (Pane p in panes) p.Visible = shown.Contains(p);
            ResumeLayout(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int n = shown.Count;
            if (n == 0 || bars.Count < n - 1) return;
            int avail = Math.Max(n, ClientSize.Width - BarWidth * (n - 1));
            float[] weights = new float[n];
            for (int i = 0; i < n; i++) weights[i] = Weight(shown[i]);
            int[] widths = PaneLayout.Split(avail, weights);
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = widths[i];
                shown[i].SetBounds(x, 0, Math.Max(1, w), ClientSize.Height);
                x += w;
                if (i < n - 1) { bars[i].SetBounds(x, 0, BarWidth, ClientSize.Height); x += BarWidth; }
            }
        }

        float Weight(Pane p) { return Math.Max(0.05f, Weights[Array.IndexOf(panes, p)]); }

        // Called while divider i (right edge of shown[i]) is dragged to barLeft (in this control's
        // coordinates). Works from the widths at the moment the divider was grabbed, so nothing jumps;
        // PaneLayout.Drag has the rules.
        float[] dragStart;
        int dragStartBar;

        internal void BeginDrag(int barLeft)
        {
            dragStart = new float[shown.Count];
            for (int k = 0; k < shown.Count; k++) dragStart[k] = shown[k].Width;
            dragStartBar = barLeft;
        }

        internal void DragBarTo(int i, int barLeft)
        {
            int n = shown.Count;
            if (i < 0 || i + 1 >= n || dragStart == null || dragStart.Length != n) return;
            float[] w = PaneLayout.Drag(dragStart, i, barLeft - dragStartBar, MinPaneWidth);
            float total = 0;
            foreach (float x in w) total += x;
            for (int k = 0; k < n; k++) Weights[Array.IndexOf(panes, shown[k])] = Math.Max(0.05f, w[k] * n / total);
            PerformLayout();
            Update();
        }

        internal void BarDropped()
        {
            if (WeightsChanged != null) WeightsChanged(this, EventArgs.Empty);
        }



        public void ApplyTheme()
        {
            BackColor = Theme.Window;
            foreach (PaneBar b in bars) b.BackColor = Theme.Border;
        }

        public void ResetWidths()
        {
            for (int i = 0; i < Weights.Length; i++) Weights[i] = 1f;
            PerformLayout();
            BarDropped();
        }
    }

    class PaneBar : Control
    {
        readonly PaneRow row;
        readonly int index;
        int grab;
        bool dragging;

        public PaneBar(PaneRow r, int i)
        {
            row = r;
            index = i;
            Cursor = Cursors.VSplit;
            BackColor = Theme.Border;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            grab = e.X; // where on the divider it was grabbed
            row.BeginDrag(Left);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) row.DragBarTo(index, Left + e.X - grab);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            row.BarDropped();
        }

        // Double-click a divider to make all visible panes the same width.
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            row.ResetWidths();
        }
    }
}
