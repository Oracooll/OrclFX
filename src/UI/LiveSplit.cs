// OrclFX: SplitContainer that resizes live while the splitter is dragged.
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
    class LiveSplit : SplitContainer
    {
        bool dragging;
        int grab;

        public LiveSplit()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        bool Vertical { get { return Orientation == Orientation.Vertical; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SplitterRectangle.Contains(e.Location))
            {
                // Skip the base class so it doesn't start its own ghost-line drag.
                dragging = true;
                grab = (Vertical ? e.X : e.Y) - SplitterDistance;
                Capture = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) { base.OnMouseMove(e); return; }
            int size = Vertical ? Width : Height;
            int pos = (Vertical ? e.X : e.Y) - grab;
            pos = Math.Max(Panel1MinSize, Math.Min(size - SplitterWidth - Panel2MinSize, pos));
            if (pos > 0 && pos != SplitterDistance)
            {
                SplitterDistance = pos;
                Update();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) { base.OnMouseUp(e); return; }
            dragging = false;
            Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            dragging = false;
            base.OnMouseCaptureChanged(e);
        }
    }
}
