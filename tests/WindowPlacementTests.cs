// Tests for where the window reopens: its title bar must always be reachable.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace OrclFileExplorer.Tests
{
    static class WindowPlacementTests
    {
        static readonly Rectangle Primary = new Rectangle(0, 0, 2560, 1400);       // work area, taskbar below
        static readonly Rectangle Above = new Rectangle(0, -1440, 2560, 1440);      // a second monitor above it

        static Rectangle Fit(Rectangle saved, params Rectangle[] screens)
        {
            return WindowPlacement.Fit(saved, new List<Rectangle>(screens), 34, 100);
        }

        [Test]
        static void VisibleWindowStaysWhereItWas()
        {
            Rectangle r = new Rectangle(100, 100, 1200, 800);
            Assert.Equal(r, Fit(r, Primary), "on the primary monitor");
            Rectangle up = new Rectangle(200, -1300, 1200, 800);
            Assert.Equal(up, Fit(up, Primary, Above), "on the monitor above, while it's connected");
            Rectangle across = new Rectangle(-300, 200, 1200, 800);
            Assert.Equal(across, Fit(across, Primary), "partly off the left edge, title bar still reachable");
        }

        [Test]
        static void TitleBarOnAnUnpluggedMonitorComesBack()
        {
            // Was on the monitor above, its bottom edge just reaching the primary; the monitor above is gone.
            Rectangle r = new Rectangle(300, -780, 1400, 800);
            Rectangle f = Fit(r, Primary);
            Assert.True(f.Top >= Primary.Top && f.Top + 34 <= Primary.Bottom, "title bar on screen: " + f);
            Assert.Equal(new Size(1400, 800), f.Size, "same size");
            Assert.Equal(300, f.X, "same horizontal place");
        }

        [Test]
        static void TooBigIsShrunkToTheScreen()
        {
            Rectangle f = Fit(new Rectangle(-5000, -5000, 9000, 6000), Primary);
            Assert.Equal(Primary, f, "fills the screen it lands on");
        }

        [Test]
        static void FarAwayGoesToTheFirstScreen()
        {
            Rectangle f = Fit(new Rectangle(9000, 9000, 800, 600), Primary, Above);
            Assert.True(Primary.Contains(f), "on the primary screen: " + f);
        }
    }
}
