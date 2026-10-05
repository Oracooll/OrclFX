// Orcl File Explorer: where settings are kept, and moving them from the old "DualPane" folders. Covered by tests\.
using System;
using System.Collections.Generic;
using System.IO;

namespace OrclFileExplorer
{
    static class AppPaths
    {
        public const string FolderName = "OrclFX";
        static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // This computer's own settings (tabs, layout, theme ...): %APPDATA%\OrclFX. Never shared, or computers
        // would overwrite each other's tabs.
        public static readonly string LocalFolder = Path.Combine(AppData, FolderName);

        // Settings shared by all your computers (the shortcuts list), in this order:
        // - Documents\OrclFX when Documents is backed up to OneDrive;
        // - OneDrive\Documents\OrclFX when OneDrive has a Documents folder (backed up from another computer), so
        //   computers with and without the backup share the same folder;
        // - otherwise this computer's %APPDATA%\OrclFX.
        public static readonly string SharedFolder = SharedFolderFor(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), OneDriveRoots(), AppData, Directory.Exists);

        // DUALPANE_STATE and DUALPANE_SHORTCUTS point these elsewhere (for tests).
        public static readonly string StateFile = Environment.GetEnvironmentVariable("DUALPANE_STATE") ?? Path.Combine(LocalFolder, "state.txt");
        public static readonly string ShortcutsFile = Environment.GetEnvironmentVariable("DUALPANE_SHORTCUTS") ?? Path.Combine(SharedFolder, "shortcuts.txt");

        static List<string> OneDriveRoots()
        {
            List<string> r = new List<string>();
            foreach (string v in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string p = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(p)) r.Add(p);
            }
            return r;
        }

        public static string SharedFolderFor(string documents, IEnumerable<string> oneDriveRoots, string appData, Func<string, bool> folderExists)
        {
            if (!string.IsNullOrEmpty(documents))
                foreach (string root in oneDriveRoots)
                    if (Util.Rebase(documents, root, root) != null) return Path.Combine(documents, FolderName);
            foreach (string root in oneDriveRoots)
            {
                string docs = Path.Combine(root, "Documents");
                if (folderExists(docs)) return Path.Combine(docs, FolderName);
            }
            return Path.Combine(appData, FolderName);
        }

        // ---- moving from the old locations (versions up to 1.1.014)

        // Tabs and settings were in %APPDATA%\DualPane; the shortcuts list in OneDrive\DualPane (or %APPDATA%\DualPane).
        static readonly string OldLocalFolder = Path.Combine(AppData, "DualPane");
        static readonly string OldShortcutsFile = Path.Combine(
            Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer") ?? AppData,
            "DualPane", "shortcuts.txt");

        // Run once at startup, before anything reads the settings. Does nothing when tests point the files elsewhere.
        public static void MigrateFromDualPane()
        {
            if (Environment.GetEnvironmentVariable("DUALPANE_STATE") != null || Environment.GetEnvironmentVariable("DUALPANE_SHORTCUTS") != null) return;
            try { Migrate(OldLocalFolder, LocalFolder, OldShortcutsFile, ShortcutsFile); }
            catch (Exception ex) { Program.LogError(ex); }
        }

        // The per-computer files (state.txt, its .bak, errors.log) are moved; the old folder goes if that leaves
        // it empty. The shortcuts list is copied when it lives in OneDrive: computers that haven't updated yet keep
        // reading the old one. One that was only on this computer is moved. Nothing is overwritten.
        internal static void Migrate(string oldLocal, string newLocal, string oldShortcuts, string newShortcuts)
        {
            if (Directory.Exists(oldLocal) && !File.Exists(Path.Combine(newLocal, "state.txt")))
                foreach (string name in new string[] { "state.txt", "state.txt.bak", "errors.log" })
                {
                    string from = Path.Combine(oldLocal, name), to = Path.Combine(newLocal, name);
                    if (!File.Exists(from) || File.Exists(to)) continue;
                    Directory.CreateDirectory(newLocal);
                    File.Move(from, to);
                }
            if (File.Exists(oldShortcuts) && !File.Exists(newShortcuts))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newShortcuts));
                bool onlyHere = Util.SameFolder(Path.GetDirectoryName(oldShortcuts), oldLocal);
                if (onlyHere) File.Move(oldShortcuts, newShortcuts);
                else File.Copy(oldShortcuts, newShortcuts);
                string bak = oldShortcuts + ".bak";
                if (onlyHere && File.Exists(bak) && !File.Exists(newShortcuts + ".bak")) File.Move(bak, newShortcuts + ".bak");
            }
            try { if (Directory.Exists(oldLocal) && Directory.GetFileSystemEntries(oldLocal).Length == 0) Directory.Delete(oldLocal); } catch { }
        }
    }
}
