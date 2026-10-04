// Tests for the side-by-side pane widths: initial split and dragging a divider.
using System;

namespace OrclFileExplorer.Tests
{
    static class PaneLayoutTests
    {
        static int Sum(int[] w) { int s = 0; foreach (int x in w) s += x; return s; }
        static float Sum(float[] w) { float s = 0; foreach (float x in w) s += x; return s; }

        [Test]
        static void Split_AlwaysFillsTheWidth()
        {
            foreach (int avail in new int[] { 1, 997, 1000, 1001, 3437 })
                foreach (float[] weights in new float[][] { new float[] { 1 }, new float[] { 1, 1 }, new float[] { 1, 1, 1 },
                    new float[] { 0.3f, 2.1f, 1.7f, 0.9f } })
                    Assert.Equal(avail, Sum(PaneLayout.Split(avail, weights)), "total for " + avail + " px, " + weights.Length + " panes");
        }

        [Test]
        static void Split_FollowsTheWeights()
        {
            Assert.Sequence(new int[] { 600, 300 }, PaneLayout.Split(900, new float[] { 2, 1 }), "2:1");
            Assert.Sequence(new int[] { 334, 334, 333 }, PaneLayout.Split(1001, new float[] { 1, 1, 1 }), "equal thirds");
        }

        [Test]
        static void Drag_TwoPanesIsAnOrdinarySplit()
        {
            Assert.Sequence(new float[] { 600, 400 }, PaneLayout.Drag(new float[] { 500, 500 }, 0, 100, 160), "dragged right");
            Assert.Sequence(new float[] { 450, 550 }, PaneLayout.Drag(new float[] { 500, 500 }, 0, -50, 160), "dragged left");
        }

        [Test]
        static void Drag_MiddleDivider_OnlyMovesThatDivider()
        {
            // Dragging the divider between panes 2 and 3 of four: pane 1 stays, pane 2 changes by exactly
            // the drag distance, panes 3 and 4 share the rest. (Version 1.1.002 shrank pane 2 a lot here.)
            float[] start = { 400, 400, 400, 400 };
            Assert.Sequence(new float[] { 400, 460, 370, 370 }, PaneLayout.Drag(start, 1, 60, 160), "dragged right");
            Assert.Sequence(new float[] { 400, 340, 430, 430 }, PaneLayout.Drag(start, 1, -60, 160), "dragged left");
            Assert.Near(1600, Sum(PaneLayout.Drag(start, 1, 60, 160)), "total width");
        }

        [Test]
        static void Drag_LastDivider_OnlyTouchesTheLastTwo()
        {
            Assert.Sequence(new float[] { 300, 500, 450, 350 }, PaneLayout.Drag(new float[] { 300, 500, 400, 400 }, 2, 50, 160), "widths");
        }

        [Test]
        static void Drag_StopsAtTheMinimumWidth()
        {
            float[] start = { 400, 400, 400, 400 };
            Assert.Sequence(new float[] { 400, 880, 160, 160 }, PaneLayout.Drag(start, 1, 10000, 160), "far right");
            Assert.Sequence(new float[] { 400, 160, 520, 520 }, PaneLayout.Drag(start, 1, -10000, 160), "far left");
        }

        [Test]
        static void Drag_StartWidthsAreNotChanged()
        {
            float[] start = { 400, 400 };
            PaneLayout.Drag(start, 0, 100, 160);
            Assert.Sequence(new float[] { 400, 400 }, start, "start widths");
        }

        [Test]
        static void Drag_InvalidDividerChangesNothing()
        {
            Assert.Sequence(new float[] { 500, 500 }, PaneLayout.Drag(new float[] { 500, 500 }, 1, 100, 160), "divider after the last pane");
            Assert.Sequence(new float[] { 500, 500 }, PaneLayout.Drag(new float[] { 500, 500 }, -1, 100, 160), "negative divider");
        }
    }
}
