// Devices.cs -- the parametric transverse devices of the ao plugin; each compiles into the core `modulator` (Optics.ModulatorElement).
//
//   DeformableMirror.Compile(services, element, compiled)   element "deformableMirror" (docs/plugins/ao.md#deformablemirror)
//     actuator layout (square | hex | ring | explicit), Gaussian influence function with a stated neighbour coupling, stroke limit,
//     drive quantisation, surface-to-wavefront factor 2 cos(aoi); the surface is sampled on cells of pitch / samplesPerActuator and
//     given to the modulator as an optical path (chromatic "opd": a mirror does not disperse).
//   Slm.Compile(services, element, compiled)                element "slm"
//     pixels, pitch, fill factor, grey levels through a voltage-to-phase curve, liquid-crystal dispersion dn(lambda), cross-talk,
//     zero-order leak; input as grey levels, or as a phase target that is wrapped and mapped to the nearest grey level.
//     The pixel phases go to the modulator unquantised: the grey-level step is already in the curve (a non-uniform curve is not a
//     uniform quantiser).
//   DeviceSpecs.*                                           the ComponentSpec declarations (keys, units, defaults)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Optics;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Ao
{
    /// <summary>Actuator layouts and the Gaussian influence function of a deformable mirror (also used by the tests and by the Python module's twin).</summary>
    public static class DmGeometry
    {
        /// <summary>Actuator positions (m) for a layout. square: actuatorsX x actuatorsY, x-major; hex: rows of actuatorsX, odd rows shifted by half a pitch,
        /// row spacing pitch sqrt(3)/2; ring: ringCounts[k] actuators on a circle of radius k pitch (k = 0 is a centre actuator); explicit: positionsXM / positionsYM.</summary>
        public static List<double[]> Layout(JsonObject o, double pitch, string origin)
        {
            string layout = o.GetString("layout", "square").ToLowerInvariant();
            var p = new List<double[]>();
            if (layout == "square")
            {
                int nx = o.GetInt("actuatorsX", 0), ny = o.GetInt("actuatorsY", nx);
                if (nx < 1 || ny < 1) throw new JsonException(origin + ": actuatorsX (and actuatorsY) are needed for the square layout.");
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) p.Add(new[] { (i - (nx - 1) / 2.0) * pitch, (j - (ny - 1) / 2.0) * pitch });
            }
            else if (layout == "hex")
            {
                int nx = o.GetInt("actuatorsX", 0), ny = o.GetInt("actuatorsY", nx);
                if (nx < 1 || ny < 1) throw new JsonException(origin + ": actuatorsX (per row) and actuatorsY (rows) are needed for the hex layout.");
                double dy = pitch * Math.Sqrt(3) / 2;
                for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) p.Add(new[] { (i - (nx - 1) / 2.0 + (j % 2 == 0 ? -0.25 : 0.25)) * pitch, (j - (ny - 1) / 2.0) * dy });
            }
            else if (layout == "ring")
            {
                var counts = o.GetDoubleArray("ringCounts");
                if (counts == null || counts.Length == 0) throw new JsonException(origin + ": ringCounts (actuators per ring, ring k at radius k pitch) is needed for the ring layout.");
                for (int k = 0; k < counts.Length; k++)
                {
                    int n = (int)counts[k];
                    if (k == 0 && n >= 1) { p.Add(new[] { 0.0, 0.0 }); continue; }
                    for (int m = 0; m < n; m++) { double a = 2 * Math.PI * m / n; p.Add(new[] { k * pitch * Math.Cos(a), k * pitch * Math.Sin(a) }); }
                }
            }
            else if (layout == "explicit")
            {
                var x = o.GetDoubleArray("positionsXM"); var y = o.GetDoubleArray("positionsYM");
                if (x == null || y == null || x.Length != y.Length || x.Length == 0) throw new JsonException(origin + ": positionsXM and positionsYM (same length) are needed for the explicit layout.");
                for (int i = 0; i < x.Length; i++) p.Add(new[] { x[i], y[i] });
            }
            else throw new JsonException(origin + ": layout must be square, hex, ring or explicit.");
            return p;
        }

        /// <summary>Influence of an actuator at distance r: exp(ln(c) (r / pitch)^2), so a neighbour at one pitch moves by exactly the coupling c.</summary>
        public static double Influence(double r, double pitch, double coupling) => Math.Exp(Math.Log(coupling) * (r / pitch) * (r / pitch));
    }

    public static class DeformableMirror
    {
        public static void Compile(CompilerServices s, SystemElement e, CompiledElement ce)
        {
            string origin = "deformableMirror '" + e.Id + "'";
            double pitch = s.Num(e, "pitchM", double.NaN);
            if (!(pitch > 0)) throw new JsonException(origin + ": pitchM (actuator pitch, m) is needed.");
            double c = s.Num(e, "influenceCoupling", 0.15);
            if (!(c > 0 && c < 1)) throw new JsonException(origin + ": influenceCoupling is the surface value at a neighbouring actuator, in (0, 1).");
            var act = DmGeometry.Layout(e.Json, pitch, origin);
            int n = act.Count;
            double[] cmd = new double[n];
            if (e.Json.TryGet("commandsM", out var cv))
            {
                var a0 = cv.AsArray();
                var flat = a0.Count > 0 && a0[0].Kind == JsonKind.Array ? cv.AsDoubleMatrix().SelectMany(r => r).ToArray() : cv.AsDoubleArray();
                if (flat.Length != n) throw new JsonException(origin + ": commandsM has " + flat.Length + " values, the layout has " + n + " actuators.");
                cmd = flat;
            }
            double stroke = s.Num(e, "strokeM", 0); int bits = (int)s.Num(e, "driveBits", 0);
            int clipped = 0;
            if (stroke > 0)
            {
                for (int i = 0; i < n; i++) if (Math.Abs(cmd[i]) > stroke) { cmd[i] = Math.Sign(cmd[i]) * stroke; clipped++; }
                if (bits > 0)
                {
                    double step = 2 * stroke / (Math.Pow(2, bits) - 1);
                    for (int i = 0; i < n; i++) cmd[i] = -stroke + step * Math.Round((cmd[i] + stroke) / step);
                }
            }
            else if (bits > 0) throw new JsonException(origin + ": driveBits needs strokeM (the drive range is +-strokeM).");

            double margin = s.Num(e, "marginPitches", 0.5) * pitch;
            double x0 = act.Min(a => a[0]) - margin, x1 = act.Max(a => a[0]) + margin, y0 = act.Min(a => a[1]) - margin, y1 = act.Max(a => a[1]) + margin;
            int spa = Math.Max(1, (int)s.Num(e, "samplesPerActuator", 4));
            int nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / pitch * spa - 1e-9)), ny = Math.Max(1, (int)Math.Ceiling((y1 - y0) / pitch * spa - 1e-9));
            double cx = (x1 - x0) / nx, cy = (y1 - y0) / ny;
            double aoi = s.Num(e, "incidenceAngleRad", 0), factor = 2 * Math.Cos(aoi);
            double rInf = Math.Sqrt(Math.Log(1e-10) / Math.Log(c)) * pitch;
            double clear = s.Num(e, "clearApertureRadiusM", 0);
            double refl = Math.Sqrt(Math.Max(0, Math.Min(1, s.Num(e, "reflectivity", 1))));
            var opd = new double[nx * ny]; var amp = new double[nx * ny];
            double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                {
                    double x = x0 + (ix + 0.5) * cx, y = y0 + (iy + 0.5) * cy, h = 0;
                    for (int k = 0; k < n; k++)
                    {
                        double dx = x - act[k][0], dy = y - act[k][1];
                        if (Math.Abs(dx) > rInf || Math.Abs(dy) > rInf) continue;
                        double r = Math.Sqrt(dx * dx + dy * dy);
                        if (r <= rInf) h += cmd[k] * DmGeometry.Influence(r, pitch, c);
                    }
                    opd[ix * ny + iy] = factor * h;
                    amp[ix * ny + iy] = clear > 0 && x * x + y * y > clear * clear ? 0 : refl;
                }
            var m = new ModulatorElement { Nx = nx, Ny = ny, PitchXM = cx, PitchYM = cy, OffsetXM = mx, OffsetYM = my, OpdM = opd, Amplitude = amp };
            m.DesignOmega = e.Json.Has("designWavelengthM") ? 2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / s.Num(e, "designWavelengthM", 0) : s.Omega0;
            m.Prepare();
            ce.Active = m;
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "deformableMirror: {0} actuators ({1}), pitch {2:G4} m, neighbour coupling {3:G3}, surface-to-wavefront factor {4:G4}, {5} x {6} surface cells{7}",
                n, e.Json.GetString("layout", "square"), pitch, c, factor, nx, ny, clipped > 0 ? "; " + clipped + " actuator(s) clipped at the stroke limit " + stroke.ToString("G4", CultureInfo.InvariantCulture) + " m" : ""));
        }
    }

    public static class Slm
    {
        /// <summary>The phase (rad, at the design wavelength) of each grey level. linear: maxPhaseRad * k / levels (k = 0 .. levels - 1, so an N-level ramp
        /// is the usual 2 pi k / N); tanh: the same range with a saturating S-shaped voltage-to-phase response; table: lutPhaseRad.</summary>
        public static double[] Lut(CompilerServices s, SystemElement e, int levels, string origin)
        {
            string kind = s.Str(e, "lutKind", e.Json.Has("lutPhaseRad") ? "table" : "linear").ToLowerInvariant();
            double max = s.Num(e, "maxPhaseRad", 2 * Math.PI);
            var lut = new double[levels];
            if (kind == "table")
            {
                var t = e.Json.GetDoubleArray("lutPhaseRad");
                if (t == null || t.Length != levels) throw new JsonException(origin + ": lutPhaseRad needs one phase per grey level (" + levels + ").");
                for (int i = 1; i < levels; i++) if (t[i] < t[i - 1]) throw new JsonException(origin + ": lutPhaseRad must not decrease with the grey level.");
                return t;
            }
            if (kind == "linear") { for (int k = 0; k < levels; k++) lut[k] = max * k / levels; return lut; }
            if (kind == "tanh")
            {
                double st = s.Num(e, "lutSteepness", 2), th = Math.Tanh(st / 2);
                for (int k = 0; k < levels; k++) { double u = (double)k / levels; lut[k] = max * (0.5 + 0.5 * Math.Tanh(st * (u - 0.5)) / th); }
                return lut;
            }
            throw new JsonException(origin + ": lutKind must be linear, tanh or table.");
        }

        public static void Compile(CompilerServices s, SystemElement e, CompiledElement ce)
        {
            string origin = "slm '" + e.Id + "'";
            int nx = (int)s.Num(e, "pixelsX", 0), ny = (int)s.Num(e, "pixelsY", 0);
            double pitch = s.Num(e, "pitchM", double.NaN);
            if (!(pitch > 0)) throw new JsonException(origin + ": pitchM (pixel pitch, m) is needed.");
            int levels = (int)s.Num(e, "greyLevels", 256);
            if (levels < 2) throw new JsonException(origin + ": greyLevels must be at least 2.");
            var lut = Lut(s, e, levels, origin);
            double[] grey = AoMath.ReadMap(s, e, "greyLevel", "grey_level", ref nx, ref ny, origin, pitch, s.Num(e, "pitchYM", pitch));
            double[] target = grey == null ? AoMath.ReadMap(s, e, "phaseTargetRad", "phase_target_rad", ref nx, ref ny, origin, pitch, s.Num(e, "pitchYM", pitch)) : null;
            if (nx < 1 || ny < 1) throw new JsonException(origin + ": give pixelsX and pixelsY, or a map whose shape fixes them.");
            int n = nx * ny;
            var phase = new double[n];
            double lamD = e.Json.Has("designWavelengthM") ? s.Num(e, "designWavelengthM", 0) : 2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / s.Omega0;
            if (grey != null)
            {
                for (int i = 0; i < n; i++)
                {
                    int g = (int)Math.Round(grey[i]);
                    if (g < 0 || g >= levels) throw new JsonException(origin + ": grey level " + g + " is outside 0 .. " + (levels - 1) + ".");
                    phase[i] = lut[g];
                }
            }
            else if (target != null)
            {
                double wrap = 2 * Math.PI;
                for (int i = 0; i < n; i++) phase[i] = lut[NearestLevel(lut, target[i], wrap)];
            }
            var m = new ModulatorElement
            {
                Nx = nx, Ny = ny, PitchXM = pitch, PitchYM = s.Num(e, "pitchYM", pitch), OffsetXM = s.Num(e, "offsetXM", 0), OffsetYM = s.Num(e, "offsetYM", 0),
                RotationRad = s.Num(e, "rotationRad", 0), PhaseRad = phase, DesignOmega = 2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / lamD,
                FillFactor = s.Num(e, "fillFactor", 1), GapTransmission = s.Num(e, "gapTransmission", 0), OutsideTransmission = s.Num(e, "outsideTransmission", 0),
                ZeroOrderLeak = s.Num(e, "zeroOrderLeak", 0), CrosstalkSigmaCells = s.Num(e, "crosstalkSigmaCells", 0)
            };
            if (e.Json.Has("crosstalkKernel")) m.CrosstalkKernel = e.Json.GetDoubleArray("crosstalkKernel");
            string disp = s.Str(e, "dispersion", "liquidCrystal").ToLowerInvariant();
            string chrom = disp == "none" ? "opd" : "index";
            string note = "an achromatic optical path (dispersion none)";
            if (chrom == "index")
            {
                var k = e.Json.Has("deltaIndexCauchy") ? e.Json.GetDoubleArray("deltaIndexCauchy") : GenericNematicCauchy;
                m.DeltaIndex = AoMath.Cauchy(k);
                note = "liquid-crystal dispersion dn = A + B/lambda^2 + C/lambda^4 " + (e.Json.Has("deltaIndexCauchy") ? "(user values)" : "(GENERIC nematic preset, unverified: replace with your device's dn(lambda))");
            }
            m.Prepare();
            ce.Active = m;
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "slm: {0} x {1} pixels of {2:G4} m, fill {3:G3}, {4} grey levels over {5:G4} rad ({6}); {7}; input {8}",
                nx, ny, pitch, m.FillFactor, levels, lut[levels - 1], s.Str(e, "lutKind", "linear"), note, grey != null ? "grey levels" : target != null ? "phase target (wrapped, nearest level)" : "none (all pixels at level 0)"));
        }

        /// <summary>A generic nematic liquid-crystal birefringence, dn = 0.1805 + 1.6e-14 / lambda^2 (m^2): 0.227 at 589 nm and 0.195 at 1064 nm.
        /// Labelled generic: the order of magnitude of published nematic mixtures (E7-like), NOT a vendor or datasheet value.</summary>
        public static readonly double[] GenericNematicCauchy = { 0.1805, 1.6e-14, 0 };

        /// <summary>The grey level whose phase is nearest to <paramref name="target"/> modulo <paramref name="wrap"/> (the LUT is non-decreasing).</summary>
        public static int NearestLevel(double[] lut, double target, double wrap)
        {
            double t = target - wrap * Math.Floor(target / wrap);
            int lo = 0, hi = lut.Length - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (lut[mid] < t) lo = mid + 1; else hi = mid; }
            int best = lo;
            if (lo > 0 && Math.Abs(lut[lo - 1] - t) <= Math.Abs(lut[lo] - t)) best = lo - 1;
            // wrap-around: a target just below 2 pi is nearest to level 0 (+2 pi)
            if (Math.Abs(lut[best] - t) > Math.Abs(lut[0] + wrap - t)) best = 0;
            return best;
        }
    }
}
