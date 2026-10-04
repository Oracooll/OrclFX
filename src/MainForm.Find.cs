// Orcl File Explorer: main window, the Find part (the search itself is in Core\FileSearch.cs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class MainForm
    {
        List<string> findHistory = new List<string>();   // settings key "find", newest first
        bool findContents;                               // settings key "findcontents"
        FindBox findBox;

        // The folder Find searches for this tab: the one it shows, or the one its results came from.
        static string FindRootFor(BrowserTab t)
        {
            if (t == null) return null;
            string root = t.IsFindResults ? t.FindRoot : t.Address;
            return root != null && root.Length >= 2 && (root[1] == ':' || root.StartsWith(@"\\")) ? root : null;
        }

        // Ctrl+F, F3 or the magnifier: drops the Find box down under the magnifier.
        public void ShowFind()
        {
            if (findBox != null) { findBox.Activate(); return; }
            BrowserTab t = ActivePane.ActiveTab;
            string root = FindRootFor(t);
            if (root == null)
            {
                SystemSounds.Beep.Play();
                Notice("Find searches a folder on a disk or network share: open one first" + (t != null ? " (" + t.Title + " isn't one)" : ""));
                return;
            }
            bool searching = t.Search != null && !t.Search.Finished && !t.Search.Cancel;
            findBox = new FindBox(root, findHistory, findContents, searching);
            findBox.FindClicked += delegate(string text, bool contents) { RunFind(text, contents); };
            findBox.StopClicked += delegate { BrowserTab a = ActivePane.ActiveTab; if (a != null) { a.StopFind(); UpdateStatus(); } };
            findBox.FormClosed += delegate { findBox = null; };
            Control b = titleBar.FindButton;
            Point at = b.PointToScreen(new Point(0, b.Height));
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            at.X = Math.Max(screen.Left, Math.Min(at.X, screen.Right - findBox.Width));
            findBox.Location = at;
            findBox.Show(this);
        }

        // Shows the results in a new tab of the active pane, or in place of earlier results.
        void RunFind(string text, bool contents)
        {
            Pane p = ActivePane;
            BrowserTab t = p.ActiveTab;
            string root = FindRootFor(t);
            if (root == null) return;
            findHistory = FindQuery.Remember(findHistory, text);
            findContents = contents;
            StateChanged();
            if (!(t.IsFindResults && !t.Locked)) t = p.AddTab(root, false, true);
            if (contents) t.StartWindowsSearch(root, text);
            else t.StartFind(root, text);
            UpdateStatus();
        }

        // The status bar text for a tab showing Find results.
        static string FindStatus(BrowserTab t, int shown)
        {
            if (t.FindInContents) return "Windows Search results for “" + t.FindText + "”" + (shown >= 0 ? ": " + shown + (shown == 1 ? " item" : " items") : "");
            FileSearch s = t.Search;
            if (s == null) return "";
            string r;
            if (s.Cancel) r = "Find stopped: " + s.Found.ToString("N0") + " found";
            else if (!s.Finished) r = "Finding “" + t.FindText + "”… " + s.Found.ToString("N0") + " found in " + s.Folders.ToString("N0") + " folders";
            else r = s.Found.ToString("N0") + (s.Found == 1 ? " item" : " items") + " found for “" + t.FindText + "” in " + s.Folders.ToString("N0") + " folders";
            if (s.Truncated) r += " (stopped at " + FileSearch.MaxResults.ToString("N0") + "; try a more specific name)";
            if (s.Errors > 0) r += "     " + s.Errors + (s.Errors == 1 ? " folder" : " folders") + " couldn't be read";
            return r;
        }
    }
}
