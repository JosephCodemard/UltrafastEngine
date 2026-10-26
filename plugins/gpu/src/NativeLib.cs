using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace UltrafastEngine.Plugins.Gpu
{
    /// <summary>
    /// Loads a vendor library (libcuda, libOpenCL, ...) at run time and resolves its exports, on .NET 10
    /// (NativeLibrary) and on .NET Framework 4.8 / Mono (LoadLibrary on Windows, dlopen elsewhere). Nothing is linked
    /// at build time, so the plugin loads on machines without the library and reports what is missing.
    /// </summary>
    public sealed class NativeLib
    {
        public IntPtr Handle { get; }
        public string Path { get; }

        private NativeLib(IntPtr h, string path) { Handle = h; Path = path; }

        /// <summary>The first of <paramref name="candidates"/> that loads (names or absolute paths), or null;
        /// <paramref name="tried"/> lists what was attempted.</summary>
        public static NativeLib TryLoad(IEnumerable<string> candidates, out string tried)
        {
            var t = new List<string>();
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                t.Add(c);
                var h = Load(c);
                if (h != IntPtr.Zero) { tried = string.Join(", ", t); return new NativeLib(h, c); }
            }
            tried = string.Join(", ", t);
            return null;
        }

        /// <summary>The address of an export, or IntPtr.Zero.</summary>
        public IntPtr Find(string name)
        {
#if NET
            return NativeLibrary.TryGetExport(Handle, name, out var p) ? p : IntPtr.Zero;
#else
            return IsWindows ? GetProcAddress(Handle, name) : DlSym(Handle, name);
#endif
        }

        /// <summary>The address of a required export (throws when missing).</summary>
        public IntPtr Get(string name)
        {
            var p = Find(name);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name + " not found in " + Path);
            return p;
        }

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private static IntPtr Load(string name)
        {
            try
            {
#if NET
                return NativeLibrary.TryLoad(name, out var h) ? h : IntPtr.Zero;
#else
                return IsWindows ? LoadLibraryW(name) : DlOpen(name);
#endif
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>Directories named by environment variables (e.g. CUDA_PATH) with sub-folders, for library search.</summary>
        public static IEnumerable<string> EnvDirs(string[] vars, string[] subdirs)
        {
            foreach (var v in vars)
            {
                var d = Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrEmpty(d)) continue;
                foreach (var s in subdirs)
                {
                    var p = s.Length == 0 ? d : System.IO.Path.Combine(d, s);
                    if (Directory.Exists(p)) yield return p;
                }
            }
        }

        /// <summary>Files in <paramref name="dirs"/> matching a pattern (newest version first by name).</summary>
        public static IEnumerable<string> Glob(IEnumerable<string> dirs, string pattern)
        {
            foreach (var d in dirs)
            {
                string[] files;
                try { files = Directory.Exists(d) ? Directory.GetFiles(d, pattern) : new string[0]; } catch { continue; }
                Array.Sort(files, StringComparer.Ordinal); Array.Reverse(files);
                foreach (var f in files) yield return f;
            }
        }

#if !NET
        [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("libdl.so.2", EntryPoint = "dlopen")] private static extern IntPtr DlOpen2(string name, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym")] private static extern IntPtr DlSym2(IntPtr h, string name);
        [DllImport("libdl", EntryPoint = "dlopen")] private static extern IntPtr DlOpenX(string name, int flags);
        [DllImport("libdl", EntryPoint = "dlsym")] private static extern IntPtr DlSymX(IntPtr h, string name);
        private static IntPtr DlOpen(string n) { try { return DlOpen2(n, 2); } catch (DllNotFoundException) { return DlOpenX(n, 2); } }
        private static IntPtr DlSym(IntPtr h, string n) { try { return DlSym2(h, n); } catch (DllNotFoundException) { return DlSymX(h, n); } }
#endif
    }
}
