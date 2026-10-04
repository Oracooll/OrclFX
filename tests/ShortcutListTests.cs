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
    }
}
