// CellMode.cs -- the Kerr-matched eigenmode of a two-mirror multi-pass cell (Herriott or quasi-concentric).
//
// What it does: a Gaussian beam that travels many times through a cell with two curved mirrors, a gas fill and / or glass plates
// is stable only if its q parameter at the launch plane reproduces itself after one round trip. In the empty cell this is the
// Kogelnik-Li eigenmode. With a Kerr medium at high peak power each pass adds a power-dependent thin lens (n2 I0 L / w^2 per
// slice), so the matched mode depends on the power; the authors of multi-pass-cell experiments find it by trial. CellMode finds it:
// the round-trip matrix is built from the mirrors, the media and the Kerr lenses (the lenses depend on the beam radius at their
// own position), and the self-consistent mode is found by a damped fixed-point iteration with continuation in the power.
//
// Main classes and signatures:
//   CellModeSolver.Solve(JsonObject request) -> JsonObject          (the "cellMode" command, ultrafast.cells.cell_mode)
//   CellModeSolver.HerriottSpacingM(radiusM, roundTrips, order)     (spacing that closes a Herriott pattern)
//   CellModeSolver.EmptyCellWaistM(wavelengthM, radius1M, radius2M, separationM)   (Kogelnik-Li waist, closed form)
//
// Model and limits: round beams (the x and y planes are the same: a Herriott cell used at small off-axis angle; the astigmatism
// of the mirrors at the injection angle is not included), thin-lens ABCD in reduced coordinates q^ = q / n (as BeamState), peak
// power (the match holds at the pulse peak; the wings see a weaker lens), instantaneous Kerr effect only (no dispersion, no
// plasma, no time dependence). The Kerr lens of a slice of length dz is 1/f = c n2 I0 dz / w^2 with I0 = 2P / (pi w^2): c = 1 is
// the second-moment lens (it reproduces the Vlasov-Petrishchev-Talanov law for the rms radius, the default), c = 4 the on-axis
// parabolic lens ("paraxial", the lens a strong beam gives at its centre).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Lasers
{
    public static class CellModeSolver
    {
        private enum OpKind { Prop, Lens, Kerr }

        /// <summary>One element of the round trip, in reduced coordinates: a propagation of reduced length l = L / n, a fixed thin
        /// lens of power phi (mirrors), or a Kerr slice of length dz and index n2 (its lens power follows the beam).</summary>
        private struct Op
        {
            public OpKind Kind; public double L, Phi, N2Dz; public string Tag;
        }

        private sealed class Segment { public double LengthM, Index, N2; public string Tag; }

        /// <summary>Spacing d = R (1 - cos(pi k / N)) of a symmetric cell of mirror radius R whose Herriott pattern closes after
        /// N round trips (2N passes) going k times round the Gouy circle (Herriott, Kogelnik, Kompfner, Appl. Opt. 3, 523 (1964)).</summary>
        public static double HerriottSpacingM(double radiusM, int roundTrips, int order) => radiusM * (1 - Math.Cos(Math.PI * order / roundTrips));

        /// <summary>Waist radius of the empty two-mirror cell (Kogelnik and Li, Appl. Opt. 5, 1550 (1966)): w0^2 = (lambda d / pi) sqrt(g1 g2 (1 - g1 g2)) / (g1 + g2 - 2 g1 g2).</summary>
        public static double EmptyCellWaistM(double wavelengthM, double radius1M, double radius2M, double separationM)
        {
            double g1 = 1 - separationM / radius1M, g2 = 1 - separationM / radius2M;
            double w2 = wavelengthM * separationM / Math.PI * Math.Sqrt(g1 * g2 * (1 - g1 * g2)) / Math.Abs(g1 + g2 - 2 * g1 * g2);
            return Math.Sqrt(w2);
        }

        /// <summary>Power (1/f, 1/m) of the thin Kerr lens of a slice of length dzM and index n2 (m^2/W) that a Gaussian beam of peak power
        /// P and 1/e^2 radius w sees: c n2 dz I0 / w^2 with I0 = 2P / (pi w^2); c = 1 for "secondMoment", 4 for "paraxial".</summary>
        public static double KerrLensPowerPerM(double n2, double dzM, double peakPowerW, double radiusM, string lensModel = "secondMoment")
        {
            double c = lensModel == "paraxial" ? 4 : 1;
            return c * 2 * n2 * dzM * peakPowerW / (Math.PI * Math.Pow(radiusM, 4));
        }

        // ------------------------------------------------------------------------------------------ matrices and tracing

        private static double[] Mul(double[] a, double[] b) => new[] { a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3], a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3] };

        private static double[] Matrix(List<Op> ops, double[] phi)
        {
            var m = new[] { 1.0, 0, 0, 1 };
            for (int i = 0; i < ops.Count; i++)
            {
                var o = ops[i];
                double[] t;
                if (o.Kind == OpKind.Prop) t = new[] { 1.0, o.L, 0, 1 };
                else t = new[] { 1.0, 0, -phi[i], 1 };
                m = Mul(t, m);
            }
            return m;
        }

        /// <summary>The self-consistent 1/q^ of a stable round trip M (the sign of B does not matter): 1/q = (D - A) / 2B - i sqrt(1 - h^2) / |B|.</summary>
        private static Complex Eigen(double[] m)
        {
            double h = (m[0] + m[3]) / 2;
            return new Complex((m[3] - m[0]) / (2 * m[1]), -Math.Sqrt(1 - h * h) / Math.Abs(m[1]));
        }

        /// <summary>Round-trip Gouy phase theta in (0, 2 pi): M = cos(theta) 1 + sin(theta) N with B = beta sin(theta), beta &gt; 0, so the sign of B
        /// tells which half of the circle it is in (quasi-concentric cells, g &lt; 0, have theta &gt; pi).</summary>
        private static double GouyPhase(double[] m)
        {
            double h = (m[0] + m[3]) / 2, s = Math.Sqrt(Math.Max(0, 1 - h * h));
            double th = Math.Atan2(m[1] >= 0 ? s : -s, h);
            return th < 0 ? th + 2 * Math.PI : th;
        }

        /// <summary>Radius of the beam with reduced inverse complex parameter p = 1/q^: w^2 = -lambda0 / (pi Im p).</summary>
        private static double Radius(Complex p, double lambda) => Math.Sqrt(-lambda / (Math.PI * p.Imaginary));

        /// <summary>Follows 1/q^ through the ops; returns the radius at each op's position (after the op for Prop, at the lens for Lens / Kerr), and the final 1/q^.</summary>
        private static Complex Trace(List<Op> ops, double[] phi, Complex p0, double lambda, double[] radiusAtOp, double[] radiusAfterOp = null)
        {
            var p = p0;
            for (int i = 0; i < ops.Count; i++)
            {
                var o = ops[i];
                if (o.Kind == OpKind.Prop) { p = p / (1 + o.L * p); }
                else { radiusAtOp[i] = Radius(p, lambda); p = p - phi[i]; }
                if (radiusAfterOp != null) radiusAfterOp[i] = Radius(p, lambda);
            }
            return p;
        }

        // ------------------------------------------------------------------------------------------ solve

        public static JsonObject Solve(JsonObject r)
        {
            double lam = r.GetRequiredDouble("wavelengthM", "cellMode");
            if (!(lam > 0)) throw new JsonException("cellMode: wavelengthM must be positive.");
            double omega = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lam;
            double R1, R2;
            if (r.Has("mirrorRadiiM")) { var a = r.GetDoubleArray("mirrorRadiiM"); if (a.Length == 1) { R1 = R2 = a[0]; } else if (a.Length == 2) { R1 = a[0]; R2 = a[1]; } else throw new JsonException("cellMode: mirrorRadiiM is one or two numbers."); }
            else { R1 = r.GetRequiredDouble("mirrorRadiusM", "cellMode (give mirrorRadiusM or mirrorRadiiM)"); R2 = R1; }
            if (!(R1 > 0) || !(R2 > 0)) throw new JsonException("cellMode: concave mirrors have positive radii of curvature.");
            int passes = r.GetInt("passes", 2);
            double d;
            var re = r.GetObject("reentrant");
            if (r.Has("separationM")) d = r.GetDouble("separationM", 0);
            else if (re != null)
            {
                if (Math.Abs(R1 - R2) > 1e-12 * R1) throw new JsonException("cellMode: reentrant spacing is for equal mirrors.");
                d = HerriottSpacingM(R1, re.GetInt("roundTrips", Math.Max(1, passes / 2)), re.GetInt("order", 1));
            }
            else throw new JsonException("cellMode: give separationM (or reentrant {roundTrips, order} for a Herriott cell of equal mirrors).");
            if (!(d > 0)) throw new JsonException("cellMode: separationM must be positive.");
            double power = r.GetDouble("peakPowerW", 0);
            if (power < 0) throw new JsonException("cellMode: peakPowerW cannot be negative.");
            double coef;
            string lensModel = r.GetString("lensModel", "secondMoment");
            if (lensModel == "secondMoment") coef = 1; else if (lensModel == "paraxial") coef = 4; else throw new JsonException("cellMode: lensModel is \"secondMoment\" or \"paraxial\".");
            int sliceDefault = Math.Max(1, r.GetInt("slices", 12));

            // the medium along one traversal, from mirror 1 to mirror 2
            double gasN = 1.0, gasN2 = 0; string gasName = "vacuum";
            var gas = r.GetObject("gas");
            if (gas != null)
            {
                string sp = gas.GetRequiredString("species", "cellMode gas");
                var m = MaterialLibrary.Gas(sp, gas.GetDouble("pressureBar", 1.0), gas.GetDouble("temperatureK", 293.15));
                gasN = m.IndexAt(omega); gasN2 = gas.Has("n2M2PerW") ? gas.GetDouble("n2M2PerW", 0) : m.N2;
                gasName = m.Name;
            }
            else if (r.Has("fillIndex") || r.Has("fillN2M2PerW")) { gasN = r.GetDouble("fillIndex", 1.0); gasN2 = r.GetDouble("fillN2M2PerW", 0); gasName = "uniform fill"; }
            var plates = new List<(double Pos, double T, double N, double N2, string Name)>();
            var pa = r.GetArray("plates");
            if (pa != null)
                foreach (var v in pa)
                {
                    var po = v.AsObject();
                    double t = po.GetRequiredDouble("thicknessM", "cellMode plate");
                    double pos = po.Has("positionM") ? po.GetDouble("positionM", 0) : d / 2;
                    double n, n2; string nm;
                    if (po.Has("material"))
                    {
                        var m = MaterialLibrary.BuiltIn(po.GetString("material"));
                        nm = m.Name; n = po.Has("index") ? po.GetDouble("index", 1) : m.IndexAt(omega); n2 = po.Has("n2M2PerW") ? po.GetDouble("n2M2PerW", 0) : m.N2;
                    }
                    else { nm = "plate"; n = po.GetRequiredDouble("index", "cellMode plate (give material or index)"); n2 = po.GetDouble("n2M2PerW", 0); }
                    if (pos - t / 2 < -1e-12 || pos + t / 2 > d + 1e-12) throw new JsonException("cellMode: a plate must lie between the mirrors (0 <= positionM -+ thicknessM / 2 <= separationM).");
                    plates.Add((pos, t, n, n2, nm));
                }
            plates.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            for (int i = 1; i < plates.Count; i++) if (plates[i].Pos - plates[i].T / 2 < plates[i - 1].Pos + plates[i - 1].T / 2 - 1e-12) throw new JsonException("cellMode: plates overlap.");
            var segs = new List<Segment>();
            double at = 0;
            foreach (var p in plates)
            {
                if (p.Pos - p.T / 2 > at + 1e-15) segs.Add(new Segment { LengthM = p.Pos - p.T / 2 - at, Index = gasN, N2 = gasN2, Tag = gasName });
                segs.Add(new Segment { LengthM = p.T, Index = p.N, N2 = p.N2, Tag = p.Name });
                at = p.Pos + p.T / 2;
            }
            if (d > at + 1e-15) segs.Add(new Segment { LengthM = d - at, Index = gasN, N2 = gasN2, Tag = gasName });

            // the round trip as a list of ops, starting just after mirror 1
            var ops = new List<Op>();
            void Traverse(IList<Segment> order, double mirrorRadius, double mirrorIndex, string mirrorName)
            {
                foreach (var s in order)
                {
                    bool kerr = s.N2 != 0 && power > 0;
                    int sl = kerr ? sliceDefault : 1;
                    double dz = s.LengthM / sl;
                    for (int k = 0; k < sl; k++)
                    {
                        if (kerr) { ops.Add(new Op { Kind = OpKind.Prop, L = dz / 2 / s.Index }); ops.Add(new Op { Kind = OpKind.Kerr, N2Dz = s.N2 * dz, Tag = s.Tag }); ops.Add(new Op { Kind = OpKind.Prop, L = dz / 2 / s.Index }); }
                        else ops.Add(new Op { Kind = OpKind.Prop, L = dz / s.Index });
                    }
                }
                ops.Add(new Op { Kind = OpKind.Lens, Phi = 2 * mirrorIndex / mirrorRadius, Tag = mirrorName });
            }
            Traverse(segs, R2, segs[segs.Count - 1].Index, "mirror 2");
            Traverse(segs.AsEnumerable().Reverse().ToList(), R1, segs[0].Index, "mirror 1");
            int nOps = ops.Count;
            var fixedPhi = new double[nOps];
            for (int i = 0; i < nOps; i++) if (ops[i].Kind == OpKind.Lens) fixedPhi[i] = ops[i].Phi;

            // ---- empty (linear) cell: the Kogelnik-Li eigenmode of the round trip
            var m0 = Matrix(ops, fixedPhi);
            double h0 = (m0[0] + m0[3]) / 2;
            var res = new JsonObject();
            res.Set("wavelengthM", lam).Set("mirrorRadiiM", JsonValue.FromArray(new[] { R1, R2 })).Set("separationM", d).Set("passes", passes)
               .Set("peakPowerW", power).Set("lensModel", lensModel).Set("gasIndex", gasN);
            res.Set("emptyCellStability", h0);
            double g1 = 1 - d / R1, g2 = 1 - d / R2;
            res.Set("g1", g1).Set("g2", g2);
            bool stable0 = Math.Abs(h0) < 1 && m0[1] != 0;
            res.Set("emptyCellStable", stable0);
            if (!stable0)
            {
                res.Set("matched", false).Set("message", "the empty cell is unstable (|(A + D) / 2| = " + Math.Abs(h0).ToString("G4", CultureInfo.InvariantCulture) + " >= 1): g1 g2 must lie between 0 and 1.");
                return res;
            }
            double n0 = segs[0].Index;   // index at the launch plane (just after mirror 1)
            var p0 = Eigen(m0);
            res.Set("emptyCell", Describe(p0, lam, n0, ops, fixedPhi, d));
            res.Set("roundTripGouyPhaseRad", GouyPhase(m0));

            // ---- the Kerr-matched mode: damped fixed point, continuation in the power
            var kerrIdx = Enumerable.Range(0, nOps).Where(i => ops[i].Kind == OpKind.Kerr).ToArray();
            var phi = (double[])fixedPhi.Clone();
            var pCur = p0; int iterations = 0; bool converged = true; string failure = null;
            double lensScale = coef * 2 / Math.PI;     // 1/f = c n2 dz (2P / pi w^2) / w^2
            if (kerrIdx.Length > 0)
            {
                int steps = 10;
                for (int s = 1; s <= steps && converged; s++)
                {
                    double P = power * s / steps; double alpha = 0.5; int inner = 0; bool ok = false;
                    var wAt = new double[nOps];
                    while (inner++ < 400)
                    {
                        iterations++;
                        Trace(ops, phi, pCur, lam, wAt);
                        double change = 0;
                        var next = (double[])phi.Clone();
                        foreach (var i in kerrIdx)
                        {
                            double w = wAt[i];
                            double target = lensScale * ops[i].N2Dz * P / (w * w * w * w);
                            next[i] = (1 - alpha) * phi[i] + alpha * target;
                            change = Math.Max(change, Math.Abs(next[i] - phi[i]) / (Math.Abs(next[i]) + 1e-30));
                        }
                        var mm = Matrix(ops, next);
                        double hh = (mm[0] + mm[3]) / 2;
                        if (!(Math.Abs(hh) < 1) || mm[1] == 0 || double.IsNaN(hh)) { alpha *= 0.5; if (alpha < 1e-4) break; continue; }
                        phi = next; pCur = Eigen(mm);
                        if (change < 1e-13) { ok = true; break; }
                    }
                    if (!ok) { converged = false; failure = "no self-consistent stable mode at " + (P).ToString("G4", CultureInfo.InvariantCulture) + " W (the Kerr lens drives the cell out of its stability range, or the iteration did not converge)."; }
                }
            }
            var mf = Matrix(ops, phi);
            double hf = (mf[0] + mf[3]) / 2;
            res.Set("iterations", iterations);
            if (!converged || !(Math.Abs(hf) < 1))
            {
                res.Set("matched", false).Set("message", failure ?? "unstable");
                return res;
            }
            // residual of the self-consistency: trace the final mode with the lenses of its own radii
            var wChk = new double[nOps];
            var pEnd = Trace(ops, phi, pCur, lam, wChk);
            double resid = Complex.Abs(pEnd - pCur) / Complex.Abs(pCur);
            foreach (var i in kerrIdx) resid = Math.Max(resid, Math.Abs(phi[i] - lensScale * ops[i].N2Dz * power / Math.Pow(wChk[i], 4)) / phi[i]);
            res.Set("matched", true).Set("residual", resid).Set("stability", hf);
            res.Set("roundTripGouyPhaseKerrRad", GouyPhase(mf));
            res.Set("mode", Describe(pCur, lam, n0, ops, phi, d));

            // ---- Kerr numbers on the matched mode
            double bRt = 0, lensSum = 0; int kerrCount = 0; double bRtAxis = 0;
            foreach (var i in kerrIdx) { double w = wChk[i]; bRtAxis += 2 * Math.PI / lam * ops[i].N2Dz * 2 * power / (Math.PI * w * w); lensSum += phi[i]; kerrCount++; }
            bRt = bRtAxis;
            double nRef = gasN; double pcr = 3.77 * lam * lam / (8 * Math.PI * nRef * Math.Max(gasN2, 1e-300));
            res.Set("bIntegralPerRoundTripRad", bRt).Set("bIntegralTotalRad", bRt * passes / 2.0);
            res.Set("kerrLensPowerPerRoundTripPerM", lensSum);
            res.Set("kerrLensFocalLengthPerRoundTripM", lensSum > 0 ? 1 / lensSum : double.PositiveInfinity);
            if (gasN2 > 0) res.Set("criticalPowerGasW", pcr).Set("peakPowerOverCriticalGas", power / pcr);
            // the empty-cell mode launched into the Kerr cell: overlap of the two Gaussian modes at the launch plane
            res.Set("overlapEmptyModeWithMatchedMode", Overlap(p0, pCur, lam));
            res.Set("shiftOfWaistRadius", ((JsonObject)res["mode"]).GetDouble("waistRadiusM", 0) / ((JsonObject)res["emptyCell"]).GetDouble("waistRadiusM", 1) - 1);
            return res;
        }

        /// <summary>Overlap |&lt;u1|u2&gt;|^2 of two round Gaussian modes at the same plane (power coupling efficiency).</summary>
        public static double Overlap(Complex p1, Complex p2, double lambda)
        {
            // for 1/q = rho - i lambda / (pi w^2) the power overlap of two round (2-D) modes is 4 / ((w1/w2 + w2/w1)^2 + (pi w1 w2 / lambda)^2 (rho1 - rho2)^2)
            double w1 = Radius(p1, lambda), w2 = Radius(p2, lambda);
            double a = w1 / w2 + w2 / w1, b = Math.PI * w1 * w2 / lambda * (p1.Real - p2.Real);
            return 4 / (a * a + b * b);
        }

        private static JsonObject Describe(Complex p, double lam, double nLaunch, List<Op> ops, double[] phi, double d)
        {
            double wL = Radius(p, lam);
            Complex q = 1 / p;                              // reduced q^ at the launch plane
            double zWaist = -q.Real * nLaunch;              // true distance from the launch plane to the waist (positive: ahead)
            double w0 = Math.Sqrt(lam * q.Imaginary / Math.PI);
            var o = new JsonObject();
            o.Set("radiusAtLaunchM", wL).Set("waistRadiusM", w0).Set("waistDistanceFromLaunchM", zWaist)
             .Set("curvatureAtLaunchPerM", p.Real / nLaunch).Set("qReducedRealM", q.Real).Set("qReducedImagM", q.Imaginary)
             .Set("rayleighRangeM", q.Imaginary * nLaunch);
            // radius at the two mirrors: just before mirror 2 (first Lens op) and before mirror 1 (second)
            var w = new double[ops.Count]; var wAfter = new double[ops.Count];
            Trace(ops, phi, p, lam, w, wAfter);
            var mir = Enumerable.Range(0, ops.Count).Where(i => ops[i].Kind == OpKind.Lens).ToArray();
            o.Set("radiusAtMirror2M", w[mir[0]]).Set("radiusAtMirror1M", w[mir[1]]);
            return o;
        }
    }
}
