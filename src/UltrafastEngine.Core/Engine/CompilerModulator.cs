// CompilerModulator.cs -- compiles the `modulator` element (docs/reference/modulator.md) into a ModulatorElement.
//
//   Compiler.CompileModulator(Ctx, SystemElement, CompiledElement)
//     reads the cell grid (cellsX, cellsY, pitchXM, pitchYM, offsetXM, offsetYM, rotationRad) and the maps phaseRad | opdM | amplitude,
//     each given as a nested array [x][y], as a callback "@name" f(x, y, z) -> values (z = 0; registered like a position-dependent
//     field, so Python passes arrays without files: uf.run(..., fields={"name": f})), or as `file` (NPZ phase_rad / opd_m / amplitude);
//     the chromatic rule (opd | fixedPhase = phaseAtDesign | index with deltaIndexCauchy or a deltaIndex table) and the cross-talk.
//     A callback map is evaluated when the run compiles, so every run (every trial of a loop) reads the callback's current values.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    public static partial class Compiler
    {
        private static void CompileModulator(Ctx c, SystemElement e, CompiledElement ce)
        {
            var m = new Optics.ModulatorElement();
            string origin = "modulator '" + e.Id + "'";
            string path = Str(c, e, "file", null);
            Dictionary<string, NdArray> fileArrays = null;
            if (path != null)
            {
                string dir = c.Sys.BaseDirectory ?? c.Run.BaseDirectory;
                if (dir != null && !System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(dir, path);
                if (!System.IO.File.Exists(path)) throw new JsonException(origin + ": file not found: " + path);
                fileArrays = NpzWriter.Read(path).ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);
            }
            int nx = (int)Num(c, e, "cellsX", 0), ny = (int)Num(c, e, "cellsY", 0);
            // the maps (an array's shape fixes the cell counts when they are not given)
            double[] Map(string key, string fileKey)
            {
                if (fileArrays != null && fileArrays.TryGetValue(fileKey, out var fa))
                {
                    if (fa.Shape.Length == 2 && nx == 0) { nx = fa.Shape[0]; ny = fa.Shape[1]; }
                    return fa.Data;
                }
                if (!e.Json.TryGet(key, out var v)) return null;
                if (v.Kind == JsonKind.String)
                {
                    string name = v.AsString();
                    if (!name.StartsWith("@")) throw new JsonException(origin + ": " + key + " is an array [x][y] or a callback \"@name\".");
                    if (nx < 1 || ny < 1) throw new JsonException(origin + ": a callback map needs cellsX and cellsY.");
                    var cb = c.Reg.GetCallback<FieldCallback>(name.Substring(1), origin);
                    return CallbackMap(cb, nx, ny, Num(c, e, "pitchXM", double.NaN), Num(c, e, "pitchYM", Num(c, e, "pitchXM", double.NaN)));
                }
                var rows = v.AsDoubleMatrix();
                if (nx == 0) { nx = rows.Length; ny = rows.Length > 0 ? rows[0].Length : 0; }
                if (rows.Length != nx || rows.Any(r => r.Length != ny)) throw new JsonException(origin + ": " + key + " must be [cellsX][cellsY] = [" + nx + "][" + ny + "].");
                var flat = new double[nx * ny];
                for (int i = 0; i < nx; i++) Array.Copy(rows[i], 0, flat, i * ny, ny);
                return flat;
            }
            m.PhaseRad = Map("phaseRad", "phase_rad"); m.OpdM = Map("opdM", "opd_m"); m.Amplitude = Map("amplitude", "amplitude");
            if (nx < 1 || ny < 1) throw new JsonException(origin + ": give cellsX and cellsY, or a map (phaseRad, opdM, amplitude or file) whose shape fixes them.");
            m.Nx = nx; m.Ny = ny;
            m.PitchXM = Num(c, e, "pitchXM", double.NaN); m.PitchYM = Num(c, e, "pitchYM", m.PitchXM);
            if (!(m.PitchXM > 0)) throw new JsonException(origin + ": pitchXM (the cell pitch in m) is needed.");
            m.OffsetXM = Num(c, e, "offsetXM", 0); m.OffsetYM = Num(c, e, "offsetYM", 0); m.RotationRad = Num(c, e, "rotationRad", 0);
            m.DesignOmega = e.Json.Has("designWavelengthM") ? 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / Num(c, e, "designWavelengthM", 0) : c.Omega0;
            string chrom = Str(c, e, "chromatic", "opd").ToLowerInvariant();
            if (chrom == "phaseatdesign") chrom = "opd";      // the plan's name (ao-twin-2026-11.md): a phase given at the design wavelength, scaled with omega
            if (chrom != "opd" && chrom != "fixedphase" && chrom != "index") throw new JsonException(origin + ": chromatic must be \"opd\" (also \"phaseAtDesign\"), \"fixedPhase\" or \"index\".");
            m.FixedPhase = chrom == "fixedphase";
            if (m.FixedPhase && m.OpdM != null) throw new JsonException(origin + ": an optical path (opdM) is chromatic; use phaseRad with \"fixedPhase\".");
            if (chrom == "index") m.DeltaIndex = DeltaIndexModel(c, e, origin);
            else if (e.Json.Has("deltaIndexCauchy") || e.Json.Has("deltaIndex") || e.Json.Has("deltaIndexWavelengthsM")) throw new JsonException(origin + ": deltaIndexCauchy / deltaIndex need \"chromatic\": \"index\".");
            if (e.Json.Has("crosstalkKernel")) m.CrosstalkKernel = e.Json.GetDoubleArray("crosstalkKernel");
            m.FillFactor = Num(c, e, "fillFactor", 1); m.GapTransmission = Num(c, e, "gapTransmission", 0); m.OutsideTransmission = Num(c, e, "outsideTransmission", 0);
            m.ZeroOrderLeak = Num(c, e, "zeroOrderLeak", 0); m.CrosstalkSigmaCells = Num(c, e, "crosstalkSigmaCells", 0);
            m.QuantisationLevels = (int)Num(c, e, "quantisationLevels", 0); m.PhaseWrapRad = Num(c, e, "phaseWrapRad", 0);
            if (m.ZeroOrderLeak < 0 || m.ZeroOrderLeak > 1) throw new JsonException(origin + ": zeroOrderLeak is a power fraction in [0, 1].");
            if (m.QuantisationLevels < 0 || m.QuantisationLevels == 1) throw new JsonException(origin + ": quantisationLevels is 0 (off) or at least 2.");
            m.Prepare();
            ce.Active = m;
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "modulator: {0} x {1} cells of {2:G3} x {3:G3} m, {4} phase; acts on the carried transverse profile", nx, ny, m.PitchXM, m.PitchYM, m.FixedPhase ? "fixedPhase" : m.DeltaIndex != null ? "index (dispersive dn, chromatic)" : "optical-path (chromatic)"));
        }

        /// <summary>The index modulation dn(omega) of chromatic "index": Cauchy coefficients [A, B (m^2), C (m^4)], dn = A + B / lambda^2 + C / lambda^4
        /// (the extended Cauchy form used for liquid-crystal birefringence), or a table deltaIndex over deltaIndexWavelengthsM (linear in lambda, held
        /// constant outside).</summary>
        private static Func<double, double> DeltaIndexModel(Ctx c, SystemElement e, string origin)
        {
            double tw = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS;
            if (e.Json.Has("deltaIndexCauchy"))
            {
                var k = e.Json.GetDoubleArray("deltaIndexCauchy");
                if (k.Length < 1 || k.Length > 3) throw new JsonException(origin + ": deltaIndexCauchy is [A], [A, B_m2] or [A, B_m2, C_m4].");
                double A = k[0], B = k.Length > 1 ? k[1] : 0, C = k.Length > 2 ? k[2] : 0;
                return w => { double l2 = Math.Pow(tw / w, 2); return A + B / l2 + C / (l2 * l2); };
            }
            if (e.Json.Has("deltaIndex"))
            {
                var dn = e.Json.GetDoubleArray("deltaIndex"); var lam = e.Json.Has("deltaIndexWavelengthsM") ? e.Json.GetDoubleArray("deltaIndexWavelengthsM") : null;
                if (lam == null || lam.Length != dn.Length || lam.Length < 2) throw new JsonException(origin + ": deltaIndex needs deltaIndexWavelengthsM of the same length (at least 2 points).");
                if (lam.Any(l => !(l > 0))) throw new JsonException(origin + ": deltaIndexWavelengthsM must be positive.");
                var order = Enumerable.Range(0, lam.Length).OrderBy(i => lam[i]).ToArray(); var L = order.Select(i => lam[i]).ToArray(); var D = order.Select(i => dn[i]).ToArray();
                return w =>
                {
                    double l = tw / w; if (l <= L[0]) return D[0]; if (l >= L[L.Length - 1]) return D[D.Length - 1];
                    int j = 1; while (L[j] < l) j++;
                    double t = (l - L[j - 1]) / (L[j] - L[j - 1]); return D[j - 1] + t * (D[j] - D[j - 1]);
                };
            }
            throw new JsonException(origin + ": chromatic \"index\" needs deltaIndexCauchy or deltaIndex (with deltaIndexWavelengthsM).");
        }

        /// <summary>Evaluate a position callback at the cell centres of a device (before offset and rotation), z = 0.</summary>
        private static double[] CallbackMap(FieldCallback cb, int nx, int ny, double px, double py)
        {
            int n = nx * ny; var x = new double[n]; var y = new double[n]; var z = new double[n]; var v = new double[n];
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) { x[i * ny + j] = (i - (nx - 1) / 2.0) * px; y[i * ny + j] = (j - (ny - 1) / 2.0) * py; }
            cb(x, y, z, v);
            return v;
        }
    }
}
