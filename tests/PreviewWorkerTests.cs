// Tests for the preview worker thread: results carry the right request, only the newest request counts,
// and it shuts down promptly.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    static class PreviewWorkerTests
    {
        static PreviewWorker.Request Req(int ticket, string path)
        {
            PreviewWorker.Request r = new PreviewWorker.Request();
            r.Ticket = ticket;
            r.Path = path;
            r.ThumbSize.cx = r.ThumbSize.cy = 64;
            return r;
        }

        [Test]
        static void ThumbnailForAFolder()
        {
            using (TempDir d = new TempDir())
            {
                List<PreviewWorker.Result> got = new List<PreviewWorker.Result>();
                PreviewWorker w = new PreviewWorker(delegate(PreviewWorker.Result r) { lock (got) got.Add(r); });
                try
                {
                    w.Submit(Req(7, d.Path));
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < 15) { lock (got) if (got.Count > 0) break; Thread.Sleep(20); }
                    Assert.Equal(1, got.Count, "results");
                    Assert.Equal(7, got[0].Ticket, "ticket");
                    Assert.True(got[0].From == w, "result names its worker");
                    Assert.True(!got[0].Handler, "a folder gets a thumbnail, not a preview handler");
                    Assert.True(got[0].Thumbnail != null, "thumbnail made");
                    got[0].Thumbnail.Dispose();
                }
                finally { w.Quit(); }
                Assert.True(w.WaitForExit(3000), "the thread exits after Quit");
            }
        }

        [Test]
        static void OnlyTheNewestWaitingRequestRuns()
        {
            using (TempDir d = new TempDir())
            {
                List<int> tickets = new List<int>();
                PreviewWorker w = new PreviewWorker(delegate(PreviewWorker.Result r)
                {
                    lock (tickets) tickets.Add(r.Ticket);
                    if (r.Thumbnail != null) r.Thumbnail.Dispose();
                });
                try
                {
                    for (int i = 1; i <= 50; i++) w.Submit(Req(i, d.Path));
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < 15) { lock (tickets) if (tickets.Contains(50)) break; Thread.Sleep(20); }
                    Thread.Sleep(200);
                    Assert.True(tickets.Contains(50), "the newest request was answered");
                    Assert.True(tickets.Count < 50, "older waiting requests were dropped (answered " + tickets.Count + ")");
                }
                finally { w.Quit(); }
            }
        }

        [Test]
        static void PreviewHandlerForATextFile()
        {
            // Windows registers a preview handler for .txt; it runs out of process into a (hidden) window here.
            using (TempDir d = new TempDir())
            using (System.Windows.Forms.Form host = new System.Windows.Forms.Form())
            {
                string file = d.File("note.txt", "Hello from the preview test");
                List<PreviewWorker.Result> got = new List<PreviewWorker.Result>();
                PreviewWorker w = new PreviewWorker(delegate(PreviewWorker.Result r) { lock (got) got.Add(r); });
                PreviewWorker.Request q = Req(3, file);
                q.Host = host.Handle;
                q.Rect.right = q.Rect.bottom = 300;
                w.Submit(q);
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 20) { lock (got) if (got.Count > 0) break; Thread.Sleep(20); }
                Assert.Equal(1, got.Count, "results");
                if (got[0].Handler) Console.WriteLine("        (shown by the .txt preview handler)");
                else Console.WriteLine("        (no out-of-process .txt handler here; got " + (got[0].Thumbnail != null ? "a thumbnail" : "nothing") + ")");
                if (got[0].Thumbnail != null) got[0].Thumbnail.Dispose();
                w.Submit(Req(4, ""));   // unloads the handler
                w.Quit();
                Assert.True(w.WaitForExit(5000), "the handler unloads and the thread exits");
            }
        }

        [Test]
        static void EmptyRequestGivesNoResult()
        {
            int results = 0;
            PreviewWorker w = new PreviewWorker(delegate(PreviewWorker.Result r) { Interlocked.Increment(ref results); });
            w.Submit(Req(1, ""));
            Thread.Sleep(300);
            w.Quit();
            Assert.True(w.WaitForExit(3000), "the thread exits");
            Assert.Equal(0, results, "results");
        }
    }
}
