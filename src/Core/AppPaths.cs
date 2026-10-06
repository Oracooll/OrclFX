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
        // Saved layouts, next to the shortcuts list (so test runs with their own list also get their own layouts).
        public static readonly string LayoutsFile = Path.Combine(Path.GetDirectoryName(ShortcutsFile), "layouts.txt");

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

        // Run at every start, before anything reads the settings. Does nothing when tests point the files elsewhere.
        public static void MigrateFromDualPane()
        {
            if (Environment.GetEnvironmentVariable("DUALPANE_STATE") != null || Environment.GetEnvironmentVariable("DUALPANE_SHORTCUTS") != null) return;
            try
            {
                Migrate(OldLocalFolder, LocalFolder, OldShortcutsFile, ShortcutsFile);
                // A list this computer kept locally before the shared folder appeared (OneDrive\Documents created later).
                string localList = Path.Combine(LocalFolder, "shortcuts.txt");
                if (!Util.SameFolder(localList, ShortcutsFile)) MergeList(localList, ShortcutsFile, true);
            }
            catch (Exception ex) { Program.LogError(ex); }
        }

        // The per-computer files (state.txt, its .bak, errors.log) are moved; the old folder goes if that leaves
        // it empty. The old shortcuts list is merged into the new one (see MergeList): one in OneDrive stays where
        // it is, because computers that haven't updated yet keep using it, and its later changes are merged in on
        // the next start; one that was only on this computer is merged once and renamed to .migrated.
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
            MergeList(oldShortcuts, newShortcuts, Util.SameFolder(Path.GetDirectoryName(oldShortcuts), oldLocal));
            try { if (Directory.Exists(oldLocal) && Directory.GetFileSystemEntries(oldLocal).Length == 0) Directory.Delete(oldLocal); } catch { }
        }

        // Merges the shortcuts list source into target whenever source has changed since it was last merged.
        // A hidden note next to target remembers what was taken from source last time, so the merge is three-way
        // (changes and removals on either side carry over, removed shortcuts don't come back). The first time,
        // or without the note, it's a union: nothing is lost. A local-only source (onlyHere) is renamed to
        // .migrated afterwards.
        internal static void MergeList(string source, string target, bool onlyHere)
        {
            if (!File.Exists(source) || Util.SameFolder(source, target)) return;
            string sourceText = File.ReadAllText(source, System.Text.Encoding.UTF8);
            // Named from the source's portable path (%OneDrive%\...), so every computer uses the same note.
            string note = Path.Combine(Path.GetDirectoryName(target), "." + Path.GetFileName(target) + ".merged-" + Util.TextKey(ShortcutList.ToPortable(source)));
            string noteText = File.Exists(note) ? File.ReadAllText(note, System.Text.Encoding.UTF8) : null;
            // Without the list itself (not synced yet), the note can't be used: a three-way merge would read every
            // shortcut in it as removed. A union then keeps everything.
            if (!File.Exists(target)) noteText = null;
            if (noteText == sourceText && !onlyHere) return; // nothing new there
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            ShortcutList.MergeInto(target, ShortcutList.Parse(Lines(sourceText)), noteText == null ? null : ShortcutList.Parse(Lines(noteText)));
            if (onlyHere)
            {
                string moved = source + ".migrated";
                if (File.Exists(moved)) File.Delete(moved);
                File.Move(source, moved);
                try { if (File.Exists(source + ".bak")) File.Delete(source + ".bak"); } catch { }
                return;
            }
            if (File.Exists(note)) File.SetAttributes(note, FileAttributes.Normal);
            File.WriteAllText(note, sourceText, new System.Text.UTF8Encoding(false));
            File.SetAttributes(note, FileAttributes.Hidden);
        }

        static string[] Lines(string text) { return text.Replace("\r\n", "\n").Split('\n'); }
    }
}
