using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// FFT providers and their selection ("compute": {"fft": "auto" | "managed" | "fftw" | "accelerate"}, or the
    /// ULTRAFAST_FFT environment variable). "auto" takes Apple's Accelerate (vDSP) on macOS, FFTW when its library is
    /// found (Windows / Linux; Intel MKL's FFTW interface through ULTRAFAST_FFTW_LIBRARY=mkl_rt also works), else the
    /// managed Stockham FFT. A native provider is used only after it reproduced the managed transform on test vectors
    /// (relative error below 1e-12); otherwise the selection falls back and says why.
    /// </summary>
    public static class FftProviders
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, IFft> Cache = new Dictionary<string, IFft>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Names accepted by <see cref="Select"/>.</summary>
        public static readonly string[] Names = { "auto", "managed", "fftw", "accelerate" };

        /// <summary>The provider for a name (null if unavailable; <paramref name="why"/> says why). "auto" never fails.</summary>
        public static IFft Get(string name, out string why)
        {
            why = null;
            name = string.IsNullOrEmpty(name) ? "auto" : name.ToLowerInvariant();
            if (name == "auto")
            {
                string w1 = null;
                if (IsMac) { var a = Get("accelerate", out w1); if (a != null) return a; }
                var f = Get("fftw", out var w2);
                if (f != null) return f;
                why = string.Join("; ", new[] { w1, w2 }).Trim(' ', ';');
                return Managed;
            }
            if (name == "managed" || name == "managed-radix2" || name == "stockham") return Managed;
            if (Array.IndexOf(Names, name) < 0) { why = "unknown FFT provider \"" + name + "\" (known: " + string.Join(", ", Names) + ")"; return null; }
            lock (Lock)
            {
                if (Cache.TryGetValue(name, out var c)) return c;
                if (Failures.TryGetValue(name, out why)) return null;
                IFft p = null;
                try
                {
#if NET
                    if (name == "fftw") p = FftwFft.TryCreate(out why);
                    else if (name == "accelerate") p = IsMac ? AccelerateFft.TryCreate(out why) : null;
                    else why = "unknown FFT provider \"" + name + "\" (known: " + string.Join(", ", Names) + ")";
                    if (p == null && why == null) why = name + " is only available on macOS";
#else
                    why = "native FFT providers need .NET 10 (this is .NET Framework)";
#endif
                    if (p != null && !SelfCheck(p, out var bad)) { why = name + " failed the self-check (" + bad + ")"; p = null; }
                }
                catch (Exception ex) { why = name + ": " + ex.Message; p = null; }
                if (p != null) Cache[name] = p; else Failures[name] = why;
                return p;
            }
        }

        /// <summary>Selects the process-wide provider (<see cref="Fft.Provider"/>); returns a note for the run.</summary>
        public static string Select(string name)
        {
            var p = Get(name, out var why);
            if (p == null) { Fft.Provider = Managed; return "FFT provider \"" + name + "\" not available (" + why + "); using " + Managed.Name; }
            Fft.Provider = p;
            return null;
        }

        public static readonly IFft Managed = new ManagedRadix2Fft();

        private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>Compares a provider with the managed FFT on random vectors (both signs, power-of-two and other lengths).</summary>
        public static bool SelfCheck(IFft p, out string problem)
        {
            problem = null;
            var rnd = new Random(12345);
            foreach (int n in new[] { 8, 256, 4096, 1536 })
                foreach (int sign in new[] { -1, 1 })
                {
                    var a = new Complex[n];
                    for (int i = 0; i < n; i++) a[i] = new Complex(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                    var b = (Complex[])a.Clone();
                    p.TransformInPlace(a, sign, sign > 0);
                    ComplexFft.TransformInPlace(b, sign, sign > 0);
                    double e = 0, m = 0;
                    for (int i = 0; i < n; i++) { e = Math.Max(e, (a[i] - b[i]).Magnitude); m = Math.Max(m, b[i].Magnitude); }
                    if (!(e <= 1e-12 * Math.Max(m, 1e-300))) { problem = "N = " + n + ", sign " + sign + ": relative error " + (e / m).ToString("G2"); return false; }
                }
            return true;
        }
    }

#if NET
    /// <summary>FFTW 3 (double precision) through its C API, loaded at run time; lengths of any size. Plans are made once
    /// per (length, sign) with FFTW_ESTIMATE | FFTW_UNALIGNED (planning is serialised: FFTW's planner is not thread-safe,
    /// executing a plan is).</summary>
    public sealed unsafe class FftwFft : IFft
    {
        private readonly IntPtr _lib;
        private readonly delegate* unmanaged[Cdecl]<int, double*, double*, int, uint, IntPtr> _plan;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void> _exec;
        private readonly Dictionary<long, IntPtr> _plans = new Dictionary<long, IntPtr>();
        private readonly object _planLock = new object();
        private readonly string _path;
        private const uint FFTW_ESTIMATE = 1U << 6, FFTW_UNALIGNED = 1U << 1;

        public string Name => "fftw (" + _path + ")";

        private FftwFft(IntPtr lib, string path)
        {
            _lib = lib; _path = path;
            _plan = (delegate* unmanaged[Cdecl]<int, double*, double*, int, uint, IntPtr>)NativeLibrary.GetExport(lib, "fftw_plan_dft_1d");
            _exec = (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void>)NativeLibrary.GetExport(lib, "fftw_execute_dft");
        }

        public static FftwFft TryCreate(out string why)
        {
            why = null;
            var env = Environment.GetEnvironmentVariable("ULTRAFAST_FFTW_LIBRARY");
            var names = new List<string>();
            if (!string.IsNullOrEmpty(env)) names.Add(env);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) names.AddRange(new[] { "libfftw3-3.dll", "fftw3.dll", "libfftw3.dll" });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) names.AddRange(new[] { "libfftw3.3.dylib", "libfftw3.dylib", "/opt/homebrew/lib/libfftw3.dylib", "/usr/local/lib/libfftw3.dylib", "/opt/local/lib/libfftw3.dylib" });
            else names.AddRange(new[] { "libfftw3.so.3", "libfftw3.so" });
            foreach (var n in names)
                if (NativeLibrary.TryLoad(n, out var h))
                {
                    if (NativeLibrary.TryGetExport(h, "fftw_plan_dft_1d", out _) && NativeLibrary.TryGetExport(h, "fftw_execute_dft", out _)) return new FftwFft(h, n);
                    why = n + " has no fftw_plan_dft_1d";
                }
            why = why ?? "FFTW library not found (install fftw3 -- e.g. apt install libfftw3-3, brew install fftw -- or set ULTRAFAST_FFTW_LIBRARY)";
            return null;
        }

        private IntPtr Plan(int n, int sign)
        {
            long key = ((long)n << 1) | (sign > 0 ? 1L : 0L);
            lock (_planLock)
            {
                if (_plans.TryGetValue(key, out var p)) return p;
                var buf = new double[2 * Math.Max(n, 1)];
                fixed (double* b = buf) p = _plan(n, b, b, sign > 0 ? 1 : -1, FFTW_ESTIMATE | FFTW_UNALIGNED);
                if (p == IntPtr.Zero) throw new InvalidOperationException("fftw_plan_dft_1d failed for N = " + n);
                _plans[key] = p;
                return p;
            }
        }

        public void TransformInPlace(Complex[] data, int exponentSign, bool normalize)
        {
            int n = data.Length;
            if (n <= 1) return;
            var plan = Plan(n, exponentSign);
            fixed (Complex* c = data) { var d = (double*)c; _exec(plan, d, d); }   // System.Numerics.Complex = two doubles, as fftw_complex
            if (normalize) { double inv = 1.0 / n; for (int i = 0; i < n; i++) data[i] = new Complex(data[i].Real * inv, data[i].Imaginary * inv); }
        }
    }

    /// <summary>Apple Accelerate (vDSP) double-precision DFT, for lengths f x 2^k (f = 1, 3, 5, 15; at least 8 points);
    /// other lengths go to the managed FFT. vDSP works on split real / imaginary arrays.</summary>
    public sealed unsafe class AccelerateFft : IFft
    {
        private readonly delegate* unmanaged[Cdecl]<IntPtr, nuint, int, IntPtr> _create;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, double*, double*, double*, double*, void> _exec;
        private readonly Dictionary<long, IntPtr> _setups = new Dictionary<long, IntPtr>();
        private readonly object _lock = new object();
        [ThreadStatic] private static double[] _re, _im, _ore, _oim;
        private const string Path = "/System/Library/Frameworks/Accelerate.framework/Accelerate";

        public string Name => "accelerate (vDSP)";

        private AccelerateFft(IntPtr lib)
        {
            _create = (delegate* unmanaged[Cdecl]<IntPtr, nuint, int, IntPtr>)NativeLibrary.GetExport(lib, "vDSP_DFT_zop_CreateSetupD");
            _exec = (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, double*, double*, void>)NativeLibrary.GetExport(lib, "vDSP_DFT_ExecuteD");
        }

        public static AccelerateFft TryCreate(out string why)
        {
            why = null;
            if (!NativeLibrary.TryLoad(Path, out var h)) { why = "Accelerate framework not found"; return null; }
            if (!NativeLibrary.TryGetExport(h, "vDSP_DFT_zop_CreateSetupD", out _) || !NativeLibrary.TryGetExport(h, "vDSP_DFT_ExecuteD", out _)) { why = "vDSP_DFT_zop_CreateSetupD not exported"; return null; }
            return new AccelerateFft(h);
        }

        private static bool Supported(int n)
        {
            if (n < 8) return false;
            foreach (int f in new[] { 1, 3, 5, 15 })
            {
                if (n % f != 0) continue;
                int m = n / f;
                if ((m & (m - 1)) == 0 && m >= 8) return true;
            }
            return false;
        }

        private IntPtr Setup(int n, int exponentSign)
        {
            long key = ((long)n << 1) | (exponentSign > 0 ? 1L : 0L);
            lock (_lock)
            {
                if (_setups.TryGetValue(key, out var s)) return s;
                // vDSP_DFT_FORWARD (+1) computes sum x exp(-i 2 pi k n / N); vDSP_DFT_INVERSE (-1) the + exponent (unscaled)
                s = _create(IntPtr.Zero, (nuint)n, exponentSign < 0 ? 1 : -1);
                if (s == IntPtr.Zero) throw new InvalidOperationException("vDSP_DFT_zop_CreateSetupD failed for N = " + n);
                _setups[key] = s;
                return s;
            }
        }

        public void TransformInPlace(Complex[] data, int exponentSign, bool normalize)
        {
            int n = data.Length;
            if (!Supported(n)) { ComplexFft.TransformInPlace(data, exponentSign, normalize); return; }
            var setup = Setup(n, exponentSign);
            if (_re == null || _re.Length < n) { _re = new double[n]; _im = new double[n]; _ore = new double[n]; _oim = new double[n]; }
            var re = _re; var im = _im; var ore = _ore; var oim = _oim;
            for (int i = 0; i < n; i++) { re[i] = data[i].Real; im[i] = data[i].Imaginary; }
            fixed (double* a = re, b = im, c = ore, d = oim) _exec(setup, a, b, c, d);
            double s = normalize ? 1.0 / n : 1.0;
            for (int i = 0; i < n; i++) data[i] = new Complex(ore[i] * s, oim[i] * s);
        }
    }
#endif
}
