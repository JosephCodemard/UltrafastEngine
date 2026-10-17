using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Crystals
{
    /// <summary>
    /// A chi(2) crystal acting on the main beam (docs/crystals.md):
    /// <code>
    /// { "id": "nopa", "type": "crystal", "crystal": "BBO", "lengthM": 3e-3, "temperatureC": 20,
    ///   "cut": {"thetaDeg": 22.0, "phiDeg": 90} | "auto",
    ///   "interaction": {"process": "opa", "type": "I", "noncollinearDeg": 2.56 | "magic",
    ///                   "qpm": {"periodM": 6.9e-6, "model": "averaged" | "domains", "periodExpr": "6.9e-6 + 1e-4*z"}},
    ///   "mainRole": "signal", "aux": [ {aux beam} ], "output": "signal", "record": ["idler", "pump"],
    ///   "deffPmPerV": 2.0, "beam": {...}, "transverse": {...}, "seedNoise": false, "chi3": true }
    /// </code>
    /// </summary>
    public sealed class CrystalElement : IActiveElement
    {
        public string Id;
        public CrystalModel Crystal;
        public double LengthM;
        public string Process;           // shg, sfg, opa
        public bool Dfg;
        public string Type = "I";
        public string[] Roles;           // waves 1, 2, 3 by frequency
        public bool RolesFromPolarizations;
        public double ThetaRad, PhiRad;
        public string Plane;
        public double NoncollinearRad;
        public double QpmPeriodM = double.NaN;
        public string QpmModel = "averaged";
        public Func<double, double> QpmPeriodOfZ;
        public bool Qpm;
        public string MainRole;
        public List<AuxBeamSpec> Aux = new List<AuxBeamSpec>();
        public string Output;
        public HashSet<string> Record;
        public double DeffOverride = double.NaN;
        public IBeamModel Beam;
        public TransverseSpec Transverse = new TransverseSpec();
        public bool SeedNoise;
        public int NoiseSeed = 12345;
        public bool Chi3 = true;
        public double N2;
        public double PolarizationAxisRad = double.NaN; // azimuth of the crystal's slow axis in the lab (NaN = auto)
        public double NominalOmegaMain;
        public bool SelfSteepening = true;
        public bool Paraxial;
        /// <summary>1-D coupling model: "gaussian" (overlap of Gaussian modes, default), "planeWave" (on-axis plane-wave
        /// coupling at the reference radius, as many textbook/pyNLO estimates) or "area" (waveguide: one mode area).</summary>
        public string OverlapModel = "gaussian";
        /// <summary>Internal window half-width as a multiple of the widest input/generated half-bandwidth (high gain and
        /// back-conversion broaden the spectra; the outer 10 % of the window is an absorbing boundary).</summary>
        public double SpectralMargin = 2.5;
        /// <summary>Domain-resolved QPM: grating phase at z = 0 (d ~ sign cos(K z - phase)); 90 deg starts with a full positive domain.</summary>
        public double QpmPhaseRad;
        public double OverlapRadiusM = double.NaN, OverlapAreaM2 = double.NaN;

        public string Kind => "crystal";
        public string Description => string.Format(CultureInfo.InvariantCulture, "{0} {1:G4} mm, {2}{3} type {4}, theta {5:F3} deg, phi {6:F2} deg{7}{8}",
            Crystal.Name, LengthM * 1e3, Dfg ? "dfg" : Process, NoncollinearRad != 0 ? " (non-collinear " + (NoncollinearRad * 180 / Math.PI).ToString("F3", CultureInfo.InvariantCulture) + " deg)" : "",
            Type, ThetaRad * 180 / Math.PI, PhiRad * 180 / Math.PI, Qpm ? ", QPM " + (QpmPeriodM * 1e6).ToString("G5", CultureInfo.InvariantCulture) + " um" : "",
            Transverse.IsSpatial ? ", " + Transverse.Model : "");
        public double OutputOmega0 { get; set; } = double.NaN;
        public bool TransverseAware => Transverse.IsSpatial;

        private const double C = PhysicalConstants.SpeedOfLightMPerS;
        private const double Eps0 = 8.8541878128e-12;
        private const double Hbar = 1.054571817e-34;

        /// <summary>Field names of the process: index by wave (1, 2, 3 by frequency).</summary>
        internal string[] WaveNames(double wMain, double wAux)
        {
            switch (Process)
            {
                case "shg": return Roles[0] == Roles[1] ? new[] { "fundamental", "fundamental", "sh" } : new[] { "fundamental_" + Roles[0], "fundamental_" + Roles[1], "sh" };
                case "sfg": return wMain <= wAux ? new[] { "a", "b", "sum" } : new[] { "b", "a", "sum" };
                default: return null; // opa: decided from frequencies
            }
        }

        internal ThreeWave Geometry(int referenceWave) => new ThreeWave
        {
            Crystal = Crystal, Process = Process, Roles = Roles, ThetaRad = ThetaRad, PhiRad = PhiRad,
            TuningAngle = Plane == "XY" ? "phi" : "theta", NoncollinearRad = NoncollinearRad,
            QpmK = 0, ReferenceWave = referenceWave
        };

        public PulseField Execute(ActiveContext ctx, PulseField p) => Run(ctx, p, double.NaN, out _);

        /// <summary>Run the crystal; <paramref name="delayOverride"/> replaces the aux beams' delay (delay scans).</summary>
        public PulseField Run(ActiveContext ctx, PulseField p, double delayOverride, out Dictionary<string, PulseField> fieldsOut)
        {
            var notes = ctx.Record.Notes;
            var g0 = p.Grid; int n0 = g0.N;
            double wMain = g0.OmegaCenter;
            bool spatial = Transverse.IsSpatial;
            // ---------------------------------------------------------------- field carriers and names
            double wAux = Aux.Count > 0 ? Aux[0].CenterOmega() : double.NaN;
            string mainName; int mainWave; double w1, w2; string[] names; int refWave;
            switch (Process)
            {
                case "shg":
                    w1 = w2 = wMain; names = WaveNames(wMain, 0); mainName = "fundamental"; mainWave = 0; refWave = 1;
                    break;
                case "sfg":
                    if (Aux.Count == 0) throw new InvalidOperationException("sfg needs an auxiliary beam (role \"b\")");
                    if (wMain <= wAux) { w1 = wMain; w2 = wAux; refWave = 1; } else { w1 = wAux; w2 = wMain; refWave = 2; }
                    names = WaveNames(wMain, wAux); mainName = "a"; mainWave = refWave - 1;
                    break;
                default:
                {
                    // opa / dfg: main is signal, idler or pump; aux beams supply the others
                    double ws, wi, wp;
                    string mr = MainRole ?? (Dfg ? "pump" : "signal");
                    var auxBy = Aux.ToDictionary(a => a.Role, a => a.CenterOmega());
                    if (mr == "signal") { ws = wMain; wp = auxBy.TryGetValue("pump", out var x) ? x : throw new InvalidOperationException("opa with the signal as main beam needs an aux beam with role \"pump\""); wi = wp - ws; }
                    else if (mr == "idler") { wi = wMain; wp = auxBy.TryGetValue("pump", out var x) ? x : throw new InvalidOperationException("opa with the idler as main beam needs an aux \"pump\""); ws = wp - wi; }
                    else if (mr == "pump") { wp = wMain; ws = auxBy.TryGetValue("signal", out var x) ? x : auxBy.TryGetValue("idler", out var y) ? wp - y : throw new InvalidOperationException("dfg with the pump as main beam needs an aux \"signal\" (or \"idler\")"); wi = wp - ws; }
                    else throw new InvalidOperationException("mainRole must be signal, idler or pump");
                    if (wi <= 0 || ws <= 0) throw new InvalidOperationException("pump frequency must exceed the signal frequency");
                    if (ws <= wi) { w1 = ws; w2 = wi; names = new[] { "signal", "idler", "pump" }; refWave = 1; } else { w1 = wi; w2 = ws; names = new[] { "idler", "signal", "pump" }; refWave = 2; }
                    mainName = mr; mainWave = Array.IndexOf(names, mr);
                    break;
                }
            }
            // ---------------------------------------------------------------- internal grid
            var tw = Geometry(refWave);
            var geo = tw.At(w1, w2);
            double vRefInv = tw.GroupIndex(mainWave, w1, w2) / C;
            var (gLo, gHi) = GridPlanner.SpectralExtent(p);
            double hMain = Math.Max(wMain - gLo, gHi - wMain);
            double hAux = 0, extAux = 0, delayAux = 0;
            foreach (var a in Aux)
            {
                double tl = a.DurationEstimateS();
                hAux = Math.Max(hAux, 2 * 2 * Math.PI * 0.441 / Math.Max(tl, 1e-16));
                extAux = Math.Max(extAux, a.ExtentEstimateS());
                if (!a.DelayAuto) delayAux = Math.Max(delayAux, Math.Abs(double.IsNaN(delayOverride) ? a.DelayS : delayOverride));
            }
            double hGen = Process == "shg" ? 2 * hMain : hMain + hAux;
            double hMax = Math.Max(hMain, Math.Max(hAux, hGen)) * SpectralMargin;
            double dtNeed = Math.PI / (1.1 * hMax);
            var mainPw = PulseMetrics.TemporalPower(p); var (tlo, thi) = PulseMetrics.SupportRange(mainPw, 1e-5);
            double extMain = (thi - tlo) * g0.DTime;
            double gvmSpread = 0;
            for (int j = 0; j < 3; j++) gvmSpread = Math.Max(gvmSpread, Math.Abs(tw.GroupIndex(j, w1, w2) / C - vRefInv) * LengthM);
            double needW = Math.Max(n0 * g0.DTime, 2.5 * (Math.Max(extMain, extAux) / 2 + delayAux + gvmSpread));
            double W0 = n0 * g0.DTime;
            int aPow = Math.Max(0, (int)Math.Ceiling(Math.Log(needW / W0, 2) - 1e-9));
            int bPow = (int)Math.Floor(Math.Log(dtNeed / g0.DTime, 2) + 1e-9);
            bPow = Math.Min(bPow, 0); // never coarser than the incoming grid
            int maxN = ctx.Run.Run.Grid.MaxN;
            while (W0 * Math.Pow(2, aPow) / (g0.DTime * Math.Pow(2, bPow)) > Math.Min(maxN, spatial ? (1 << 16) : maxN)) bPow++;
            var gMain = GridResampler.Derive(g0, aPow, bPow, wMain);
            int n = gMain.N; double dwInt = gMain.DOmega;
            FrequencyGrid GridAt(double w) => FrequencyGrid.Create(n, gMain.DTime, Math.Round(w / dwInt) * dwInt);
            // carriers snapped so that w3 = w1 + w2 exactly
            double s1, s2, s3;
            if (Process == "shg") { s1 = s2 = gMain.OmegaCenter; s3 = 2 * s1; }
            else
            {
                double sm = gMain.OmegaCenter;
                double[] ws = { w1, w2, w1 + w2 };
                var snapped = ws.Select(x => Math.Round(x / dwInt) * dwInt).ToArray();
                snapped[mainWave] = sm;
                // re-derive the generated one exactly
                var auxNames = new HashSet<string>(Aux.Select(a => a.Role == "partner" ? "b" : a.Role));
                int gen = Enumerable.Range(0, 3).First(j => j != mainWave && !auxNames.Contains(names[j]));
                if (gen == 2) snapped[2] = snapped[0] + snapped[1]; else snapped[gen] = snapped[2] - snapped[1 - gen];
                s1 = snapped[0]; s2 = snapped[1]; s3 = snapped[2];
            }
            // ---------------------------------------------------------------- the fields
            var pM = GridResampler.Resample(p, gMain, out double lostIn);
            if (lostIn > 1e-6) notes.Add(string.Format(CultureInfo.InvariantCulture, "crystal grid: {0:P3} of the input energy fell outside the internal grid", lostIn));
            int K = Process == "shg" && Roles[0] == Roles[1] ? 2 : 3;
            var fields = new Chi2Field[K];
            double[] wc = { s1, s2, s3 };
            int[] waveOf = K == 2 ? new[] { 0, 2 } : new[] { 0, 1, 2 };
            // beam radii (1-D overlaps; spatial grid sizing)
            double wMainBeam = Beam != null ? 0.5 * (Beam.RadiusX(LengthM / 2) + Beam.RadiusY(LengthM / 2)) : double.NaN;
            if (p.Profile != null && double.IsNaN(wMainBeam)) { var (a, b) = p.Profile.SecondMomentRadius(); wMainBeam = Math.Sqrt(a * b); }
            if (double.IsNaN(wMainBeam) && !spatial && OverlapModel == "planewave" && !double.IsNaN(OverlapRadiusM)) wMainBeam = OverlapRadiusM;
            if (double.IsNaN(wMainBeam) && !spatial && OverlapModel == "area") wMainBeam = Math.Sqrt(2 * OverlapAreaM2 / Math.PI);
            if (double.IsNaN(wMainBeam)) throw new InvalidOperationException("crystal '" + Id + "' needs a \"beam\" for the main beam (or a carried transverse profile, or an overlap with a radius/area)");
            for (int k = 0; k < K; k++)
            {
                int wv = waveOf[k];
                var f = new Chi2Field { Name = names[wv], Role = Roles[wv], Omega0 = wc[wv], Grid = Math.Abs(wc[wv] - gMain.OmegaCenter) < 1e-9 * wc[wv] ? gMain : GridAt(wc[wv]) };
                f.AngleRad = geo.AngleToRefRad[wv];
                f.N0 = geo.N[wv]; f.Ng = tw.GroupIndex(wv, w1, w2);
                f.Kz = new double[n];
                var dir = geo.Directions[wv];
                bool isIdlerNc = Process == "opa" && NoncollinearRad != 0 && wv != 2 && wv != refWave - 1;
                double kpT = Process == "opa" && NoncollinearRad != 0 ? geo.N[2] * wc[2] / C * Math.Sin(NoncollinearRad) : 0;
                for (int i = 0; i < n; i++)
                {
                    double w = f.Grid.OmegaAbsolute[i];
                    if (w <= 0) { f.Kz[i] = double.NaN; continue; }
                    double nn = Crystal.Mode(w, dir, f.Role).N, kk = nn * w / C;
                    if (isIdlerNc) { double q = kk * kk - kpT * kpT; f.Kz[i] = q > 0 ? Math.Sqrt(q) : double.NaN; }
                    else f.Kz[i] = kk * Math.Cos(f.AngleRad);
                }
                if (Crystal.Absorption != null) { f.Alpha = new double[n]; Crystal.Absorption.Evaluate(f.Grid.OmegaAbsolute, f.Alpha); }
                fields[k] = f;
            }
            // which fields come from where
            var auxFor = new Dictionary<int, AuxBeamSpec>();
            foreach (var a in Aux)
            {
                string role = a.Role == "partner" ? "b" : a.Role;
                int idx = Array.FindIndex(fields, f => f.Name == role);
                if (idx < 0) throw new InvalidOperationException("aux beam role \"" + a.Role + "\" is not a field of this " + Process + " (fields: " + string.Join(", ", fields.Select(f => f.Name)) + ")");
                auxFor[idx] = a;
            }
            int mainIdx = K == 2 ? 0 : Array.FindIndex(fields, f => f.Name == mainName || (Process == "shg" && f.Name.StartsWith("fundamental")));
            // radii
            foreach (var kv in auxFor) { double ae = kv.Value.EffectiveAreaM2(); fields[kv.Key].RadiusM = Math.Sqrt(2 * ae / Math.PI); }
            if (Process == "shg")
            {
                for (int k = 0; k < K - 1; k++) fields[k].RadiusM = wMainBeam;
                fields[K - 1].RadiusM = wMainBeam / Math.Sqrt(2);
            }
            else
            {
                fields[mainIdx].RadiusM = wMainBeam;
                for (int k = 0; k < K; k++)
                    if (fields[k].RadiusM == 0)
                    {
                        double s = 0; for (int q = 0; q < K; q++) if (q != k && fields[q].RadiusM > 0) s += 1 / (fields[q].RadiusM * fields[q].RadiusM);
                        fields[k].RadiusM = 1 / Math.Sqrt(s);
                        fields[k].Generated = true;
                    }
            }
            if (Process == "shg") fields[K - 1].Generated = true;
            // ---------------------------------------------------------------- coupling constants
            double deff = !double.IsNaN(DeffOverride) ? DeffOverride : tw.Deff(geo);
            if (Qpm && QpmModel == "averaged") deff *= 2 / Math.PI;
            double gamma = K == 2
                ? deff * Math.Sqrt(2 / (Eps0 * C * C * C * geo.N[0] * geo.N[0] * geo.N[2]))
                : deff * Math.Sqrt(2 / (Eps0 * C * C * C * geo.N[0] * geo.N[1] * geo.N[2]));
            double overlap;
            if (spatial) overlap = 1.0;
            else if (OverlapModel == "area")
            {
                overlap = 1 / Math.Sqrt(OverlapAreaM2);
                double wEq = Math.Sqrt(2 * OverlapAreaM2 / Math.PI);
                foreach (var f in fields) f.RadiusM = wEq;
                notes.Add(string.Format(CultureInfo.InvariantCulture, "coupling: waveguide mode area {0:G4} um^2 for all fields", OverlapAreaM2 * 1e12));
            }
            else if (OverlapModel == "planewave")
            {
                double wref = OverlapRadiusM;
                if (double.IsNaN(wref))
                {
                    var inputs = fields.Where(f => !f.Generated).ToList();
                    var top = inputs.OrderByDescending(f => f.Omega0).First();
                    wref = top.RadiusM;
                }
                overlap = Math.Sqrt(2 / (Math.PI * wref * wref));
                foreach (var f in fields) f.RadiusM = wref;
                notes.Add(string.Format(CultureInfo.InvariantCulture, "coupling: plane wave at the on-axis intensity of a {0:G4} um Gaussian (all fields)", wref * 1e6));
            }
            else
            {
                double w1r = fields[0].RadiusM, w2r = K == 2 ? fields[0].RadiusM : fields[1].RadiusM, w3r = fields[K - 1].RadiusM;
                overlap = Math.Pow(2 / Math.PI, 1.5) / (w1r * w2r * w3r) * Math.PI / (1 / (w1r * w1r) + 1 / (w2r * w2r) + 1 / (w3r * w3r));
            }
            double dk0 = fields.Length == 2
                ? 2 * fields[0].Kz[fields[0].Grid.CarrierIndex] - fields[1].Kz[fields[1].Grid.CarrierIndex]
                : fields[0].Kz[fields[0].Grid.CarrierIndex] + fields[1].Kz[fields[1].Grid.CarrierIndex] - fields[2].Kz[fields[2].Grid.CarrierIndex];
            Func<double, double> qpmPhase = null, domainSign = null; double maxStep = double.PositiveInfinity; double[] walls = null;
            if (Qpm)
            {
                double period = double.IsNaN(QpmPeriodM) ? 2 * Math.PI / Math.Abs(dk0) : QpmPeriodM;
                double sgn = dk0 >= 0 ? 1.0 : -1.0;
                Func<double, double> phase;
                if (QpmPeriodOfZ != null)
                {
                    int nz = 4096; var tab = new double[nz + 1]; double dz = LengthM / nz;
                    for (int q = 1; q <= nz; q++) tab[q] = tab[q - 1] + 0.5 * dz * (2 * Math.PI / QpmPeriodOfZ((q - 1) * dz) + 2 * Math.PI / QpmPeriodOfZ(q * dz));
                    phase = z => { double x = Math.Max(0, Math.Min(nz, z / dz)); int q = Math.Min(nz - 1, (int)x); double t = x - q; return sgn * (tab[q] * (1 - t) + tab[q + 1] * t); };
                    double pmin = double.PositiveInfinity; for (int q = 0; q <= 64; q++) pmin = Math.Min(pmin, QpmPeriodOfZ(LengthM * q / 64));
                    period = pmin;
                }
                else phase = z => sgn * 2 * Math.PI / period * z;
                if (QpmModel == "domains")
                {
                    // walls where cos(Phi(z) - phi0) changes sign: Phi = phi0 + pi/2 + m pi (phi0 = grating phase at z = 0)
                    var wl = new List<double>();
                    double phEnd = phase(LengthM) * sgn, ph0 = QpmPhaseRad;
                    for (int m = (int)Math.Ceiling((-ph0 - Math.PI / 2) / Math.PI); ph0 + Math.PI / 2 + m * Math.PI < phEnd; m++)
                    {
                        double target = ph0 + Math.PI / 2 + m * Math.PI, lo = 0, hi = LengthM;
                        if (target <= 0) continue;
                        for (int it = 0; it < 80 && hi - lo > 1e-15 * LengthM; it++) { double mid = 0.5 * (lo + hi); if (phase(mid) * sgn < target) lo = mid; else hi = mid; }
                        wl.Add(0.5 * (lo + hi));
                        if (wl.Count > 2000000) throw new InvalidOperationException("crystal '" + Id + "': too many QPM domains for the domain-resolved model (use \"averaged\")");
                    }
                    walls = wl.ToArray();
                    var wallsLocal = walls;
                    double first = wallsLocal.Length > 0 ? Math.Min(wallsLocal[0], LengthM) : LengthM;
                    double s0 = Math.Cos(phase(0.5 * first) * sgn - QpmPhaseRad) >= 0 ? 1.0 : -1.0;
                    domainSign = z => { int i = Array.BinarySearch(wallsLocal, z); i = i >= 0 ? i + 1 : ~i; return (i & 1) == 0 ? s0 : -s0; };
                    maxStep = period / 4;
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "QPM: domain-resolved, {0} domain walls (the integrator lands on every wall)", walls.Length));
                }
                else qpmPhase = phase;
                ctx.Record.Values["qpmPeriodM"] = period;
            }
            ctx.Record.Values["deffPmPerV"] = deff * 1e12;
            ctx.Record.Values["deltaK0PerM"] = dk0;
            ctx.Record.Values["thetaDeg"] = ThetaRad * 180 / Math.PI;
            ctx.Record.Values["phiDeg"] = PhiRad * 180 / Math.PI;
            if (NoncollinearRad != 0) ctx.Record.Values["noncollinearDeg"] = NoncollinearRad * 180 / Math.PI;
            // ---------------------------------------------------------------- transverse setup
            ITransverseBasis basis = null; TransverseProfile mainProfile = null;
            if (spatial)
            {
                double wmax = wMainBeam;
                foreach (var a in Aux) wmax = Math.Max(wmax, Math.Max(a.RadiusXM, a.RadiusYM) * (a.Beam.GetString("kind", "gaussian").ToLowerInvariant() == "gaussian" ? 1 : 1.2));
                basis = Transverse.CreateBasis(wmax);
                Func<double, double> idxMain = w => Crystal.Mode(w, geo.Directions[waveOf[mainIdx]], fields[mainIdx].Role).N;
                mainProfile = TransverseStart.Initial(Transverse, basis, pM, Beam, idxMain, Id, notes);
                basis = mainProfile.Basis;
                // walk-off / lateral drift (cartesian only)
                for (int k = 0; k < K; k++)
                {
                    var mode = geo.Modes[waveOf[k]];
                    // lateral drift along x: non-collinear tilt + Poynting walk-off (zero for ordinary waves, E || D)
                    double drift = Math.Tan(fields[k].AngleRad) + mode.WalkOffRad;
                    fields[k].KxDrift = basis is CartesianBasis ? drift : 0;
                    if (basis is RadialBasis && mode.WalkOffRad > 1e-3)
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "field {0}: walk-off {1:G3} mrad ignored by the radial model (walk-off length w/rho = {2:G3} mm vs crystal {3:G3} mm) -- use transverse.model = cartesian", fields[k].Name, mode.WalkOffRad * 1e3, fields[k].RadiusM / mode.WalkOffRad * 1e3, LengthM * 1e3));
                }
            }
            // ---------------------------------------------------------------- initial spectra
            int M = basis?.M ?? 1;
            Complex[] Spatialize(Complex[] a, FrequencyGrid g, TransverseProfile prof)
            {
                if (basis == null) return a;
                var e = TransverseOps.Expand(a, g.OmegaAbsolute, prof);
                basis.ToK(e, n);
                return e;
            }
            // main beam projected on the crystal's eigen-polarisation(s)
            var jIn = MainJones(pM);
            Complex[] mainA, leftover = null;
            if (double.IsNaN(PolarizationAxisRad))
            {
                mainA = new Complex[n];
                for (int i = 0; i < n; i++) mainA[i] = Complex.Conjugate(jIn.Ex) * pM.Ax[i] + Complex.Conjugate(jIn.Ey) * pM.Ay[i];
            }
            else
            {
                double ca = Math.Cos(PolarizationAxisRad), sa = Math.Sin(PolarizationAxisRad);
                bool mainSlow = fields[mainIdx].Role == "slow";
                var sl = new Complex[n]; var fa = new Complex[n];
                for (int i = 0; i < n; i++) { sl[i] = ca * pM.Ax[i] + sa * pM.Ay[i]; fa[i] = -sa * pM.Ax[i] + ca * pM.Ay[i]; }
                mainA = mainSlow ? sl : fa; leftover = mainSlow ? fa : sl;
            }
            if (Process == "shg" && K == 3)
            {
                // type II: the main beam feeds both fundamental fields (slow and fast components)
                Complex[] a0, a1;
                if (double.IsNaN(PolarizationAxisRad)) { a0 = mainA.Select(z => z / Math.Sqrt(2)).ToArray(); a1 = (Complex[])a0.Clone(); notes.Add("type-II SHG with automatic crystal azimuth: the main beam is split equally between the slow and fast axes (45 deg)"); }
                else { double ca = Math.Cos(PolarizationAxisRad), sa = Math.Sin(PolarizationAxisRad); a0 = Enumerable.Range(0, n).Select(i => ca * pM.Ax[i] + sa * pM.Ay[i]).ToArray(); a1 = Enumerable.Range(0, n).Select(i => -sa * pM.Ax[i] + ca * pM.Ay[i]).ToArray(); if (Roles[0] != "slow") { var t = a0; a0 = a1; a1 = t; } leftover = null; }
                fields[0].Initial = Spatialize(a0, gMain, mainProfile); fields[1].Initial = Spatialize(a1, gMain, mainProfile);
            }
            else fields[mainIdx].Initial = Spatialize(mainA, gMain, mainProfile);
            double mainCentroid = PulseMetrics.Moments(gMain.Time, PulseMetrics.TemporalPower(pM)).Mean;
            var auxEnergyIn = new Dictionary<string, double>();
            foreach (var kv in auxFor)
            {
                var f = fields[kv.Key]; var spec = kv.Value;
                var keepDelay = spec.DelayS; var keepAuto = spec.DelayAuto;
                if (!double.IsNaN(delayOverride)) { spec.DelayS = delayOverride; spec.DelayAuto = false; }
                var ap = spec.Build(f.Grid, mainCentroid, notes);
                spec.DelayS = keepDelay; spec.DelayAuto = keepAuto;
                var a = new Complex[n]; for (int i = 0; i < n; i++) a[i] = ap.Ax[i] + ap.Ay[i];
                auxEnergyIn[f.Name] = ap.TotalEnergyJ();
                ctx.Record.Aux[f.Name] = spec.Describe();
                TransverseProfile prof = null;
                if (basis != null)
                {
                    double R = spec.CurvatureRadiusM;
                    var bins = TransverseOps.SampleBins(ap.SpectralIntensity());
                    var mode = geo.Modes[waveOf[kv.Key]];
                    prof = TransverseOps.FromShape(basis, spec.Beam, spec.RadiusXM, spec.RadiusYM, R, w => mode.N, bins.Select(b => f.Grid.OmegaAbsolute[b]).ToArray(), Id + "#" + f.Name);
                }
                f.Initial = Spatialize(a, f.Grid, prof);
            }
            var rng = new Random(NoiseSeed);
            for (int k = 0; k < K; k++)
            {
                if (fields[k].Initial != null) continue;
                var init = new Complex[M * n];
                if (SeedNoise)
                {
                    var g = fields[k].Grid;
                    for (int m = 0; m < M; m++)
                        for (int i = 0; i < n; i++)
                        {
                            double w = g.OmegaAbsolute[i]; if (w <= 0) continue;
                            double area = basis == null ? 1.0 : basis.Weights[m];
                            double amp = Math.Sqrt(Hbar * w * n / (g.DTime * area));
                            init[m * n + i] = Complex.FromPolarCoordinates(amp, 2 * Math.PI * rng.NextDouble());
                        }
                    if (basis != null) basis.ToK(init, n);
                    fields[k].Generated = true;
                }
                fields[k].Initial = init;
            }
            if (SeedNoise) notes.Add("quantum-noise seeding: one photon per mode in the generated field(s) (parametric superfluorescence)");
            // ---------------------------------------------------------------- propagate
            var state = new Complex[K * M * n];
            for (int k = 0; k < K; k++) Array.Copy(fields[k].Initial, 0, state, k * M * n, M * n);
            double eTot0 = 0; for (int k = 0; k < K; k++) eTot0 += FieldEnergy(fields[k].Initial, fields[k].Grid, basis);
            var prob = new Chi2Problem(fields, K == 2, gamma, overlap, dk0, LengthM, basis, qpmPhase, domainSign, Chi3 ? N2 : 0, SelfSteepening, vRefInv, Paraxial, maxStep) { Walls = walls };
            var st = ctx.Run.Run.Stepper;
            var settings = new StepperSettings
            {
                Method = st.Method, Tolerance = st.Tolerance, MaxNonlinearPhasePerStepRad = st.MaxNonlinearPhasePerStepRad, MaxAreaChangePerStep = st.MaxAreaChangePerStep,
                MinStepM = st.MinStepM, MaxSteps = st.MaxSteps, FixedSteps = st.FixedSteps,
                MaxStepM = Math.Min(st.MaxStepM, Math.Min(maxStep, LengthM / 20))
            };
            var stats = new StepStatistics();
            var outS = LengthM > 0 ? Steppers.Propagate(prob, state, settings, stats, ctx.Progress) : state;
            ctx.Record.Steps = stats; ctx.Record.BIntegralRad = stats.BIntegralRad;
            stats.Representation = (basis == null ? "1-D coupled waves" : basis.Describe()) + string.Format(CultureInfo.InvariantCulture, ", {0} fields x {1} samples", K, n);
            ctx.Record.Representation = stats.Representation;
            foreach (var s in stats.Notes) notes.Add(s);
            // ---------------------------------------------------------------- results per field
            var fo = new Dictionary<string, PulseField>(StringComparer.OrdinalIgnoreCase);
            fieldsOut = fo;
            var outJonesOf = new Dictionary<string, JonesVector>();
            double eTot1 = 0;
            for (int k = 0; k < K; k++)
            {
                var f = fields[k];
                var a = new Complex[M * n]; Array.Copy(outS, k * M * n, a, 0, M * n);
                eTot1 += FieldEnergy(a, f.Grid, basis);
                TransverseProfile prof = null;
                if (basis != null) { basis.ToSpace(a, n); a = TransverseOps.Collapse(a, basis, f.Grid.OmegaAbsolute, Id, out prof); if (k != mainIdx) prof.Origin = Id; }
                var jv = OutputJones(f.Role, fields[mainIdx].Role, jIn);
                var ax = a.Select(z => z * jv.Ex).ToArray(); var ay = a.Select(z => z * jv.Ey).ToArray();
                fo[f.Name] = new PulseField(f.Grid, ax, ay, p.RepRateHz) { Profile = prof };
            }
            // the leftover (orthogonal) part of the main beam: linear propagation with its own index
            string outName = (Output ?? DefaultOutput()).ToLowerInvariant();
            if (Process == "shg" && (outName == "fundamental") && K == 3) outName = fields[0].Name;
            if (!fo.ContainsKey(outName)) throw new InvalidOperationException("crystal '" + Id + "': output \"" + outName + "\" is not a field (" + string.Join(", ", fo.Keys) + ")");
            var outP = fo[outName];
            if (leftover != null && leftover.Any(z => z != Complex.Zero))
            {
                string otherRole = fields[mainIdx].Role == "slow" ? "fast" : "slow";
                var dir = geo.Directions[waveOf[mainIdx]];
                var lf = new Complex[n];
                double kc = Crystal.Mode(gMain.OmegaCenter, dir, otherRole).N * gMain.OmegaCenter / C;
                for (int i = 0; i < n; i++)
                {
                    double w = gMain.OmegaAbsolute[i]; if (w <= 0) continue;
                    double k = Crystal.Mode(w, dir, otherRole).N * w / C;
                    lf[i] = leftover[i] * Complex.FromPolarCoordinates(1, (k - kc - (w - gMain.OmegaCenter) * vRefInv) * LengthM);
                }
                double ca = Math.Cos(PolarizationAxisRad), sa = Math.Sin(PolarizationAxisRad);
                var axisOther = otherRole == "slow" ? new JonesVector(ca, sa) : new JonesVector(-sa, ca);
                var leftP = new PulseField(gMain, lf.Select(z => z * axisOther.Ex).ToArray(), lf.Select(z => z * axisOther.Ey).ToArray(), p.RepRateHz);
                if (ReferenceEquals(outP.Grid, gMain))
                    outP = new PulseField(gMain, outP.Ax.Zip(leftP.Ax, (x, y) => x + y).ToArray(), outP.Ay.Zip(leftP.Ay, (x, y) => x + y).ToArray(), p.RepRateHz) { Profile = outP.Profile };
                else fo[fields[mainIdx].Name + "_unconverted"] = leftP;
                notes.Add("the main-beam component on the crystal's other axis was propagated linearly (no interaction)");
            }
            // ---------------------------------------------------------------- energy budget
            double eIn = p.TotalEnergyJ();
            double eOut = outP.TotalEnergyJ();
            foreach (var kv in auxEnergyIn) ctx.Energy("from " + kv.Key + " (aux)", -kv.Value);
            foreach (var kv in fo) if (kv.Key != outName) ctx.Energy("to " + kv.Key, kv.Value.TotalEnergyJ());
            double resid = eIn - eOut + auxEnergyIn.Values.Sum() - fo.Where(kv => kv.Key != outName).Sum(kv => kv.Value.TotalEnergyJ());
            ctx.Energy(Crystal.Absorption != null ? "absorption" : "numerical", resid);
            double totIn = eIn + auxEnergyIn.Values.Sum();
            if (Crystal.Absorption == null && resid > 1e-3 * totIn)
            {
                string msg = string.Format(CultureInfo.InvariantCulture, "crystal '{0}': {1:P2} of the energy reached the edge of the internal spectral window and was absorbed -- raise \"spectralMargin\" (now {2:G3})", Id, resid / totIn, SpectralMargin);
                notes.Add(msg);
                if (!ctx.Result.Warnings.Contains(msg)) ctx.Result.Warnings.Add(msg);
            }
            ctx.Record.Values["conversionEfficiency"] = eOut / Math.Max(1e-300, eIn + auxEnergyIn.Values.Sum());
            foreach (var kv in fo) ctx.Record.Values["energy_" + kv.Key + "_J"] = kv.Value.TotalEnergyJ();
            // photon bookkeeping (Manley-Rowe check), lossless case
            if (Crystal.Absorption == null && !SeedNoise)
            {
                // spectrally resolved photon numbers: sum |A(w)|^2 / (hbar w)
                double Ph(string nm, bool start)
                {
                    int k = Array.FindIndex(fields, f => f.Name == nm); if (k < 0) return 0; var f = fields[k];
                    if (start) return PhotonCount(f.Initial, f.Grid, basis);
                    var po = fo[nm]; return PhotonCount(po.Ax, po.Grid, null) + PhotonCount(po.Ay, po.Grid, null);
                }
                if (K == 3) ctx.Record.Values["manleyRoweResidual"] = ((Ph(fields[0].Name, false) - Ph(fields[0].Name, true)) - (Ph(fields[1].Name, false) - Ph(fields[1].Name, true))) / Math.Max(1e-300, Ph(fields[2].Name, true) + Ph(fields[0].Name, true) + Ph(fields[1].Name, true));
            }
            // ---------------------------------------------------------------- by-products and output grid
            var rec = Record ?? new HashSet<string>(fo.Keys.Where(k => k != outName), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fo) if (kv.Key != outName && rec.Contains(kv.Key)) ctx.Byproduct(kv.Key, kv.Value);
            // own dispersion of the output field through the crystal (frame group delay = L / v_ref)
            {
                int ko = Array.FindIndex(fields, f => f.Name == outName); if (ko < 0) ko = mainIdx;
                var f = fields[ko]; var gq = f.Grid; int c = gq.CarrierIndex;
                double dwq = gq.DOmega, k1 = (f.Kz[c + 1] - f.Kz[c - 1]) / (2 * dwq), k2 = (f.Kz[c + 1] - 2 * f.Kz[c] + f.Kz[c - 1]) / (dwq * dwq);
                double k3 = (f.Kz[c + 2] - 2 * f.Kz[c + 1] + 2 * f.Kz[c - 1] - f.Kz[c - 2]) / (2 * dwq * dwq * dwq);
                ctx.Own = new DispersionPoint { GroupDelayS = vRefInv * LengthM, GddS2 = k2 * LengthM, TodS3 = k3 * LengthM };
            }
            var downstream = ctx.DownstreamGrid?.Invoke(outP) ?? outP.Grid;
            var final = GridResampler.Resample(outP, downstream, out double lostOut);
            if (lostOut > 1e-4) notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:P3} of the output field's energy fell outside the downstream grid", lostOut));
            final.Profile = outP.Profile;
            return final;
        }

        private string DefaultOutput()
        {
            switch (Process)
            {
                case "shg": return "sh";
                case "sfg": return "sum";
                default: return Dfg ? "idler" : (MainRole ?? "signal");
            }
        }

        private static double PhotonCount(Complex[] a, FrequencyGrid g, ITransverseBasis basis)
        {
            int n = g.N; double s = 0;
            Complex[] b = a; if (basis != null) { b = (Complex[])a.Clone(); basis.ToSpace(b, n); }
            int M = basis?.M ?? 1;
            for (int m = 0; m < M; m++)
            {
                double wt = basis?.Weights[m] ?? 1.0;
                for (int i = 0; i < n; i++) { double w = g.OmegaAbsolute[i]; if (w <= 0) continue; var z = b[m * n + i]; s += wt * (z.Real * z.Real + z.Imaginary * z.Imaginary) / (Hbar * w); }
            }
            return s / n * g.DTime;
        }

        private static double FieldEnergy(Complex[] a, FrequencyGrid g, ITransverseBasis basis)
        {
            int n = g.N; double s = 0;
            if (basis == null) { for (int i = 0; i < n; i++) s += a[i].Real * a[i].Real + a[i].Imaginary * a[i].Imaginary; return s / n * g.DTime; }
            var b = (Complex[])a.Clone(); basis.ToSpace(b, n);
            for (int m = 0; m < basis.M; m++) { double w = basis.Weights[m]; for (int i = 0; i < n; i++) { var z = b[m * n + i]; s += w * (z.Real * z.Real + z.Imaginary * z.Imaginary); } }
            return s / n * g.DTime;
        }

        /// <summary>Dominant polarisation of the main beam (energy-weighted Jones vector).</summary>
        private static JonesVector MainJones(PulseField p)
        {
            double ex = 0, ey = 0; Complex cxy = Complex.Zero;
            for (int i = 0; i < p.Grid.N; i++) { ex += p.Ax[i].Magnitude * p.Ax[i].Magnitude; ey += p.Ay[i].Magnitude * p.Ay[i].Magnitude; cxy += p.Ax[i] * Complex.Conjugate(p.Ay[i]); }
            if (ex + ey == 0) return JonesVector.LinearX;
            // principal eigenvector of the coherency matrix [[ex, cxy],[cxy*, ey]]
            double tr = ex + ey, det = ex * ey - cxy.Magnitude * cxy.Magnitude, l = tr / 2 + Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            Complex vx, vy;
            if (cxy.Magnitude > 1e-30 * tr) { vx = cxy; vy = l - ex; } else if (ex >= ey) { vx = 1; vy = 0; } else { vx = 0; vy = 1; }
            return new JonesVector(vx, vy).Normalized();
        }

        private JonesVector OutputJones(string role, string mainRole, JonesVector jIn)
        {
            if (double.IsNaN(PolarizationAxisRad))
                return role == mainRole ? jIn : new JonesVector(-Complex.Conjugate(jIn.Ey), Complex.Conjugate(jIn.Ex));
            double ca = Math.Cos(PolarizationAxisRad), sa = Math.Sin(PolarizationAxisRad);
            return role == "slow" ? new JonesVector(ca, sa) : new JonesVector(-sa, ca);
        }
    }

    /// <summary>Compiles "type": "crystal" elements.</summary>
    public static class CrystalCompiler
    {
        public static void Compile(CompilerServices c, SystemElement e, CompiledElement ce)
        {
            var j = e.Json;
            double T = c.Num(e, "temperatureC", double.NaN);
            var cv = c.Find(e, "crystal")?.RawValue ?? j["crystal"] ?? throw new JsonException("crystal element needs \"crystal\" (a name like \"BBO\" or a user-crystal object)");
            CrystalModel crystal;
            if (cv.Kind == JsonKind.String)
            {
                string nm = cv.AsString();
                var userDef = c.System.Materials.Has(nm) ? c.System.Materials.GetObject(nm)?.GetObject("crystal") : null;
                crystal = userDef != null ? CrystalLibrary.FromJson(userDef, c.Registry, T, "materials." + nm + ".crystal") : CrystalLibrary.Get(nm, T);
            }
            else crystal = CrystalLibrary.FromJson(cv.AsObject(), c.Registry, T, "element " + e.Id + ".crystal");
            var el = new CrystalElement { Id = e.Id, Crystal = crystal };
            el.LengthM = c.Num(e, "lengthM", double.NaN);
            if (double.IsNaN(el.LengthM) || el.LengthM < 0) throw new JsonException("crystal needs lengthM");
            ce.LengthM = el.LengthM;
            var it = j.GetObject("interaction") ?? new JsonObject();
            string proc = it.GetString("process", j.GetString("process", "shg")).ToLowerInvariant();
            el.Dfg = proc == "dfg"; el.Process = el.Dfg ? "opa" : proc;
            if (el.Process != "shg" && el.Process != "sfg" && el.Process != "opa") throw new JsonException("interaction.process must be shg, sfg, opa or dfg");
            el.Type = it.GetString("type", it.Has("qpm") && !it["qpm"].IsNull && !(it["qpm"].Kind == JsonKind.Bool && !it["qpm"].AsBool()) ? "0" : "I");
            el.Roles = ThreeWave.RolesForType(el.Type);
            el.MainRole = j.GetString("mainRole", null)?.ToLowerInvariant();
            el.Output = j.GetString("output", null);
            if (j.Has("record")) el.Record = new HashSet<string>(j["record"].AsStringArray(), StringComparer.OrdinalIgnoreCase);
            el.DeffOverride = j.Has("deffPmPerV") ? c.Num(e, "deffPmPerV", 0) * 1e-12 : double.NaN;
            if (!double.IsNaN(el.DeffOverride)) crystal.DeffOverrideMPerV = el.DeffOverride;
            el.SeedNoise = j.GetBool("seedNoise", false);
            el.SpectralMargin = c.Num(e, "spectralMargin", 2.5);
            if (!(el.SpectralMargin >= 1.2)) throw new JsonException("crystal '" + e.Id + "': spectralMargin must be >= 1.2");
            el.NoiseSeed = j.GetInt("noiseSeed", 12345);
            el.Chi3 = j.GetBool("chi3", true) && c.Run.Physics.Nonlinear;
            el.N2 = j.Has("n2") ? c.Num(e, "n2", crystal.N2) : crystal.N2;
            el.SelfSteepening = c.Run.Physics.SelfSteepening;
            el.PolarizationAxisRad = j.Has("polarizationAxisDeg") ? c.Num(e, "polarizationAxisDeg", 0) * Math.PI / 180 : double.NaN;
            el.Transverse = TransverseSpec.FromJson(c.Find(e, "transverse")?.RawValue ?? (j.Has("transverse") ? j["transverse"] : c.Run.Physics.Transverse));
            el.Paraxial = el.Transverse.Paraxial;
            if (j.TryGet("overlap", out var ov) && !ov.IsNull)
            {
                if (ov.Kind == JsonKind.String) el.OverlapModel = ov.AsString().ToLowerInvariant();
                else
                {
                    var oo = ov.AsObject();
                    el.OverlapModel = oo.Has("areaM2") ? "area" : oo.GetString("model", "planeWave").ToLowerInvariant();
                    el.OverlapRadiusM = oo.GetDouble("radiusM", double.NaN);
                    el.OverlapAreaM2 = oo.GetDouble("areaM2", double.NaN);
                }
                if (el.OverlapModel != "gaussian" && el.OverlapModel != "planewave" && el.OverlapModel != "area") throw new JsonException("overlap must be \"gaussian\", \"planeWave\" or {\"areaM2\": ...}");
            }
            var auxArr = c.Find(e, "aux")?.RawValue?.AsArray() ?? j.GetArray("aux") ?? new JsonArray();
            foreach (var a in auxArr) el.Aux.Add(AuxBeamSpec.FromJson(a.AsObject(), c.System.BaseDirectory ?? c.Run.BaseDirectory, "element " + e.Id + ".aux"));
            // polarisations
            var pol = it.GetObject("polarizations");
            // beam
            ce.TrackingIndex = CrystalModel.Idx(crystal.Nx, c.Omega0);
            var tracked = j.Has("beam") ? null : c.TrackedBeam(ce.TrackingIndex);
            if (tracked != null) { el.Beam = tracked; ce.Notes.Add("beam: the tracked Gaussian beam (" + tracked.Description + ")"); }
            var bspec = j.Has("beam") ? ModelSpec.FromJson(j["beam"], ModelSource.SystemData, "element " + e.Id + ".beam") : tracked == null && (c.Run.InputBeam ?? c.System.InputBeam) != null ? ModelSpec.FromJson(c.Run.InputBeam ?? c.System.InputBeam, ModelSource.SystemData, "inputBeam") : null;
            if (bspec != null)
            {
                if (bspec.Kind == "gaussian" && !bspec.Json.Has("wavelengthM") && !bspec.Json.Has("zRayleighM")) bspec.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                if (bspec.Kind == "gaussian" && !bspec.Json.Has("n")) bspec.Json.Set("n", CrystalModel.Idx(crystal.Nx, c.Omega0));
                el.Beam = c.Registry.Create<IBeamModel>("beam", bspec);
            }
            // QPM
            if (it.TryGet("qpm", out var qv) && !qv.IsNull)
            {
                el.Qpm = qv.Kind != JsonKind.Bool || qv.AsBool();
                if (qv.Kind == JsonKind.Object)
                {
                    var qo = qv.AsObject();
                    el.QpmPeriodM = qo.GetDouble("periodM", double.NaN);
                    el.QpmModel = qo.GetString("model", "averaged").ToLowerInvariant();
                    if (el.QpmModel != "averaged" && el.QpmModel != "domains") throw new JsonException("qpm.model must be \"averaged\" or \"domains\"");
                    el.QpmPhaseRad = qo.GetDouble("phaseDeg", 0) * Math.PI / 180;
                    if (qo.Has("periodExpr"))
                    {
                        var ex = CompiledExpression.Compile(qo.GetString("periodExpr"), new[] { "z" }, new Dictionary<string, double> { { "L", el.LengthM } });
                        el.QpmPeriodOfZ = z => ex.Evaluate(new Dictionary<string, double[]> { { "z", new[] { z } } }, 1)[0];
                    }
                }
            }
            // nominal wavelengths for the cut
            double wMain = c.Omega0;
            double wAuxN = el.Aux.Count > 0 ? el.Aux[0].CenterOmega() : double.NaN;
            var req = new JsonObject().Set("crystal", "x").Set("process", el.Dfg ? "dfg" : el.Process).Set("type", el.Type).Set("lengthM", el.LengthM);
            var wl = new JsonObject();
            double C0 = PhysicalConstants.SpeedOfLightMPerS;
            string mr = el.MainRole;
            if (mr == null && el.Process == "opa")
            {
                // infer the main beam from the aux roles: aux pump -> main signal (or idler if a signal is also given); otherwise main pump
                bool hasPump = el.Aux.Any(a => a.Role == "pump"), hasSig = el.Aux.Any(a => a.Role == "signal");
                mr = hasPump ? (hasSig ? "idler" : "signal") : "pump";
                el.MainRole = mr;
            }
            mr = mr ?? "signal";
            if (el.Process == "opa")
            {
                bool needPump = mr != "pump";
                if (needPump && !el.Aux.Any(a => a.Role == "pump")) throw new JsonException("crystal '" + e.Id + "': " + (el.Dfg ? "dfg" : "opa") + " with the " + mr + " as main beam needs an aux beam with role \"pump\"");
                if (!needPump && !el.Aux.Any(a => a.Role == "signal" || a.Role == "idler")) throw new JsonException("crystal '" + e.Id + "': with the pump as main beam an aux beam with role \"signal\" (or \"idler\") is needed");
            }
            else if (el.Process == "sfg" && el.Aux.Count == 0) throw new JsonException("crystal '" + e.Id + "': sfg needs an aux beam");
            switch (el.Process)
            {
                case "shg": wl.Set("fundamental", 2 * Math.PI * C0 / wMain); break;
                case "sfg": wl.Set("a", 2 * Math.PI * C0 / wMain).Set("b", 2 * Math.PI * C0 / wAuxN); break;
                default:
                    if (mr == "signal") wl.Set("signal", 2 * Math.PI * C0 / wMain).Set("pump", 2 * Math.PI * C0 / el.Aux.First(a => a.Role == "pump").CenterOmega());
                    else if (mr == "idler") wl.Set("idler", 2 * Math.PI * C0 / wMain).Set("pump", 2 * Math.PI * C0 / el.Aux.First(a => a.Role == "pump").CenterOmega());
                    else { wl.Set("pump", 2 * Math.PI * C0 / wMain); var sAux = el.Aux.FirstOrDefault(a => a.Role == "signal"); if (sAux != null) wl.Set("signal", 2 * Math.PI * C0 / sAux.CenterOmega()); else wl.Set("idler", 2 * Math.PI * C0 / el.Aux.First(a => a.Role == "idler").CenterOmega()); }
                    break;
            }
            req.Set("wavelengthsM", wl);
            if (pol != null) req.Set("polarizations", pol);
            if (el.Qpm) req.Set("qpm", double.IsNaN(el.QpmPeriodM) ? (JsonValue)true : new JsonObject().Set("periodM", el.QpmPeriodM));
            if (it.Has("plane")) req.Set("plane", it.GetString("plane"));
            if (it.TryGet("noncollinearDeg", out var nc)) req.Set("noncollinearDeg", nc);
            var cut = j["cut"];
            bool autoCut = cut == null || (cut.Kind == JsonKind.String && cut.AsString().Equals("auto", StringComparison.OrdinalIgnoreCase));
            if (!autoCut)
            {
                var co = cut.AsObject();
                if (co.Has("thetaDeg")) req.Set("thetaDeg", co.GetDouble("thetaDeg", 90));
                if (co.Has("phiDeg")) req.Set("phiDeg", co.GetDouble("phiDeg", 0));
            }
            else if (el.Qpm) { req.Set("thetaDeg", 90).Set("phiDeg", 0); }
            // solve with the actual crystal object
            var sol = SolveWith(crystal, req, c.Registry);
            var best = sol.GetObject("best");
            if (best == null)
            {
                if (autoCut) throw new JsonException("crystal '" + e.Id + "': cut \"auto\" found no phase matching for " + crystal.Name + " (" + el.Process + " type " + el.Type + ") at the nominal wavelengths");
                // fixed angles that are not phase matched: still fine (just mismatched)
                var co = cut.AsObject();
                el.ThetaRad = co.GetDouble("thetaDeg", 90) * Math.PI / 180; el.PhiRad = co.GetDouble("phiDeg", 0) * Math.PI / 180;
                el.Plane = it.GetString("plane", crystal.IsUniaxial ? "uniaxial" : "XZ");
                el.NoncollinearRad = nc != null && nc.Kind == JsonKind.Number ? nc.AsDouble() * Math.PI / 180 : 0;
            }
            else
            {
                el.ThetaRad = best.GetDouble("thetaDeg", 90) * Math.PI / 180; el.PhiRad = best.GetDouble("phiDeg", 0) * Math.PI / 180;
                el.Plane = best.GetString("plane", "uniaxial");
                el.NoncollinearRad = best.GetDouble("noncollinearDeg", 0) * Math.PI / 180;
                el.Roles = best["roles"].AsStringArray();
                if (el.Qpm && double.IsNaN(el.QpmPeriodM)) el.QpmPeriodM = best.GetDouble("qpmPeriodM", double.NaN);
                if (!autoCut)
                {
                    var co = cut.AsObject();
                    if (co.Has("thetaDeg")) el.ThetaRad = co.GetDouble("thetaDeg", 90) * Math.PI / 180;
                    if (co.Has("phiDeg")) el.PhiRad = co.GetDouble("phiDeg", 0) * Math.PI / 180;
                }
            }
            if (!autoCut && cut.AsObject().Has("detuneDeg")) el.ThetaRad += cut.AsObject().GetDouble("detuneDeg", 0) * Math.PI / 180;
            if (autoCut && j.Has("detuneDeg")) el.ThetaRad += j.GetDouble("detuneDeg", 0) * Math.PI / 180;
            // output carrier (compile-time tracking)
            string outName = (el.Output ?? (el.Process == "shg" ? "sh" : el.Process == "sfg" ? "sum" : el.Dfg ? "idler" : mr)).ToLowerInvariant();
            double wOut = wMain;
            if (el.Process == "shg" && outName == "sh") wOut = 2 * wMain;
            else if (el.Process == "sfg" && outName == "sum") wOut = wMain + wAuxN;
            else if (el.Process == "sfg" && outName == "b") wOut = wAuxN;
            else if (el.Process == "opa")
            {
                double wp = mr == "pump" ? wMain : el.Aux.First(a => a.Role == "pump").CenterOmega();
                double ws = mr == "signal" ? wMain : mr == "idler" ? wp - wMain : (el.Aux.FirstOrDefault(a => a.Role == "signal")?.CenterOmega() ?? wp - el.Aux.First(a => a.Role == "idler").CenterOmega());
                wOut = outName == "pump" ? wp : outName == "signal" ? ws : wp - ws;
            }
            el.OutputOmega0 = Math.Abs(wOut - wMain) > 1e-6 * wMain ? wOut : double.NaN;
            el.NominalOmegaMain = wMain;
            ce.Active = el;
            c.Provenance.Record("element:" + e.Id, "crystal", crystal.Describe() + "; " + crystal.References, ModelSource.MaterialDefault, "crystal library");
            c.Provenance.Record("element:" + e.Id, "cut", string.Format(CultureInfo.InvariantCulture, "theta {0:F4} deg, phi {1:F3} deg{2}, roles {3}", el.ThetaRad * 180 / Math.PI, el.PhiRad * 180 / Math.PI,
                el.NoncollinearRad != 0 ? string.Format(CultureInfo.InvariantCulture, ", non-collinear {0:F3} deg", el.NoncollinearRad * 180 / Math.PI) : "", string.Join("/", el.Roles)),
                autoCut ? ModelSource.EngineDefault : ModelSource.SystemData, autoCut ? "phase matched at the nominal wavelengths (cut \"auto\")" : "system file");
            foreach (var a in el.Aux) c.Provenance.Record("element:" + e.Id, "aux " + a.Role, a.Describe(), ModelSource.SystemData, "system file");
            foreach (var nt in crystal.Notes) ce.Notes.Add(nt);
            if (!double.IsNaN(crystal.ValidMaxUm))
            {
                foreach (var wv in new[] { wMain, wOut, wAuxN }.Where(w => !double.IsNaN(w)))
                {
                    double lu = 2 * Math.PI * C0 / wv * 1e6;
                    if (lu < crystal.ValidMinUm || lu > crystal.ValidMaxUm) ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} um is outside the Sellmeier range {2:G3}-{3:G3} um (extrapolated)", crystal.Name, lu, crystal.ValidMinUm, crystal.ValidMaxUm));
                }
            }
        }

        private static JsonObject SolveWith(CrystalModel crystal, JsonObject req, ModelRegistry reg) => PhaseMatchCalculator.Solve(req, reg, crystal);
    }
}
