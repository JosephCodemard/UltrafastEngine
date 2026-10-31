using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Crystals
{
    /// <summary>
    /// Three-wave mixing geometry in a crystal. Waves are named by role: for OPA/DFG "signal" (the reference
    /// direction, along the crystal's propagation axis), "idler", "pump"; for SHG "fundamental" (type I) or
    /// "fundamental_slow" + "fundamental_fast" (type II) and "sh"; for SFG "a" (reference), "b" and "sum".
    /// Internally wave 3 is the highest frequency (omega3 = omega1 + omega2). In non-collinear OPA the pump is
    /// tilted by alpha from the signal in the tuning plane (towards larger theta for alpha &gt; 0); the idler angle
    /// follows from transverse momentum conservation.
    /// </summary>
    public sealed class ThreeWave
    {
        public CrystalModel Crystal { get; set; }
        /// <summary>"shg", "sfg" or "opa" ("dfg" is opa with the idler as output).</summary>
        public string Process { get; set; } = "shg";
        /// <summary>Polarisation roles ("slow"/"fast") of waves 1, 2, 3 (by frequency, 3 highest).</summary>
        public string[] Roles { get; set; } = { "slow", "slow", "fast" };
        public double ThetaRad { get; set; } = Math.PI / 2;
        public double PhiRad { get; set; }
        /// <summary>"theta" or "phi": which angle a non-collinear tilt (and angle tuning) acts on.</summary>
        public string TuningAngle { get; set; } = "theta";
        public double NoncollinearRad { get; set; }
        /// <summary>SFG: angle between the two inputs outside the crystal (rad), used instead of <see cref="NoncollinearRad"/>. Signed: wave
        /// <see cref="ReferenceWave"/> goes towards larger theta for a positive angle.</summary>
        public double ExternalRad { get; set; }
        /// <summary>Grating vector of quasi-phase matching (rad/m, 0 = none): Delta k_eff = Delta k - Kg.</summary>
        public double QpmK { get; set; }
        /// <summary>Which of waves 1/2 is the reference (signal) direction in OPA: 1 or 2.</summary>
        public int ReferenceWave { get; set; } = 1;

        public sealed class Geometry
        {
            public double[] Omega = new double[3];
            public double[] N = new double[3];
            public double[] Kz = new double[3];
            public double[] AngleToRefRad = new double[3]; // signed in-plane angle of each wave's k from the reference direction
            public CrystalMode[] Modes = new CrystalMode[3];
            public Vec3[] Directions = new Vec3[3];
            public double DeltaK; // kz1 + kz2 - kz3 - Kg
        }

        private Vec3 Dir(double dAngle)
        {
            double th = ThetaRad, ph = PhiRad;
            if (TuningAngle == "phi") ph += dAngle; else th += dAngle;
            return CrystalModel.Direction(th, ph);
        }

        public Geometry At(double w1, double w2)
        {
            var g = new Geometry();
            g.Omega[0] = w1; g.Omega[1] = w2; g.Omega[2] = w1 + w2;
            const double c = PhysicalConstants.SpeedOfLightMPerS;
            int refW = ReferenceWave - 1, other = 1 - refW;
            // reference wave along the propagation axis
            g.Directions[refW] = Dir(0); g.AngleToRefRad[refW] = 0;
            g.Modes[refW] = Crystal.Mode(g.Omega[refW], g.Directions[refW], Roles[refW]);
            double kRef = g.Modes[refW].N * g.Omega[refW] / c;
            if (Process == "sfg" && (NoncollinearRad != 0 || ExternalRad != 0))
            {
                // the sum wave along the axis, the inputs on either side; transverse momentum balances, k_a sin a = k_b sin b
                g.Directions[2] = Dir(0); g.AngleToRefRad[2] = 0;
                g.Modes[2] = Crystal.Mode(g.Omega[2], g.Directions[2], Roles[2]);
                double wr = g.Omega[refW], wo = g.Omega[other], ar = 0, ao = 0;
                double er = ExternalRad == 0 ? 0 : Math.Atan2(wo * Math.Sin(ExternalRad), wr + wo * Math.Cos(ExternalRad)), eo = ExternalRad - er;
                for (int it = 0; it < 6; it++)
                {
                    g.Directions[refW] = Dir(ar); g.Directions[other] = Dir(-ao);
                    g.Modes[refW] = Crystal.Mode(wr, g.Directions[refW], Roles[refW]);
                    g.Modes[other] = Crystal.Mode(wo, g.Directions[other], Roles[other]);
                    double kr = g.Modes[refW].N * wr / c, ko = g.Modes[other].N * wo / c;
                    if (ExternalRad != 0) { ar = Math.Asin(Math.Sin(er) / g.Modes[refW].N); ao = Math.Asin(Math.Sin(eo) / g.Modes[other].N); }
                    else { ar = Math.Atan2(ko * Math.Sin(NoncollinearRad), kr + ko * Math.Cos(NoncollinearRad)); ao = NoncollinearRad - ar; }
                }
                g.AngleToRefRad[refW] = ar; g.AngleToRefRad[other] = -ao;
            }
            else if (Process == "opa" && NoncollinearRad != 0)
            {
                double a = NoncollinearRad;
                g.Directions[2] = Dir(a); g.AngleToRefRad[2] = a;
                g.Modes[2] = Crystal.Mode(g.Omega[2], g.Directions[2], Roles[2]);
                double kp = g.Modes[2].N * g.Omega[2] / c;
                // idler: transverse momentum balance kp sin(a) = ki sin(b) on the other side, iterate on its index
                double b = 0;
                for (int it = 0; it < 6; it++)
                {
                    g.Directions[other] = Dir(-b);
                    g.Modes[other] = Crystal.Mode(g.Omega[other], g.Directions[other], Roles[other]);
                    double ki = g.Modes[other].N * g.Omega[other] / c;
                    double sb = kp * Math.Sin(a) / ki;
                    b = Math.Asin(Math.Max(-1, Math.Min(1, sb)));
                }
                g.AngleToRefRad[other] = -b;
            }
            else
            {
                for (int j = 0; j < 3; j++)
                {
                    if (j == refW) continue;
                    g.Directions[j] = Dir(0);
                    g.Modes[j] = Crystal.Mode(g.Omega[j], g.Directions[j], Roles[j]);
                }
            }
            for (int j = 0; j < 3; j++)
            {
                g.N[j] = g.Modes[j].N;
                g.Kz[j] = g.N[j] * g.Omega[j] / c * Math.Cos(g.AngleToRefRad[j]);
            }
            g.DeltaK = g.Kz[0] + g.Kz[1] - g.Kz[2] - QpmK;
            return g;
        }

        public double Deff(Geometry g) => Crystal.Deff(g.Modes[0].E, g.Modes[1].E, g.Modes[2].E);

        /// <summary>z-component of the wavevector of wave j as a function of its own frequency, with the other
        /// input held fixed as appropriate (OPA: pump fixed; SHG: both fundamentals move together).</summary>
        public double KzOf(int wave, double omega, double w1, double w2)
        {
            // geometry recomputed with the given wave's frequency; the third wave adjusts
            var g = wave == 2 ? At(w1, omega - w1) : wave == 0 ? At(omega, w2) : At(w1, omega);
            return g.Kz[wave];
        }

        /// <summary>Group index along z of wave j (c dkz/domega).</summary>
        public double GroupIndex(int wave, double w1, double w2)
        {
            double w = wave == 0 ? w1 : wave == 1 ? w2 : w1 + w2, h = w * 1e-4;
            Vec3 dir = At(w1, w2).Directions[wave];
            double Kz(double ww) { var m = Crystal.Mode(ww, dir, Roles[wave]); double ang = At(w1, w2).AngleToRefRad[wave]; return m.N * ww / PhysicalConstants.SpeedOfLightMPerS * Math.Cos(ang); }
            return (Kz(w + h) - Kz(w - h)) / (2 * h) * PhysicalConstants.SpeedOfLightMPerS;
        }

        /// <summary>Roles for a phase-matching type: "I" (slow, slow, fast), "II"/"IIa" (slow, fast, fast), "IIb"
        /// (fast, slow, fast), "0" (all equal; the role with the larger |deff| is chosen by the caller).</summary>
        public static string[] RolesForType(string type)
        {
            switch ((type ?? "I").Trim().ToUpperInvariant())
            {
                case "I": case "1": return new[] { "slow", "slow", "fast" };
                case "II": case "IIA": case "2": return new[] { "slow", "fast", "fast" };
                case "IIB": return new[] { "fast", "slow", "fast" };
                case "0": case "ZERO": return new[] { "fast", "fast", "fast" };
                default: throw new ModelException("unknown phase-matching type \"" + type + "\" (I, II, IIa, IIb, 0)");
            }
        }
    }

    /// <summary>
    /// The phase-matching calculator behind <c>ultrafast crystal</c> and <c>ultrafast.crystal.phase_match</c>.
    /// Request (JSON):
    /// <code>{ "crystal": "BBO" | {user crystal}, "process": "shg" | "sfg" | "opa" | "dfg",
    ///   "type": "I" | "II" | "IIb" | "0", "polarizations": {"signal": "o", ...} (optional, overrides type),
    ///   "wavelengthsM": {"fundamental": 1.03e-6} | {"pump": 5.15e-7, "signal": 8e-7} | {"a": ..., "b": ...},
    ///   "plane": "XZ" | "YZ" | "XY" (biaxial), "thetaDeg" / "phiDeg" (fix an angle), "noncollinearDeg": 2.4 | "magic",
    ///   "qpm": true | {"periodM": 6.9e-6}, "lengthM": 1e-3, "temperatureC": 20, "pumpIntensityWPerM2": 1e14 }</code>
    /// </summary>
    public static class PhaseMatchCalculator
    {
        private const double C = PhysicalConstants.SpeedOfLightMPerS;
        private const double SincHalf = 2.7831; // sinc^2(x/2) = 1/2 at x = 2.7831

        private static double W(double lam) => 2 * Math.PI * C / lam;

        public static JsonObject Solve(JsonObject req, ModelRegistry reg = null, CrystalModel crystalOverride = null)
        {
            reg = reg ?? ModelRegistry.CreateDefault();
            double T = req.GetDouble("temperatureC", double.NaN);
            CrystalModel crystal = crystalOverride;
            if (crystal == null)
            {
                var cv = req["crystal"] ?? throw new JsonException("\"crystal\" is required");
                crystal = cv.Kind == JsonKind.String ? CrystalLibrary.Get(cv.AsString(), T) : CrystalLibrary.FromJson(cv.AsObject(), reg, T, "crystal");
            }
            string process = req.GetString("process", "shg").ToLowerInvariant();
            bool dfg = process == "dfg"; if (dfg) process = "opa";
            var wl = req.GetObject("wavelengthsM") ?? throw new JsonException("\"wavelengthsM\" is required");
            double w1, w2; string[] names; int refWave = 1;
            switch (process)
            {
                case "shg":
                {
                    double lf = wl.GetDouble("fundamental", double.NaN);
                    if (double.IsNaN(lf)) throw new JsonException("shg needs wavelengthsM.fundamental");
                    w1 = w2 = W(lf); names = new[] { "fundamental", "fundamental", "sh" };
                    break;
                }
                case "sfg":
                {
                    double la = wl.GetDouble("a", double.NaN), lb = wl.GetDouble("b", double.NaN);
                    if (double.IsNaN(la) || double.IsNaN(lb)) throw new JsonException("sfg needs wavelengthsM.a and .b");
                    double wa = W(la), wb = W(lb);
                    if (wa <= wb) { w1 = wa; w2 = wb; names = new[] { "a", "b", "sum" }; refWave = 1; } else { w1 = wb; w2 = wa; names = new[] { "b", "a", "sum" }; refWave = 2; }
                    break;
                }
                case "opa":
                {
                    double lp = wl.GetDouble("pump", double.NaN), ls = wl.GetDouble("signal", double.NaN), li = wl.GetDouble("idler", double.NaN);
                    if (double.IsNaN(lp)) { if (double.IsNaN(ls) || double.IsNaN(li)) throw new JsonException("opa/dfg needs two of wavelengthsM.pump, .signal, .idler"); lp = 1 / (1 / ls + 1 / li); }
                    if (double.IsNaN(ls)) ls = 1 / (1 / lp - 1 / li);
                    double ws = W(ls), wp = W(lp), wi = wp - ws;
                    if (wi <= 0) throw new JsonException("signal frequency must be below the pump frequency");
                    if (ws <= wi) { w1 = ws; w2 = wi; names = new[] { "signal", "idler", "pump" }; refWave = 1; } else { w1 = wi; w2 = ws; names = new[] { "idler", "signal", "pump" }; refWave = 2; }
                    break;
                }
                default: throw new JsonException("process must be shg, sfg, opa or dfg");
            }
            var tw = new ThreeWave { Crystal = crystal, Process = process, ReferenceWave = refWave };
            // roles
            string type = req.GetString("type", req.Has("qpm") && !req["qpm"].IsNull && !(req["qpm"].Kind == JsonKind.Bool && !req["qpm"].AsBool()) ? "0" : "I");
            tw.Roles = ThreeWave.RolesForType(type);
            var pol = req.GetObject("polarizations");
            if (pol != null)
                for (int j = 0; j < 3; j++) if (pol.Has(names[j])) tw.Roles[j] = crystal.IsSlow(pol.GetString(names[j]), W(1e-6)) ? "slow" : "fast";
            bool typeZero = type.Trim() == "0" && pol == null;
            // QPM
            double qpmPeriod = double.NaN; bool qpmAuto = false;
            if (req.TryGet("qpm", out var qv) && !qv.IsNull)
            {
                if (qv.Kind == JsonKind.Bool) qpmAuto = qv.AsBool();
                else { var qo = qv.AsObject(); qpmPeriod = qo.GetDouble("periodM", double.NaN); qpmAuto = double.IsNaN(qpmPeriod); }
            }
            bool qpm = qpmAuto || !double.IsNaN(qpmPeriod);
            double L = req.GetDouble("lengthM", 1e-3);

            // candidate planes / fixed angles
            var candidates = new List<(string Plane, double Theta, double Phi, string Tune)>();
            bool hasTheta = req.Has("thetaDeg"), hasPhi = req.Has("phiDeg");
            double thetaFix = req.GetDouble("thetaDeg", 90) * Math.PI / 180, phiFix = req.GetDouble("phiDeg", 0) * Math.PI / 180;
            string plane = req.GetString("plane", null)?.ToUpperInvariant();
            if (qpm) candidates.Add(("QPM", hasTheta ? thetaFix : Math.PI / 2, hasPhi ? phiFix : 0, "none"));
            else if (crystal.IsUniaxial)
            {
                var phis = hasPhi ? new[] { phiFix } : Enumerable.Range(0, 7).Select(k => k * 15.0 * Math.PI / 180).ToArray();
                foreach (var ph in phis) candidates.Add(("uniaxial", hasTheta ? thetaFix : double.NaN, ph, hasTheta ? "none" : "theta"));
            }
            else
            {
                var planes = plane != null ? new[] { plane } : new[] { "XY", "XZ", "YZ" };
                foreach (var pl in planes)
                    switch (pl)
                    {
                        case "XY": candidates.Add(("XY", Math.PI / 2, hasPhi ? phiFix : double.NaN, hasPhi ? "none" : "phi")); break;
                        case "XZ": candidates.Add(("XZ", hasTheta ? thetaFix : double.NaN, 0, hasTheta ? "none" : "theta")); break;
                        case "YZ": candidates.Add(("YZ", hasTheta ? thetaFix : double.NaN, Math.PI / 2, hasTheta ? "none" : "theta")); break;
                        default: throw new JsonException("plane must be XY, XZ or YZ");
                    }
            }

            // non-collinear angle
            bool magic = false; double alpha = 0;
            if (req.TryGet("noncollinearDeg", out var nv))
            {
                if (nv.Kind == JsonKind.String && nv.AsString().Equals("magic", StringComparison.OrdinalIgnoreCase)) magic = true;
                else alpha = nv.AsDouble() * Math.PI / 180;
            }
            double external = req.GetDouble("externalAngleDeg", 0) * Math.PI / 180;
            if (process == "shg" && (magic || alpha != 0 || external != 0)) throw new JsonException("one beam cannot cross itself: non-collinear SHG is two beams, so use process \"sfg\" with the same colour on both");
            if (magic && process != "opa") throw new JsonException("noncollinearDeg \"magic\" applies to opa/dfg only");
            if (external != 0 && process != "sfg") throw new JsonException("externalAngleDeg applies to sfg only");

            var solutions = new JsonArray();
            JsonObject best = null; double bestD = -1;
            var roleSets = typeZero ? new[] { new[] { "fast", "fast", "fast" }, new[] { "slow", "slow", "slow" } } : new[] { tw.Roles };
            foreach (var roles in roleSets)
                foreach (var cand in candidates)
                {
                    tw.Roles = (string[])roles.Clone();
                    tw.PhiRad = double.IsNaN(cand.Phi) ? 0 : cand.Phi; tw.ThetaRad = double.IsNaN(cand.Theta) ? Math.PI / 2 : cand.Theta;
                    tw.TuningAngle = cand.Plane == "XY" ? "phi" : "theta";
                    tw.NoncollinearRad = alpha; tw.ExternalRad = external; tw.QpmK = 0;
                    var angles = new List<double>();
                    if (cand.Tune == "none") angles.Add(cand.Tune == "none" && cand.Plane == "XY" ? tw.PhiRad : tw.ThetaRad);
                    else angles.AddRange(FindRoots(x => { Set(tw, cand.Tune, x); return tw.At(w1, w2).DeltaK; }, cand.Tune == "phi" ? 0 : 0, Math.PI / 2));
                    foreach (var ang in angles)
                    {
                        if (cand.Tune != "none") Set(tw, cand.Tune, ang);
                        if (magic)
                        {
                            var mg = MagicAngle(tw, w1, w2, cand.Tune);
                            if (mg == null) continue;
                            tw.NoncollinearRad = mg.Value.Alpha; Set(tw, cand.Tune, mg.Value.Angle);
                        }
                        if (qpm)
                        {
                            double dk = tw.At(w1, w2).DeltaK;
                            double period = !double.IsNaN(qpmPeriod) ? qpmPeriod : 2 * Math.PI / Math.Abs(dk);
                            tw.QpmK = Math.Sign(dk) * 2 * Math.PI / period;
                        }
                        var sol = Describe(tw, w1, w2, names, L, req, qpm, process, dfg, cand.Plane);
                        double d = Math.Abs(sol.GetDouble("deffPmPerV", 0));
                        solutions.Add(sol);
                        if (d > bestD) { bestD = d; best = sol; }
                        tw.QpmK = 0; tw.NoncollinearRad = alpha;
                    }
                }
            var res = new JsonObject().Set("crystal", crystal.Describe()).Set("references", crystal.References).Set("process", dfg ? "dfg" : process);
            res.Set("notes", JsonValue.FromArray(crystal.Notes));
            if (best == null)
            {
                res.Set("phaseMatched", false);
                res.Set("message", "no phase-matching solution for this crystal, type and wavelengths" + (qpm ? "" : " (try another type, plane, or qpm)"));
                return res;
            }
            res.Set("phaseMatched", true);
            res.Set("best", best);
            res.Set("solutions", solutions);
            return res;
        }

        private static void Set(ThreeWave tw, string tune, double x) { if (tune == "phi") tw.PhiRad = x; else if (tune == "theta") tw.ThetaRad = x; }

        /// <summary>Roots of f on [a, b] (sampled, then bisected).</summary>
        internal static List<double> FindRoots(Func<double, double> f, double a, double b, int samples = 360)
        {
            var roots = new List<double>();
            double xPrev = a + 1e-6, fPrev = f(xPrev);
            for (int i = 1; i <= samples; i++)
            {
                double x = a + (b - a) * i / samples - (i == samples ? 1e-9 : 0), fx = f(x);
                if (double.IsNaN(fx) || double.IsNaN(fPrev)) { xPrev = x; fPrev = fx; continue; }
                if (fx == 0) roots.Add(x);
                else if (Math.Sign(fx) != Math.Sign(fPrev))
                {
                    double lo = xPrev, hi = x, flo = fPrev;
                    for (int it = 0; it < 80; it++)
                    {
                        double mid = 0.5 * (lo + hi), fm = f(mid);
                        if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
                    }
                    roots.Add(0.5 * (lo + hi));
                }
                xPrev = x; fPrev = fx;
            }
            return roots;
        }

        /// <summary>Non-collinear angle and phase-matching angle that make d(Delta k)/d(omega_signal) = 0 at the centre
        /// (group-velocity matching of signal and idler along the signal: the broadband "magic" angle).</summary>
        internal static (double Alpha, double Angle)? MagicAngle(ThreeWave tw, double w1, double w2, string tune)
        {
            int sig = tw.ReferenceWave - 1;
            double wp = w1 + w2;
            double angle0 = tune == "phi" ? tw.PhiRad : tw.ThetaRad;
            Func<double, (double angle, double slope)> eval = a =>
            {
                tw.NoncollinearRad = a;
                var roots = FindRoots(x => { Set(tw, tune, x); return tw.At(w1, w2).DeltaK; }, 0, Math.PI / 2, 180);
                if (roots.Count == 0) return (double.NaN, double.NaN);
                double ang = roots.OrderBy(r => Math.Abs(r - angle0)).First();
                Set(tw, tune, ang);
                double h = (sig == 0 ? w1 : w2) * 1e-4;
                double DK(double ws) { double a1 = sig == 0 ? ws : wp - ws, a2 = sig == 0 ? wp - ws : ws; return tw.At(a1, a2).DeltaK; }
                double ws0 = sig == 0 ? w1 : w2;
                return (ang, (DK(ws0 + h) - DK(ws0 - h)) / (2 * h));
            };
            double prevA = 0.0; var prev = eval(prevA);
            for (int i = 1; i <= 120; i++)
            {
                double a = i * 0.1 * Math.PI / 180;
                var cur = eval(a);
                if (!double.IsNaN(cur.slope) && !double.IsNaN(prev.slope) && Math.Sign(cur.slope) != Math.Sign(prev.slope))
                {
                    double lo = prevA, hi = a; var flo = prev;
                    for (int it = 0; it < 50; it++)
                    {
                        double mid = 0.5 * (lo + hi); var fm = eval(mid);
                        if (double.IsNaN(fm.slope)) break;
                        if (Math.Sign(fm.slope) == Math.Sign(flo.slope)) { lo = mid; flo = fm; } else hi = mid;
                    }
                    var fin = eval(0.5 * (lo + hi));
                    return (0.5 * (lo + hi), fin.angle);
                }
                prevA = a; prev = cur;
            }
            return null;
        }

        /// <summary>Full width of the region around x0 where |(dk(x) - dk(x0)) L| &lt; 2.7831 (the sinc^2 FWHM when x0 is
        /// phase matched); works at group-velocity-matched points where the first derivative vanishes.</summary>
        internal static double Fwhm(Func<double, double> dk, double x0, double L, double relStep = 1e-6)
        {
            double k0 = dk(x0), target = SincHalf / L;
            double Side(int dir)
            {
                double step = Math.Max(Math.Abs(x0), 1e-30) * relStep, lo = 0, hi = step;
                for (int it = 0; it < 200; it++)
                {
                    double v = dk(x0 + dir * hi);
                    if (double.IsNaN(v)) return double.PositiveInfinity;
                    if (Math.Abs(v - k0) >= target) break;
                    lo = hi; hi *= 2;
                    if (hi > Math.Abs(x0) * 0.5 + 1) return double.PositiveInfinity;
                }
                for (int it = 0; it < 60; it++) { double mid = 0.5 * (lo + hi); if (Math.Abs(dk(x0 + dir * mid) - k0) >= target) hi = mid; else lo = mid; }
                return 0.5 * (lo + hi);
            }
            return Side(+1) + Side(-1);
        }

        private static JsonObject Describe(ThreeWave tw, double w1, double w2, string[] names, double L, JsonObject req, bool qpm, string process, bool dfg, string plane)
        {
            var ci = CultureInfo.InvariantCulture;
            var g = tw.At(w1, w2);
            double deff = tw.Deff(g) * (qpm ? 2 / Math.PI : 1);
            var o = new JsonObject()
                .Set("plane", plane)
                .Set("thetaDeg", tw.ThetaRad * 180 / Math.PI).Set("phiDeg", tw.PhiRad * 180 / Math.PI)
                .Set("deffPmPerV", deff * 1e12)
                .Set("deltaKPerM", g.DeltaK)
                .Set("roles", JsonValue.FromArray(tw.Roles));
            if (qpm) o.Set("qpmPeriodM", tw.QpmK != 0 ? 2 * Math.PI / Math.Abs(tw.QpmK) : double.PositiveInfinity).Set("qpmNote", "first-order QPM: deff includes the 2/pi factor");
            if (tw.NoncollinearRad != 0 && tw.Process != "opa") o.Set("noncollinearDeg", tw.NoncollinearRad * 180 / Math.PI);
            else if (tw.NoncollinearRad != 0)
            {
                o.Set("noncollinearDeg", tw.NoncollinearRad * 180 / Math.PI);
                // literature usually quotes the crystal cut as the PUMP angle to the optic axis
                o.Set("pumpThetaDeg", (tw.TuningAngle == "theta" ? tw.ThetaRad + tw.NoncollinearRad : tw.ThetaRad) * 180 / Math.PI);
                o.Set("idlerAngleToSignalDeg", -g.AngleToRefRad[1 - (tw.ReferenceWave - 1)] * 180 / Math.PI);
            }
            var waves = new JsonArray();
            var ng = new double[3];
            for (int j = 0; j < 3; j++)
            {
                if (process == "shg" && j == 1 && tw.Roles[0] == tw.Roles[1]) { ng[1] = ng[0]; continue; }
                ng[j] = tw.GroupIndex(j, w1, w2);
                string nm = names[j];
                if (process == "shg" && tw.Roles[0] != tw.Roles[1] && j < 2) nm = "fundamental_" + tw.Roles[j];
                waves.Add(new JsonObject().Set("name", nm).Set("wavelengthM", 2 * Math.PI * C / g.Omega[j]).Set("role", tw.Roles[j])
                    .Set("polarization", tw.Crystal.IsUniaxial ? (tw.Crystal.IsSlow("o", g.Omega[j]) == (tw.Roles[j] == "slow") ? "o" : "e") : tw.Roles[j])
                    .Set("n", g.N[j]).Set("groupIndex", ng[j]).Set("walkOffMrad", g.Modes[j].WalkOffRad * 1e3)
                    .Set("angleToSignalDeg", g.AngleToRefRad[j] * 180 / Math.PI));
            }
            o.Set("waves", waves);
            o.Set("coherenceLengthM", Math.Abs(g.DeltaK) > 1e-9 ? Math.PI / Math.Abs(g.DeltaK) : double.PositiveInfinity);
            // GVM (fs/mm)
            var gvm = new JsonObject();
            void G(int a, int b, string key) => gvm.Set(key, (ng[a] - ng[b]) / C * 1e15 * 1e-3);
            if (process == "opa") { int s = tw.ReferenceWave - 1, i = 1 - s; G(2, s, "pump-signal fs/mm"); G(i, s, "idler-signal fs/mm"); G(2, i, "pump-idler fs/mm"); }
            else if (process == "shg") G(2, 0, "sh-fundamental fs/mm");
            else { G(2, 0, "sum-" + names[0] + " fs/mm"); G(1, 0, names[1] + "-" + names[0] + " fs/mm"); }
            o.Set("gvm", gvm);
            // acceptance bandwidths (FWHM of sinc^2 for length L): searched on the actual mismatch function
            var acc = new JsonObject().Set("lengthM", L);
            if (process == "shg")
            {
                double dw = Fwhm(x => tw.At(x, x).DeltaK, w1, L);
                acc.Set("fundamentalFwhmM", 2 * Math.PI * C / (w1 * w1) * dw);
            }
            else if (process == "opa")
            {
                int s = tw.ReferenceWave - 1; double wp = w1 + w2, ws = s == 0 ? w1 : w2;
                double dw = Fwhm(x => s == 0 ? tw.At(x, wp - x).DeltaK : tw.At(wp - x, x).DeltaK, ws, L);
                acc.Set("signalFwhmM", 2 * Math.PI * C / (ws * ws) * dw);
                double hh = ws * 1e-5;
                acc.Set("dDeltaK_dOmegaSignal", s == 0 ? (tw.At(ws + hh, wp - ws - hh).DeltaK - tw.At(ws - hh, wp - ws + hh).DeltaK) / (2 * hh) : (tw.At(wp - ws - hh, ws + hh).DeltaK - tw.At(wp - ws + hh, ws - hh).DeltaK) / (2 * hh));
            }
            else
            {
                double dw = Fwhm(x => tw.At(x, w2).DeltaK, w1, L);
                acc.Set(names[0] + "FwhmM", 2 * Math.PI * C / (w1 * w1) * dw);
            }
            {
                string tune = plane == "XY" ? "phi" : "theta";
                double a0 = tune == "phi" ? tw.PhiRad : tw.ThetaRad;
                double da = Fwhm(x => { Set(tw, tune, x); return tw.At(w1, w2).DeltaK; }, a0, L, 1e-7);
                Set(tw, tune, a0);
                acc.Set("angularFwhmRad", da);
            }
            if (tw.Crystal.TemperatureDependent)
            {
                double T0 = tw.Crystal.TemperatureC;
                var hot = CrystalLibrary.Get(tw.Crystal.Name.StartsWith("MgO") ? "MgO:LiNbO3" : "LiNbO3", T0 + 0.5);
                var cold = CrystalLibrary.Get(tw.Crystal.Name.StartsWith("MgO") ? "MgO:LiNbO3" : "LiNbO3", T0 - 0.5);
                var keep = tw.Crystal;
                tw.Crystal = hot; double kh = tw.At(w1, w2).DeltaK; tw.Crystal = cold; double kc = tw.At(w1, w2).DeltaK; tw.Crystal = keep;
                double dkdt = kh - kc;
                acc.Set("thermalFwhmK", Math.Abs(dkdt) > 1e-12 ? SincHalf / (L * Math.Abs(dkdt)) : double.PositiveInfinity);
            }
            o.Set("acceptance", acc);
            // OPA small-signal gain spectrum
            double ip = req.GetDouble("pumpIntensityWPerM2", double.NaN);
            if (process == "opa" && !double.IsNaN(ip))
            {
                int s = tw.ReferenceWave - 1; double wp = w1 + w2, ws0 = s == 0 ? w1 : w2;
                var lam = new List<double>(); var gain = new List<double>();
                double eps0 = 8.8541878128e-12;
                for (int k = -200; k <= 200; k++)
                {
                    double ws = ws0 * (1 + k * 0.0015);
                    double wi = wp - ws; if (wi <= 0) continue;
                    var gg = s == 0 ? tw.At(ws, wi) : tw.At(wi, ws);
                    double d = tw.Deff(gg) * (qpm ? 2 / Math.PI : 1);
                    double Gam = d * Math.Sqrt(2 / (eps0 * C * C * C * gg.N[0] * gg.N[1] * gg.N[2]));
                    double g0sq = ws * wi * Gam * Gam * ip;
                    double dk = gg.DeltaK;
                    double gsq = g0sq - dk * dk / 4;
                    double gainV;
                    if (gsq > 0) { double gl = Math.Sqrt(gsq) * L; gainV = 1 + g0sq / gsq * Math.Pow(Math.Sinh(gl), 2); }
                    else { double gl = Math.Sqrt(-gsq) * L; gainV = 1 + g0sq / (-gsq) * Math.Pow(Math.Sin(gl), 2); }
                    lam.Add(2 * Math.PI * C / ws); gain.Add(gainV);
                }
                o.Set("gainSpectrum", new JsonObject().Set("signalWavelengthM", JsonValue.FromArray(lam)).Set("gain", JsonValue.FromArray(gain)).Set("pumpIntensityWPerM2", ip));
            }
            return o;
        }
    }
}
