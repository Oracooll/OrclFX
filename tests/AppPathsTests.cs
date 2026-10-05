// Tests for where settings live and the move from the old "DualPane" folders.
using System;
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

        [Test]
        static void MovesFromOneDriveDualPane()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, @"AppData\DualPane"), newLocal = Path.Combine(d.Path, @"AppData\OrclFX");
                d.File(@"AppData\DualPane\state.txt", "pane0.tab=U|C:\\");
                d.File(@"AppData\DualPane\state.txt.bak", "old");
                d.File(@"AppData\DualPane\errors.log", "log");
                string oldList = d.File(@"OneDrive\DualPane\shortcuts.txt", "Docs|C:\\Docs");
                string newList = Path.Combine(d.Path, @"OneDrive\Documents\OrclFX\shortcuts.txt");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Equal("pane0.tab=U|C:\\", File.ReadAllText(Path.Combine(newLocal, "state.txt")), "settings moved");
                Assert.True(File.Exists(Path.Combine(newLocal, "state.txt.bak")) && File.Exists(Path.Combine(newLocal, "errors.log")), "backup and log moved");
                Assert.True(!Directory.Exists(oldLocal), "the emptied old folder is removed");
                Assert.Equal("Docs|C:\\Docs", File.ReadAllText(newList), "shortcuts copied");
                Assert.True(File.Exists(oldList), "the OneDrive copy stays for computers that haven't updated yet");

                // Running again changes nothing and overwrites nothing.
                File.WriteAllText(newList, "newer");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Equal("newer", File.ReadAllText(newList), "an existing list is never overwritten");
            }
        }

        [Test]
        static void MovesALocalOnlyList()
        {
            using (TempDir d = new TempDir())
            {
                string oldLocal = Path.Combine(d.Path, "DualPane"), newLocal = Path.Combine(d.Path, "OrclFX");
                d.File(@"DualPane\state.txt", "s");
                string oldList = d.File(@"DualPane\shortcuts.txt", "list");
                d.File(@"DualPane\shortcuts.txt.bak", "previous");
                string newList = Path.Combine(newLocal, "shortcuts.txt");
                AppPaths.Migrate(oldLocal, newLocal, oldList, newList);
                Assert.Equal("list", File.ReadAllText(newList), "list moved");
                Assert.Equal("previous", File.ReadAllText(newList + ".bak"), "its backup moved");
                Assert.True(!Directory.Exists(oldLocal), "nothing left behind");
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
