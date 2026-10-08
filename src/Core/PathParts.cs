// OrclFX: splitting a folder path into the parts of the address bar. No UI; covered by tests\.
using System;
using System.Collections.Generic;

namespace OrclFileExplorer
{
    static class PathParts
    {
        // (label, folder) for each level: "C:\Users\Me" gives C:, Users, Me; "\\server\share\x" gives server,
        // share, x. Empty for locations that aren't folders on a disk or share (This PC, Find results ...).
        public static List<KeyValuePair<string, string>> Split(string path)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\?\") || path.StartsWith("::")) return r;
            string rest;
            string acc;
            if (path.StartsWith(@"\\"))
            {
                string[] unc = path.Substring(2).Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (unc.Length == 0) return r;
                acc = @"\\" + unc[0];
                r.Add(new KeyValuePair<string, string>(unc[0], acc));
                for (int i = 1; i < unc.Length; i++)
                {
                    acc += @"\" + unc[i];
                    r.Add(new KeyValuePair<string, string>(unc[i], acc));
                }
                return r;
            }
            if (path.Length < 2 || path[1] != ':' || !char.IsLetter(path[0])) return r;
            string drive = path.Substring(0, 2).ToUpperInvariant();
            r.Add(new KeyValuePair<string, string>(drive, drive + @"\"));
            rest = path.Substring(2);
            acc = drive;
            foreach (string s in rest.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                acc += @"\" + s;
                r.Add(new KeyValuePair<string, string>(s, acc));
            }
            return r;
        }
    }
}
