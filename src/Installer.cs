// Orcl File Explorer: Per-user install, update and uninstall (no admin rights needed).
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
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OrclFileExplorer";
        const string OldUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DualPane";
        public static readonly string Version = Util.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName);
        static readonly string InstalledExe = Path.Combine(InstallDir, "orclfx.exe");
        // Where versions before 1.1 (named DualPane) were installed.
        static readonly string OldInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DualPane");
        static readonly string OldInstalledExe = Path.Combine(OldInstallDir, "DualPane.exe");
        static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
        static readonly string OldStartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DualPane.lnk");

        public static bool IsRunningInstalledCopy()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase);
        }

        // Returns true when this copy should keep running.
        public static bool OfferInstall()
        {
            bool installed = File.Exists(InstalledExe);
            string msg = installed
                ? "Orcl File Explorer is already installed on this computer.\n\nYes: update the installed copy to version " + Version + " and start it\nNo: just run this copy without installing\nCancel: quit"
                : "Install Orcl File Explorer on this computer?\n\nIt installs for your Windows account only (no admin rights needed), adds it to the Start menu, and can be removed from Settings > Apps.\n\nYes: install and start\nNo: just run this copy without installing\nCancel: quit";
            DialogResult r = MessageBox.Show(msg, Program.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
            if (!Install()) return false;
            Process.Start(InstalledExe);
            return false;
        }

        public static void InstallFromMenu()
        {
            if (Install())
                MessageBox.Show(Program.AppName + " " + Version + " is installed. You'll find it in the Start menu; right-click it there to pin it to the taskbar.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // "DualPane.exe --install": install or update without asking (errors are still shown).
        public static bool Quiet;   // --quiet: never show a window (unattended install / uninstall)

        public static bool InstallQuietly()
        {
            if (IsRunningInstalledCopy()) return true; // already the installed copy; nothing to copy
            return Install();
        }

        // Brings the already-running DualPane window to the front (used when it is launched a second time).
        public static void ActivateRunningCopy()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("orclfx"))
            {
                if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                Native.ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                Native.SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }

        static bool Install()
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                File.Copy(Application.ExecutablePath, InstalledExe, true);
            }
            catch (IOException)
            {
                if (!Quiet) MessageBox.Show("The installed Orcl File Explorer is running. Close it and try again.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                if (!Quiet) MessageBox.Show("Install failed: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            Register();
            return true;
        }

        public static string InstalledPath { get { return InstalledExe; } }

        // After an update has replaced the installed exe: refresh the Start menu shortcut and the Settings > Apps entry.
        public static void Register()
        {
            Util.RemoveDownloadMark(InstalledExe);
            try { CreateShortcut(StartMenuLink, InstalledExe); } catch { }
            MigrateOldInstall();
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Orcl");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        // Removes the pre-1.1 "DualPane" install and points a taskbar pin made for it at the new exe.
        static void MigrateOldInstall()
        {
            try { if (File.Exists(OldStartMenuLink)) File.Delete(OldStartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(OldUninstallKey, false); } catch { }
            try
            {
                string pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(pins))
                    foreach (string link in Directory.GetFiles(pins, "*.lnk"))
                    {
                        Type t = Type.GetTypeFromProgID("WScript.Shell");
                        object shell = Activator.CreateInstance(t);
                        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                        Type lt = lnk.GetType();
                        string target = lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null) as string;
                        if (string.Equals(target, OldInstalledExe, StringComparison.OrdinalIgnoreCase))
                        {
                            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
                            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
                            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
                            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                        }
                        Marshal.ReleaseComObject(lnk);
                        Marshal.ReleaseComObject(shell);
                    }
            }
            catch { }
            // The old exe may still be running; delete what we can, the rest goes next time.
            try { if (File.Exists(OldInstalledExe)) File.Delete(OldInstalledExe); } catch { }
            try { if (Directory.Exists(OldInstallDir)) Directory.Delete(OldInstallDir, false); } catch { } // only if now empty
        }

        static void CreateShortcut(string link, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Multi-pane file manager" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            Marshal.ReleaseComObject(lnk);
            Marshal.ReleaseComObject(shell);
        }

        public static void Uninstall()
        {
            // Quiet (winget): no questions, and the settings are kept.
            if (!Quiet && MessageBox.Show("Uninstall Orcl File Explorer from this computer?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool wipeSettings = !Quiet && MessageBox.Show("Also delete your saved tabs and settings?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            try { File.Delete(StartMenuLink); } catch { }
            try { File.Delete(OldStartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (wipeSettings)
            {
                // Only this computer's settings files, and the folder only if it is the default one and now empty.
                // The shared shortcuts list in OneDrive is left alone: other computers use it.
                string dir = Path.GetDirectoryName(MainForm.StateFile);
                foreach (string name in new string[] { MainForm.StateFile, MainForm.StateFile + ".bak" })
                    try { File.Delete(name); } catch { }
                string defaultDir = AppPaths.LocalFolder;
                if (string.Equals(dir, defaultDir, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(dir, false); } catch { }
            }
            if (!Quiet)
                MessageBox.Show("Orcl File Explorer was uninstalled. Its program file is removed a few seconds after this message closes.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            // The running exe can't delete itself: delete just orclfx.exe a moment after we exit (retrying while it
            // is still in use), then the folder only if nothing else is left in it.
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c for /l %i in (1,1,15) do (ping 127.0.0.1 -n 2 > nul & del /f /q \"" + InstalledExe + "\" 2> nul & if not exist \"" + InstalledExe + "\" (rmdir \"" + InstallDir + "\" 2> nul & exit))");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try { Process.Start(psi); } catch { }
        }
    }
}
