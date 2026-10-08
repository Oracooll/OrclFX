// OrclFX: finding, downloading, checking and applying updates from the GitHub releases.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

namespace OrclFileExplorer
{
    // How an update works:
    // 1. FetchLatest asks GitHub for the newest release of this repository.
    // 2. Download saves its orclfx.exe to %TEMP%\orclfx-update and checks it: the size and SHA-256 that GitHub
    //    reports, and that it is OrclFX with the release's version number.
    // 3. The running app saves its settings, starts the download with --update <its process id> <its exe> and
    //    closes. The new exe waits for it to exit, copies itself over the old exe (for the installed copy:
    //    reinstalls, which also updates the Start menu and Settings > Apps entries) and starts it again.
    static class Updater
    {
        public const string Repo = "Oracooll/OrclFX";
        public static readonly string ReleasesPage = "https://github.com/" + Repo + "/releases";
        // DUALPANE_UPDATE_URL points the check somewhere else (for tests).
        static readonly string ApiUrl = Environment.GetEnvironmentVariable("DUALPANE_UPDATE_URL") ??
            "https://api.github.com/repos/" + Repo + "/releases/latest";
        public static readonly string DownloadFolder = Path.Combine(Path.GetTempPath(), "orclfx-update");

        public class Release
        {
            public string Tag, ExeUrl, Sha256, Notes;
            public Version Version;
            public long ExeSize;
        }

        // "v1.1.009" -> 1.1.9.0 (the assembly version); null if the tag isn't a version.
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string[] p = tag.TrimStart('v', 'V').Split('.');
            int major, minor, build;
            if (p.Length != 3 || !int.TryParse(p[0], out major) || !int.TryParse(p[1], out minor) || !int.TryParse(p[2], out build)) return null;
            if (major < 0 || minor < 0 || build < 0) return null;
            return new Version(major, minor, build, 0);
        }

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        public static bool IsNewer(Release r) { return r != null && r.Version != null && r.Version > Current; }

        // Reads GitHub's "latest release" JSON. Null when it has no orclfx.exe or no version tag.
        public static Release ParseRelease(string json)
        {
            Dictionary<string, object> d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || d.ContainsKey("draft") && true.Equals(d["draft"]) || d.ContainsKey("prerelease") && true.Equals(d["prerelease"])) return null;
            Release r = new Release();
            r.Tag = d.ContainsKey("tag_name") ? d["tag_name"] as string : null;
            r.Version = ParseTag(r.Tag);
            r.Notes = d.ContainsKey("body") ? d["body"] as string : null;
            IEnumerable assets = d.ContainsKey("assets") ? d["assets"] as IEnumerable : null;
            if (assets != null)
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(a.ContainsKey("name") ? a["name"] as string : null, "orclfx.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    r.ExeUrl = a.ContainsKey("browser_download_url") ? a["browser_download_url"] as string : null;
                    if (a.ContainsKey("size") && a["size"] != null) r.ExeSize = Convert.ToInt64(a["size"]);
                    string digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) r.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            if (r.Version == null || string.IsNullOrEmpty(r.ExeUrl) || !r.ExeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                Environment.GetEnvironmentVariable("DUALPANE_UPDATE_URL") == null) return null;
            return r;
        }

        static HttpWebRequest Request(string url)
        {
            // .NET Framework 4.x may default to TLS 1.0, which GitHub refuses.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            HttpWebRequest q = (HttpWebRequest)WebRequest.Create(url);
            q.UserAgent = "OrclFileExplorer/" + Current;
            q.Timeout = 20000;
            q.ReadWriteTimeout = 30000;
            return q;
        }

        public static Release FetchLatest()
        {
            HttpWebRequest q = Request(ApiUrl);
            q.Accept = "application/vnd.github+json";
            using (WebResponse resp = q.GetResponse())
            using (StreamReader rd = new StreamReader(resp.GetResponseStream()))
                return ParseRelease(rd.ReadToEnd());
        }

        // Downloads and checks the release's exe; returns its path. Throws with a readable message on any problem.
        public static string Download(Release r)
        {
            Directory.CreateDirectory(DownloadFolder);
            string file = Path.Combine(DownloadFolder, "orclfx-" + r.Tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".exe");
            try
            {
                using (WebResponse resp = Request(r.ExeUrl).GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream f = File.Create(file))
                    s.CopyTo(f);
                Verify(file, r);
                return file;
            }
            catch
            {
                try { File.Delete(file); } catch { }
                throw;
            }
        }

        // The downloaded file must be exactly what GitHub lists, and really be this app in the release's version.
        public static void Verify(string file, Release r)
        {
            long size = new FileInfo(file).Length;
            if (r.ExeSize > 0 && size != r.ExeSize) throw new IOException("the download is incomplete (" + size + " of " + r.ExeSize + " bytes)");
            if (r.Sha256 != null)
            {
                string hash;
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream f = File.OpenRead(file))
                    hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (hash != r.Sha256) throw new IOException("the download doesn't match the release's checksum");
            }
            AssemblyName name;
            try { name = AssemblyName.GetAssemblyName(file); }
            catch { throw new IOException("the download isn't a valid OrclFX program"); }
            string product = FileVersionInfo.GetVersionInfo(file).ProductName;
            if (product != Program.FormerName && product != Program.AppName)
                throw new IOException("the download isn't OrclFX");
            if (name.Version != r.Version)
                throw new IOException("the download is version " + Util.FormatVersion(name.Version) + ", not " + Util.FormatVersion(r.Version));
        }

        // Run by the new exe: waits up to 30 s for the old app to exit, then copies source over target, retrying
        // for up to 10 s while Windows still holds the old file.
        public static void WaitAndReplace(int pid, string source, string target)
        {
            try
            {
                using (Process old = Process.GetProcessById(pid))
                    if (!old.WaitForExit(30000)) throw new IOException("the old version didn't close");
            }
            catch (ArgumentException) { } // already gone
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                try { Util.ReplaceFileSafely(source, target); return; }
                catch (IOException) { if (sw.Elapsed.TotalSeconds > 10) throw; }
                catch (UnauthorizedAccessException) { if (sw.Elapsed.TotalSeconds > 10) throw; }
                Thread.Sleep(250);
            }
        }

        // Removes downloads left from earlier updates (the update exe can't delete itself).
        public static void CleanDownloads()
        {
            Util.DeleteOld(System.Windows.Forms.Application.ExecutablePath); // the previous version, kept during the swap
            try
            {
                if (!Directory.Exists(DownloadFolder)) return;
                foreach (string f in Directory.GetFiles(DownloadFolder, "*.exe"))
                    try { if (!string.Equals(f, System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
