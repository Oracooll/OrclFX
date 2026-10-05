// Tests for updates: reading GitHub's release data, checking a download, and replacing the exe.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OrclFileExplorer.Tests
{
    static class UpdaterTests
    {
        // The app exe the tests were built against (next to the test exe).
        static string AppExe { get { return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "orclfx.exe"); } }

        static string Json(string tag, string assetName, string url, long size, string digest, bool prerelease)
        {
            return "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (prerelease ? "true" : "false") + ",\"draft\":false,\"body\":\"- Fixes\",\"assets\":[" +
                "{\"name\":\"notes.txt\",\"browser_download_url\":\"https://github.com/x/notes.txt\",\"size\":5}," +
                "{\"name\":\"" + assetName + "\",\"browser_download_url\":\"" + url + "\",\"size\":" + size +
                (digest != null ? ",\"digest\":\"" + digest + "\"" : "") + "}]}";
        }

        [Test]
        static void Tags()
        {
            Assert.Equal(new Version(1, 1, 9, 0), Updater.ParseTag("v1.1.009"), "v1.1.009");
            Assert.Equal(new Version(1, 2, 0, 0), Updater.ParseTag("1.2.000"), "without v");
            foreach (string bad in new string[] { null, "", "v1.1", "v1.1.x", "latest", "v1.1.1.1", "v-1.1.1" })
                Assert.True(Updater.ParseTag(bad) == null, "'" + bad + "'");
        }

        [Test]
        static void ReadsTheRelease()
        {
            Updater.Release r = Updater.ParseRelease(Json("v1.1.012", "orclfx.exe", "https://github.com/Oracooll/orcl-file-explorer/releases/download/v1.1.012/orclfx.exe",
                123456, "sha256:ABCDEF0123", false));
            Assert.True(r != null, "parsed");
            Assert.Equal(new Version(1, 1, 12, 0), r.Version, "version");
            Assert.Equal(123456L, r.ExeSize, "size");
            Assert.Equal("abcdef0123", r.Sha256, "checksum, lower case");
            Assert.Equal("- Fixes", r.Notes, "notes");
            Assert.True(r.ExeUrl.EndsWith("/orclfx.exe"), "the exe, not the other asset");
        }

        [Test]
        static void IgnoresUnusableReleases()
        {
            Assert.True(Updater.ParseRelease(Json("v1.1.012", "other.zip", "https://x/other.zip", 1, null, false)) == null, "no orclfx.exe");
            Assert.True(Updater.ParseRelease(Json("v1.1.012", "orclfx.exe", "https://x/orclfx.exe", 1, null, true)) == null, "prerelease");
            Assert.True(Updater.ParseRelease(Json("nightly", "orclfx.exe", "https://x/orclfx.exe", 1, null, false)) == null, "tag isn't a version");
            Assert.True(Updater.ParseRelease(Json("v1.1.012", "orclfx.exe", "http://x/orclfx.exe", 1, null, false)) == null, "not https");
        }

        [Test]
        static void NewerOnlyWhenTheVersionIsHigher()
        {
            Version cur = Updater.Current;
            Updater.Release r = new Updater.Release();
            r.Version = new Version(cur.Major, cur.Minor, cur.Build + 1, 0);
            Assert.True(Updater.IsNewer(r), "next build");
            r.Version = new Version(cur.Major, cur.Minor, cur.Build, 0);
            Assert.True(!Updater.IsNewer(r), "same version");
            r.Version = new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build - 1), 0);
            Assert.True(!Updater.IsNewer(r), "older");
        }

        static Updater.Release ReleaseFor(string file)
        {
            Updater.Release r = new Updater.Release();
            r.Version = AssemblyName.GetAssemblyName(file).Version;
            r.ExeSize = new FileInfo(file).Length;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream f = File.OpenRead(file))
                r.Sha256 = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            return r;
        }

        static string Fails(string file, Updater.Release r)
        {
            try { Updater.Verify(file, r); return null; }
            catch (IOException e) { return e.Message; }
        }

        [Test]
        static void VerifiesTheDownload()
        {
            Updater.Release r = ReleaseFor(AppExe);
            Assert.Equal(null, Fails(AppExe, r), "the genuine file");
            Updater.Release wrong = ReleaseFor(AppExe);
            wrong.Sha256 = "00" + wrong.Sha256.Substring(2);
            Assert.True(Fails(AppExe, wrong) != null, "a wrong checksum is refused");
            wrong = ReleaseFor(AppExe);
            wrong.ExeSize++;
            Assert.True(Fails(AppExe, wrong) != null, "a wrong size is refused");
            wrong = ReleaseFor(AppExe);
            wrong.Version = new Version(9, 9, 9, 0);
            Assert.True(Fails(AppExe, wrong) != null, "a different version is refused");
            using (TempDir d = new TempDir())
            {
                string fake = d.File("orclfx.exe", "not a program");
                Updater.Release f = new Updater.Release();
                f.Version = Updater.Current;
                f.ExeSize = new FileInfo(fake).Length;
                Assert.True(Fails(fake, f) != null, "something that isn't the app is refused");
                // Another .NET program (the test runner itself) with matching size and checksum is refused too.
                string other = Assembly.GetExecutingAssembly().Location;
                Updater.Release o = ReleaseFor(other);
                Assert.True(Fails(other, o) != null, "another program is refused");
            }
        }

        [Test]
        static void AFailedReplaceLeavesTheOldProgram()
        {
            using (TempDir d = new TempDir())
            {
                string target = d.File("orclfx.exe", "old version");
                bool failed = false;
                try { Util.ReplaceFileSafely(Path.Combine(d.Path, "missing.exe"), target); }
                catch (IOException) { failed = true; }
                Assert.True(failed, "the failure is reported");
                Assert.Equal("old version", File.ReadAllText(target), "the old program is untouched");
                Assert.True(!File.Exists(target + ".new"), "no half copy left behind");

                string source = d.File("new.exe", "new version");
                Util.ReplaceFileSafely(source, target);
                Assert.Equal("new version", File.ReadAllText(target), "replaced");
                Assert.True(!File.Exists(target + ".old") && !File.Exists(target + ".new"), "nothing left behind");
            }
        }

        [Test]
        static void ReplacesTheExeAfterTheOldOneExits()
        {
            using (TempDir d = new TempDir())
            {
                string source = d.File("new.exe", "new version");
                string target = d.File("orclfx.exe", "old version");
                Process p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false });
                int pid = p.Id;
                p.WaitForExit();
                Updater.WaitAndReplace(pid, source, target);
                Assert.Equal("new version", File.ReadAllText(target), "replaced");
            }
        }
    }
}
