// Tests for the Space Viewer's settings: they survive the settings file, and odd values are ignored.
using System.Text;
using System.Windows.Forms;

namespace OrclFileExplorer.Tests
{
    static class ViewerSettingsTests
    {
        [Test]
        static void SavedAndReadBack()
        {
            ViewerSettings.FullScreenClicks = false; ViewerSettings.Enlarge = 2; ViewerSettings.ExitMiddle = false;
            ViewerSettings.ConfirmExit = true; ViewerSettings.HideCursor = false; ViewerSettings.Wheel = ViewerSettings.WheelStep;
            ViewerSettings.DragButtons = MouseButtons.Middle | MouseButtons.Right; ViewerSettings.Smooth = false; ViewerSettings.ZoomStep = 2;
            ViewerSettings.Gpu = false; ViewerSettings.Background = 4; ViewerSettings.Wrap = true;
            StringBuilder sb = new StringBuilder();
            ViewerSettings.Write(sb);
            string saved = sb.ToString();
            // Back to the defaults, then read.
            ViewerSettings.FullScreenClicks = true; ViewerSettings.Enlarge = 1; ViewerSettings.ExitMiddle = true; ViewerSettings.ConfirmExit = false;
            ViewerSettings.HideCursor = true; ViewerSettings.Wheel = ViewerSettings.WheelZoomPointer; ViewerSettings.DragButtons = MouseButtons.Left;
            ViewerSettings.Smooth = true; ViewerSettings.ZoomStep = 1; ViewerSettings.Gpu = true; ViewerSettings.Background = 0; ViewerSettings.Wrap = false;
            foreach (string line in saved.Split('\n'))
            {
                string l = line.Trim();
                int eq = l.IndexOf('=');
                if (eq > 0) Assert.True(ViewerSettings.Read(l.Substring(0, eq), l.Substring(eq + 1)), "known key " + l);
            }
            Assert.True(!ViewerSettings.FullScreenClicks && ViewerSettings.Enlarge == 2 && !ViewerSettings.ExitMiddle && ViewerSettings.ConfirmExit, "full screen");
            Assert.True(!ViewerSettings.HideCursor && ViewerSettings.Wheel == ViewerSettings.WheelStep, "pointer, wheel");
            Assert.Equal(MouseButtons.Middle | MouseButtons.Right, ViewerSettings.DragButtons, "drag buttons");
            Assert.True(!ViewerSettings.Smooth && ViewerSettings.ZoomStep == 2 && !ViewerSettings.Gpu && ViewerSettings.Background == 4 && ViewerSettings.Wrap, "display");
            // Out of range: ignored. Unknown keys: not these settings.
            ViewerSettings.Read("svwheel", "7");
            ViewerSettings.Read("svzoomstep", "x");
            Assert.True(ViewerSettings.Wheel == ViewerSettings.WheelStep && ViewerSettings.ZoomStep == 2, "bad values ignored");
            Assert.True(!ViewerSettings.Read("svsomething", "1"), "unknown key");
            // Defaults again for the other tests.
            ViewerSettings.FullScreenClicks = true; ViewerSettings.Enlarge = 1; ViewerSettings.ExitMiddle = true; ViewerSettings.ConfirmExit = false;
            ViewerSettings.HideCursor = true; ViewerSettings.Wheel = ViewerSettings.WheelZoomPointer; ViewerSettings.DragButtons = MouseButtons.Left;
            ViewerSettings.Smooth = true; ViewerSettings.ZoomStep = 1; ViewerSettings.Gpu = true; ViewerSettings.Background = 0; ViewerSettings.Wrap = false;
        }
    }
}
