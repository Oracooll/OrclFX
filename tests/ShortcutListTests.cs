// Tests for the shared shortcuts file: format, portable paths and the three-way merge between computers.
using System;
using System.Collections.Generic;

namespace OrclFileExplorer.Tests
{
    static class ShortcutListTests
    {
        static KeyValuePair<string, string> S(string label, string path) { return new KeyValuePair<string, string>(label, path); }

        static List<KeyValuePair<string, string>> L(params KeyValuePair<string, string>[] items)
        {
            return new List<KeyValuePair<string, string>>(items);
        }

        static List<string> Show(List<KeyValuePair<string, string>> list)
        {
            List<string> r = new List<string>();
            foreach (KeyValuePair<string, string> e in list) r.Add(e.Key + "=" + e.Value);
            return r;
        }

        static readonly KeyValuePair<string, string> A = S("Alpha", @"C:\A"), B = S("Beta", @"C:\B"), C = S("Gamma", @"C:\C");

        // Runs body with the OneDrive / profile variables set to known values, then restores them.
        static void WithFolders(string oneDrive, string profile, Action body)
        {
            string[] names = { "OneDrive", "OneDriveConsumer", "OneDriveCommercial", "USERPROFILE" };
            string[] saved = new string[names.Length];
            for (int i = 0; i < names.Length; i++) saved[i] = Environment.GetEnvironmentVariable(names[i]);
            try
            {
                Environment.SetEnvironmentVariable("OneDrive", oneDrive);
                Environment.SetEnvironmentVariable("OneDriveConsumer", null);
                Environment.SetEnvironmentVariable("OneDriveCommercial", null);
                Environment.SetEnvironmentVariable("USERPROFILE", profile);
                body();
            }
            finally
            {
                for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], saved[i]);
            }
        }

        [Test]
        static void Parse_SkipsCommentsAndBrokenLines()
        {
            List<KeyValuePair<string, string>> r = ShortcutList.Parse(new string[] {
                ShortcutList.Header, "", "no bar here", "|C:\\NoLabel", "  Work  |  C:\\Work  ", "#Archive|C:\\Archive" });
            Assert.Sequence(new string[] { @"Work=C:\Work", @"#Archive=C:\Archive" }, Show(r), "parsed entries");
        }

        [Test]
        static void Portable_UsesOneDriveBeforeProfile()
        {
            WithFolders(@"C:\Users\Me\OneDrive", @"C:\Users\Me", delegate
            {
                Assert.Equal(@"%OneDrive%\Docs", ShortcutList.ToPortable(@"C:\Users\Me\OneDrive\Docs"), "inside OneDrive");
                Assert.Equal(@"%OneDrive%", ShortcutList.ToPortable(@"c:\users\me\onedrive"), "OneDrive itself, any case");
                Assert.Equal(@"%USERPROFILE%\Desktop", ShortcutList.ToPortable(@"C:\Users\Me\Desktop"), "inside the profile");
                Assert.Equal(@"C:\Users\Me2\X", ShortcutList.ToPortable(@"C:\Users\Me2\X"), "a folder that only starts with the same letters");
                Assert.Equal(@"D:\Data", ShortcutList.ToPortable(@"D:\Data"), "elsewhere");
            });
        }

        [Test]
        static void SavedOnOneComputer_OpensOnAnother()
        {
            string text = null;
            WithFolders(@"C:\Users\Me\OneDrive", @"C:\Users\Me", delegate
            {
                text = ShortcutList.Serialize(L(S("Docs", @"C:\Users\Me\OneDrive\Docs"), S("Dl", @"C:\Users\Me\Downloads"), S("Data", @"D:\Data")));
            });
            WithFolders(@"E:\OneDrive", @"C:\Users\Other", delegate
            {
                List<KeyValuePair<string, string>> r = ShortcutList.Parse(text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None));
                Assert.Sequence(new string[] { @"Docs=E:\OneDrive\Docs", @"Dl=C:\Users\Other\Downloads", @"Data=D:\Data" }, Show(r), "resolved on the second computer");
            });
        }

        [Test]
        static void Merge_NoChanges()
        {
            Assert.Sequence(Show(L(A, B)), Show(ShortcutList.Merge(L(A, B), L(A, B), L(A, B))), "merged");
        }

        [Test]
        static void Merge_AdditionsOnBothSidesAreKept()
        {
            Assert.Sequence(Show(L(A, C, B)), Show(ShortcutList.Merge(L(A), L(A, B), L(A, C))), "merged");
        }

        [Test]
        static void Merge_DeletedElsewhereStaysDeleted()
        {
            // The audit's case: another computer removed B; saving here must not bring it back.
            Assert.Sequence(Show(L(A, C)), Show(ShortcutList.Merge(L(A, B, C), L(A, B, C), L(A, C))), "merged");
        }

        [Test]
        static void Merge_DeletedHereStaysDeleted()
        {
            Assert.Sequence(Show(L(A, C)), Show(ShortcutList.Merge(L(A, B, C), L(A, C), L(A, B, C))), "merged");
        }

        [Test]
        static void Merge_DeletionWinsOverRename()
        {
            Assert.Sequence(Show(L(A)), Show(ShortcutList.Merge(L(A, B), L(A), L(A, S("Beta 2", @"C:\B")))), "merged");
        }

        [Test]
        static void Merge_RenameElsewhereIsKept()
        {
            Assert.Sequence(Show(L(A, S("Beta 2", @"C:\B"))), Show(ShortcutList.Merge(L(A, B), L(A, B), L(A, S("Beta 2", @"C:\B")))), "merged");
        }

        [Test]
        static void Merge_RenameHereWinsOverRenameElsewhere()
        {
            Assert.Sequence(Show(L(S("Mine", @"C:\A"))), Show(ShortcutList.Merge(L(A), L(S("Mine", @"C:\A")), L(S("Theirs", @"C:\A")))), "merged");
        }

        [Test]
        static void Merge_ReorderHereIsKeptWithAdditionsElsewhere()
        {
            Assert.Sequence(Show(L(C, B, A, S("New", @"C:\N"))),
                Show(ShortcutList.Merge(L(A, B, C), L(C, B, A), L(A, B, C, S("New", @"C:\N")))), "merged");
        }

        [Test]
        static void Merge_ReorderElsewhereIsKept()
        {
            Assert.Sequence(Show(L(C, A, B)), Show(ShortcutList.Merge(L(A, B, C), L(A, B, C), L(C, A, B))), "merged");
        }

        [Test]
        static void Merge_SameFolderWrittenDifferentlyIsOneShortcut()
        {
            List<KeyValuePair<string, string>> r = ShortcutList.Merge(L(), L(S("Here", @"C:\Data")), L(S("There", @"c:\data\")));
            Assert.Equal(1, r.Count, "number of shortcuts");
        }

        [Test]
        static void UnionKeepsEverything()
        {
            Assert.Sequence(Show(L(A, B, C)), Show(ShortcutList.Union(L(A, B), L(S("Other label", @"c:\b\"), C))), "current first, then what's new");
        }

        [Test]
        static void FindsOneDriveConflictCopies()
        {
            using (TempDir d = new TempDir())
            {
                string f = d.File("shortcuts.txt", "x");
                d.File("shortcuts-DESKTOP-ABC.txt", "y");
                d.File("shortcuts-LAPTOP-2.txt", "z");
                d.File("shortcuts.txt.bak", "b");
                d.File("shortcuts-old.txtx", "no");
                d.File("other-DESKTOP.txt", "no");
                List<string> c = ShortcutList.ConflictCopies(f);
                List<string> names = new List<string>();
                foreach (string p in c) names.Add(System.IO.Path.GetFileName(p));
                Assert.Sequence(new string[] { "shortcuts-DESKTOP-ABC.txt", "shortcuts-LAPTOP-2.txt" }, names, "conflict copies");
            }
        }

        [Test]
        static void Reload_KeepsUnsavedChanges()
        {
            // A save failed here (shortcut B added), then another computer's version (with C) arrives:
            // the reload merges instead of replacing, so B survives. (1.1.007 dropped B here.)
            Assert.Sequence(Show(L(A, C, B)), Show(ShortcutList.Merge(L(A), L(A, B), L(A, C))), "after the reload");
        }

        [Test]
        static void SaveMerged_WritesTheListWhenThereIsNoFile()
        {
            using (TempDir d = new TempDir())
            {
                string f = System.IO.Path.Combine(d.Path, "shortcuts.txt");
                List<KeyValuePair<string, string>> saved = ShortcutList.SaveMerged(f, L(), L(A, B));
                Assert.Sequence(Show(L(A, B)), Show(saved), "returned");
                Assert.Sequence(Show(L(A, B)), Show(ShortcutList.Parse(System.IO.File.ReadAllLines(f))), "in the file");
            }
        }

        [Test]
        static void DamagedFile_IsReadFromItsBackup()
        {
            using (TempDir d = new TempDir())
            {
                string f = System.IO.Path.Combine(d.Path, "shortcuts.txt");
                ShortcutList.SaveMerged(f, L(), L(A));
                ShortcutList.SaveMerged(f, L(A), L(A, B)); // shortcuts.txt.bak now holds [A]
                System.IO.File.WriteAllText(f, "\0\0\0\0");
                Assert.Sequence(Show(L(A)), Show(ShortcutList.Parse(ShortcutList.ReadLines(f))), "read from the backup");
                Assert.True(ShortcutList.IsIntact(new string[] { ShortcutList.Header }), "an empty list is intact");
                Assert.True(!ShortcutList.IsIntact(new string[0]), "an empty file is damaged");
            }
        }

        [Test]
        static void SaveMerged_OverADamagedFileKeepsTheListAndTheBackup()
        {
            using (TempDir d = new TempDir())
            {
                string f = System.IO.Path.Combine(d.Path, "shortcuts.txt");
                ShortcutList.SaveMerged(f, L(), L(A));
                ShortcutList.SaveMerged(f, L(A), L(A, B)); // backup: [A]
                System.IO.File.WriteAllText(f, "");
                // Read as empty, the merge would remove A and B (the base had them, "the file" doesn't).
                List<KeyValuePair<string, string>> saved = ShortcutList.SaveMerged(f, L(A, B), L(A, B, C));
                Assert.Sequence(Show(L(A, B, C)), Show(saved), "saved");
                Assert.Sequence(Show(L(A)), Show(ShortcutList.Parse(System.IO.File.ReadAllLines(f + ".bak"))), "the good backup is kept");
            }
        }

        [Test]
        static void SaveMerged_KeepsWhatOthersSavedMeanwhile()
        {
            using (TempDir d = new TempDir())
            {
                string f = System.IO.Path.Combine(d.Path, "shortcuts.txt");
                ShortcutList.SaveMerged(f, L(), L(A, B));
                // Another window, starting from [A, B], removed B and added C.
                ShortcutList.SaveMerged(f, L(A, B), L(A, C));
                // This window also started from [A, B] and renamed A.
                List<KeyValuePair<string, string>> saved = ShortcutList.SaveMerged(f, L(A, B), L(S("Alpha 2", @"C:\A"), B));
                Assert.Sequence(Show(L(S("Alpha 2", @"C:\A"), C)), Show(saved), "merged");
            }
        }

        [Test]
        static void SaveMerged_ManyWindowsAtOnceLoseNothing()
        {
            // Eight windows start from the same list and each adds its own shortcut at the same moment.
            // The read-merge-write is one locked step, so every addition ends up in the file.
            using (TempDir d = new TempDir())
            {
                string f = System.IO.Path.Combine(d.Path, "shortcuts.txt");
                ShortcutList.SaveMerged(f, L(), L(A));
                List<Exception> errors = new List<Exception>();
                List<System.Threading.Thread> threads = new List<System.Threading.Thread>();
                System.Threading.ManualResetEvent go = new System.Threading.ManualResetEvent(false);
                for (int i = 0; i < 8; i++)
                {
                    int id = i;
                    System.Threading.Thread th = new System.Threading.Thread(delegate()
                    {
                        go.WaitOne();
                        try { ShortcutList.SaveMerged(f, L(A), L(A, S("New " + id, @"C:\New" + id))); }
                        catch (Exception e) { lock (errors) errors.Add(e); }
                    });
                    threads.Add(th);
                    th.Start();
                }
                go.Set();
                foreach (System.Threading.Thread th in threads) th.Join();
                Assert.Equal(0, errors.Count, "failed saves");
                Assert.Equal(9, ShortcutList.Parse(System.IO.File.ReadAllLines(f)).Count, "shortcuts in the file");
            }
        }
    }
}
