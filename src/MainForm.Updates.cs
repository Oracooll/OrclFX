// Orcl File Explorer: main window, the "Check for updates" part (the work itself is in Core\Updater.cs).
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class MainForm
    {
        public bool AutoUpdateCheck = true;   // look for a new version once a day (settings key "autoupdate")
        DateTime lastUpdateCheck;             // settings key "updatecheck"
        bool updateBusy;
        Updater.Release available;            // a newer release found by the daily check
        public Updater.Release AvailableUpdate { get { return available; } }

        // Once a day, quietly: if there's a newer version, the status bar and the menu say so. Nothing is
        // downloaded or installed without asking.
        void ScheduleUpdateCheck()
        {
            if (!AutoUpdateCheck || (DateTime.UtcNow - lastUpdateCheck).TotalHours < 20) return;
            // Test runs (with their own settings file) don't contact GitHub unless they point the check elsewhere.
            if (Environment.GetEnvironmentVariable("DUALPANE_STATE") != null && Environment.GetEnvironmentVariable("DUALPANE_UPDATE_URL") == null) return;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 15000;
            t.Tick += delegate { t.Stop(); t.Dispose(); CheckForUpdates(false); };
            t.Start();
        }

        public void SetAutoUpdateCheck(bool on)
        {
            AutoUpdateCheck = on;
            StateChanged();
            if (on) ScheduleUpdateCheck();
        }

        // userAsked: from the menu (always answers); otherwise the daily check (silent unless there's news).
        public void CheckForUpdates(bool userAsked)
        {
            if (updateBusy) return;
            updateBusy = true;
            if (userAsked) Notice("Checking for updates…");
            Thread th = new Thread(delegate()
            {
                Updater.Release r = null;
                string error = null;
                try { r = Updater.FetchLatest(); }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate { UpdateChecked(r, error, userAsked); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        void UpdateChecked(Updater.Release r, string error, bool userAsked)
        {
            updateBusy = false;
            if (error == null) { lastUpdateCheck = DateTime.UtcNow; StateChanged(); }
            if (error != null || r == null)
            {
                if (userAsked)
                    MessageBox.Show(this, "Couldn't check for updates" + (error != null ? ": " + error : " (no release with orclfx.exe was found).") +
                        "\n\nReleases: " + Updater.ReleasesPage, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Updater.IsNewer(r))
            {
                available = null;
                if (userAsked)
                    MessageBox.Show(this, "You have the latest version (" + Installer.Version + ").", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus();
                return;
            }
            available = r;
            UpdateStatus();
            if (!userAsked && !TestAutoUpdate) return; // the status bar and the menu now offer it
            OfferUpdate(r);
        }

        // Test hook: DUALPANE_TEST_UPDATE=1 accepts an update without asking (used to test the whole update cycle).
        static readonly bool TestAutoUpdate = Environment.GetEnvironmentVariable("DUALPANE_TEST_UPDATE") == "1";

        public void OfferUpdate(Updater.Release r)
        {
            string notes = (r.Notes ?? "").Trim();
            if (notes.Length > 700) notes = notes.Substring(0, 700).TrimEnd() + "…";
            if (!TestAutoUpdate && MessageBox.Show(this, "Version " + Util.FormatVersion(r.Version) + " is available (you have " + Installer.Version + ").\n\n" +
                (notes.Length > 0 ? notes + "\n\n" : "") + "Update now? Your tabs are saved and the window restarts with the new version.",
                Program.AppName + " update", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            updateBusy = true;
            Notice("Downloading version " + Util.FormatVersion(r.Version) + "…");
            Thread th = new Thread(delegate()
            {
                string file = null, error = null;
                try { file = Updater.Download(r); }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate { Downloaded(r, file, error); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        void Downloaded(Updater.Release r, string file, string error)
        {
            updateBusy = false;
            if (error != null)
            {
                MessageBox.Show(this, "The update couldn't be downloaded: " + error + "\n\nNothing was changed. Releases: " + Updater.ReleasesPage,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (ProcessReference.Busy > 0)
            {
                MessageBox.Show(this, "A copy or move is still running in Orcl File Explorer. Update when it has finished (menu \u203A Update).",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // The new version reads the saved settings, so they must be saved first.
            string problem = SaveAll();
            if (problem != null)
            {
                MessageBox.Show(this, problem + "\n\nThe update was not installed, so nothing is lost. Try again later.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Process.Start(file, "--update " + Process.GetCurrentProcess().Id + " \"" + Application.ExecutablePath + "\"" + (Program.Portable ? " --portable" : ""));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The update couldn't be started: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Program.Trace("updating to " + r.Tag);
            restarting = true;
            Close();
        }
    }
}
