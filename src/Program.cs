// Orcl File Explorer (orclfx.exe): a light multi-pane file manager that hosts the real Windows Explorer view.
// Built with the C# 5 compiler that ships with .NET Framework 4.x (see build.ps1). This file: the entry point
// (command-line switches, single-instance check, error logging).
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
    static class Program
    {
        public const string AppName = "Orcl File Explorer";
        // Set by a second start of the app: the running one shows its window (see MainForm.ListenForShow).
        public static string ShowEventName { get { return "OrclFx.Show." + Util.PathKey(MainForm.StateFile); } }
        public static bool Portable;
        static bool showingError;

        public static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MainForm.StateFile));
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(MainForm.StateFile), "errors.log"),
                    DateTime.Now.ToString("s") + "  " + Installer.Version + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
        }

        // Diagnostics: set DUALPANE_TRACE to a file path to log startup, restarts and exits.
        public static void Trace(string s)
        {
            string f = Environment.GetEnvironmentVariable("DUALPANE_TRACE");
            if (f == null) return;
            try { File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Process.GetCurrentProcess().Id + "] " + s + "\r\n"); } catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // An unexpected error shows a message instead of closing the app (and losing unsaved state).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
                // One box at a time: an error repeating on a timer mustn't stack up boxes.
                if (showingError) return;
                showingError = true;
                try
                {
                    MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { showingError = false; }
            };
            Trace("start: " + string.Join(" ", args));
            // --update <process id> <exe to replace> [--portable]: this is a downloaded update (see Updater).
            if (args.Length >= 3 && args[0] == "--update") { FinishUpdate(args); return; }
            bool portable = false, restarted = false;
            int from = 0; // --from <process id>: the window this one replaces (theme restart)
            // --quiet with --install / --uninstall: no windows at all; the exit code says whether it worked
            // (used by winget and other unattended installs).
            Installer.Quiet = Array.IndexOf(args, "--quiet") >= 0;
            foreach (string a in args)
            {
                if (a == "--restart") restarted = true;
                if (a == "--from") { int i = Array.IndexOf(args, a); if (i + 1 < args.Length) int.TryParse(args[i + 1], out from); }
                if (a == "--uninstall") { Installer.Uninstall(); return; }
                if (a == "--install") { if (!Installer.InstallQuietly()) Environment.ExitCode = 1; return; }
                if (a == "--portable") portable = true;
            }
            Portable = portable;
            if (!restarted && !portable && !Installer.IsRunningInstalledCopy() && !Installer.OfferInstall()) return;
            // One window per user: a second launch brings the running one to the front instead,
            // so two windows never overwrite each other's saved tabs.
            bool first;
            using (System.Threading.Mutex single = new System.Threading.Mutex(true, "OrclFx.Instance." + Util.PathKey(MainForm.StateFile), out first))
            {
                // After a restart (theme change) the previous window may still be closing: wait for it.
                if (!first && restarted)
                {
                    try
                    {
                        first = single.WaitOne(15000);
                        // It may be asking the user something (a save problem): wait until it has really gone.
                        if (!first && from > 0)
                        {
                            // (Bounded: if the user keeps the old window after all, this one gives up instead of
                            // popping up whenever that window is closed.)
                            try { using (Process old = Process.GetProcessById(from)) old.WaitForExit(120000); } catch { }
                            first = single.WaitOne(15000);
                        }
                    }
                    catch (System.Threading.AbandonedMutexException) { first = true; }
                }
                // Another window already uses this settings file (installed or portable): bring it forward instead,
                // so two windows never overwrite each other's tabs and shortcuts.
                if (!first)
                {
                    Trace("another copy is running: exit");
                    // Ask it to show its window: it may be hidden while a copy finishes (and then can't be found
                    // as a visible window).
                    try
                    {
                        System.Threading.EventWaitHandle show;
                        if (System.Threading.EventWaitHandle.TryOpenExisting(ShowEventName, out show)) using (show) show.Set();
                    }
                    catch { }
                    Installer.ActivateRunningCopy();
                    return;
                }
                AppPaths.MigrateFromDualPane();
                Updater.CleanDownloads();
                // Copies installed by 1.1.010 or earlier from a browser download kept the browser's mark (see Util).
                if (Installer.IsRunningInstalledCopy()) Util.RemoveDownloadMark(Application.ExecutablePath);
                ProcessReference.Register();
                Application.Run(new MainForm());
                // Let a new window (theme restart, update) start now, but don't end this process while the shell is
                // still copying or moving files for it: that would cut the operation off halfway.
                try { single.ReleaseMutex(); } catch { }
                while (ProcessReference.Busy > 0) { Application.DoEvents(); System.Threading.Thread.Sleep(200); }
                Trace("exit");
            }
        }

        // Replaces the old exe with this one once the old app has closed, then starts it again. If anything
        // fails, the old version is started instead, so the user is never left without the app.
        static void FinishUpdate(string[] args)
        {
            int pid;
            int.TryParse(args[1], out pid);
            string target = args[2];
            bool portable = Array.IndexOf(args, "--portable") >= 0;
            string startArgs = "--restart" + (portable ? " --portable" : "");
            try
            {
                Updater.WaitAndReplace(pid, Application.ExecutablePath, target);
                Util.RemoveDownloadMark(target);
                if (string.Equals(Path.GetFullPath(target), Installer.InstalledPath, StringComparison.OrdinalIgnoreCase)) Installer.Register();
                Trace("updated " + target);
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show("The update couldn't be installed: " + ex.Message + "\n\nThe current version will start instead. You can download the new version from " +
                    Updater.ReleasesPage, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            try { Process.Start(target, startArgs); }
            catch (Exception ex) { LogError(ex); }
        }
    }
}
