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
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Gain
{
    /// <summary>Everything a gain or amplifier element was configured with (docs/reference/gain.md).</summary>
    public sealed class GainSetup
    {
        public string Id;
        public GainMedium Medium;
        public double LengthM;
        public int Slices = 24;
        public PumpSpec Pump = new PumpSpec();
        public double InversionFraction = double.NaN;
        public double SmallSignalGain = double.NaN, SmallSignalGainWavelengthM = double.NaN;
        public IBeamModel Beam;
        public double ModeRadiusM = double.NaN;
        public Material Host;
        public double N2;
        public bool Spm = true;
        public int Segments;                       // 0 = automatic
        public bool ThermalLens = true;
        public bool ApplyThermalLens = true;       // 1-D: apply f_th to a carried transverse profile
        public double RodRadiusM = double.NaN;
        public double HeatFraction = double.NaN;   // NaN = quantum defect 1 - lambda_p / lambda_s
        public bool TrackThermalLens = true;       // apply the thermal lens to the tracked Gaussian beam
        public double ThermalFocalLengthM = double.NaN;      // measured thermal focal length (replaces the heat model)
        public double TrackedThermalFocalLengthM = double.NaN; // the compile-time lens used by the tracking (for the beam)
        public double TrackedOnAxisFocalLengthM = double.NaN;  // the compile-time on-axis f_th (pump alone), to compare with the run
        public bool Stress;                        // thermal stress birefringence (cubic hosts)
        public double RepRateHz = double.NaN;
        public bool SteadyState = true;
        public int MaxPulses = 400;
        public double Tolerance = 1e-5;
        public double SingleShotPumpS = double.NaN; // cw pump time for a single shot (default: to equilibrium)
        public ISpatialField Doping;
        public bool Ase; public int AseSeed = 7;
        public TransverseSpec Transverse;
        public SpatialProperties Properties;

        public string Describe() => Medium.Name + ", " + (LengthM * 1e3).ToString("G4", CultureInfo.InvariantCulture) + " mm, " + Pump.Describe();
    }

    /// <summary>A between-pass / cavity operation: loss, GDD, TOD and material passes (with their n2).</summary>
    public sealed class PassOperation
    {
        public double Loss;                 // fractional energy loss
        public double GddS2, TodS3;
        public List<(Material M, double L)> Materials = new List<(Material, double)>();
        public bool IsIdentity => Loss == 0 && GddS2 == 0 && TodS3 == 0 && Materials.Count == 0;
        private Complex[] _h; private FrequencyGrid _g; private double _kerr;

        public static PassOperation FromJson(JsonObject o, Func<string, Material> mat, string origin)
        {
            var p = new PassOperation();
            if (o == null) return p;
            p.Loss = o.GetDouble("loss", o.GetDouble("lossPerRoundTrip", 0));
            if (!(p.Loss >= 0 && p.Loss < 1)) throw new JsonException(origin + ": loss must be in [0, 1)");
            p.GddS2 = o.GetDouble("gddFs2", 0) * 1e-30 + o.GetDouble("gddS2", 0);
            p.TodS3 = o.GetDouble("todFs3", 0) * 1e-45 + o.GetDouble("todS3", 0);
            foreach (var m in o.GetArray("materials") ?? new JsonArray())
            {
                var mo = m.AsObject();
                p.Materials.Add((mat(mo.GetRequiredString("name", origin + ".materials")), mo.GetRequiredDouble("lengthM", origin + ".materials")));
            }
            return p;
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture; var parts = new List<string>();
            if (Loss > 0) parts.Add((Loss * 100).ToString("G3", ci) + " % loss");
            if (GddS2 != 0) parts.Add((GddS2 * 1e30).ToString("G4", ci) + " fs^2");
            if (TodS3 != 0) parts.Add((TodS3 * 1e45).ToString("G4", ci) + " fs^3");
            foreach (var (m, l) in Materials) parts.Add(m.Name + " " + (l * 1e3).ToString("G4", ci) + " mm");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        /// <summary>Apply to a 1-D pulse (spectra in place); SPM of the materials on the mode area. Returns the B-integral.</summary>
        public double Apply(Complex[] ax, Complex[] ay, FrequencyGrid g, double areaM2)
        {
            if (IsIdentity) return 0;
            if (_g != g)
            {
                _g = g; int n = g.N; _h = new Complex[n];
                var axis = OmegaAxis.FromGrid(g);
                double[] ph = new double[n];
                if (Materials.Count > 0)
                {
                    Executor.Combine(Materials.Select(t => (LinearTerm)new BulkTerm(t.M, t.L)), axis, out ph, out _, out _, out _);
                }
                double a = Math.Sqrt(1 - Loss);
                for (int i = 0; i < n; i++)
                {
                    double W = g.OmegaOffset[i];
                    _h[i] = Complex.FromPolarCoordinates(a, ph[i] + 0.5 * GddS2 * W * W + TodS3 * W * W * W / 6);
                }
                _kerr = Materials.Sum(t => t.M.N2 * t.L) * g.OmegaCenter / PhysicalConstants.SpeedOfLightMPerS;
            }
            for (int i = 0; i < ax.Length; i++) { ax[i] *= _h[i]; if (ay != null) ay[i] *= _h[i]; }
            if (_kerr == 0) return 0;
            var tx = SpectralTransform.FreqToTime(ax); var ty = ay != null ? SpectralTransform.FreqToTime(ay) : null;
            double pk = 0;
            for (int i = 0; i < tx.Length; i++)
            {
                double P = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + (ty != null ? ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary : 0);
                double phi = _kerr * P / areaM2; pk = Math.Max(pk, phi);
                var r = Complex.FromPolarCoordinates(1, phi); tx[i] *= r; if (ty != null) ty[i] *= r;
            }
            Array.Copy(SpectralTransform.TimeToFreq(tx), ax, ax.Length);
            if (ty != null) Array.Copy(SpectralTransform.TimeToFreq(ty), ay, ay.Length);
            return pk;
        }

        public void ApplyEnergy(double[] e) { if (Loss > 0) for (int q = 0; q < e.Length; q++) e[q] *= 1 - Loss; }
    }

    /// <summary>Shared run-time machinery of the gain and amplifier elements.</summary>
    internal static class GainRun
    {
        public static double ModeRadius(GainSetup s, PulseField p, List<string> notes, Optics.BeamState tracked = null)
        {
            if (s.ModeRadiusM > 0) return s.ModeRadiusM;
            if (s.Beam != null) return 0.5 * (s.Beam.RadiusX(s.LengthM / 2) + s.Beam.RadiusY(s.LengthM / 2));
            if (p.Profile != null) { var (a, b) = p.Profile.SecondMomentRadius(); return Math.Sqrt(a * b); }
            if (tracked != null && tracked.RadiusX > 0) { notes.Add("signal mode: the tracked Gaussian beam at the entrance (" + tracked.Describe() + ")"); return Math.Sqrt(tracked.RadiusX * tracked.RadiusY); }
            if (s.Pump.RadiusM > 0) { notes.Add("no beam given: the signal mode radius is taken equal to the pump radius"); return s.Pump.RadiusM; }
            throw new InvalidOperationException("element '" + s.Id + "' needs a \"beam\" (or \"modeRadiusM\") to know the signal mode area");
        }

        /// <summary>Time-ordering segments from the time-bandwidth product (1 for a transform-limited pulse, up to 24 for a stretched one).</summary>
        public static int AutoSegments(PulseField p)
        {
            var g = p.Grid; var tx = p.TemporalAx(); var ty = p.TemporalAy();
            double s0 = 0, s1 = 0, s2 = 0;
            for (int i = 0; i < g.N; i++) { double P = tx[i].Magnitude * tx[i].Magnitude + ty[i].Magnitude * ty[i].Magnitude, t = g.Time[i]; s0 += P; s1 += P * t; s2 += P * t * t; }
            double st = Math.Sqrt(Math.Max(0, s2 / s0 - Math.Pow(s1 / s0, 2)));
            var sp = p.SpectralIntensity(); double w0 = 0, w1 = 0, w2 = 0;
            for (int i = 0; i < g.N; i++) { double W = g.OmegaOffset[i]; w0 += sp[i]; w1 += sp[i] * W; w2 += sp[i] * W * W; }
            double sw = Math.Sqrt(Math.Max(0, w2 / w0 - Math.Pow(w1 / w0, 2)));
            double ratio = st * sw / 0.5;
            if (!(ratio > 1.5)) return 1;
            return Math.Max(2, Math.Min(24, (int)Math.Ceiling(4 * Math.Log10(ratio) + 2)));
        }

        public static void Initialise(GainCrystal gc, GainSetup s, double omegaSignal)
        {
            if (!double.IsNaN(s.InversionFraction)) { gc.SetInversionFraction(s.InversionFraction); return; }
            if (!double.IsNaN(s.SmallSignalGain))
            {
                double w = !double.IsNaN(s.SmallSignalGainWavelengthM) ? GainMedium.Omega(s.SmallSignalGainWavelengthM) : omegaSignal;
                double se = s.Medium.SigmaE(w), sa = s.Medium.SigmaA(w), nt = gc.Nt.Average();
                double beta = (Math.Log(s.SmallSignalGain) / (s.LengthM * nt) + sa) / (se + sa);
                if (!(beta >= 0 && beta <= 1)) throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "element '{0}': smallSignalGain {1:G4} is not reachable (needs an inversion fraction of {2:G3})", s.Id, s.SmallSignalGain, beta));
                gc.SetInversionFraction(beta);
            }
        }

        /// <summary>Pumping between two pulses (period T): cw rate equations, or decay + pump pulse + decay.</summary>
        public static void Between(GainCrystal gc, GainSetup s, double T)
        {
            if (s.Pump.Kind == "cw") gc.PumpCw(T, Math.Max(24, Math.Min(400, (int)Math.Ceiling(8 * T / s.Medium.LifetimeS) + 24)));
            else if (s.Pump.Kind == "pulsed") { double d = Math.Min(T, Math.Max(0, s.Pump.DelayS)); gc.Decay(T - d); gc.PumpPulse(); gc.Decay(d); }
            else gc.Decay(T);
        }

        /// <summary>Single shot: cw pump to equilibrium (or for singleShotPumpS), pulsed pump then decay.</summary>
        public static double SingleShot(GainCrystal gc, GainSetup s)
        {
            if (s.Pump.Kind == "cw")
            {
                double t = s.SingleShotPumpS > 0 ? s.SingleShotPumpS : 8 * s.Medium.LifetimeS;
                gc.PumpCw(t, 400, false);
                double tt = 0.05 * s.Medium.LifetimeS; gc.PumpCw(tt, 20, true);  // tally the steady absorbed power
                return 1 / tt;
            }
            if (s.Pump.Kind == "pulsed") { gc.PumpPulse(); gc.Decay(s.Pump.DelayS); }
            return double.NaN;
        }

        /// <summary>Heat per absorbed pump power: the given heatFraction, else the quantum defect 1 - lambda_p / lambda_s.</summary>
        public static double HeatFraction(GainSetup s, double omegaSignal)
        {
            if (!double.IsNaN(s.HeatFraction)) return s.HeatFraction;
            if (s.Pump.Kind == "none") return 0;
            double ls = 2 * Math.PI * GainMedium.C / omegaSignal;
            return Math.Max(0, 1 - s.Pump.WavelengthM / ls);
        }

        public static double[] SpectralEnergy(PulseField p)
        {
            var sp = p.SpectralIntensity(); double k = p.Grid.DTime / p.Grid.N;
            return sp.Select(v => v * k).ToArray();
        }

        /// <summary>The thermal lens as a Gradient-1 GRIN (z-averaged): n = n_host + Delta n(0) + Nr2 r^2 (for OpticStudio).</summary>
        public static void RecordGrin(ElementRecord r, ISpatialField field, double fth, GainSetup s, double omega)
        {
            if (!(Math.Abs(fth) < double.PositiveInfinity)) return;
            r.Values["thermalGrinNr2PerM2"] = -1 / (2 * fth * s.LengthM);
            if (field is RadialZTableField t) { double a = 0; for (int j = 0; j < s.Slices; j++) a += t.PeakAt(j); r.Values["thermalDnOnAxis"] = a / s.Slices; }
            r.Values["hostIndex"] = s.Host.IndexAt(omega);
        }

        /// <summary>Record diagnostics shared by both elements.</summary>
        public static void RecordBefore(ActiveContext ctx, GainCrystal gc, GainSetup s, PulseField p, double omegaSignal)
        {
            var g = p.Grid; var r = ctx.Record;
            double G0 = gc.SmallSignalGain(new[] { omegaSignal })[0];
            r.Values["smallSignalGain"] = G0;
            r.Values["inversionFractionBefore"] = gc.MeanInversionFraction();
            r.Values["storedEnergyJ"] = gc.ExtractableEnergy(omegaSignal);
            r.Values["saturationFluenceJPerM2"] = s.Medium.SaturationFluence(omegaSignal);
            // gain spectrum over the medium's band (and the grid)
            double lc = 2 * Math.PI * GainMedium.C / omegaSignal;
            int nl = 401; var lam = new double[nl]; var om = new double[nl];
            for (int i = 0; i < nl; i++) { lam[i] = lc * (0.85 + 0.30 * i / (nl - 1)); om[i] = GainMedium.Omega(lam[i]); }
            var G = gc.SmallSignalGain(om);
            int ib = Array.IndexOf(G, G.Max()); r.Values["gainPeakWavelengthM"] = lam[ib];
            r.AddArray("gainSpectrumWavelengthM", lam, "m", "wavelength axis of the single-pass small-signal gain");
            r.AddArray("gainSpectrum", G, "", "single-pass small-signal gain (on axis) before the pulse");
            r.AddArray("zM", Enumerable.Range(0, gc.Nz).Select(j => (j + 0.5) * gc.Dz).ToArray(), "m", "slice positions in the gain medium");
            r.AddArray("inversionBefore", gc.InversionVsZ(), "", "upper-state fraction N2/N_t vs z (on axis) before the pulse");
            if (gc.Basis != null)
            {
                r.AddArray("cellXM", gc.CellX, "m", "x (radius for radial models) of the transverse points");
                r.AddArray("cellYM", gc.CellY, "m", "y of the transverse points");
                r.AddArray("inversionVsCellBefore", gc.InversionVsCell(), "", "z-averaged upper-state fraction at each transverse point before the pulse");
            }
        }

        public static void RecordAfter(ActiveContext ctx, GainCrystal gc, double extractedJ)
        {
            var r = ctx.Record;
            r.Values["inversionFractionAfter"] = gc.MeanInversionFraction();
            r.Values["extractedEnergyJ"] = extractedJ;
            if (r.Values.TryGetValue("storedEnergyJ", out var st) && st > 0) r.Values["extractionEfficiency"] = extractedJ / st;
            r.AddArray("inversionAfter", gc.InversionVsZ(), "", "upper-state fraction N2/N_t vs z (on axis) after the pulse");
            if (gc.Basis != null) r.AddArray("inversionVsCellAfter", gc.InversionVsCell(), "", "z-averaged upper-state fraction at each transverse point after the pulse");
        }

        /// <summary>Iterate the pulse train with the energy model until the output repeats; leaves gc in the state seen by the last pulse.</summary>
        public static (List<double> Energies, bool Converged, bool PeriodDoubling) Train(GainCrystal gc, GainSetup s, double T, Func<GainCrystal, double> onePulse)
        {
            var E = new List<double>(); bool conv = false, pd = false; double[] before = null;
            for (int k = 0; k < s.MaxPulses; k++)
            {
                Between(gc, s, T);
                before = gc.SaveN2();
                E.Add(onePulse(gc));
                int c = E.Count;
                if (c >= 4)
                {
                    double e1 = E[c - 1], e2 = E[c - 2], e3 = E[c - 3];
                    if (Math.Abs(e1 - e2) <= s.Tolerance * Math.Abs(e1) && Math.Abs(e2 - e3) <= s.Tolerance * Math.Abs(e1)) { conv = true; break; }
                    if (c >= 12 && Math.Abs(e1 - e3) <= s.Tolerance * Math.Abs(e1) && Math.Abs(E[c - 2] - E[c - 4]) <= s.Tolerance * Math.Abs(e1) && Math.Abs(e1 - e2) > 100 * s.Tolerance * Math.Abs(e1)) { pd = true; break; }
                }
            }
            // the full-model pulse sees the state after the last pumping interval
            gc.RestoreN2(before);
            return (E, conv, pd);
        }
    }

    // ============================================================================================ gain element

    /// <summary>
    /// A pumped gain medium crossed once (docs/reference/gain.md): spectrally resolved gain with time-ordered saturation,
    /// host dispersion and SPM, pumping between pulses and the steady state of the pulse train, thermal lens; 1-D
    /// (mode-area cell) or spatial (radial / cartesian, with diffraction, per-point inversion and the thermal lens
    /// as a position-dependent index).
    /// </summary>
    public sealed class GainElement : IActiveElement, Engine.IReportsCarrierPhase
    {
        public GainSetup S;
        public string Kind => "gain";
        public string Description => "gain medium " + S.Describe();
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => S.Transverse != null && S.Transverse.Model != "none";

        /// <summary>
        /// Thermal stress birefringence of a uniformly heated rod (Koechner, Solid-State Laser Engineering, ch. 7): the retardation
        /// between radial and tangential polarisation is delta = C (r / r0)^2, C = n0^3 alpha C_B P_h / (lambda K) with
        /// C_B = C_r - C_phi. An x-polarised beam filling the rod loses to y the fraction D = (1 - sin C / C) / 4, which tends to 1/4;
        /// this moves that fraction of the energy, and reports D and the two thermal lenses f_r, f_phi.
        /// </summary>
        private void ApplyStress(ActiveContext ctx, Complex[] ax, ref Complex[] ay, double wmode, double w0)
        {
            var r = ctx.Record; var m = S.Medium;
            if (double.IsNaN(m.StressCr) || double.IsNaN(m.ThermalExpansionPerK)) { ctx.Result.Warnings.Add("Element '" + S.Id + "': no stress coefficients for " + m.Name + " (thermalExpansionPerK, stressCr, stressCphi): thermal birefringence is not applied."); return; }
            if (!r.Values.TryGetValue("heatW", out double heat) || !(heat > 0)) return;
            double n0 = S.Host.IndexAt(w0), lam = 2 * Math.PI * GainMedium.C / w0, a = m.ThermalExpansionPerK, K = m.ThermalConductivity;
            double r0 = S.RodRadiusM > 0 ? S.RodRadiusM : 3 * S.Pump.RadiusM, area = Math.PI * r0 * r0;
            double C = n0 * n0 * n0 * a * (m.StressCr - m.StressCphi) * heat / (lam * K);
            double D = StressDepolarization(C, wmode, r0);
            double F(double cs) => K * area / heat / (0.5 * m.DnDT + a * cs * n0 * n0 * n0 + a * r0 * (n0 - 1) / S.LengthM);
            r.Values["depolarizationFraction"] = D; r.Values["stressRetardationRad"] = C;
            r.Values["stressFocalLengthRadialM"] = F(m.StressCr); r.Values["stressFocalLengthTangentialM"] = F(m.StressCphi);
            ctx.Note(string.Format(CultureInfo.InvariantCulture, "thermal stress: retardation {0:G4} rad at the rod edge, depolarised fraction {1:P2}, f_r = {2:G4} m, f_phi = {3:G4} m", C, D, F(m.StressCr), F(m.StressCphi)));
            // a rotation, so the energy is kept and y input depolarises as x does
            var ny = new Complex[ax.Length]; double tx = Math.Sqrt(1 - D), ty = Math.Sqrt(D);
            for (int i = 0; i < ax.Length; i++) { var x = ax[i]; ny[i] = ay[i] * tx + x * ty; ax[i] = x * tx - ay[i] * ty; }
            ay = ny;
        }

        /// <summary>Depolarised fraction of a Gaussian beam (1/e^2 radius w) in a rod of radius r0 with edge retardation C:
        /// D = (1/2) &lt;sin^2(delta/2)&gt; over the beam inside the rod, delta = C r^2 / r0^2. A beam much wider than the rod gives
        /// Koechner's filled-rod (1 - sin C / C) / 4; a small beam gives (x^2 / (1 + x^2)) / 4, x = C w^2 / (2 r0^2).</summary>
        internal static double StressDepolarization(double C, double w, double r0)
        {
            if (!(w > 0) || !(r0 > 0)) return (1 - (Math.Abs(C) < 1e-9 ? 1 : Math.Sin(C) / C)) / 4;
            double U = r0 * r0, s = 2 / (w * w), beta = C / U;           // in u = r^2 the weight is exp(-s u), the phase cos(beta u)
            var z = new Complex(s, -beta);
            double mean = s < 1e-9 / U ? (Math.Abs(C) < 1e-9 ? 1 : Math.Sin(C) / C) : ((1 - Complex.Exp(-z * U)) / z).Real * s / (1 - Math.Exp(-s * U));
            return (1 - mean) / 4;
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            var g = p.Grid; int n = g.N; var notes = ctx.Record.Notes; var r = ctx.Record;
            bool spatial = TransverseAware;
            double w0 = g.OmegaCenter, wmode = GainRun.ModeRadius(S, p, notes, ctx.Element.BeamIn);
            double e0 = p.TotalEnergyJ();
            // host dispersion (frame-referenced, as for a bulk element)
            var axis = OmegaAxis.FromGrid(g);
            var bulk = new BulkTerm(S.Host, S.LengthM);
            Executor.Combine(new LinearTerm[] { bulk }, axis, out var phHost, out _, out _, out _);
            ctx.Own = bulk.DispersionAt(w0);
            // transverse grid
            ITransverseBasis basis = null; TransverseProfile prof = null;
            if (spatial)
            {
                basis = S.Transverse.CreateBasis(Math.Max(wmode, S.Pump.Kind != "none" ? Math.Min(S.Pump.RadiusM, 3 * wmode) : wmode));
                prof = TransverseStart.Initial(S.Transverse, basis, p, S.Beam, w => S.Host.IndexAt(w), S.Id, notes);
                basis = prof.Basis;
            }
            var gc = new GainCrystal(S.Medium, S.LengthM, S.Slices, S.Pump, wmode, basis, S.Doping);
            if (S.Ase && basis == null) gc.Ase = new Random(S.AseSeed); else if (S.Ase) notes.Add("ASE is modelled in 1-D only (ignored on the spatial grid)");
            var gd = gc.Prepare(g, S.Host);
            for (int i = 0; i < n; i++) gd.KRel[i] = phHost[i] / Math.Max(1e-30, S.LengthM);
            GainRun.Initialise(gc, S, w0);
            int K = S.Segments > 0 ? S.Segments : GainRun.AutoSegments(p);
            r.Values["segments"] = K;
            double rep = !double.IsNaN(S.RepRateHz) ? S.RepRateHz : p.RepRateHz;
            bool pumped = S.Pump.Kind != "none";
            double powerScale = double.NaN;
            // ------------------------------------------------ pumping / steady state
            var spec = GainRun.SpectralEnergy(p);
            if (pumped && rep > 0 && S.SteadyState)
            {
                double T = 1 / rep;
                var bins = gc.MakeBins(g.OmegaAbsolute, spec);
                double[] cellFrac = null;
                if (spatial)
                {
                    var flu = TransverseOps.Fluence(TransverseOps.Expand(p.Ax, g.OmegaAbsolute, prof), n, g, basis);
                    double tot = 0; for (int m = 0; m < basis.M; m++) tot += flu[m] * basis.Weights[m];
                    cellFrac = flu.Select(f => f / Math.Max(1e-300, tot)).ToArray();  // 1/m^2
                }
                var (E, conv, pd) = GainRun.Train(gc, S, T, c =>
                {
                    if (!spatial) { var e = (double[])bins.Energy.Clone(); c.FastPass(e, bins, 0, c.CellArea[0], false); return e.Sum(); }
                    double outE = 0;
                    for (int m = 0; m < c.Cells; m++)
                    {
                        var e = bins.Energy.Select(v => v * cellFrac[m]).ToArray();   // J/m^2
                        c.FastPass(e, bins, m, 1.0, false); outE += e.Sum() * c.CellArea[m];
                    }
                    return outE;
                });
                // the output energy of the train is that of the on-axis cell in 1-D: scale to the pulse energy
                double ratio = bins.Energy.Sum() > 0 ? 1 / bins.Energy.Sum() * e0 : 1;
                r.AddArray("energyVsPulse", E.Select(v => v * (spatial ? 1 : ratio)).ToArray(), "J", "output energy of successive pulses of the train (energy model)");
                r.Values["steadyStatePulses"] = E.Count; r.Values["converged"] = conv ? 1 : 0;
                if (pd) { r.Values["periodDoubling"] = 1; ctx.Result.Warnings.Add("Element '" + S.Id + "': the pulse train does not settle -- the output alternates between two energies (period doubling); the last pulse is shown."); }
                else if (!conv) ctx.Result.Warnings.Add("Element '" + S.Id + "': the pulse train did not reach a steady state within maxPulses = " + S.MaxPulses + ".");
                notes.Add(string.Format(CultureInfo.InvariantCulture, "steady state at {0:G4} Hz: {1} pulses iterated with the energy model{2}", rep, E.Count, conv ? "" : " (not converged)"));
                powerScale = rep;
            }
            else if (pumped)
            {
                powerScale = GainRun.SingleShot(gc, S);
                notes.Add(rep > 0 ? "steady state disabled: single shot from the unpumped crystal" : "no repetition rate: single shot (" + (S.Pump.Kind == "cw" ? "cw pump to equilibrium" : "pump pulse " + (S.Pump.DelayS * 1e6).ToString("G3", CultureInfo.InvariantCulture) + " us before the seed") + ")");
            }
            GainRun.RecordBefore(ctx, gc, S, p, w0);
            // ------------------------------------------------ thermal lens
            ISpatialField thermal = null; double fth = double.PositiveInfinity;
            if (pumped)
            {
                double absJ = gc.AbsorbedPumpJ.Sum();
                r.Values["absorbedPumpEnergyJ"] = absJ;
                if (powerScale > 0)
                {
                    r.Values["absorbedPumpPowerW"] = absJ * powerScale;
                    if (S.ThermalLens)
                    {
                        double rod = S.RodRadiusM > 0 ? S.RodRadiusM : 3 * S.Pump.RadiusM;
                        var tl = gc.ThermalLens(powerScale, GainRun.HeatFraction(S, w0), rod);
                        thermal = tl.Field; fth = tl.FocalLengthM;
                        r.Values["heatW"] = tl.HeatW; r.Values["thermalLensFocalLengthM"] = fth; r.Values["peakTemperatureRiseK"] = tl.PeakDeltaTK;
                        GainRun.RecordGrin(r, tl.Field, fth, S, w0);
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "thermal lens: {0:G4} W of heat, Delta T(0) = {1:G4} K, f_th = {2:G4} m", tl.HeatW, tl.PeakDeltaTK, fth));
                    }
                }
            }
            if (!double.IsNaN(S.TrackedThermalFocalLengthM))
            {
                r.Values["trackedThermalLensFocalLengthM"] = S.TrackedThermalFocalLengthM;
                if (!double.IsNaN(S.TrackedOnAxisFocalLengthM) && !double.IsInfinity(fth) && Math.Abs(S.TrackedOnAxisFocalLengthM / fth - 1) > 0.05)
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "beam tracking estimated the thermal lens with the pump alone (on-axis f_th = {0:G4} m); with the pulse train it is f_th = {1:G4} m -- the tracked beam after this element uses the estimate (give thermalLens.focalLengthM to set it)", S.TrackedOnAxisFocalLengthM, fth));
            }
            if (!double.IsNaN(S.ThermalFocalLengthM) && S.ThermalLens) fth = S.ThermalFocalLengthM;
            // ------------------------------------------------ amplification
            Complex[] ax, ay = p.Ay; GainCrystal.PassStats st; TransverseProfile profOut = p.Profile;
            double nonGainLoss = 0;
            if (!spatial)
            {
                ax = (Complex[])p.Ax.Clone(); ay = (Complex[])p.Ay.Clone();
                st = gc.Pass1D(ax, ay, gd, S.Spm ? S.N2 : 0, K, false);
                if (p.Profile != null && (thermal != null || (S.ThermalLens && !double.IsNaN(S.ThermalFocalLengthM))) && S.ApplyThermalLens && !double.IsInfinity(fth))
                {
                    profOut = TransverseOps.ApplyLens(p.Profile, _ => fth, w => S.Host.IndexAt(w));
                    notes.Add("1-D: the thermal lens was applied as a thin lens to the carried transverse profile");
                }
                if (S.Properties != null && S.Properties.Any) notes.Add("1-D: indexProfile / absorptionProfile / n2Scale need a transverse model -- ignored in the gain element");
            }
            else
            {
                var props = SpatialProperties.Combine(S.Properties, thermal != null ? new SpatialProperties { IndexChange = thermal } : null);
                var E = TransverseOps.Expand(p.Ax, g.OmegaAbsolute, prof);
                basis.ToK(E, n);
                var amp1 = Enumerable.Repeat(1.0, n).ToArray();
                var rate = SpatialEnvelopeProblem.BuildLinearRate(g, basis, S.LengthM, phHost, amp1, w => S.Host.IndexAt(w), S.Transverse.Paraxial, null);
                var half = rate.Select(z => Complex.Exp(z * (gc.Dz / 2))).ToArray();
                double before = Energy(E, basis, g);
                st = gc.PassSpatial(E, gd, half, S.Spm ? S.N2 : 0, props, K, false);
                double after = Energy(E, basis, g);
                nonGainLoss = before + st.EnergyGainedJ - after;
                basis.ToSpace(E, n);
                ax = TransverseOps.Collapse(E, basis, g.OmegaAbsolute, S.Id, out profOut);
                if (p.Ay.Any(c => c != Complex.Zero)) notes.Add("spatial gain is scalar (x); the y component passed unchanged");
                var flu = TransverseOps.Fluence(E, n, g, basis);
                r.Values["peakFluenceOutJPerM2"] = flu.Max();
                r.Representation = basis.Describe() + " x " + n + " samples, " + S.Slices + " slices";
                if (props != null) notes.Add("position-dependent properties: " + props.Describe());
            }
            if (r.Representation == null) r.Representation = "1-D (mode area " + (gc.CellArea[0] * 1e6).ToString("G4", CultureInfo.InvariantCulture) + " mm^2), " + S.Slices + " slices, " + K + " time segments";
            r.BIntegralRad = st.BIntegralRad;
            if (S.Stress) ApplyStress(ctx, ax, ref ay, wmode, w0);
            var q = new PulseField(g, ax, ay, rep > 0 ? rep : p.RepRateHz) { Profile = profOut };
            double e1 = q.TotalEnergyJ();
            GainRun.RecordAfter(ctx, gc, st.EnergyGainedJ);
            r.Values["gain"] = e1 / Math.Max(1e-300, e0);
            if (rep > 0) r.Values["averageOutputPowerW"] = e1 * rep;
            ctx.Energy("gain (from inversion)", -st.EnergyGainedJ);
            if (st.AseSeedJ != 0) { ctx.Energy("ASE (spontaneous emission)", -st.AseSeedJ); r.Values["aseSeedEnergyJ"] = st.AseSeedJ; }
            double rest0 = st.AseSeedJ;
            double rest = e0 - e1 + st.EnergyGainedJ + rest0;
            if (spatial && S.Properties?.Absorption != null) ctx.Energy("absorption", rest); else ctx.Energy("numerical", rest);
            return q;
        }

        private static double Energy(Complex[] Ek, ITransverseBasis b, FrequencyGrid g)
        {
            var e = (Complex[])Ek.Clone(); b.ToSpace(e, g.N);
            var flu = TransverseOps.Fluence(e, g.N, g, b); double s = 0;
            for (int m = 0; m < b.M; m++) s += flu[m] * b.Weights[m];
            return s;
        }
    }

    // ============================================================================================ amplifier

    /// <summary>
    /// A multipass or regenerative amplifier around one gain medium (1-D): passes through the crystal with
    /// time-ordered saturation, dispersion and SPM; between passes / per cavity round trip a loss, GDD, TOD and
    /// material passes. Regenerative: a fixed number of round trips or "auto" (eject at the energy maximum).
    /// The pumping between pulses and the steady state at the repetition rate are those of the gain element.
    /// </summary>
    public sealed class AmplifierElement : IActiveElement, Engine.IReportsCarrierPhase
    {
        public GainSetup S;
        public string Geometry = "multipass";
        public int Passes = 4;
        public int RoundTrips;              // 0 = auto
        public int PassesPerRoundTrip = 2, MaxRoundTrips = 300;
        public PassOperation Between = new PassOperation();
        public string Kind => "amplifier";
        public string Description => (Geometry == "regenerative" ? "regenerative amplifier (" + (RoundTrips > 0 ? RoundTrips + " round trips" : "auto round trips") + ")" : "multipass amplifier (" + Passes + " passes)") + ", " + S.Describe();
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => false;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            var g = p.Grid; int n = g.N; var notes = ctx.Record.Notes; var r = ctx.Record;
            double w0 = g.OmegaCenter, wmode = GainRun.ModeRadius(S, p, notes, ctx.Element.BeamIn), e0 = p.TotalEnergyJ();
            var axis = OmegaAxis.FromGrid(g);
            var bulk = new BulkTerm(S.Host, S.LengthM);
            // the host's carrier phase and delay, per pass; a regenerative amplifier's count is not known yet, so its phase is not stated
            { var host = bulk.DispersionAt(p.Grid.OmegaCenter); ctx.Own = new DispersionPoint { FramePhaseRad = host.FramePhaseRad * Passes, FrameDelayS = host.FrameDelayS * Passes }; ctx.PhaseLost = Geometry == "regenerative"; }
            Executor.Combine(new LinearTerm[] { bulk }, axis, out var phHost, out _, out _, out _);
            var gc = new GainCrystal(S.Medium, S.LengthM, S.Slices, S.Pump, wmode, null, S.Doping);
            if (S.Ase) gc.Ase = new Random(S.AseSeed);
            var gd = gc.Prepare(g, S.Host);
            for (int i = 0; i < n; i++) gd.KRel[i] = phHost[i] / Math.Max(1e-30, S.LengthM);
            GainRun.Initialise(gc, S, w0);
            int K = S.Segments > 0 ? S.Segments : GainRun.AutoSegments(p);
            r.Values["segments"] = K;
            if (S.Transverse != null && S.Transverse.Model != "none") notes.Add("the amplifier element is 1-D: transverse settings ignored (use a gain element for spatial gain)");
            bool regen = Geometry == "regenerative";
            double rep = !double.IsNaN(S.RepRateHz) ? S.RepRateHz : p.RepRateHz;
            bool pumped = S.Pump.Kind != "none";
            double powerScale = double.NaN;
            double area = gc.CellArea[0];
            int roundTripsUsed = RoundTrips;
            // ---------------------------------------------- steady state (energy model)
            if (pumped && rep > 0 && S.SteadyState)
            {
                var bins = gc.MakeBins(g.OmegaAbsolute, GainRun.SpectralEnergy(p));
                double sc = e0 / Math.Max(1e-300, bins.Energy.Sum());
                var (E, conv, pd) = GainRun.Train(gc, S, 1 / rep, c => { var e = (double[])bins.Energy.Clone(); var res = FastAmplify(c, bins, e, area, out int rt); roundTripsUsed = RoundTrips > 0 ? RoundTrips : rt; return res; });
                r.AddArray("energyVsPulse", E.Select(v => v * sc).ToArray(), "J", "output energy of successive pulses of the train (energy model)");
                r.Values["steadyStatePulses"] = E.Count; r.Values["converged"] = conv ? 1 : 0;
                if (pd) { r.Values["periodDoubling"] = 1; ctx.Result.Warnings.Add("Element '" + S.Id + "': the pulse train does not settle -- the output alternates between two energies (period doubling, typical of regenerative amplifiers at repetition rates near 1/tau); the last pulse is shown."); }
                else if (!conv) ctx.Result.Warnings.Add("Element '" + S.Id + "': the pulse train did not reach a steady state within maxPulses = " + S.MaxPulses + ".");
                notes.Add(string.Format(CultureInfo.InvariantCulture, "steady state at {0:G4} Hz: {1} pulses iterated with the energy model{2}", rep, E.Count, conv ? "" : " (not converged)"));
                powerScale = rep;
            }
            else if (pumped) powerScale = GainRun.SingleShot(gc, S);
            GainRun.RecordBefore(ctx, gc, S, p, w0);
            if (pumped)
            {
                double absJ = gc.AbsorbedPumpJ.Sum(); r.Values["absorbedPumpEnergyJ"] = absJ;
                if (powerScale > 0)
                {
                    r.Values["absorbedPumpPowerW"] = absJ * powerScale;
                    if (S.ThermalLens)
                    {
                        var tl = gc.ThermalLens(powerScale, GainRun.HeatFraction(S, w0), S.RodRadiusM > 0 ? S.RodRadiusM : 3 * S.Pump.RadiusM);
                        r.Values["heatW"] = tl.HeatW; r.Values["thermalLensFocalLengthM"] = tl.FocalLengthM; r.Values["peakTemperatureRiseK"] = tl.PeakDeltaTK;
                        GainRun.RecordGrin(r, tl.Field, tl.FocalLengthM, S, w0);
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "thermal lens (reported, not applied in the 1-D amplifier): f_th = {0:G4} m, {1:G4} W of heat", tl.FocalLengthM, tl.HeatW));
                    }
                }
            }
            // ---------------------------------------------- full model
            var ax = (Complex[])p.Ax.Clone(); var ay = (Complex[])p.Ay.Clone();
            bool hasY = ay.Any(z => z != Complex.Zero);
            double B = 0, gained = 0, betweenLoss = 0;
            var history = new List<double> { e0 };
            double En() => EnergyNormalization.ComputeEnergyJFromFreqDomain(ax, ay, g);
            double aseJ = 0;
            void CrystalPass(bool rev) { var stp = gc.Pass1D(ax, ay, gd, S.Spm ? S.N2 : 0, K, rev); B += stp.BIntegralRad; gained += stp.EnergyGainedJ; aseJ += stp.AseSeedJ; }
            void Op() { double a = En(); B += Between.Apply(ax, hasY ? ay : null, g, area); betweenLoss += a - En(); }
            if (!regen)
            {
                for (int k = 0; k < Passes; k++)
                {
                    CrystalPass(k % 2 == 1);
                    history.Add(En());
                    if (k < Passes - 1) Op();
                }
                r.AddArray("energyPerPass", history.ToArray(), "J", "energy after each pass (index 0: input)");
                r.Values["passes"] = Passes;
            }
            else
            {
                int maxRt = RoundTrips > 0 ? RoundTrips : MaxRoundTrips;
                Complex[] bx = null, by = null; double best = -1; int bestRt = 0; double[] bestN2 = null; double bestB = 0, bestGain = 0, bestLoss = 0;
                for (int rt = 1; rt <= maxRt; rt++)
                {
                    for (int k = 0; k < PassesPerRoundTrip; k++) CrystalPass(k % 2 == 1);
                    Op();
                    double e = En(); history.Add(e);
                    if (RoundTrips == 0)
                    {
                        if (e > best) { best = e; bestRt = rt; bx = (Complex[])ax.Clone(); by = (Complex[])ay.Clone(); bestN2 = gc.SaveN2(); bestB = B; bestGain = gained; bestLoss = betweenLoss; }
                        else if (e < 0.97 * best) break;
                    }
                }
                if (RoundTrips == 0 && bx != null)
                {
                    ax = bx; ay = by; gc.RestoreN2(bestN2); B = bestB; gained = bestGain; betweenLoss = bestLoss; roundTripsUsed = bestRt;
                    notes.Add("auto round trips: ejected at the energy maximum after " + bestRt + " round trips");
                }
                else roundTripsUsed = RoundTrips;
                r.AddArray("energyPerRoundTrip", history.ToArray(), "J", "intracavity energy after each round trip (index 0: injected seed)");
                r.Values["roundTrips"] = roundTripsUsed;
            }
            r.BIntegralRad = B;
            r.Representation = "1-D (mode area " + (area * 1e6).ToString("G4", CultureInfo.InvariantCulture) + " mm^2), " + S.Slices + " slices, " + K + " time segments";
            var q = new PulseField(g, ax, ay, rep > 0 ? rep : p.RepRateHz) { Profile = p.Profile };
            double e1 = q.TotalEnergyJ();
            GainRun.RecordAfter(ctx, gc, gained);
            r.Values["gain"] = e1 / Math.Max(1e-300, e0);
            if (rep > 0) r.Values["averageOutputPowerW"] = e1 * rep;
            ctx.Energy("gain (from inversion)", -gained);
            if (aseJ != 0) { ctx.Energy("ASE (spontaneous emission)", -aseJ); r.Values["aseSeedEnergyJ"] = aseJ; }
            ctx.Energy(regen ? "cavity loss" : "between-pass loss", betweenLoss);
            ctx.Energy("numerical", e0 - e1 + gained + aseJ - betweenLoss);
            return q;
        }

        /// <summary>The energy model of the whole amplifier for one pulse; returns the output energy (in the bins' units).</summary>
        private double FastAmplify(GainCrystal c, GainCrystal.Bins b, double[] e, double area, out int rtUsed)
        {
            rtUsed = 0;
            if (Geometry != "regenerative")
            {
                for (int k = 0; k < Passes; k++) { c.FastPass(e, b, 0, area, k % 2 == 1); if (k < Passes - 1) Between.ApplyEnergy(e); }
                return e.Sum();
            }
            int maxRt = RoundTrips > 0 ? RoundTrips : MaxRoundTrips;
            double best = -1; double[] bestE = null, bestN2 = null;
            for (int rt = 1; rt <= maxRt; rt++)
            {
                for (int k = 0; k < PassesPerRoundTrip; k++) c.FastPass(e, b, 0, area, k % 2 == 1);
                Between.ApplyEnergy(e);
                if (RoundTrips == 0)
                {
                    double s = e.Sum();
                    if (s > best) { best = s; bestE = (double[])e.Clone(); bestN2 = c.SaveN2(); rtUsed = rt; }
                    else if (s < 0.97 * best) break;
                }
            }
            if (RoundTrips == 0 && bestE != null) { Array.Copy(bestE, e, e.Length); c.RestoreN2(bestN2); }
            return e.Sum();
        }
    }

    // ============================================================================================ compiler

    public static class GainCompiler
    {
        public static void Compile(CompilerServices c, SystemElement e, CompiledElement ce, bool amplifier)
        {
            var j = e.Json; string origin = "element " + e.Id;
            var s = new GainSetup { Id = e.Id };
            var mv = c.Find(e, "medium")?.RawValue ?? j["medium"] ?? throw new JsonException(origin + ": needs \"medium\" (\"Yb:YAG\", \"Ti:sapphire\", \"Nd:YAG\" or a gain-medium object)");
            if (mv.Kind == JsonKind.String && c.System.Materials.Has(mv.AsString()) && c.System.Materials.GetObject(mv.AsString())?.GetObject("gain") != null)
                s.Medium = GainMedium.FromJson(c.System.Materials.GetObject(mv.AsString()).GetObject("gain"), c.Registry, "materials." + mv.AsString() + ".gain");
            else s.Medium = GainMedium.FromJson(mv, c.Registry, origin + ".medium");
            if (j.Has("temperatureK")) s.Medium.TemperatureK = c.Num(e, "temperatureK", s.Medium.TemperatureK);
            if (j.Has("dopingDensityM3")) s.Medium.DopingDensityM3 = c.Num(e, "dopingDensityM3", s.Medium.DopingDensityM3);
            s.LengthM = c.Num(e, "lengthM", double.NaN);
            if (!(s.LengthM > 0)) throw new JsonException(origin + ": needs lengthM > 0");
            ce.LengthM = s.LengthM;
            s.Slices = Math.Max(1, (int)c.Num(e, "slices", 24));
            s.Pump = PumpSpec.FromJson(c.Find(e, "pump")?.RawValue?.AsObject() ?? j.GetObject("pump"), origin + ".pump");
            var inv = j.GetObject("inversion");
            if (inv != null)
            {
                s.InversionFraction = inv.GetDouble("fraction", double.NaN);
                s.SmallSignalGain = inv.GetDouble("smallSignalGain", double.NaN);
                s.SmallSignalGainWavelengthM = inv.GetDouble("wavelengthM", double.NaN);
            }
            if (s.Pump.Kind == "none" && double.IsNaN(s.InversionFraction) && double.IsNaN(s.SmallSignalGain))
                throw new JsonException(origin + ": give a \"pump\" or an \"inversion\" ({\"fraction\": 0.3} or {\"smallSignalGain\": 2})");
            if (s.Pump.Kind != "none" && inv != null) c.Warnings.Add(origin + ": both pump and inversion given -- the inversion is the starting state, the pump acts on top");
            s.Host = c.GetMaterial(j.GetString("host", s.Medium.Host));
            s.N2 = j.Has("n2") ? c.Num(e, "n2", s.Host.N2) : s.Host.N2;
            s.Spm = j.GetBool("spm", true) && c.Run.Physics.Nonlinear;
            s.Segments = j.Has("segments") && j["segments"].Kind == JsonKind.Number ? Math.Max(1, j.GetInt("segments", 0)) : 0;
            s.ModeRadiusM = c.Num(e, "modeRadiusM", double.NaN);
            var bspec = j.Has("beam") ? ModelSpec.FromJson(j["beam"], ModelSource.SystemData, origin + ".beam") : null;
            if (bspec != null)
            {
                if (bspec.Kind == "gaussian" && !bspec.Json.Has("wavelengthM") && !bspec.Json.Has("zRayleighM")) bspec.Json.Set("wavelengthM", 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / c.Omega0);
                if (bspec.Kind == "gaussian" && !bspec.Json.Has("n")) bspec.Json.Set("n", s.Host.IndexAt(c.Omega0));
                s.Beam = c.Registry.Create<IBeamModel>("beam", bspec);
            }
            var tlv = j["thermalLens"];
            if (tlv != null && !tlv.IsNull)
            {
                if (tlv.Kind == JsonKind.Bool) s.ThermalLens = tlv.AsBool();
                else
                {
                    var t = tlv.AsObject();
                    s.ThermalLens = t.GetBool("enabled", true); s.ApplyThermalLens = t.GetBool("apply", true);
                    s.RodRadiusM = t.GetDouble("rodRadiusM", double.NaN); s.HeatFraction = t.GetDouble("heatFraction", double.NaN);
                    s.TrackThermalLens = t.GetBool("track", true); s.ThermalFocalLengthM = t.GetDouble("focalLengthM", double.NaN);
                    s.Stress = t.GetBool("stress", false);
                }
            }
            s.RepRateHz = c.Num(e, "repRateHz", double.NaN);
            var ssv = j["steadyState"];
            if (ssv != null && !ssv.IsNull)
            {
                if (ssv.Kind == JsonKind.Bool) s.SteadyState = ssv.AsBool();
                else { var so = ssv.AsObject(); s.SteadyState = so.GetBool("enabled", true); s.MaxPulses = so.GetInt("maxPulses", s.MaxPulses); s.Tolerance = so.GetDouble("tolerance", s.Tolerance); }
            }
            s.SingleShotPumpS = c.Num(e, "pumpDurationS", double.NaN);
            s.Ase = j.GetBool("ase", false); s.AseSeed = j.GetInt("aseSeed", 7);
            if (j.Has("dopingProfile")) s.Doping = c.Registry.Create<ISpatialField>("field", ModelSpec.FromJson(j["dopingProfile"], ModelSource.SystemData, origin + ".dopingProfile"));
            s.Properties = c.SpatialProperties(e);
            if (!amplifier)
            {
                s.Transverse = TransverseSpec.FromJson(c.Find(e, "transverse")?.RawValue ?? (j.Has("transverse") ? j["transverse"] : c.Run.Physics.Transverse));
                ce.Active = new GainElement { S = s };
                ce.TrackingIndex = s.Host.IndexAt(c.Omega0);
                TrackThermalLens(c, ce, s);
            }
            else
            {
                var a = new AmplifierElement { S = s };
                a.Geometry = j.GetString("geometry", "multipass").ToLowerInvariant();
                if (a.Geometry == "regen") a.Geometry = "regenerative";
                if (a.Geometry != "multipass" && a.Geometry != "regenerative") throw new JsonException(origin + ": geometry must be \"multipass\" or \"regenerative\"");
                if (a.Geometry == "multipass")
                {
                    a.Passes = Math.Max(1, (int)c.Num(e, "passes", 4));
                    a.Between = PassOperation.FromJson(j.GetObject("betweenPasses"), c.GetMaterial, origin + ".betweenPasses");
                }
                else
                {
                    var rt = j["roundTrips"];
                    a.RoundTrips = rt == null || rt.IsNull || (rt.Kind == JsonKind.String && rt.AsString().Equals("auto", StringComparison.OrdinalIgnoreCase)) ? 0 : Math.Max(1, rt.AsInt());
                    a.PassesPerRoundTrip = Math.Max(1, j.GetInt("passesPerRoundTrip", 2));
                    a.MaxRoundTrips = Math.Max(1, j.GetInt("maxRoundTrips", 300));
                    a.Between = PassOperation.FromJson(j.GetObject("cavity"), c.GetMaterial, origin + ".cavity");
                }
                s.Transverse = j.Has("transverse") ? TransverseSpec.FromJson(j["transverse"]) : null;
                ce.Active = a;
                var tlo = tlv != null && tlv.Kind == JsonKind.Object ? tlv.AsObject() : null;
                if (a.Geometry == "multipass" && tlo != null && (tlo.Has("track") || tlo.Has("passGapM")))
                {
                    // on request: the path between passes is only known if it is given
                    ce.TrackingIndex = s.Host.IndexAt(c.Omega0);
                    TrackThermalLens(c, ce, s);
                    ce.TrackPasses = a.Passes; ce.TrackPassGapM = tlo.GetDouble("passGapM", 0);
                }
                else if (c.Tracked != null && s.ThermalLens && s.Pump.Kind != "none") ce.Notes.Add("beam tracking: the amplifier's thermal lens is not applied to the tracked beam (multipass: give thermalLens.track and passGapM)");
            }
            c.Provenance.Record("element:" + e.Id, amplifier ? "amplifier" : "gain", ce.Active.Description, ModelSource.SystemData, "system file");
            c.Provenance.Record("element:" + e.Id, "gainMedium", s.Medium.Describe() + (string.IsNullOrEmpty(s.Medium.References) ? "" : " [" + s.Medium.References + "]"), ModelSource.SystemData, "gain-medium library / system file");
        }
    
        /// <summary>
        /// The thermal lens on the tracked Gaussian beam (docs/reference/gain.md#thermal-lens): a measured
        /// thermalLens.focalLengthM as one thin lens at the centre, or the heat model of the element evaluated at compile
        /// time with the pump alone (cw pumping to equilibrium, or a pulsed pump repeated to steady state at the
        /// repetition rate) -- one thin lens per slice, 1/f_j from the parabolic index profile of that slice. The signal's
        /// extraction can change the absorbed pump: the run compares this estimate with the thermal lens it computes.
        /// </summary>
        private static void TrackThermalLens(CompilerServices c, CompiledElement ce, GainSetup s)
        {
            var t = c.Tracked;
            if (t == null || !s.ThermalLens || !s.TrackThermalLens) return;
            var ci = CultureInfo.InvariantCulture;
            if (!double.IsNaN(s.ThermalFocalLengthM))
            {
                ce.TrackLenses = new List<(double, double)> { (s.LengthM / 2, 1 / s.ThermalFocalLengthM) };
                s.TrackedThermalFocalLengthM = s.ThermalFocalLengthM;
                ce.Notes.Add(string.Format(ci, "beam tracking: thermal lens f_th = {0:G4} m (thermalLens.focalLengthM) at the centre of the medium", s.ThermalFocalLengthM));
                return;
            }
            if (s.Pump.Kind == "none") return;
            double wmode = s.ModeRadiusM > 0 ? s.ModeRadiusM : s.Beam != null ? 0.5 * (s.Beam.RadiusX(s.LengthM / 2) + s.Beam.RadiusY(s.LengthM / 2)) : Math.Sqrt(t.RadiusX * t.RadiusY);
            if (!(wmode > 0)) return;
            var gc = new GainCrystal(s.Medium, s.LengthM, s.Slices, s.Pump, wmode, null, s.Doping);
            GainRun.Initialise(gc, s, c.Omega0);
            double rep = !double.IsNaN(s.RepRateHz) ? s.RepRateHz : (c.Run.Pulse?.RepRateHz ?? 0);
            double scale;
            if (s.Pump.Kind == "cw") scale = GainRun.SingleShot(gc, s);
            else
            {
                if (!(rep > 0)) { ce.Notes.Add("beam tracking: pulsed pump without a repetition rate -- no thermal lens on the tracked beam"); return; }
                for (int k = 0; k < 40; k++) GainRun.Between(gc, s, 1 / rep);    // pump-only steady state; the last pump pulse is tallied
                scale = rep;
            }
            if (!(scale > 0)) return;
            var inv = new double[gc.Nz];
            var tl = gc.ThermalLens(scale, GainRun.HeatFraction(s, c.Omega0), s.RodRadiusM > 0 ? s.RodRadiusM : 3 * s.Pump.RadiusM, inv, wmode);
            double sum = inv.Sum();
            if (double.IsInfinity(tl.FocalLengthM) || sum == 0) return;
            ce.TrackLenses = Enumerable.Range(0, gc.Nz).Select(j => ((j + 0.5) * gc.Dz, inv[j])).ToList();
            s.TrackedThermalFocalLengthM = 1 / sum; s.TrackedOnAxisFocalLengthM = tl.FocalLengthM;
            ce.Notes.Add(string.Format(ci, "beam tracking: thermal lens f = {0:G4} m for the {1:G4} mm beam (on-axis f_th = {2:G4} m; {3:G4} W of heat, pump alone), applied as {4} thin lenses through the medium", 1 / sum, wmode * 1e3, tl.FocalLengthM, tl.HeatW, gc.Nz));
        }
    }
}
