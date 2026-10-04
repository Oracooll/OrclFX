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
        public static bool Portable;

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
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Trace("start: " + string.Join(" ", args));
            bool portable = false, restarted = false;
            foreach (string a in args)
            {
                if (a == "--restart") restarted = true;
                if (a == "--uninstall") { Installer.Uninstall(); return; }
                if (a == "--install") { Installer.InstallQuietly(); return; }
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
                if (!first && restarted) { try { first = single.WaitOne(15000); } catch (System.Threading.AbandonedMutexException) { first = true; } }
                // Another window already uses this settings file (installed or portable): bring it forward instead,
                // so two windows never overwrite each other's tabs and shortcuts.
                if (!first) { Trace("another copy is running: exit"); Installer.ActivateRunningCopy(); return; }
                Application.Run(new MainForm());
                Trace("exit");
            }
        }
    }
}
