// Orcl File Explorer tests: a minimal runner, so the tests build with the same Windows C# compiler as the app
// and need no packages. Every static method marked [Test] runs; the exit code is the number of failures.
// Usage: orclfx.tests.exe [part of a test name to run only matching tests]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    class TestAttribute : Attribute { }

    class TestFailure : Exception { public TestFailure(string m) : base(m) { } }

    static class Assert
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new TestFailure(what);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(what + ": expected <" + expected + ">, got <" + actual + ">");
        }

        public static void Near(double expected, double actual, string what)
        {
            if (Math.Abs(expected - actual) > 0.01) throw new TestFailure(what + ": expected " + expected + ", got " + actual);
        }

        public static void Sequence<T>(IList<T> expected, IList<T> actual, string what)
        {
            bool same = expected.Count == actual.Count;
            for (int i = 0; same && i < expected.Count; i++) same = EqualityComparer<T>.Default.Equals(expected[i], actual[i]);
            if (!same) throw new TestFailure(what + ": expected [" + Join(expected) + "], got [" + Join(actual) + "]");
        }

        static string Join<T>(IList<T> items)
        {
            List<string> s = new List<string>();
            foreach (T x in items) s.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return string.Join(", ", s.ToArray());
        }
    }

    // A fresh folder under %TEMP% per test, deleted afterwards.
    class TempDir : IDisposable
    {
        public readonly string Path;

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orclfx-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Path);
        }

        public string File(string relative, string text)
        {
            string p = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
            System.IO.File.WriteAllText(p, text);
            return p;
        }

        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    static class TestRunner
    {
        static int Main(string[] args)
        {
            // Number formats in the app follow the user's locale; tests pin them so results are predictable.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            string filter = args.Length > 0 ? args[0] : null;
            int passed = 0, failed = 0;
            Stopwatch all = Stopwatch.StartNew();
            foreach (Type t in typeof(TestRunner).Assembly.GetTypes())
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.GetCustomAttributes(typeof(TestAttribute), false).Length == 0) continue;
                    string name = t.Name + "." + m.Name;
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        m.Invoke(null, null);
                        passed++;
                        Console.WriteLine("  pass  " + name);
                    }
                    catch (TargetInvocationException e)
                    {
                        failed++;
                        Exception inner = e.InnerException;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  FAIL  " + name);
                        Console.ResetColor();
                        Console.WriteLine("        " + (inner is TestFailure ? inner.Message : inner.ToString()).Replace("\n", "\n        "));
                    }
                }
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed (" + all.ElapsedMilliseconds + " ms)");
            return failed;
        }
    }
}
