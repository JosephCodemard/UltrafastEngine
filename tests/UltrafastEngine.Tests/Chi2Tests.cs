using System;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Crystals;
using UltrafastEngine.Engine;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// chi(2) crystals: the coupled-wave solver against closed-form results (Armstrong SHG, parametric gain, sinc^2
    /// detuning, cascaded phase), the phase-matching calculator against literature angles, QPM models, carrier
    /// changes and resampling, aux beams from saved results, the active instruments and user elements.
    /// </summary>
    public class Chi2Tests
    {
        private const double Eps0 = 8.8541878128e-12;
        private const double NIdeal = 1.7;
        private const double DIdeal = 2e-12;

        /// <summary>A dispersion-free crystal (n_e = 1.7 everywhere): type 0 (eee) is phase matched at every
        /// wavelength and there is no group-velocity mismatch, so the 1-D plane-wave equations have closed forms.</summary>
        private static JsonObject Ideal() => O("name", "ideal", "no", O("kind", "constant", "value", 1.8), "ne", O("kind", "constant", "value", NIdeal),
            "deffPmPerV", DIdeal * 1e12, "n2", 0.0);

        private static JsonObject PlaneWave(double w) => O("model", "planeWave", "radiusM", w);

        private static RunSpec NoSteep(JsonObject pulse) => Spec(pulse, O("physics", O("selfSteepening", false)));

        private static double GaussPower(double t, double p0, double fwhm) => p0 * Math.Exp(-4 * Math.Log(2) * t * t / (fwhm * fwhm));

        private static double PeakPower(double e, double fwhm) => e * Math.Sqrt(4 * Math.Log(2) / Math.PI) / fwhm;

        /// <summary>Integrate f(P(t)) P(t) dt over a Gaussian pulse.</summary>
        private static double PulseAverage(Func<double, double> f, double p0, double fwhm)
        {
            double s = 0, sw = 0; int n = 20001; double t0 = 3 * fwhm, dt = 2 * t0 / (n - 1);
            for (int i = 0; i < n; i++) { double p = GaussPower(-t0 + i * dt, p0, fwhm); s += p * f(p); sw += p; }
            return s / sw;
        }

        // ------------------------------------------------------------------------------------------ closed forms

        [Test(Category = "physics")]
        public void Shg_PlaneWave_MatchesArmstrongTanhSquared()
        {
            double lam = 1030e-9, fwhm = 1e-12, w = 100e-6, L = 5e-3;
            double kappa = W(lam) * DIdeal * Math.Sqrt(2 / (Eps0 * Math.Pow(C, 3) * NIdeal * NIdeal * NIdeal)) * Math.Sqrt(2 / (Math.PI * w * w));
            foreach (double e in new[] { 0.5e-9, 50e-9 })
            {
                var sys = Sys(O("id", "x", "type", "crystal", "crystal", Ideal(), "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0),
                    "interaction", O("process", "shg", "type", "0"), "overlap", PlaneWave(w), "chi3", false, "output", "sh"));
                var r = Go(sys, NoSteep(Gauss(lam, fwhm, e)));
                double p0 = PeakPower(e, fwhm);
                double expected = PulseAverage(p => Math.Pow(Math.Tanh(kappa * L * Math.Sqrt(p)), 2), p0, fwhm);
                Assert.Close(expected, r.Records[0].Values["conversionEfficiency"], 2e-4, 0, "SHG efficiency at " + e + " J");
                Assert.Close(2 * W(lam), r.Output.Grid.OmegaCenter, 0, 2 * r.Output.Grid.DOmega, "output carrier is the second harmonic (snapped to a grid bin)");
                // energy budget closes: main in - main out = sum of the budget entries
                var rec = r.Records[0];
                Assert.Close(rec.EnergyInJ - rec.EnergyOutJ, rec.LossJ.Values.Sum(), 1e-9, 1e-20, "energy budget");
            }
        }

        private static RunResult Opa(double pumpEnergy, double signalEnergy, double w, double L, string record = null)
        {
            var aux = A(O("role", "pump", "pulse", Gauss(1030e-9, 1e-12, pumpEnergy), "beam", O("kind", "gaussian", "radiusM", w), "delayS", 0.0));
            var el = O("id", "opa", "type", "crystal", "crystal", Ideal(), "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0),
                "interaction", O("process", "opa", "type", "0"), "overlap", PlaneWave(w), "chi3", false, "aux", aux, "output", "signal");
            if (record != null) el.Set("record", A(record));
            return Go(Sys(el), NoSteep(Gauss(1550e-9, 1e-12, signalEnergy)));
        }

        [Test(Category = "physics")]
        public void Opa_SmallSignalGain_MatchesCoshSquared_AndConservesPhotons()
        {
            double w = 100e-6, L = 5e-3, ep = 1e-6, es = 1e-15;
            double ws = W(1550e-9), wp = W(1030e-9), wi = wp - ws;
            double gamma = DIdeal * Math.Sqrt(2 / (Eps0 * Math.Pow(C, 3) * Math.Pow(NIdeal, 3))) * Math.Sqrt(2 / (Math.PI * w * w));
            var r = Opa(ep, es, w, L);
            double p0 = PeakPower(ep, 1e-12);
            // signal and pump have the same duration: P_s(t) / P_p(t) is constant, so weight by P_p
            double gain = PulseAverage(p => Math.Pow(Math.Cosh(gamma * Math.Sqrt(ws * wi * p) * L), 2), p0, 1e-12);
            double idler = PulseAverage(p => wi / ws * Math.Pow(Math.Sinh(gamma * Math.Sqrt(ws * wi * p) * L), 2), p0, 1e-12);
            var v = r.Records[0].Values;
            Assert.Greater(gain, 3, "test regime has real gain");
            // 1e-3: carriers are snapped to grid bins (omega_s omega_i shifts at the 1e-4 level, amplified by the gain)
            Assert.Close(gain, v["energy_signal_J"] / es, 1e-3, 0, "signal gain");
            Assert.Close(idler, v["energy_idler_J"] / es, 1e-3, 0, "idler energy");
            Assert.Less(Math.Abs(v["manleyRoweResidual"]), 1e-9, "Manley-Rowe");
            Assert.True(r.At.ContainsKey("opa#idler"), "idler kept as a by-product");
            Assert.True(r.Records[0].LossJ["from pump (aux)"] < 0, "aux energy enters the budget as a negative (gain) entry");
        }

        [Test(Category = "physics")]
        public void Shg_AngleDetuning_FollowsSincSquared()
        {
            double eta0 = double.NaN;
            foreach (double det in new[] { 0.0, 0.05, 0.1 })
            {
                var sys = Sys(O("id", "bbo", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                    "detuneDeg", det, "overlap", PlaneWave(100e-6), "chi3", false, "output", "sh"));
                var r = Go(sys, NoSteep(Gauss(1030e-9, 3e-12, 1e-9)));
                var v = r.Records[0].Values;
                if (det == 0) { eta0 = v["conversionEfficiency"]; Assert.Less(Math.Abs(v["deltaK0PerM"]) * 1e-3, 1e-3, "phase matched at the auto cut"); continue; }
                double a = v["deltaK0PerM"] * 1e-3 / 2;
                Assert.Greater(Math.Abs(a), 0.3, "detuning is significant");
                Assert.Close(Math.Pow(Math.Sin(a) / a, 2), v["conversionEfficiency"] / eta0, 0.01, 0, "sinc^2 at " + det + " deg");
            }
        }

        [Test(Category = "physics")]
        public void CascadedChi2_PhaseMatchesEffectiveKerrFormula()
        {
            // strongly mismatched SHG (QPM grating on a phase-matched crystal: dk = -K_G): the fundamental picks up
            // phi = kappa^2 P / dk (L - sin(dk L) / dk), kappa = w G O (deff -> 2/pi deff)
            double lam = 1030e-9, w = 30e-6, L = 5e-3, period = 50e-6, fwhm = 1e-12;
            double PhaseAtPeak(double energy)
            {
                var sys = Sys(O("id", "x", "type", "crystal", "crystal", Ideal(), "lengthM", L, "cut", O("thetaDeg", 90, "phiDeg", 0),
                    "interaction", O("process", "shg", "type", "0", "qpm", O("periodM", period)), "overlap", PlaneWave(w), "chi3", false, "output", "fundamental"));
                var r = Go(sys, NoSteep(Gauss(lam, fwhm, energy)));
                var t = r.Output.TemporalAx(); var tt = r.Output.Grid.Time;
                int k = Enumerable.Range(0, t.Length).OrderByDescending(i => t[i].Magnitude).First();
                return t[k].Phase;
            }
            double e = 500e-9;
            double phi = PhaseAtPeak(e) - PhaseAtPeak(1e-15);
            phi = Math.Atan2(Math.Sin(phi), Math.Cos(phi));
            double kappa = W(lam) * DIdeal * 2 / Math.PI * Math.Sqrt(2 / (Eps0 * Math.Pow(C, 3) * Math.Pow(NIdeal, 3))) * Math.Sqrt(2 / (Math.PI * w * w));
            double dk = -2 * Math.PI / period;
            double expected = kappa * kappa * PeakPower(e, fwhm) / dk * (L - Math.Sin(dk * L) / dk);
            Assert.Greater(Math.Abs(expected), 0.05, "measurable cascaded phase");
            Assert.Close(expected, phi, 0.02, 0, "cascaded phase at the peak");
        }

        // ------------------------------------------------------------------------------------------ QPM

        [Test(Category = "physics")]
        public void Qpm_AveragedAndDomainResolvedModelsAgree()
        {
            double Eta(string model)
            {
                var sys = Sys(O("id", "ppln", "type", "crystal", "crystal", "MgO:LiNbO3", "lengthM", 1e-3, "temperatureC", 24.5,
                    "interaction", O("process", "shg", "qpm", O("model", model)), "overlap", PlaneWave(50e-6), "chi3", false, "output", "sh"));
                var r = Go(sys, NoSteep(Gauss(1550e-9, 5e-12, 2e-9)));
                Assert.Close(19.40e-6, r.Records[0].Values["qpmPeriodM"], 0.01, 0, "period");
                return r.Records[0].Values["conversionEfficiency"];
            }
            double avg = Eta("averaged"), dom = Eta("domains");
            Assert.Greater(avg, 1e-4, "measurable conversion");
            Assert.Close(avg, dom, 0.02, 0, "averaged (2/pi deff) vs domain-resolved");
        }

        // ------------------------------------------------------------------------------------------ calculator

        private static JsonObject Pm(string crystal, string process, string type, JsonObject wl, JsonObject extra = null)
        {
            var req = O("crystal", crystal, "process", process, "type", type, "wavelengthsM", wl, "lengthM", 1e-3);
            if (extra != null) foreach (var kv in extra) req.Set(kv.Key, kv.Value);
            return PhaseMatchCalculator.Solve(req).GetObject("best") ?? throw new AssertionException("no phase matching for " + crystal);
        }

        [Test(Category = "physics")]
        public void PhaseMatchCalculator_ReproducesLiteratureAngles()
        {
            // BBO type-I SHG 800 nm: 29.2 deg; 1030 nm: 23.4 deg (SNLO)
            Assert.Close(29.2, Pm("BBO", "shg", "I", O("fundamental", 800e-9)).GetDouble("thetaDeg", 0), 0, 0.15, "BBO 800 nm SHG");
            Assert.Close(23.4, Pm("BBO", "shg", "I", O("fundamental", 1030e-9)).GetDouble("thetaDeg", 0), 0, 0.15, "BBO 1030 nm SHG");
            // KDP type-I SHG 1064 nm: 41.2 deg
            Assert.Close(41.2, Pm("KDP", "shg", "I", O("fundamental", 1064e-9)).GetDouble("thetaDeg", 0), 0, 0.2, "KDP 1064 nm SHG");
            // LBO type-I SHG in XY: phi = 11.6 deg (1064 nm), 13.8 deg (1030 nm), 31.7 deg (800 nm); |deff| ~ 0.83 pm/V
            var lbo = Pm("LBO", "shg", "I", O("fundamental", 1064e-9));
            Assert.Equal("XY", lbo.GetString("plane"), "LBO plane");
            Assert.Close(11.6, lbo.GetDouble("phiDeg", 0), 0, 0.15, "LBO phi at 1064 nm");
            Assert.Close(13.8, Pm("LBO", "shg", "I", O("fundamental", 1030e-9)).GetDouble("phiDeg", 0), 0, 0.15, "LBO phi at 1030 nm");
            Assert.Close(31.7, Pm("LBO", "shg", "I", O("fundamental", 800e-9)).GetDouble("phiDeg", 0), 0, 0.2, "LBO phi at 800 nm");
            Assert.Close(0.83, Math.Abs(lbo.GetDouble("deffPmPerV", 0)), 0.05, 0, "LBO deff");
            // KTP type-II SHG 1064 nm in XY: phi ~ 23.5-24.5 deg, deff ~ 3.6 pm/V
            var ktp = Pm("KTP", "shg", "II", O("fundamental", 1064e-9));
            Assert.Close(24.0, ktp.GetDouble("phiDeg", 0), 0, 0.8, "KTP phi");
            Assert.Close(3.6, Math.Abs(ktp.GetDouble("deffPmPerV", 0)), 0.1, 0, "KTP deff");
            // BBO non-collinear OPA pumped at 515 nm, signal 800 nm: magic angle ~2.5 deg (pump-signal), signal ~22 deg
            var nc = Pm("BBO", "opa", "I", O("pump", 515e-9, "signal", 800e-9), O("noncollinearDeg", "magic"));
            Assert.Close(2.55, nc.GetDouble("noncollinearDeg", 0), 0, 0.15, "magic angle");
            Assert.Close(24.6, nc.GetDouble("pumpThetaDeg", 0), 0, 0.3, "pump angle to the optic axis (the crystal cut)");
            // MgO:PPLN first-order QPM periods
            Assert.Close(19.4e-6, Pm("MgO:LiNbO3", "shg", "0", O("fundamental", 1550e-9), O("qpm", true)).GetDouble("qpmPeriodM", 0), 0.01, 0, "PPLN 1550 SHG period");
            Assert.Close(6.95e-6, Pm("MgO:LiNbO3", "shg", "0", O("fundamental", 1064e-9), O("qpm", true)).GetDouble("qpmPeriodM", 0), 0.01, 0, "PPLN 1064 SHG period");
        }

        // ------------------------------------------------------------------------------------------ grids

        [Test]
        public void GridResampler_ExactRoundTripToADerivedGrid()
        {
            var g = FrequencyGrid.Create(1024, 10e-15, W(1030e-9));
            var a = new Complex[g.N];
            for (int i = 0; i < g.N; i++) { double d = (g.OmegaAbsolute[i] - g.OmegaCenter) / 2e13; a[i] = Complex.FromPolarCoordinates(Math.Exp(-d * d), 0.3 * d * d); }
            var g2 = GridResampler.Derive(g, 1, -1, g.OmegaCenter + 37 * g.DOmega); // wider window, finer dt, shifted carrier
            var b = GridResampler.Resample(a, g, g2, out double lost1);
            var back = GridResampler.Resample(b, g2, g, out double lost2);
            double err = a.Zip(back, (x, y) => (x - y).Magnitude).Max() / a.Max(z => z.Magnitude);
            Assert.Less(lost1 + lost2, 1e-9, "nothing lost");
            Assert.Less(err, 1e-9, "round trip");
        }

        [Test(Category = "system")]
        public void GridPlanner_StronglyChirpedInputIsNotWrapped()
        {
            // 15 fs TL stretched to ~0.6 ps: far beyond the provisional 30 x TL window
            var pulse = Gauss(800e-9, 15e-15, 1e-9); pulse.Set("chirp", O("gddFs2", 3000));
            var r = Go(Sys(O("id", "air", "type", "gap", "lengthM", 1e-3)), Spec(pulse));
            var p = Results.PulseMetrics.TemporalPower(r.Input); int n = p.Length;
            Assert.Less(Math.Max(p[0], p[n - 1]) / p.Max(), 1e-6, "input pulse inside the window");
            double tau = 15e-15 * Math.Sqrt(1 + Math.Pow(4 * Math.Log(2) * 3000e-30 / (15e-15 * 15e-15), 2));
            Assert.Close(PeakPower(1e-9, tau), p.Max(), 0.01, 0, "peak power of the stretched Gaussian");
        }

        [Test(Category = "system")]
        public void CarrierChange_ShgThenGlass_ReplansTheGridAndKeepsEnergy()
        {
            var sys = Sys(
                O("id", "bbo", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                  "beam", O("kind", "gaussian", "w0M", 200e-6), "output", "sh"),
                O("id", "glass", "type", "bulk", "material", "N-BK7", "lengthM", 10e-3));
            var r = Go(sys, Spec(Gauss(1030e-9, 300e-15, 20e-6)));
            Assert.Close(2 * W(1030e-9), 2 * Math.PI * C / r.Records[0].CarrierWavelengthM, 2e-3, 0, "carrier after the crystal");
            Assert.Close(r.Records[0].EnergyOutJ, r.Output.TotalEnergyJ(), 1e-6, 0, "glass is lossless (no coating)");
            Assert.Close(2 * W(1030e-9), SpectralCentroid(r.Output), 2e-3, 0, "output spectrum centred at the SH");
            Assert.True(!ReferenceEquals(r.Input.Grid, r.Output.Grid), "output on its own grid");
            // N-BK7 GDD at 515 nm ~ 94 fs^2/mm: the SH from a 300 fs pulse barely changes, but the dispersion was applied
            Assert.Greater(r.Records[1].Own.GddS2, 0.8e-27, "glass dispersion at the SH carrier");
        }

        // ------------------------------------------------------------------------------------------ aux from saved results

        [Test(Category = "system")]
        public void AuxBeam_FromSavedResults_PumpsAnOpa()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf_aux_" + Guid.NewGuid().ToString("N"));
            try
            {
                var shg = Sys(O("id", "bbo", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                    "overlap", PlaneWave(150e-6), "output", "sh"));
                var r1 = Go(shg, Spec(Gauss(1030e-9, 500e-15, 50e-6)));
                r1.WriteAll(dir);
                double pumpE = r1.Output.TotalEnergyJ();
                var opa = Sys(O("id", "opa", "type", "crystal", "crystal", "BBO", "lengthM", 2e-3, "interaction", O("process", "opa", "type", "I"),
                    "overlap", PlaneWave(150e-6), "aux", A(O("role", "pump", "from", dir, "beam", O("kind", "gaussian", "radiusM", 150e-6), "delayS", "auto")),
                    "output", "signal"));
                var r2 = Go(opa, Spec(Gauss(800e-9, 500e-15, 1e-9)));
                Assert.Close(pumpE, -r2.Records[0].LossJ["from pump (aux)"], 1e-6, 0, "aux energy = saved output energy");
                Assert.Greater(r2.Output.TotalEnergyJ(), 2e-9, "signal amplified");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ------------------------------------------------------------------------------------------ instruments

        [Test(Category = "system")]
        public void ActiveInstruments_ConversionDelayScanAndPhaseMatchingCurve()
        {
            double w = 100e-6;
            var aux = A(O("role", "pump", "pulse", Gauss(1030e-9, 1e-12, 20e-9), "beam", O("kind", "gaussian", "radiusM", w), "delayS", 0.0));
            var sys = Sys(O("id", "opa", "type", "crystal", "crystal", Ideal(), "lengthM", 5e-3, "cut", O("thetaDeg", 90, "phiDeg", 0),
                "interaction", O("process", "opa", "type", "0"), "overlap", PlaneWave(w), "chi3", false, "aux", aux, "output", "signal"));
            var ins = A(O("kind", "conversion", "at", "opa"),
                        O("kind", "delayScan", "at", "opa", "fromS", -2e-12, "toS", 2e-12, "points", 9),
                        O("kind", "phaseMatchingCurve", "at", "opa", "scan", "wavelength", "rangeM", 100e-9, "points", 51));
            var r = Go(sys, Spec(Gauss(1550e-9, 1e-12, 1e-15), O("instruments", ins, "physics", O("selfSteepening", false))));
            Assert.Equal(0, r.Warnings.Count(x => x.StartsWith("Instrument")), "instrument warnings: " + string.Join("; ", r.Warnings));
            var conv = r.Instruments.First(i => i.Kind == "conversion");
            Assert.Close(20e-9, conv.Values.GetDouble("auxEnergyInJ", 0), 1e-6, 0, "aux energy in");
            Assert.Close(r.Records[0].Values["energy_idler_J"], conv.Values.GetDouble("idlerEnergyJ", 0), 1e-9, 0, "idler energy");
            var scan = r.Instruments.First(i => i.Kind == "delayScan");
            Assert.Close(0, scan.Values.GetDouble("optimalDelayS", 1), 0, 1e-15, "best timing at zero delay");
            Assert.Close(r.Output.TotalEnergyJ(), scan.Values.GetDouble("maxOutputEnergyJ", 0), 1e-6, 0, "scan reproduces the run at its delay");
            var pmc = r.Instruments.First(i => i.Kind == "phaseMatchingCurve");
            Assert.Close(1.0, pmc.Arrays.First(a => a.Name.EndsWith("efficiency_norm")).Data.Min(), 1e-9, 0, "dispersion-free crystal: phase matched everywhere");
        }

        [Test(Category = "system")]
        public void BeamProfileInstrument_ReportsRadiusAfterARadialElement()
        {
            double w0 = 300e-6;
            var sys = Sys(O("id", "free", "type", "gap", "material", "Vacuum", "lengthM", 0.1, "beam", O("kind", "gaussian", "w0M", w0, "wavelengthM", 1030e-9),
                "transverse", O("model", "radial", "points", 64, "radiusM", 1.5e-3)));
            var r = Go(sys, Spec(Gauss(1030e-9, 300e-15, 1e-6), O("instruments", A(O("kind", "beamProfile", "at", "free")))));
            var bp = r.Instruments.First(i => i.Kind == "beamProfile");
            double zr = Math.PI * w0 * w0 / 1030e-9;
            Assert.Close(w0 * Math.Sqrt(1 + Math.Pow(0.1 / zr, 2)), bp.Values.GetDouble("radiusXM", 0), 0.01, 0, "1/e^2 radius");
            Assert.Close(1.0, bp.Values.GetDouble("m2", 0), 0.02, 0, "M^2");
            Assert.True(bp.Arrays.Any(a => a.Name.EndsWith("fluence_j_m2")), "radial fluence array");
        }

        // ------------------------------------------------------------------------------------------ user element, spatial

        [Test(Category = "system")]
        public void UserElement_CSharpCallbackAppliesSpectralPhase()
        {
            var reg = ModelRegistry.CreateDefault();
            double gdd = 2000e-30;
            reg.RegisterCallback("dazzler", (ElementCallback)(ctx =>
            {
                for (int i = 0; i < ctx.N; i++)
                {
                    double d = ctx.Omega[i] - ctx.Omega0;
                    var z = new Complex(ctx.AxRe[i], ctx.AxIm[i]) * Complex.FromPolarCoordinates(1, 0.5 * gdd * d * d);
                    ctx.AxRe[i] = z.Real; ctx.AxIm[i] = z.Imaginary;
                }
            }));
            var sys = Sys(O("id", "dz", "type", "user", "callback", "@dazzler", "params", O("note", "test")));
            var r = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-6)), reg);
            Assert.Close(gdd, r.Output.GdGddTodX().GddAtCarrierS2, 0.01, 0, "applied GDD");
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-12, 0, "energy");
        }

        [Test(Category = "physics")]
        public void Shg_RadialModelAgreesWithGaussianOverlapAtLowConversion()
        {
            double Eta(JsonObject tv)
            {
                var el = O("id", "x", "type", "crystal", "crystal", Ideal(), "lengthM", 2e-3, "cut", O("thetaDeg", 90, "phiDeg", 0),
                    "interaction", O("process", "shg", "type", "0"), "beam", O("kind", "gaussian", "w0M", 200e-6), "chi3", false, "output", "sh");
                if (tv != null) el.Set("transverse", tv);
                return Go(Sys(el), NoSteep(Gauss(1030e-9, 500e-15, 20e-9))).Records[0].Values["conversionEfficiency"];
            }
            double oneD = Eta(null), radial = Eta(O("model", "radial", "points", 48));
            Assert.Less(oneD, 0.05, "low conversion");
            Assert.Close(oneD, radial, 0.01, 0, "1-D Gaussian overlap vs radial");
        }
    }
}
