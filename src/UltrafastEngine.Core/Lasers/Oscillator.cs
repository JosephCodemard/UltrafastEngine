using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Gain;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Lasers
{
    /// <summary>
    /// Lumped gain for oscillators: ln power gain per round trip g(E) = g0 / (1 + E / E_sat) (or average power
    /// P = E f_rep against P_sat), with a spectral shape of FWHM <see cref="BandwidthOmega"/>:
    /// "lorentzian" 1/(1+u^2), "gaussian" exp(-ln2 u^2), "parabolic" 1 - u^2/2 (Haus), u = 2 (w - w_g) / dw_FWHM.
    /// </summary>
    public sealed class SimpleGain
    {
        public double G0 = 0.2, SaturationEnergyJ = double.NaN, SaturationPowerW = double.NaN, CenterOmega, BandwidthOmega = double.PositiveInfinity;
        public string Shape = "lorentzian";
        public double QmlSaturationEnergyJ = double.NaN;

        public static SimpleGain FromJson(JsonObject o, double omega0, string origin)
        {
            var g = new SimpleGain { Shape = o.GetString("shape", "lorentzian").ToLowerInvariant() };
            double G = o.GetDouble("smallSignalGain", double.NaN);
            g.G0 = !double.IsNaN(G) ? Math.Log(G) : o.GetDouble("smallSignalLnGain", double.NaN);
            if (!(g.G0 > 0)) throw new JsonException(origin + ": simple gain needs smallSignalGain (power gain per round trip, > 1)");
            g.SaturationEnergyJ = o.GetDouble("saturationEnergyJ", double.NaN);
            g.SaturationPowerW = o.GetDouble("saturationPowerW", double.NaN);
            if (double.IsNaN(g.SaturationEnergyJ) && double.IsNaN(g.SaturationPowerW)) throw new JsonException(origin + ": simple gain needs saturationEnergyJ or saturationPowerW (intracavity)");
            double lc = o.GetDouble("centerWavelengthM", double.NaN);
            g.CenterOmega = double.IsNaN(lc) ? omega0 : 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / lc;
            double bw = o.GetDouble("bandwidthM", double.NaN);
            if (bw > 0) { double l0 = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / g.CenterOmega; g.BandwidthOmega = g.CenterOmega * bw / l0; }
            g.QmlSaturationEnergyJ = o.GetDouble("fluenceSaturationEnergyJ", double.NaN);
            if (g.Shape != "lorentzian" && g.Shape != "gaussian" && g.Shape != "parabolic") throw new JsonException(origin + ": gain shape must be lorentzian, gaussian or parabolic");
            return g;
        }

        public double Saturated(double energyJ, double frep) =>
            G0 / (1 + (!double.IsNaN(SaturationEnergyJ) ? energyJ / SaturationEnergyJ : energyJ * frep / SaturationPowerW));

        public double ShapeAt(double w)
        {
            if (double.IsInfinity(BandwidthOmega)) return 1;
            double u = 2 * (w - CenterOmega) / BandwidthOmega;
            switch (Shape)
            {
                case "gaussian": return Math.Exp(-Math.Log(2) * u * u);
                case "parabolic": return Math.Max(-1, 1 - 0.5 * u * u);
                default: return 1 / (1 + u * u);
            }
        }

        public void Apply(Complex[] ax, Complex[] ay, FrequencyGrid g, double gsat)
        {
            for (int i = 0; i < g.N; i++)
            {
                double f = Math.Exp(0.5 * gsat * ShapeAt(g.OmegaAbsolute[i]));
                ax[i] *= f; if (ay != null) ay[i] *= f;
            }
        }

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Format(ci, "gain e^{0:G3} small-signal per round trip, {1}{2}", G0,
                !double.IsNaN(SaturationEnergyJ) ? "E_sat = " + SaturationEnergyJ.ToString("G3", ci) + " J" : "P_sat = " + SaturationPowerW.ToString("G3", ci) + " W",
                double.IsInfinity(BandwidthOmega) ? "" : ", " + Shape + " " + (2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / CenterOmega * BandwidthOmega / CenterOmega * 1e9).ToString("G3", ci) + " nm FWHM");
        }
    }

    /// <summary>
    /// A real gain medium inside a cavity (1-D): the inversion is set by the average powers (pump and the
    /// intracavity signal, f_rep x energy) through the cw rate equations, relaxed towards that quasi-steady state
    /// a little every round trip (the inversion cannot follow single round trips: tau >> T_rt); every pass applies
    /// the spectral gain, host dispersion and SPM of the crystal without depleting the inversion.
    /// </summary>
    public sealed class CavityGain
    {
        public GainSetup S;
        public int Passes = 2;
        public double Relaxation = 0.2;
        public GainCrystal Crystal;
        private GainCrystal.GridData _gd;
        private FrequencyGrid _g;
        public double ModeAreaM2 => Crystal.CellArea[0];

        public void Init(FrequencyGrid g, double modeRadiusM)
        {
            Crystal = new GainCrystal(S.Medium, S.LengthM, S.Slices, S.Pump, modeRadiusM, null, S.Doping);
            if (!double.IsNaN(S.InversionFraction)) Crystal.SetInversionFraction(S.InversionFraction);
            else Crystal.PumpCw(8 * S.Medium.LifetimeS, 400, false);
            Prepare(g);
        }

        private void Prepare(FrequencyGrid g)
        {
            if (ReferenceEquals(_g, g)) return;
            _g = g; _gd = Crystal.Prepare(g, S.Host);
            var axis = OmegaAxis.FromGrid(g);
            Executor.Combine(new LinearTerm[] { new BulkTerm(S.Host, S.LengthM) }, axis, out var ph, out _, out _, out _);
            for (int i = 0; i < g.N; i++) _gd.KRel[i] = ph[i] / S.LengthM;
        }

        /// <summary>All passes of one round trip; returns (B-integral, energy entering the first pass).</summary>
        public double Passes1D(Complex[] ax, Complex[] ay, FrequencyGrid g, out double gainedJ)
        {
            Prepare(g);
            var keep = Crystal.SaveN2(); double B = 0; gainedJ = 0;
            for (int k = 0; k < Passes; k++) { var st = Crystal.Pass1D(ax, ay, _gd, S.Spm ? S.N2 : 0, 1, k % 2 == 1); B += st.BIntegralRad; gainedJ += st.EnergyGainedJ; }
            Crystal.RestoreN2(keep);
            return B;
        }

        /// <summary>Relax the inversion towards the cw steady state for an intracavity pulse energy E (J) at f_rep.</summary>
        public void Update(double energyJ, double frep, double[] spectralEnergy, double[] omegaAbs)
        {
            double se = 0, sa = 0, w = 0, tot = 0;
            for (int i = 0; i < omegaAbs.Length; i++)
            {
                if (spectralEnergy[i] <= 0 || omegaAbs[i] <= 0) continue;
                se += spectralEnergy[i] * S.Medium.SigmaE(omegaAbs[i]); sa += spectralEnergy[i] * S.Medium.SigmaA(omegaAbs[i]); w += spectralEnergy[i] * omegaAbs[i]; tot += spectralEnergy[i];
            }
            if (tot <= 0) return;
            se /= tot; sa /= tot; w /= tot;
            double phiS = Passes * energyJ * frep / (GainMedium.H * w / (2 * Math.PI) * ModeAreaM2);
            Crystal.RelaxTowardsCw(phiS, se, sa, Relaxation);
        }

        /// <summary>Saturation energy of the gain for the QML criterion: h nu A / (m (sigma_e + sigma_a)).</summary>
        public double FluenceSaturationEnergy(double omega) => S.Medium.SaturationFluence(omega) * ModeAreaM2 / Passes;
    }

    /// <summary>Everything an oscillator was configured with (docs/oscillators.md).</summary>
    public sealed class OscillatorSetup
    {
        public string Id, Model = "haus", Start = "noise";
        public double RepRateHz = double.NaN, OutputCoupler = 0.05;
        public SimpleGain Simple;
        public CavityGain Cavity;
        public double LossPerRoundTrip, GddS2, TodS3, SpmRadPerW;
        public SaturableAbsorber Absorber;
        public List<CompiledElement> Elements = new List<CompiledElement>();
        public int MaxRoundTrips = 5000, CheckEvery = 25, RecordSnapshots = 120, Seed = 1;
        public double Tolerance = 1e-5, SeedEnergyJ = double.NaN, ModeRadiusM = double.NaN;
        public int GridPoints = 4096;
        public JsonObject OutputPulse;    // parametric model
        public string BaseDirectory;
    }

    /// <summary>
    /// The `oscillator` element: produces the steady-state output of a mode-locked laser and replaces the incoming
    /// beam with it (place it first; the run's "pulse" sets the grid and, with "start": "pulse", the seed).
    /// Models: "parametric" (you give the output pulse), "haus" (lumped master equation: gain with bandwidth and
    /// saturation, loss, GDD/TOD, SPM, saturable absorber, iterated round trip by round trip) and "cavity" (a real
    /// gain medium plus any engine elements -- Kerr media with or without a transverse model, apertures, SESAMs,
    /// chirped mirrors, prisms -- run every round trip until the pulse reproduces itself).
    /// </summary>
    public sealed class OscillatorElement : IActiveElement
    {
        public OscillatorSetup S;
        public string Kind => "oscillator";
        public string Description => S.Model + " oscillator" + (S.RepRateHz > 0 ? " at " + (S.RepRateHz / 1e6).ToString("G4", CultureInfo.InvariantCulture) + " MHz" : "") + ", " + (S.OutputCoupler * 100).ToString("G3", CultureInfo.InvariantCulture) + " % output coupler";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => S.Model == "cavity" && S.Elements.Any(e => e.Active?.TransverseAware ?? false);

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            int no = S.GridPoints;
            if (S.Model == "parametric" || p.Grid.N <= no) return Run(ctx, p);
            // iterate on a smaller window (same time step and carrier): the central part of the run's window;
            // the result is embedded back exactly (zero padding in time)
            var big = p.Grid; var small = FrequencyGrid.Create(no, big.DTime, big.OmegaCenter);
            int off = big.N / 2 - no / 2;
            Complex[] Crop(Complex[] a) { var t = SpectralTransform.FreqToTime(a); var c = new Complex[no]; Array.Copy(t, off, c, 0, no); return SpectralTransform.TimeToFreq(c); }
            Complex[] Embed(Complex[] a) { var t = SpectralTransform.FreqToTime(a); var c = new Complex[big.N]; Array.Copy(t, 0, c, off, no); return SpectralTransform.TimeToFreq(c); }
            var ps = new PulseField(small, Crop(p.Ax), Crop(p.Ay), p.RepRateHz) { Profile = p.Profile };
            ctx.Note(string.Format(CultureInfo.InvariantCulture, "oscillator iterated on {0} points ({1:G4} ps window) of the run's {2}-point grid (gridPoints)", no, no * big.DTime * 1e12, big.N));
            var q = Run(ctx, ps);
            return new PulseField(big, Embed(q.Ax), Embed(q.Ay), q.RepRateHz) { Profile = q.Profile };
        }

        private PulseField Run(ActiveContext ctx, PulseField p)
        {
            var g = p.Grid; int n = g.N; var r = ctx.Record; var notes = r.Notes;
            double ein = p.TotalEnergyJ();
            if (S.Model == "parametric")
            {
                var ps = PulseSpec.FromJson(S.OutputPulse, S.BaseDirectory, "element " + S.Id + ".output");
                var q0 = ps.Build(g);
                double frep0 = S.RepRateHz > 0 ? S.RepRateHz : q0.RepRateHz;
                var q = new PulseField(g, q0.Ax, q0.Ay, frep0) { Profile = p.Profile };
                ctx.Energy("oscillator (new beam)", ein - q.TotalEnergyJ());
                r.Values["outputEnergyJ"] = q.TotalEnergyJ();
                if (frep0 > 0) { r.Values["repRateHz"] = frep0; r.Values["averagePowerW"] = q.TotalEnergyJ() * frep0; }
                notes.Add("parametric oscillator: output pulse given (" + ps.Describe() + ")");
                return q;
            }
            double frep = S.RepRateHz;
            if (!(frep > 0)) throw new InvalidOperationException("oscillator '" + S.Id + "' needs repRateHz (or cavityLengthM)");
            // ---------------------------------------------------------------- start
            var rng = new Random(S.Seed);
            Complex[] ax, ay = new Complex[n];
            if (S.Start == "pulse")
            {
                double sc = S.SeedEnergyJ > 0 && ein > 0 ? Math.Sqrt(S.SeedEnergyJ / ein) : 1;
                ax = p.Ax.Select(z => z * sc).ToArray();
                if (p.Ay.Any(z => z != Complex.Zero)) ay = p.Ay.Select(z => z * sc).ToArray();
                notes.Add("started from the run's pulse" + (sc != 1 ? " scaled to " + S.SeedEnergyJ.ToString("G3", CultureInfo.InvariantCulture) + " J" : ""));
            }
            else
            {
                ax = Noise(g, rng, S.SeedEnergyJ);
                notes.Add("started from noise (" + (S.SeedEnergyJ > 0 ? S.SeedEnergyJ.ToString("G3", CultureInfo.InvariantCulture) + " J" : "one photon per mode") + ", seed " + S.Seed + ")");
            }
            bool hasY = ay.Any(z => z != Complex.Zero);
            double wmode = S.ModeRadiusM;
            if (S.Cavity != null)
            {
                if (!(wmode > 0)) wmode = S.Cavity.S.ModeRadiusM > 0 ? S.Cavity.S.ModeRadiusM : (S.Cavity.S.Beam != null ? S.Cavity.S.Beam.RadiusX(S.Cavity.S.LengthM / 2) : S.Cavity.S.Pump.RadiusM);
                if (!(wmode > 0)) throw new InvalidOperationException("oscillator '" + S.Id + "': give the gain medium a modeRadiusM or beam");
                S.Cavity.Init(g, wmode);
            }
            // ---------------------------------------------------------------- round trips
            var hist = new History();
            int every = 1;   // snapshot spacing: doubles whenever the history gets too long (thinned), so any run length gives ~RecordSnapshots..2x snapshots
            PulseField cur = new PulseField(g, ax, ay, frep) { Profile = p.Profile };
            Complex[] outX = null, outY = null;
            double gddRt = double.NaN, bRt = 0, lastGain = 0, cumShift = 0;
            int stable = 0, rt = 0; bool converged = false;
            double prevE = double.NaN, prevF = double.NaN, prevC = double.NaN;
            var shiftPhase = new double[n];
            var subRecords = new List<ElementRecord>();
            for (rt = 1; rt <= S.MaxRoundTrips; rt++)
            {
                ctx.Cancel.ThrowIfCancellationRequested();
                ax = cur.Ax; ay = cur.Ay;
                double eStart = Energy(ax, ay, g), gainNow = 0; bRt = 0;
                // gain
                if (S.Simple != null) { gainNow = S.Simple.Saturated(eStart, frep); S.Simple.Apply(ax, hasY ? ay : null, g, gainNow); }
                else if (S.Cavity != null)
                {
                    var spec = Spectrum(ax, ay, g);
                    bRt += S.Cavity.Passes1D(ax, hasY ? ay : null, g, out _);
                    S.Cavity.Update(eStart, frep, spec, g.OmegaAbsolute);
                    gainNow = Math.Log(Math.Max(1e-300, Energy(ax, ay, g) / Math.Max(1e-300, eStart)));
                }
                if (S.Model == "haus")
                {
                    double l = Math.Sqrt(Math.Max(0, 1 - S.LossPerRoundTrip));
                    for (int i = 0; i < n; i++)
                    {
                        double W = g.OmegaOffset[i];
                        var h = Complex.FromPolarCoordinates(l, 0.5 * S.GddS2 * W * W + S.TodS3 * W * W * W / 6);
                        ax[i] *= h; if (hasY) ay[i] *= h;
                    }
                    var tx = SpectralTransform.FreqToTime(ax); var ty = hasY ? SpectralTransform.FreqToTime(ay) : null;
                    if (S.Absorber != null) S.Absorber.Apply(tx, ty, g.DTime);
                    if (S.SpmRadPerW != 0)
                    {
                        double pk = 0;
                        for (int i = 0; i < n; i++)
                        {
                            double P = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + (ty != null ? ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary : 0);
                            var f = Complex.FromPolarCoordinates(1, S.SpmRadPerW * P); tx[i] *= f; if (ty != null) ty[i] *= f; pk = Math.Max(pk, P);
                        }
                        bRt += S.SpmRadPerW * pk;
                    }
                    ax = SpectralTransform.TimeToFreq(tx); if (hasY) ay = SpectralTransform.TimeToFreq(ty);
                    gddRt = S.GddS2;
                    cur = new PulseField(g, ax, ay, frep) { Profile = cur.Profile };
                }
                else
                {
                    cur = new PulseField(g, ax, ay, frep) { Profile = cur.Profile };
                    if (S.Elements.Count > 0)
                    {
                        var sub = Executor.RunElements(ctx.Run, S.Elements, cur, ctx.Cancel);
                        cur = sub.Output;
                        bRt += sub.Records.Sum(x => x.BIntegralRad);
                        double gddSum = sub.Records.Sum(x => x.Own.GddS2);
                        gddRt = gddSum + (S.Cavity != null ? S.Cavity.Passes * new BulkTerm(S.Cavity.S.Host, S.Cavity.S.LengthM).DispersionAt(g.OmegaCenter).GddS2 : 0);
                        if (rt == 1 || rt % every == 0) { subRecords.Clear(); subRecords.AddRange(sub.Records); }
                    }
                    ax = cur.Ax; ay = cur.Ay;
                }
                // output coupler
                double toc = S.OutputCoupler, s1 = Math.Sqrt(toc), s2 = Math.Sqrt(1 - toc);
                outX = ax.Select(z => z * s1).ToArray(); outY = ay.Select(z => z * s1).ToArray();
                ax = ax.Select(z => z * s2).ToArray(); ay = ay.Select(z => z * s2).ToArray();
                // keep the pulse in the middle of the window (repetition-rate drift)
                double tc = Centroid(ax, ay, g);
                if (Math.Abs(tc) > 2 * g.DTime)
                {
                    cumShift += tc;
                    for (int i = 0; i < n; i++) { var f = Complex.FromPolarCoordinates(1, -g.OmegaOffset[i] * tc); ax[i] *= f; ay[i] *= f; outX[i] *= f; outY[i] *= f; }
                }
                cur = new PulseField(g, ax, ay, frep) { Profile = cur.Profile };
                lastGain = gainNow;
                // history and convergence
                double eOut = Energy(outX, outY, g);
                bool snap = rt == 1 || rt % every == 0;
                hist.Add(rt, eOut, eStart, gainNow, snap ? new PulseField(g, outX, outY, frep) : null);
                if (hist.Snaps.Count > 2 * Math.Max(4, S.RecordSnapshots)) { hist.Thin(); every *= 2; }
                if (rt % S.CheckEvery == 0)
                {
                    var tmp = new PulseField(g, outX, outY, frep);
                    double f = FwhmOf(tmp), c = SpectralCentroid(tmp);
                    // energy, duration and spectral centroid repeat (over checkEvery round trips)
                    bool ok = !double.IsNaN(prevE) && Math.Abs(eOut - prevE) <= S.Tolerance * eOut * S.CheckEvery
                              && Math.Abs(f - prevF) <= 10 * S.Tolerance * f * S.CheckEvery + g.DTime && Math.Abs(c - prevC) <= 0.05 * g.DOmega;
                    stable = ok ? stable + 1 : 0;
                    prevE = eOut; prevF = f; prevC = c;
                    if (stable >= 3) { converged = true; break; }
                }
            }
            rt = Math.Min(rt, S.MaxRoundTrips);
            // ---------------------------------------------------------------- output and diagnostics
            var outp = new PulseField(g, outX, outY, frep) { Profile = cur.Profile };
            var intra = new PulseField(g, outX.Select(z => z / Math.Sqrt(S.OutputCoupler)).ToArray(), outY.Select(z => z / Math.Sqrt(S.OutputCoupler)).ToArray(), frep);
            double eo = outp.TotalEnergyJ(), ei = intra.TotalEnergyJ();
            var v = r.Values;
            v["repRateHz"] = frep; v["roundTrips"] = rt; v["converged"] = converged ? 1 : 0;
            v["outputEnergyJ"] = eo; v["averagePowerW"] = eo * frep; v["intracavityEnergyJ"] = ei; v["intracavityAveragePowerW"] = ei * frep;
            v["netGainPerRoundTrip"] = lastGain; v["bIntegralPerRoundTripRad"] = bRt;
            if (!double.IsNaN(gddRt)) v["gddPerRoundTripS2"] = gddRt;
            v["roundTripTimingOffsetS"] = cumShift / Math.Max(1, rt);
            var sum = PulseMetrics.Summarize(outp);
            v["outputFwhmS"] = sum.FwhmS; v["outputSpectralFwhmM"] = sum.SpectralFwhmM; v["timeBandwidthProduct"] = sum.TimeBandwidthProduct;
            // soliton area theorem: tau = 3.53 |GDD| / (delta E), delta = B / P_peak
            var pi = PulseMetrics.TemporalPower(intra); double ppk = pi.Max();
            if (!double.IsNaN(gddRt) && gddRt < 0 && bRt > 0 && ppk > 0)
            {
                double delta = bRt / ppk, tsol = 3.53 * Math.Abs(gddRt) / (delta * ei);
                v["solitonFwhmS"] = tsol; v["fwhmOverSoliton"] = sum.FwhmS / tsol;
            }
            // multipulsing / cw background
            int pulses = Diagnostics.CountPulses(pi, 0.1);
            v["pulsesInCavity"] = pulses;
            double bg = Diagnostics.BackgroundFraction(pi, g.DTime, sum.FwhmS);
            v["backgroundFraction"] = bg;
            if (pulses > 1) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Oscillator '{0}': {1} pulses circulate in the cavity (multi-pulsing) -- reduce the pump / energy or increase the dispersion.", S.Id, pulses));
            else if (bg > 0.05) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Oscillator '{0}': {1:P1} of the energy is outside the pulse (cw breakthrough or an unsettled background).", S.Id, bg));
            if (!converged) ctx.Result.Warnings.Add("Oscillator '" + S.Id + "': no steady state within " + S.MaxRoundTrips + " round trips (tolerance " + S.Tolerance.ToString("G2", CultureInfo.InvariantCulture) + ").");
            // QML criterion (Hoenninger 1999): E_p^2 > E_sat,L E_sat,A dR
            var abs = S.Absorber ?? S.Elements.Select(e => (e.Active as SaturableAbsorberElement)?.A).FirstOrDefault(a => a != null);
            double esatL = S.Cavity != null ? S.Cavity.FluenceSaturationEnergy(g.OmegaCenter) : (S.Simple?.QmlSaturationEnergyJ ?? double.NaN);
            if (abs != null && abs.Kind == "slow" && esatL > 0)
            {
                double qml = ei * ei / (esatL * abs.SaturationEnergyJ * abs.ModulationDepth);
                v["qmlParameter"] = qml; v["gainSaturationEnergyJ"] = esatL;
                if (qml < 1) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Oscillator '{0}': Q-switched mode-locking expected (E_p^2 / (E_sat,L E_sat,A dR) = {1:G3} < 1, Hoenninger et al. 1999); the simulation does not resolve the slow gain dynamics that would show it.", S.Id, qml));
            }
            if (S.Cavity != null)
            {
                v["inversionFraction"] = S.Cavity.Crystal.MeanInversionFraction();
                v["smallSignalGainPerPass"] = S.Cavity.Crystal.SmallSignalGain(new[] { g.OmegaCenter })[0];
                double absJ = 0; S.Cavity.Crystal.PumpCw(1e-3 * S.Cavity.S.Medium.LifetimeS, 4, true); absJ = S.Cavity.Crystal.AbsorbedPumpJ.Sum();
                v["absorbedPumpPowerW"] = absJ / (1e-3 * S.Cavity.S.Medium.LifetimeS);
                v["opticalEfficiency"] = eo * frep / Math.Max(1e-300, S.Cavity.S.Pump.PowerW);
            }
            if (hist.Snaps.Count == 0 || hist.Snaps[hist.Snaps.Count - 1].Rt != rt) hist.Snaps.Add((rt, outp));
            hist.Write(r, g);
            foreach (var sr in subRecords) r.Notes.Add(string.Format(CultureInfo.InvariantCulture, "cavity element {0}: B = {1:G3} rad, GDD = {2:G4} fs^2{3}", sr.Id, sr.BIntegralRad, sr.Own.GddS2 * 1e30,
                sr.BeamRadiusXM > 0 ? string.Format(CultureInfo.InvariantCulture, ", beam radius {0:G4} um", sr.BeamRadiusXM * 1e6) : ""));
            notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} after {1} round trips: {2:G4} J out ({3:G4} W), FWHM {4:G4} fs, {5:G4} nm",
                converged ? "steady state" : "stopped", rt, eo, eo * frep, sum.FwhmS * 1e15, sum.SpectralFwhmM * 1e9));
            ctx.Energy("oscillator (new beam)", ein - eo);
            ctx.Record.Representation = S.Model + " model, " + n + " samples";
            return outp;
        }

        private static Complex[] Noise(FrequencyGrid g, Random rng, double energyJ)
        {
            int n = g.N; var a = new Complex[n]; double tot = 0;
            for (int i = 0; i < n; i++)
            {
                double w = g.OmegaAbsolute[i]; if (w <= 0) continue;
                double e = 1.054571817e-34 * w;                        // one photon per bin
                double u1 = Math.Max(1e-300, rng.NextDouble()), u2 = rng.NextDouble();
                double amp = Math.Sqrt(-Math.Log(u1)) * Math.Sqrt(e * n / g.DTime);
                a[i] = Complex.FromPolarCoordinates(amp, 2 * Math.PI * u2);
                tot += amp * amp * g.DTime / n;
            }
            if (energyJ > 0 && tot > 0) { double s = Math.Sqrt(energyJ / tot); for (int i = 0; i < n; i++) a[i] *= s; }
            return a;
        }

        internal static double Energy(Complex[] ax, Complex[] ay, FrequencyGrid g) => EnergyNormalization.ComputeEnergyJFromFreqDomain(ax, ay, g);

        private static double[] Spectrum(Complex[] ax, Complex[] ay, FrequencyGrid g)
        {
            var s = new double[g.N]; double k = g.DTime / g.N;
            for (int i = 0; i < g.N; i++) s[i] = (ax[i].Real * ax[i].Real + ax[i].Imaginary * ax[i].Imaginary + ay[i].Real * ay[i].Real + ay[i].Imaginary * ay[i].Imaginary) * k;
            return s;
        }

        /// <summary>Energy centroid in time (circular: around the peak, so a pulse straddling the edge is handled).</summary>
        private static double Centroid(Complex[] ax, Complex[] ay, FrequencyGrid g)
        {
            var tx = SpectralTransform.FreqToTime(ax); var ty = SpectralTransform.FreqToTime(ay); int n = g.N;
            var P = new double[n]; int ip = 0;
            for (int i = 0; i < n; i++) { P[i] = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary; if (P[i] > P[ip]) ip = i; }
            double s = 0, sw = 0;
            for (int k = -n / 2; k < n / 2; k++) { int i = ((ip + k) % n + n) % n; s += P[i] * k; sw += P[i]; }
            double off = sw > 0 ? s / sw : 0;
            return (ip - n / 2 + off) * g.DTime;
        }

        private static double FwhmOf(PulseField p) => PulseMetrics.Fwhm(p.Grid.Time, PulseMetrics.TemporalPower(p));
        private static double SpectralCentroid(PulseField p) => PulseMetrics.Moments(p.Grid.OmegaAbsolute, p.SpectralIntensity()).Mean;

        /// <summary>Per-round-trip history and decimated snapshots of the output (for the evolution plots).</summary>
        private sealed class History
        {
            public List<double> Rt = new List<double>(), Eout = new List<double>(), Ein = new List<double>(), Gain = new List<double>();
            public List<(int Rt, PulseField P)> Snaps = new List<(int, PulseField)>();
            public void Add(int rt, double eOut, double eIn, double gain, PulseField snap)
            {
                Rt.Add(rt); Eout.Add(eOut); Ein.Add(eIn); Gain.Add(gain);
                if (snap != null) Snaps.Add((rt, snap));
            }

            public void Thin()
            {
                var keep = new List<(int, PulseField)>();
                for (int k = 0; k < Snaps.Count; k++) if (k % 2 == 0) keep.Add(Snaps[k]);
                Snaps = keep;
            }

            public void Write(ElementRecord r, FrequencyGrid g)
            {
                r.AddArray("roundTrip", Rt.ToArray(), "", "round-trip number");
                r.AddArray("outputEnergyPerRoundTrip", Eout.ToArray(), "J", "output-coupled energy after each round trip");
                r.AddArray("netGainPerRoundTrip", Gain.ToArray(), "", "ln of the saturated gain of each round trip");
                if (Snaps.Count == 0) return;
                // a common window around the final pulse: +-6 FWHM (at least 1 % of the window), 256 points
                var last = Snaps[Snaps.Count - 1].P;
                var pw = PulseMetrics.TemporalPower(last); double pk = pw.Max();
                int lo = 0, hi = pw.Length - 1; while (lo < hi && pw[lo] < 1e-3 * pk) lo++; while (hi > lo && pw[hi] < 1e-3 * pk) hi--;
                int pad = Math.Max(g.N / 100, hi - lo); lo = Math.Max(0, lo - pad); hi = Math.Min(g.N - 1, hi + pad);
                int nt = 256; var tAx = Enumerable.Range(0, nt).Select(k => g.Time[lo] + (g.Time[hi] - g.Time[lo]) * k / (nt - 1)).ToArray();
                var sp = last.SpectralIntensity(); double spk = sp.Max(); int a = 0, b = sp.Length - 1;
                while (a < b && sp[a] < 1e-4 * spk) a++; while (b > a && sp[b] < 1e-4 * spk) b--;
                int spad = Math.Max(4, (b - a) / 2); a = Math.Max(1, a - spad); b = Math.Min(sp.Length - 1, b + spad);
                var wAx = Enumerable.Range(0, nt).Select(k => g.OmegaAbsolute[a] + (g.OmegaAbsolute[b] - g.OmegaAbsolute[a]) * k / (nt - 1)).ToArray();
                var P = new double[Snaps.Count * nt]; var S = new double[Snaps.Count * nt];
                for (int s = 0; s < Snaps.Count; s++)
                {
                    var tp = PulseMetrics.TemporalPower(Snaps[s].P); var ss = Snaps[s].P.SpectralIntensity();
                    for (int k = 0; k < nt; k++)
                    {
                        P[s * nt + k] = Interp(g.Time, tp, tAx[k]);
                        S[s * nt + k] = Interp(g.OmegaAbsolute, ss, wAx[k]) * g.DTime / g.N;
                    }
                }
                r.AddArray("evolutionRoundTrip", Snaps.Select(x => (double)x.Rt).ToArray(), "", "round trips of the evolution snapshots");
                r.AddArray("evolutionTimeS", tAx, "s", "time axis of the evolution snapshots");
                r.AddArray("evolutionPower", P, "W", "output power vs time for each snapshot [snapshot, time]", Snaps.Count, nt);
                r.AddArray("evolutionWavelengthM", wAx.Select(w => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / w).ToArray(), "m", "wavelength axis of the evolution snapshots (descending)");
                r.AddArray("evolutionSpectrum", S, "J/bin", "output spectrum for each snapshot [snapshot, frequency]", Snaps.Count, nt);
            }

            private static double Interp(double[] x, double[] y, double v)
            {
                if (v <= x[0]) return y[0]; if (v >= x[x.Length - 1]) return y[y.Length - 1];
                double dx = x[1] - x[0]; int i = (int)((v - x[0]) / dx); i = Math.Min(x.Length - 2, Math.Max(0, i));
                double t = (v - x[i]) / (x[i + 1] - x[i]); return y[i] * (1 - t) + y[i + 1] * t;
            }
        }
    }

    /// <summary>Mode-locking diagnostics on a temporal power trace.</summary>
    public static class Diagnostics
    {
        /// <summary>Number of separate pulses: regions above <paramref name="level"/> x peak, separated by gaps below level/2.</summary>
        public static int CountPulses(double[] p, double level = 0.1)
        {
            double pk = p.Max(); if (pk <= 0) return 0;
            int n = p.Length, count = 0; bool inside = false;
            // start at the global minimum so a pulse across the window edge is counted once
            int start = Array.IndexOf(p, p.Min());
            for (int k = 0; k < n; k++)
            {
                double v = p[(start + k) % n];
                if (!inside && v > level * pk) { inside = true; count++; }
                else if (inside && v < 0.5 * level * pk) inside = false;
            }
            return count;
        }

        /// <summary>Energy fraction more than 5 FWHM (or 5 % of the window) away from the peak.</summary>
        public static double BackgroundFraction(double[] p, double dt, double fwhm)
        {
            int n = p.Length, ip = Array.IndexOf(p, p.Max());
            double half = Math.Max(5 * (double.IsNaN(fwhm) ? 0 : fwhm), 0.05 * n * dt);
            int h = (int)Math.Ceiling(half / dt);
            double tot = p.Sum(), inner = 0;
            for (int k = -h; k <= h; k++) inner += p[((ip + k) % n + n) % n];
            return tot > 0 ? Math.Max(0, 1 - inner / tot) : 0;
        }
    }

    public static class OscillatorCompiler
    {
        public static void Compile(CompilerServices c, SystemElement e, CompiledElement ce)
        {
            var j = e.Json; string origin = "element " + e.Id;
            var s = new OscillatorSetup { Id = e.Id, BaseDirectory = c.System.BaseDirectory ?? c.Run.BaseDirectory };
            s.Model = j.GetString("model", "haus").ToLowerInvariant();
            if (s.Model != "parametric" && s.Model != "haus" && s.Model != "cavity") throw new JsonException(origin + ": model must be parametric, haus or cavity");
            s.RepRateHz = c.Num(e, "repRateHz", double.NaN);
            double L = c.Num(e, "cavityLengthM", double.NaN);
            if (double.IsNaN(s.RepRateHz) && L > 0) s.RepRateHz = PhysicalConstants.SpeedOfLightMPerS / (j.GetBool("ring", false) ? L : 2 * L);
            s.OutputCoupler = c.Num(e, "outputCoupler", 0.05);
            if (!(s.OutputCoupler > 0 && s.OutputCoupler < 1)) throw new JsonException(origin + ": outputCoupler (power transmission) must be in (0, 1)");
            if (s.Model == "parametric")
            {
                s.OutputPulse = j.GetObject("output") ?? throw new JsonException(origin + ": the parametric model needs \"output\" (a pulse object as in the run spec)");
                ce.Active = new OscillatorElement { S = s };
                return;
            }
            s.Start = j.GetString("start", s.Model == "cavity" ? "pulse" : "noise").ToLowerInvariant();
            if (s.Start != "noise" && s.Start != "pulse") throw new JsonException(origin + ": start must be noise or pulse");
            s.SeedEnergyJ = c.Num(e, "seedEnergyJ", double.NaN);
            s.Seed = j.GetInt("seed", 1);
            s.GridPoints = j.GetInt("gridPoints", 4096);
            if (s.GridPoints < 64 || (s.GridPoints & (s.GridPoints - 1)) != 0) throw new JsonException(origin + ": gridPoints must be a power of two >= 64");
            var rto = j.GetObject("roundTrips");
            if (rto != null) { s.MaxRoundTrips = rto.GetInt("max", s.MaxRoundTrips); s.Tolerance = rto.GetDouble("tolerance", s.Tolerance); s.CheckEvery = Math.Max(1, rto.GetInt("checkEvery", s.CheckEvery)); s.RecordSnapshots = rto.GetInt("snapshots", s.RecordSnapshots); }
            else if (j.Has("roundTrips")) s.MaxRoundTrips = j.GetInt("roundTrips", s.MaxRoundTrips);
            // gain
            var go = j.GetObject("gain") ?? throw new JsonException(origin + ": needs \"gain\" ({\"smallSignalGain\", \"saturationEnergyJ\"|\"saturationPowerW\", \"bandwidthM\"} or a gain medium with a pump)");
            if (go.Has("medium"))
            {
                var gs = new SystemElement { Id = e.Id + ".gain", Type = "gain", Json = go };
                var tmp = new CompiledElement();
                GainCompiler.Compile(c, gs, tmp, false);
                var setup = ((GainElement)tmp.Active).S;
                if (setup.Pump.Kind == "pulsed") throw new JsonException(origin + ": oscillator gain media need a cw pump (or an inversion)");
                s.Cavity = new CavityGain { S = setup, Passes = Math.Max(1, go.GetInt("passesPerRoundTrip", 2)), Relaxation = go.GetDouble("relaxation", 0.2) };
                s.ModeRadiusM = setup.ModeRadiusM;
            }
            else s.Simple = SimpleGain.FromJson(go, c.Omega0, origin + ".gain");
            if (s.Model == "haus")
            {
                s.LossPerRoundTrip = c.Num(e, "lossPerRoundTrip", 0);
                s.GddS2 = c.Num(e, "gddFs2", 0) * 1e-30 + c.Num(e, "gddS2", 0);
                s.TodS3 = c.Num(e, "todFs3", 0) * 1e-45 + c.Num(e, "todS3", 0);
                s.SpmRadPerW = c.Num(e, "spmRadPerW", 0);
                if (s.Cavity != null) throw new JsonException(origin + ": the haus model takes a lumped gain (smallSignalGain, saturation, bandwidth); use model \"cavity\" for a gain medium");
            }
            var ab = j.GetObject("absorber");
            if (ab != null) s.Absorber = SaturableAbsorber.FromJson(ab, (k, f) => ab.GetDouble(k, f), null, origin + ".absorber");
            if (s.Model == "cavity")
            {
                if (s.Absorber != null) throw new JsonException(origin + ": in the cavity model put the absorber in \"elements\" ({\"type\": \"saturableAbsorber\", ...})");
                foreach (var ev in j.GetArray("elements") ?? new JsonArray())
                    s.Elements.Add(c.CompileNested(ev.AsObject(), e.Id));
                if (s.Elements.Any(x => x.Active is OscillatorElement)) throw new JsonException(origin + ": oscillators cannot be nested");
            }
            ce.Active = new OscillatorElement { S = s };
            c.Provenance.Record("element:" + e.Id, "oscillator", ce.Active.Description + "; " + (s.Simple?.Describe() ?? s.Cavity.S.Describe()), ModelSource.SystemData, "system file");
        }
    }
}
