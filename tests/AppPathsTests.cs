// Tests for where settings live and the move from the old "DualPane" folders.
using System;
using System.Collections.Generic;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class AppPathsTests
    {
        static bool None(string path) { return false; }
        static bool OneDriveDocs(string path) { return path.Equals(@"C:\Users\Me\OneDrive\Documents", StringComparison.OrdinalIgnoreCase); }

        [Test]
        static void SharedFolderRule()
        {
            string appData = @"C:\Users\Me\AppData\Roaming";
            string[] od = { @"C:\Users\Me\OneDrive" };
            Assert.Equal(@"C:\Users\Me\OneDrive\Documents\OrclFX",
                AppPaths.SharedFolderFor(@"C:\Users\Me\OneDrive\Documents", od, appData, None), "Documents backed up to OneDrive");
            Assert.Equal(@"D:\OneDrive - Work\Documents\OrclFX",
                AppPaths.SharedFolderFor(@"D:\OneDrive - Work\Documents", new string[] { @"C:\Users\Me\OneDrive", @"d:\onedrive - work\" }, appData, None), "work OneDrive, any case");
            Assert.Equal(@"C:\Users\Me\OneDrive\Documents\OrclFX",
                AppPaths.SharedFolderFor(@"C:\Users\Me\Documents", od, appData, OneDriveDocs), "Documents not backed up here, but OneDrive has a Documents folder");
            Assert.Equal(appData + @"\OrclFX",
                AppPaths.SharedFolderFor(@"C:\Users\Me\Documents", od, appData, None), "OneDrive without a Documents folder");
            Assert.Equal(appData + @"\OrclFX",
                AppPaths.SharedFolderFor(@"C:\Users\Me\OneDriveX\Documents", od, appData, None), "a folder that only starts with the same letters");
            Assert.Equal(appData + @"\OrclFX", AppPaths.SharedFolderFor(@"C:\Users\Me\Documents", new string[0], appData, OneDriveDocs), "no OneDrive");
        }

        static List<string> Folders(string file)
        {
            List<string> r = new List<string>();
            foreach (KeyValuePair<string, string> e in ShortcutList.Parse(File.ReadAllLines(file))) r.Add(e.Value);
            return r;
        }

        [Test]
        static void MovesFromOneDriveDualPane()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, @"AppData\DualPane"), newLocal = Path.Combine(d.Path, @"AppData\OrclFX");
                d.File(@"AppData\DualPane\state.txt", "pane0.tab=U|C:\\");
                d.File(@"AppData\DualPane\state.txt.bak", "old");
                d.File(@"AppData\DualPane\errors.log", "log");
                string oldList = d.File(@"OneDrive\DualPane\shortcuts.txt", "Docs|C:\\Docs\r\nWork|C:\\Work\r\n");
                string newList = Path.Combine(d.Path, @"OneDrive\Documents\OrclFX\shortcuts.txt");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Equal("pane0.tab=U|C:\\", File.ReadAllText(Path.Combine(newLocal, "state.txt")), "settings moved");
                Assert.True(File.Exists(Path.Combine(newLocal, "state.txt.bak")) && File.Exists(Path.Combine(newLocal, "errors.log")), "backup and log moved");
                Assert.True(!Directory.Exists(oldLocal), "the emptied old folder is removed");
                Assert.Sequence(new string[] { @"C:\Docs", @"C:\Work" }, Folders(newList), "shortcuts brought over");
                Assert.True(File.Exists(oldList), "the OneDrive copy stays for computers that haven't updated yet");
            }
        }

        [Test]
        static void EditsOnNotYetUpdatedComputersAreMergedIn()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, "DualPane"), newLocal = Path.Combine(d.Path, "OrclFX");
                string oldList = d.File(@"OneDrive\DualPane\shortcuts.txt", "A|C:\\A\r\nB|C:\\B\r\nC|C:\\C\r\n");
                string newList = Path.Combine(d.Path, @"OneDrive\Documents\OrclFX\shortcuts.txt");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);     // first updated computer
                // In the new list (updated computers): C removed, N added.
                File.WriteAllText(newList, ShortcutList.Serialize(ShortcutList.Parse(new string[] { "A|C:\\A", "B|C:\\B", "N|C:\\N" })));
                // In the old list (a computer still on the old version): B removed, O added.
                File.WriteAllText(oldList, "A|C:\\A\r\nC|C:\\C\r\nO|C:\\O\r\n");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);     // next start of an updated computer
                List<string> got = Folders(newList);
                got.Sort(StringComparer.OrdinalIgnoreCase);
                Assert.Sequence(new string[] { @"C:\A", @"C:\N", @"C:\O" }, got, "both sides' additions and removals carry over");
                // Nothing changed in the old list since: starting again changes nothing.
                File.WriteAllText(newList, ShortcutList.Serialize(ShortcutList.Parse(new string[] { "A|C:\\A" })));
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Sequence(new string[] { @"C:\A" }, Folders(newList), "an unchanged old list isn't merged again (removed shortcuts stay removed)");
            }
        }

        [Test]
        static void EarlierMigrationWithoutNoteKeepsEverything()
        {
            // 1.1.015 copied the old list without leaving a note: the first merge is a union, so nothing is lost.
            using (TempDir d = new TempDir())
            {
                string oldList = d.File(@"OneDrive\DualPane\shortcuts.txt", "A|C:\\A\r\nOld|C:\\Old\r\n");
                string newList = d.File(@"OneDrive\Documents\OrclFX\shortcuts.txt", "A|C:\\A\r\nNew|C:\\New\r\n");
                AppPaths.Migrate(Path.Combine(d.Path, "DualPane"), Path.Combine(d.Path, "OrclFX"), oldList, newList);
                Assert.Sequence(new string[] { @"C:\A", @"C:\New", @"C:\Old" }, Folders(newList), "union");
            }
        }

        [Test]
        static void NoteWithoutTheListDoesNotEmptyIt()
        {
            // The note has synced to a new computer but shortcuts.txt hasn't yet: merging must keep everything.
            using (TempDir d = new TempDir())
            {
                string oldList = d.File(@"OneDrive\DualPane\shortcuts.txt", "A|C:\\A\r\nB|C:\\B\r\nNew|C:\\New\r\n");
                string newList = Path.Combine(d.Path, @"OneDrive\Documents\OrclFX\shortcuts.txt");
                string note = Path.Combine(Path.GetDirectoryName(newList), ".shortcuts.txt.merged-" + Util.TextKey(ShortcutList.ToPortable(oldList)));
                d.File(@"OneDrive\Documents\OrclFX\" + Path.GetFileName(note), "A|C:\\A\r\nB|C:\\B\r\n");
                AppPaths.MergeList(oldList, newList, false);
                Assert.Sequence(new string[] { @"C:\A", @"C:\B", @"C:\New" }, Folders(newList), "everything kept");
            }
        }

        [Test]
        static void MovesALocalOnlyList()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, "DualPane"), newLocal = Path.Combine(d.Path, "OrclFX");
                d.File(@"DualPane\state.txt", "s");
                string oldList = d.File(@"DualPane\shortcuts.txt", "L|C:\\L\r\n");
                d.File(@"DualPane\shortcuts.txt.bak", "previous");
                string newList = Path.Combine(newLocal, "shortcuts.txt");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Sequence(new string[] { @"C:\L" }, Folders(newList), "list brought over");
                Assert.True(File.Exists(oldList + ".migrated") && !File.Exists(oldList), "the local old list is set aside");
            }
        }

        [Test]
        static void LocalListJoinsTheSharedOneLater()
        {
            // A computer kept its list in %APPDATA%\OrclFX until OneDrive\Documents appeared: its list is merged in.
            using (TempDir d = new TempDir())
            {
                string local = d.File(@"AppData\OrclFX\shortcuts.txt", "Mine|C:\\Mine\r\n");
                string shared = d.File(@"OneDrive\Documents\OrclFX\shortcuts.txt", "Theirs|C:\\Theirs\r\n");
                AppPaths.MergeList(local, shared, true);
                Assert.Sequence(new string[] { @"C:\Theirs", @"C:\Mine" }, Folders(shared), "both kept");
                Assert.True(!File.Exists(local) && File.Exists(local + ".migrated"), "the local list is set aside");
            }
        }

        [Test]
        static void KeepsNewSettings()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, "DualPane"), newLocal = Path.Combine(d.Path, "OrclFX");
                d.File(@"DualPane\state.txt", "old");
                d.File(@"OrclFX\state.txt", "new");
                AppPaths.Migrate(oldLocal, newLocal, Path.Combine(d.Path, "none.txt"), Path.Combine(newLocal, "shortcuts.txt"));
                Assert.Equal("new", File.ReadAllText(Path.Combine(newLocal, "state.txt")), "settings already in the new place win");
                Assert.True(File.Exists(Path.Combine(oldLocal, "state.txt")), "the old ones are left alone then");
            }
        }
    }
}
