// MemoryEstimate.cs -- how much managed memory a run will hold, worked out from the compiled run before it starts.
//
// A run keeps the pulse after every element (res.At: Ax and Ay, N complex samples each) and, with outputs.keepProfiles = "all", the
// transverse profile of every spatial element (M points x spectralSamples frequencies, doubled for vector fields). The estimate adds the
// solver's working arrays (1-D: a few dozen N-sample arrays; spatial: M x frequency-block arrays) and the delayed-light tail grid.
// It is a planning figure (within a factor of about 2 of the managed peak in the tests), not an exact count; see docs/reference/memory.md.
//
//   class MemoryEstimate
//     static MemoryEstimate Of(CompiledRun cr)            the estimate (bytes by part) of a compiled run
//     long TotalBytes; PulseBytes, ProfileBytes, WorkspaceBytes, TailBytes, InstrumentBytes
//     string Summary(); JsonObject ToJson()
//     string Check(long limitBytes, out bool isError)      the message when the estimate is above the limit / above 80 % of the free memory (null if fine)
//     static long AvailableBytes()                         free physical memory (container-aware on .NET), -1 when unknown
//     static long? AvailableBytesOverride                  tests: a fake machine size
//
// Reference: docs/reference/memory.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Engine
{
    public sealed class MemoryEstimate
    {
        private const int ComplexBytes = 16;
        /// <summary>1-D solver working arrays (state, stages, operator, FFT scratch), in complex arrays of N samples.</summary>
        public const int Workspace1DArrays = 24;
        /// <summary>Spatial solver working arrays of M x (frequency block) complex samples.</summary>
        public const int WorkspaceSpatialArrays = 8;
        /// <summary>The warning fires above this fraction of the free physical memory.</summary>
        public const double WarnFraction = 0.8;

        /// <summary>Tests: pretend the machine has this much free memory (bytes).</summary>
        public static long? AvailableBytesOverride;

        public int GridN, KeptPulses, SpatialElements, KeptProfiles, ProfilePoints, ProfileFrequencies, ProfilePolarisations = 1;
        public string KeepProfiles = "all";
        public long PulseBytes, ProfileBytes, WorkspaceBytes, TailBytes, InstrumentBytes;
        public long TotalBytes => PulseBytes + ProfileBytes + WorkspaceBytes + TailBytes + InstrumentBytes;

        public static MemoryEstimate Of(CompiledRun cr)
        {
            var m = new MemoryEstimate { GridN = cr.Grid.N, KeepProfiles = cr.Run.Outputs?.KeepProfiles ?? "all" };
            long n = cr.Grid.N;
            var on = cr.Elements.Where(e => e.Enabled).ToList();
            // the pulse after every element, and the input; "end" is the last element's pulse
            m.KeptPulses = on.Count + 1;
            m.PulseBytes = m.KeptPulses * 2L * n * ComplexBytes;
            // transverse profiles
            long workspaceSpatial = 0, profileBytesOne = 0;
            foreach (var e in on)
            {
                var tv = SpecOf(cr, e);
                if (tv == null || !tv.IsSpatial) continue;
                m.SpatialElements++;
                long pts = tv.Model == "radial" ? tv.Points : (long)tv.Nx * tv.Ny;
                int pol = tv.Polarization == "vector" ? 2 : 1;
                long one = pts * tv.SpectralSamples * ComplexBytes * pol;
                if (one > profileBytesOne) { profileBytesOne = one; m.ProfilePoints = (int)pts; m.ProfileFrequencies = tv.SpectralSamples; m.ProfilePolarisations = pol; }
                long block = tv.FrequencyBlock > 0 ? Math.Min(tv.FrequencyBlock, tv.SpectralSamples) : tv.SpectralSamples;
                workspaceSpatial = Math.Max(workspaceSpatial, WorkspaceSpatialArrays * pts * block * ComplexBytes * pol);
            }
            if (m.SpatialElements > 0)
            {
                int used = Math.Min(m.SpatialElements, Math.Max(1, cr.Run.Instruments.Count) + 1);
                m.KeptProfiles = (m.KeepProfiles == "used" ? used : m.SpatialElements) + 1;   // + the profile of the pulse in flight
                m.ProfileBytes = m.KeptProfiles * profileBytesOne;                              // bounded by the largest element's profile
            }
            m.WorkspaceBytes = Math.Max(Workspace1DArrays * n * ComplexBytes, workspaceSpatial);
            // delayed-light tail: the ledger and a snapshot (two polarisations) at every probe, instrument location and the end
            if (cr.Run.Grid.TailEnabled && cr.Graph?.IsBranched != true)
            {
                double mainW = n * cr.Grid.DTime;
                double sat = Compiler.SatelliteRoom(on, cr.Run.Grid.SatelliteLevel);
                double wTail = double.IsNaN(cr.Run.Grid.TailWindowS) ? Math.Max(8 * mainW, 2.5 * (2 * sat + mainW)) : Math.Max(cr.Run.Grid.TailWindowS, 2 * mainW);
                long nLong = (long)Math.Ceiling(wTail / mainW) * n;
                if (cr.Run.Grid.MaxN > 0) nLong = Math.Min(nLong, cr.Run.Grid.MaxN);
                int keys = 1 + cr.Run.Instruments.Count + on.Count(e => e.Probe);
                m.TailBytes = (6 + keys) * 2L * nLong * ComplexBytes;
            }
            // instrument read-outs: arrays of the grid's length (spectra, traces); two-dimensional ones (spectrograms) come on top
            m.InstrumentBytes = cr.Run.Instruments.Count * 16L * n * sizeof(double);
            return m;
        }

        private static TransverseSpec SpecOf(CompiledRun cr, CompiledElement e)
        {
            if (cr.Run.Fidelity != null && cr.Run.Fidelity.CollapsedElements.Contains(e.Id)) return null;
            var json = e.Source?.Json;
            JsonValue v = json != null && json.Has("transverse") ? json["transverse"] : cr.Run.Physics.Transverse;
            if (v == null || v.IsNull) return null;
            try { return TransverseSpec.FromJson(v); } catch (JsonException) { return null; }
        }

        public static string Human(long bytes)
        {
            double b = bytes;
            string[] u = { "B", "kB", "MB", "GB", "TB" }; int k = 0;
            while (b >= 1000 && k < u.Length - 1) { b /= 1000; k++; }
            return b.ToString(k == 0 ? "F0" : "G3", CultureInfo.InvariantCulture) + " " + u[k];
        }

        public string Summary()
        {
            var parts = new List<string> { Human(PulseBytes) + " pulses (" + KeptPulses + " kept x 2 x N = " + GridN + ")" };
            if (SpatialElements > 0) parts.Add(Human(ProfileBytes) + " transverse profiles (" + KeptProfiles + " kept, " + ProfilePoints + " points x " + ProfileFrequencies + " frequencies" + (ProfilePolarisations == 2 ? " x 2" : "") + ", outputs.keepProfiles \"" + KeepProfiles + "\")");
            parts.Add(Human(WorkspaceBytes) + " solver workspace");
            if (TailBytes > 0) parts.Add(Human(TailBytes) + " delayed-light tail");
            if (InstrumentBytes > 0) parts.Add(Human(InstrumentBytes) + " instrument read-outs");
            return "Estimated memory of one run: " + Human(TotalBytes) + " (" + string.Join("; ", parts) + "). docs/reference/memory.md.";
        }

        public JsonObject ToJson() => new JsonObject().Set("totalBytes", (double)TotalBytes).Set("pulseBytes", (double)PulseBytes).Set("profileBytes", (double)ProfileBytes)
            .Set("workspaceBytes", (double)WorkspaceBytes).Set("tailBytes", (double)TailBytes).Set("instrumentBytes", (double)InstrumentBytes)
            .Set("gridN", GridN).Set("keptPulses", KeptPulses).Set("spatialElements", SpatialElements).Set("keptProfiles", KeptProfiles)
            .Set("keepProfiles", KeepProfiles).Set("availableBytes", (double)AvailableBytes());

        /// <summary>The message when the estimate is above the limit (<paramref name="isError"/> true) or above 80 % of the free memory (false), else null.</summary>
        public string Check(long limitBytes, out bool isError)
        {
            isError = false;
            if (limitBytes > 0 && TotalBytes > limitBytes)
            {
                isError = true;
                return "the run needs an estimated " + Human(TotalBytes) + " (" + TotalBytes.ToString(CultureInfo.InvariantCulture) + " B), above compute.memoryLimitBytes = " + Human(limitBytes)
                    + ". " + Advice();
            }
            long free = AvailableBytes();
            if (free > 0 && TotalBytes > WarnFraction * free)
                return "the run needs an estimated " + Human(TotalBytes) + ", above " + (int)(WarnFraction * 100) + " % of the free memory (" + Human(free) + "); it may run out of memory. " + Advice();
            return null;
        }

        private string Advice()
        {
            var a = new List<string>();
            if (SpatialElements > 0 && KeepProfiles == "all") a.Add("set outputs.keepProfiles to \"used\" (keeps the transverse profile only where an instrument reads it)");
            if (SpatialElements > 0) a.Add("set transverse.frequencyBlock (streams the frequencies) or lower transverse.spectralSamples / points");
            a.Add("lower grid.maxN or the bandwidth (grid.auto), or use fidelity \"quick\"");
            return "To reduce it: " + string.Join("; ", a) + ".";
        }

        /// <summary>Free physical memory in bytes (what a new allocation can use); -1 when it cannot be found.</summary>
        public static long AvailableBytes()
        {
            if (AvailableBytesOverride.HasValue) return AvailableBytesOverride.Value;
            try
            {
#if NET
                var gc = GC.GetGCMemoryInfo();
                if (gc.TotalAvailableMemoryBytes > 0) return Math.Max(0, gc.TotalAvailableMemoryBytes - gc.MemoryLoadBytes);
                return -1;
#else
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                    return GlobalMemoryStatusEx(ref s) ? (long)s.AvailPhys : -1;
                }
                if (File.Exists("/proc/meminfo"))
                    foreach (var line in File.ReadLines("/proc/meminfo"))
                        if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                            return long.Parse(line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) * 1024;
                return -1;
#endif
            }
            catch (Exception) { return -1; }   // unknown (Mono, sandbox): no warning
        }

#if !NET
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx s);
#endif
    }
}
