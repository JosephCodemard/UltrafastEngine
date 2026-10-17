using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace UltrafastEngine.Tests
{
    /// <summary>Marks a public static or instance method with no parameters as a test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
        public string Category { get; set; } = "unit";
        public bool Slow { get; set; }
    }

    public sealed class AssertionException : Exception { public AssertionException(string m) : base(m) { } }
    public sealed class SkipException : Exception { public SkipException(string m) : base(m) { } }

    public static class Assert
    {
        public static void True(bool c, string msg = "condition was false") { if (!c) throw new AssertionException(msg); }
        public static void False(bool c, string msg = "condition was true") { if (c) throw new AssertionException(msg); }
        public static void Equal<T>(T expected, T actual, string what = "value")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new AssertionException(what + ": expected " + expected + ", got " + actual);
        }
        /// <summary>|actual - expected| &lt;= absTol + relTol*|expected|</summary>
        public static void Close(double expected, double actual, double relTol, double absTol = 0, string what = "value")
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > absTol + relTol * Math.Abs(expected))
                throw new AssertionException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}: expected {1:G6}, got {2:G6} (rel err {3:G3}, tol {4:G3}{5})", what, expected, actual,
                    expected != 0 ? Math.Abs(actual - expected) / Math.Abs(expected) : double.NaN, relTol, absTol > 0 ? " + abs " + absTol.ToString("G3") : ""));
        }
        public static void Less(double a, double b, string what = "value") { if (!(a < b)) throw new AssertionException(what + ": expected " + a.ToString("G6") + " < " + b.ToString("G6")); }
        public static void Greater(double a, double b, string what = "value") { if (!(a > b)) throw new AssertionException(what + ": expected " + a.ToString("G6") + " > " + b.ToString("G6")); }
        public static void Throws<TEx>(Action a, string what = "action") where TEx : Exception
        {
            try { a(); } catch (TEx) { return; } catch (Exception ex) { throw new AssertionException(what + ": expected " + typeof(TEx).Name + ", got " + ex.GetType().Name + ": " + ex.Message); }
            throw new AssertionException(what + ": expected " + typeof(TEx).Name + ", nothing thrown");
        }
        public static void Skip(string why) => throw new SkipException(why);
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            // the first-party plugins are referenced by this runner: register them in code (as a NativeAOT host would)
            UltrafastEngine.Plugins.PluginManager.Register(new UltrafastEngine.Plugins.Fitting.FittingPlugin());
            UltrafastEngine.Plugins.PluginManager.Register(new UltrafastEngine.Zemax.ZemaxPlugin());
            var filters = args.Where(a => !a.StartsWith("--")).ToArray();
            bool includeSlow = args.Contains("--slow") || filters.Length > 0;
            bool verbose = args.Contains("--verbose");
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Select(m => (m, a: m.GetCustomAttribute<TestAttribute>()))
                .Where(x => x.a != null)
                .Where(x => filters.Length == 0 || filters.Any(f => (x.m.DeclaringType.Name + "." + x.m.Name).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(x => x.m.DeclaringType.Name).ThenBy(x => x.m.Name)
                .ToList();
            int pass = 0, fail = 0, skip = 0;
            var failures = new List<string>();
            Console.WriteLine("UltrafastEngine tests (" + (Environment.Version) + ", " + tests.Count + " selected)");
            foreach (var (m, a) in tests)
            {
                string name = m.DeclaringType.Name + "." + m.Name;
                if (a.Slow && !includeSlow) { skip++; Console.WriteLine("  SKIP  " + name + " (slow; run with --slow)"); continue; }
                var sw = Stopwatch.StartNew();
                try
                {
                    object inst = m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType);
                    m.Invoke(inst, null);
                    pass++;
                    Console.WriteLine(string.Format("  PASS  {0} ({1:F2} s)", name, sw.Elapsed.TotalSeconds));
                }
                catch (TargetInvocationException tie) when (tie.InnerException is SkipException se)
                {
                    skip++; Console.WriteLine("  SKIP  " + name + ": " + se.Message);
                }
                catch (TargetInvocationException tie)
                {
                    fail++;
                    var ex = tie.InnerException ?? tie;
                    string msg = ex is AssertionException ? ex.Message : ex.GetType().Name + ": " + ex.Message + (verbose ? "\n" + ex.StackTrace : "");
                    failures.Add(name + ": " + msg);
                    Console.WriteLine("  FAIL  " + name + ": " + msg);
                }
            }
            Console.WriteLine();
            Console.WriteLine(string.Format("{0} passed, {1} failed, {2} skipped", pass, fail, skip));
            foreach (var f in failures) Console.WriteLine("  - " + f);
            return fail == 0 ? 0 : 1;
        }
    }
}
