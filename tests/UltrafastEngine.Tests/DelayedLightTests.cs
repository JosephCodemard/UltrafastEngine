using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The delayed-light ledger (grid.tail): a second, long, linear grid next to the main nonlinear grid.
    /// The reference is always the same system on one big grid without a tail (brute force).</summary>
    [TestArea("Delayed light: the tail ledger, ghosts, echoes, coating ripple, scatter, detectors")]
    public class DelayedLightTests
    {
        private const double Lam = 1030e-9;

        private static SystemDescription SysOf(JsonObject materials, params JsonObject[] elements)
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "t");
            if (materials != null) o.Set("materials", materials);
            o.Set("elements", new JsonArray(elements));
            return SystemDescription.FromJson(o);
        }

        private static RunResult Go(SystemDescription s, JsonObject pulse, JsonObject grid = null, JsonObject extra = null)
        {
            var o = O("format", "ultrafast-run", "version", 2);
            o.Set("pulse", pulse);
            if (grid != null) o.Set("grid", grid);
            if (extra != null) foreach (var kv in extra) o.Set(kv.Key, kv.Value);
            return Simulation.Run(s, Specs.RunSpec.FromJson(o));
        }

        private static Pulses.PulseField CompositeOf(RunResult r) => r.TailLedger.Composite(r.Output, r.TailAt["end"]);

        private static (double Delay, double Ratio) Largest(Pulses.PulseField p, double tMin, double tMax)
        {
            var pw = PulseMetrics.TemporalPower(p); var t = p.Grid.Time;
            int ip = Array.IndexOf(pw, pw.Max()), best = -1;
            for (int i = 0; i < pw.Length; i++)
            {
                double dt = t[i] - t[ip];
                if (dt < tMin || dt > tMax) continue;
                if (best < 0 || pw[i] > pw[best]) best = i;
            }
            return best < 0 ? (0.0, 0.0) : (t[best] - t[ip], pw[best] / pw[ip]);
        }

        private static SystemDescription GhostPlate(double r = 0.01)
            => SysOf(O("ar", O("kind", "ar")), O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3,
                "coating", O("kind", "ar", "R", r), "ghosts", true));

        /// <summary>Reference: the ledger must reproduce a run of the same system on a single window that holds the ghost.</summary>
        [Test(Reference = "cross-check: one large grid holding the whole window", Tolerance = "1e-05, 0.1 %")]
        public void Ledger_GhostOfAPlate_MatchesTheBruteForceBigWindow()
        {
            var sys = GhostPlate();
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var brute = Go(sys, pulse);
            var led = Go(sys, pulse, O("tail", true));
            Assert.True(led.TailLedger != null, "the ledger is active");
            Assert.Less(led.Output.Grid.N * led.Output.Grid.DTime, 0.25 * brute.Output.Grid.N * brute.Output.Grid.DTime, "the main window is much smaller than the brute-force window");
            var comp = CompositeOf(led);
            var (d0, r0) = Largest(brute.Output, 5e-12, 1e-9);
            var (d1, r1) = Largest(comp, 5e-12, 1e-9);
            Assert.Close(d0, d1, 1e-3, 0, "ghost delay");
            Assert.Close(r0, r1, 1e-3, 0, "ghost relative peak power");
            Assert.Close(brute.Output.TotalEnergyJ(), comp.TotalEnergyJ(), 1e-5, 0, "total energy main + tail");
            // the ghost really is in the tail, not in the main field
            var (_, rMain) = Largest(led.Output, 5e-12, 1e-9);
            Assert.Less(rMain, 1e-2 * r1, "main field holds no ghost");
        }

        [Test]
        public void Ledger_EnergyBudgetCloses_WithTheTailBooked()
        {
            var sys = GhostPlate(0.02);
            var led = Go(sys, Gauss(Lam, 100e-15, 1e-6), O("tail", true));
            double lost = led.Records.Sum(x => x.LossJ.Values.Sum());
            Assert.Close(led.Input.TotalEnergyJ() - led.Output.TotalEnergyJ(), lost, 1e-6, 1e-18, "input - main output = booked losses");
            Assert.True(led.Records.Any(x => x.LossJ.ContainsKey("delayed light (tail)")), "tail energy is booked as its own mechanism");
            double eTail = led.TailLedger.TailEnergyJ;
            double booked = led.Records.Sum(x => x.LossJ.TryGetValue("delayed light (tail)", out var v) ? v : 0);
            Assert.Close(eTail, booked, 1e-5, 0, "booked tail energy = tail energy");
        }

        [Test]
        public void Ledger_Off_ChangesNothing()
        {
            var sys = GhostPlate();
            var a = Go(sys, Gauss(Lam, 100e-15, 1e-6));
            var b = Go(sys, Gauss(Lam, 100e-15, 1e-6), O("tail", false));
            Assert.True(b.TailLedger == null, "no ledger");
            Assert.Close(a.Output.TotalEnergyJ(), b.Output.TotalEnergyJ(), 1e-12, 0, "same energy");
            Assert.Equal(a.Output.Grid.N, b.Output.Grid.N);
        }

        [Test]
        public void Ledger_LightLeavingTheMainWindowComesBack()
        {
            // +GDD then -GDD: the pulse is stretched far beyond a deliberately small main window and recompressed. With the
            // ledger the light that left the window is kept in the tail and returns exactly.
            var sys = SysOf(null, O("id", "s", "type", "dispersion", "gddFs2", 4.0e6), O("id", "c", "type", "dispersion", "gddFs2", -4.0e6));
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var led = Go(sys, pulse, O("tail", O("windowS", 400e-12)));
            var ref0 = Go(sys, pulse);
            var comp = CompositeOf(led);
            Assert.Close(ref0.Output.TotalEnergyJ(), comp.TotalEnergyJ(), 1e-6, 0, "energy kept");
            Assert.Close(PulseMetrics.Summarize(ref0.Output).PeakPowerW, PulseMetrics.Summarize(led.Output).PeakPowerW, 1e-3, 0, "recompressed peak power");
        }

        [Test]
        public void Ledger_CrossCorrelator_ReadsTheGhostInTheProbe()
        {
            var sys = GhostPlate(0.01);
            var xc = new JsonArray(new JsonValue[] { O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -300e-12, 300e-12 }, "points", 6001, "noise", false, "dynamicRangeDb", 130.0) });
            var extra = O("instruments", xc);
            var brute = Go(sys, Gauss(Lam, 100e-15, 1e-6), null, extra);
            var led = Go(sys, Gauss(Lam, 100e-15, 1e-6), O("tail", true), extra);
            double pb = brute.Instruments.Single().Values.GetDouble("postPulseContrastDb", double.NaN);
            double pl = led.Instruments.Single().Values.GetDouble("postPulseContrastDb", double.NaN);
            Assert.Close(pb, pl, 0, 0.1, "post-pulse contrast (dB) of the ghost");
            double db = brute.Instruments.Single().Values.GetDouble("postPulseDelayS", double.NaN);
            double dl = led.Instruments.Single().Values.GetDouble("postPulseDelayS", double.NaN);
            Assert.Close(db, dl, 0, 0.1e-12, "post-pulse delay");
            Assert.True(led.Instruments.Single().Notes.Any(n => n.Contains("gate is made from the main field")), "note says where the gate comes from");
        }

        [Test]
        public void Ledger_WarnsWhenTheTailIsAFewPercentOfTheEnergy()
        {
            var sys = GhostPlate(0.2);
            var led = Go(sys, Gauss(Lam, 100e-15, 1e-6), O("tail", true));
            Assert.True(led.Warnings.Any(w => w.Contains("delayed-light tail holds")), "tail energy warning");
        }
    
        // ------------------------------------------------------------------------------------------------ plate ghost with a table coating

        private static double FresnelR(double n) => Math.Pow((n - 1) / (n + 1), 2);

        /// <summary>A ghost uses the coating's reflectance at each wavelength: with a table coating of R(lambda) on both faces the
        /// ghost amplitude is R_front R_back = R(lambda)^2 -- no flat value anywhere.</summary>
        [Test]
        public void Plate_Ghost_FollowsTheWavelengthDependentCoatingTable()
        {
            double l1 = 1000e-9, l2 = 1060e-9, r1 = 0.002, r2 = 0.0005;
            var coat = O("kind", "table", "wavelengthsM", new[] { 900e-9, l1, l2, 1200e-9 }, "R", new[] { 0.004, r1, r2, 0.0002 }, "T", new[] { 0.996, 1 - r1, 1 - r2, 0.9998 });
            var sys = SysOf(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3, "coating", coat, "ghosts", true));
            var run = Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 100e-15, 1e-6)));
            var cr = Compiler.Compile(sys, run);
            var ghost = cr.Elements[0].Post.OfType<UltrafastEngine.Optics.GhostTerm>().Single();
            var h = ghost.Evaluate(new UltrafastEngine.Physics.OmegaAxis(new[] { W(l1), W(l2) }, 0));
            // |H - 1| = R_front R_back = R^2 ... in amplitude: sqrt(R) * sqrt(R) = R
            Assert.Close(r1, (h.Xx[0] - 1).Magnitude, 0.01, 0, "ghost amplitude at 1000 nm");
            Assert.Close(r2, (h.Xx[1] - 1).Magnitude, 0.01, 0, "ghost amplitude at 1060 nm");
            Assert.Close(Math.Pow(r1 / r2, 2), Math.Pow((h.Xx[0] - 1).Magnitude / (h.Xx[1] - 1).Magnitude, 2), 0.02, 0, "intensity ratio follows R(lambda)^2");
        }

        // ------------------------------------------------------------------------------------------------ mirror substrate echoes

        private static SystemDescription EchoMirror(object substrate, double r = 0.99, double aoiDeg = 0, JsonObject coatingOverride = null)
        {
            var m = O("id", "m", "type", "mirror", "coating", coatingOverride ?? O("kind", "ideal", "R", r, "T", 1 - r), "aoiDeg", aoiDeg);
            m.Set("substrate", substrate is string str ? (JsonValue)str : (JsonObject)substrate);
            return SysOf(null, m);
        }

        /// <summary>The echo delay is the group delay of exp(i 2 n(w) w d cos(theta)/c): 2 n_g d / c. For fused silica near 1030 nm
        /// n_g is about 1.46, so a 6.35 mm substrate gives about 62 ps (not the 61.4 ps of the phase index 1.45).</summary>
        [Test(Reference = "echo delay 2 n_g d / c (fused silica n_g = 1.4635 at 1030 nm)", Tolerance = "0.3 %, 1 %, 8 %")]
        public void MirrorEcho_DelayIsTwoGroupIndexThicknessOverC()
        {
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            double ng = fs.GroupIndexAt(W(Lam)), n = fs.IndexAt(W(Lam));
            Assert.Close(1.462, ng, 0.01, 0, "group index of fused silica at 1030 nm");
            foreach (var (name, d) in new[] { ("FS-6.35mm", 6.35e-3), ("FS-9.5mm", 9.5e-3), ("FS-12.7mm", 12.7e-3) })
            {
                var r = Go(EchoMirror(name), Gauss(Lam, 100e-15, 1e-6));
                var (delay, ratio) = Largest(r.Output, 20e-12, 400e-12);
                Assert.Close(2 * ng * d / C, delay, 0.003, 0, name + ": echo delay 2 n_g d / c");
                Assert.Greater(delay, 2 * n * d / C, name + ": later than the phase-index estimate");
                // intensity: (T r_b)^2 / R with T = 0.01 (amplitude t^2 = T), r_b the glass-air amplitude
                double expect = Math.Pow(0.01, 2) * FresnelR(n) / 0.99;
                Assert.Close(expect, ratio, 0.08, 0, name + ": echo peak power relative to the main pulse");
            }
            Assert.Close(61.9e-12, 2 * ng * 6.35e-3 / C, 0.01, 0, "about 61.9 ps for 6.35 mm");
        }

        [Test]
        public void MirrorEcho_LeakageFollowsTheCoatingTransmissionAndTheBackFace()
        {
            double l1 = 1000e-9, l2 = 1060e-9;
            // HR coating whose transmission rises towards the blue
            var coat = O("kind", "table", "wavelengthsM", new[] { 900e-9, l1, l2, 1200e-9 }, "R", new[] { 0.98, 0.99, 0.999, 0.9995 }, "T", new[] { 0.02, 0.01, 0.001, 0.0005 });
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            Func<JsonObject, UltrafastEngine.Optics.MirrorSubstrateTerm> term = sub =>
            {
                var cr = Compiler.Compile(EchoMirror(sub, 0.99, 0, coat), Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 100e-15, 1e-6))));
                return cr.Elements[0].Pre.OfType<UltrafastEngine.Optics.MirrorSubstrateTerm>().Single();
            };
            var bare = term(O("material", "FusedSilica", "thicknessM", 6.35e-3));
            var ar = term(O("material", "FusedSilica", "thicknessM", 6.35e-3, "backReflectance", 1e-4));
            double Echo(UltrafastEngine.Optics.MirrorSubstrateTerm t, double lam) { var a = t.Reflection(W(lam), 1); var b = t.Reflection(W(lam), 0); return (a.S - b.S).Magnitude; }
            // |E1| = T r_b: independent evaluation from the table and the Fresnel amplitude
            double rb = (fs.IndexAt(W(l1)) - 1) / (fs.IndexAt(W(l1)) + 1);
            Assert.Close(0.01 * rb, Echo(bare, l1), 0.01, 0, "|t^2 r_b| at 1000 nm");
            double rb2 = (fs.IndexAt(W(l2)) - 1) / (fs.IndexAt(W(l2)) + 1);
            Assert.Close(0.001 * rb2, Echo(bare, l2), 0.01, 0, "|t^2 r_b| at 1060 nm");
            Assert.Greater(Echo(bare, l1) / Echo(bare, l2), 9.0, "the echo is stronger where the mirror leaks (blue side of this coating)");
            Assert.Close(Math.Sqrt(1e-4) , Echo(ar, l1) / 0.01, 0.01, 0, "an AR-coated back face (R = 1e-4) gives r_b = 0.01");
            // the whole echo is an exact phase factor exp(i 2 n w d / c): check the phase against the formula
            var e1 = bare.Reflection(W(l1), 1).S - bare.Reflection(W(l1), 0).S;
            double psi = 2 * fs.IndexAt(W(l1)) * W(l1) * 6.35e-3 / C;
            var expect = 0.01 * ((fs.IndexAt(W(l1)) - 1) / (fs.IndexAt(W(l1)) + 1)) * System.Numerics.Complex.Exp(System.Numerics.Complex.ImaginaryOne * psi);
            Assert.Close(expect.Real, e1.Real, 0, 2e-6, "echo, real part (exact transfer)");
            Assert.Close(expect.Imaginary, e1.Imaginary, 0, 2e-6, "echo, imaginary part");
        }

        [Test]
        public void MirrorEcho_AWedgedSubstrateSendsTheEchoOutOfTheBeam()
        {
            var plain = Go(EchoMirror(O("material", "FusedSilica", "thicknessM", 6.35e-3, "beamRadiusM", 2e-3)), Gauss(Lam, 100e-15, 1e-6));
            var wedged = Go(EchoMirror(O("material", "FusedSilica", "thicknessM", 6.35e-3, "beamRadiusM", 2e-3, "wedgeDeg", 0.5)), Gauss(Lam, 100e-15, 1e-6), O("tail", true));
            var (_, r0) = Largest(plain.Output, 20e-12, 400e-12);
            var comp = wedged.TailLedger.Composite(wedged.Output, wedged.TailAt["end"]);
            var (_, r1) = Largest(comp, 20e-12, 400e-12);
            Assert.Greater(r0, 1e-7, "an echo without a wedge");
            Assert.Less(r1, 1e-3 * r0, "a 0.5 deg wedge separates the echo from a 2 mm beam");
        }

        [Test]
        public void MirrorEcho_NeedsTheTailOrTheWindow_AndTheLedgerCarriesIt()
        {
            var sys = EchoMirror("FS-12.7mm");
            var brute = Go(sys, Gauss(Lam, 100e-15, 1e-6));
            var led = Go(sys, Gauss(Lam, 100e-15, 1e-6), O("tail", true));
            var comp = led.TailLedger.Composite(led.Output, led.TailAt["end"]);
            var (d0, r0) = Largest(brute.Output, 20e-12, 400e-12); var (d1, r1) = Largest(comp, 20e-12, 400e-12);
            Assert.Close(d0, d1, 1e-3, 0, "echo delay, tail vs one big window");
            Assert.Close(r0, r1, 1e-3, 0, "echo power, tail vs one big window");
            Assert.True(led.Output.Grid.N * led.Output.Grid.DTime < 0.3 * brute.Output.Grid.N * brute.Output.Grid.DTime, "main window much smaller");
        }
    
        // ------------------------------------------------------------------------------------------------ window planning

        [Test]
        public void Window_ManualGridTooSmall_NamesTheGhostItsDelayAndEnergy()
        {
            var r = Go(GhostPlate(), Gauss(Lam, 100e-15, 1e-6), O("auto", false, "n", 4096, "windowS", 20e-12));
            string w = r.Warnings.FirstOrDefault(x => x.StartsWith("Window: element 'w'"));
            Assert.True(w != null, "a specific warning names the element: " + string.Join(" | ", r.Warnings));
            Assert.True(w.Contains("+48.7") || w.Contains("+48.8"), "the delay of the ghost (2 n_g d / c = 48.8 ps) is in the warning: " + w);
            Assert.True(w.Contains("dB") && w.Contains("J of") && w.Contains("wraps around to"), "level, energy and wrapped position: " + w);
        }

        [Test]
        public void Window_ExtendForSatellites_GrowsAManualWindow()
        {
            var r = Go(GhostPlate(), Gauss(Lam, 100e-15, 1e-6), O("auto", false, "n", 4096, "windowS", 20e-12, "extendForSatellites", true));
            Assert.Greater(r.Output.Grid.N * r.Output.Grid.DTime, 2 * 48.8e-12, "the window now holds the ghost");
            Assert.True(!r.Warnings.Any(x => x.StartsWith("Window:")), "no wrap warning after the extension");
            var (d, ratio) = Largest(r.Output, 5e-12, 1e-9);
            Assert.Close(48.8e-12, d, 0.01, 0, "ghost delay found in the extended window");
        }

        [Test]
        public void Window_Strict_TurnsTheWarningIntoAnError()
        {
            Assert.Throws<JsonException>(() => Go(GhostPlate(), Gauss(Lam, 100e-15, 1e-6), O("auto", false, "n", 4096, "windowS", 20e-12, "strict", true)));
            // a window that holds it passes in strict mode
            Go(GhostPlate(), Gauss(Lam, 100e-15, 1e-6), O("auto", false, "n", 65536, "windowS", 500e-12, "strict", true));
        }

        [Test]
        public void Window_PedestalReplicaThatWraps_IsNamed()
        {
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            pulse.Set("pedestal", O("kind", "postPulse", "delayS", 80e-12, "relativeIntensity", 1e-4));
            var sys = SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0));
            var r = Go(sys, pulse, O("auto", false, "n", 4096, "windowS", 20e-12));
            Assert.True(r.Warnings.Any(x => x.StartsWith("Window: pedestal postpulse") && x.Contains("+80")), "pedestal named: " + string.Join(" | ", r.Warnings));
        }

        [Test]
        public void Window_AutoGrid_KeepsItsBehaviour()
        {
            var r = Go(GhostPlate(), Gauss(Lam, 100e-15, 1e-6));
            Assert.True(!r.Warnings.Any(x => x.StartsWith("Window:")), "auto grids hold the ghost: no wrap warning");
        }
    
        // ------------------------------------------------------------------------------------------------ coating ripple

        private static SystemDescription RippleMirror(JsonObject ripple)
            => SysOf(null, O("id", "m", "type", "mirror", "coating", O("kind", "ideal", "R", 1.0, "T", 0.0), "ripple", ripple));

        private static double EnergyOutside(Pulses.PulseField p, double tAbs)
        {
            var pw = PulseMetrics.TemporalPower(p); var t = p.Grid.Time; double o = 0, all = 0;
            for (int i = 0; i < pw.Length; i++) { all += pw[i]; if (Math.Abs(t[i]) > tAbs) o += pw[i]; }
            return o / all;
        }

        /// <summary>First order in a small phase ripple phi = sigma N(w): each cos component makes two replicas at +-tau_k, together sigma^2
        /// of the pulse energy (analytic). The structure lives within +-maxDelay, so nothing (to first order) is further out.</summary>
        [Test(Reference = "first-order phase-noise pedestal energy σ²", Tolerance = "1e-09, 30 %")]
        public void CoatingRipple_FirstOrderPedestal_HasEnergySigmaSquaredWithinTheMaxDelay()
        {
            double sigma = 0.03;
            var r = Go(RippleMirror(O("phaseRmsRad", sigma, "maxDelayS", 5e-12, "seed", 3)), Gauss(Lam, 20e-15, 1e-6));
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-9, 0, "a pure phase ripple keeps the energy");
            // delays are uniform in (0, 5 ps): the part beyond 0.4 ps is 92 % of sigma^2
            Assert.Close(sigma * sigma * (1 - 0.4 / 5), EnergyOutside(r.Output, 0.4e-12), 0.3, 0, "pedestal energy sigma^2 beyond 0.4 ps");
            Assert.Less(EnergyOutside(r.Output, 5.5e-12), 3e-6, "second order only: nothing beyond the max delay at first order");
            Assert.Greater(EnergyOutside(r.Output, 5.5e-12), 0, "the second-order tail exists");
        }

        [Test(Reference = "amplitude-ripple energy 1 − √(2/π) σ + σ²/2", Tolerance = "0.5 %")]
        public void CoatingRipple_AmplitudeRippleOnlyTakesLightAway()
        {
            var r = Go(RippleMirror(O("amplitudeRms", 0.05, "maxDelayS", 5e-12, "seed", 5)), Gauss(Lam, 20e-15, 1e-6));
            double t = r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
            Assert.Less(t, 1.0, "no gain");
            // A = min(1, 1 + sigma N), N ~ unit Gaussian: E[A^2] = 1 - sqrt(2/pi) sigma + sigma^2 / 2
            Assert.Close(1 - Math.Sqrt(2 / Math.PI) * 0.05 + 0.05 * 0.05 / 2, t, 0.005, 0, "mean energy transmission of a dips-only amplitude ripple");
            Assert.True(r.Records.Single().LossJ.ContainsKey("coating"), "booked as a coating loss");
        }

        [Test]
        public void CoatingRipple_IsReproducibleFromTheSeed()
        {
            double Pedestal(int seed) => EnergyOutside(Go(RippleMirror(O("phaseRmsRad", 0.03, "seed", seed)), Gauss(Lam, 20e-15, 1e-6)).Output, 0.4e-12);
            Assert.Close(Pedestal(3), Pedestal(3), 1e-12, 0, "same seed, same ripple");
            Assert.True(Math.Abs(Pedestal(3) - Pedestal(4)) > 1e-9, "another seed, another ripple");
        }

        // ------------------------------------------------------------------------------------------------ delayed scatter

        private static SystemDescription ScatterMirror(JsonObject scatter)
            => SysOf(null, O("id", "m", "type", "mirror", "coating", O("kind", "ideal", "R", 1.0, "T", 0.0), "delayedScatter", scatter));

        [Test]
        public void DelayedScatter_ReturnsTheFractionAfterTheGivenDelays_AndConservesEnergy()
        {
            double f = 0.01, maxS = 40e-12;
            var r = Go(ScatterMirror(O("fraction", f, "delay", O("kind", "uniform", "maxS", maxS), "seed", 11)), Gauss(Lam, 50e-15, 1e-6));
            Assert.Close(1.0, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 0.0, 0.03, "power is returned, not lost (on average)");
            var pw = PulseMetrics.TemporalPower(r.Output); var t = r.Output.Grid.Time;
            double eAll = pw.Sum(), eLate = 0, tm = 0;
            for (int i = 0; i < pw.Length; i++) if (t[i] > 1e-12) { eLate += pw[i]; tm += pw[i] * t[i]; }
            Assert.Close(f * (1 - 1.0 / 40), eLate / eAll, 0.25, 0, "scattered fraction after the pulse");
            Assert.Close(maxS / 2, tm / eLate, 0.15, 0, "mean delay of a uniform 0 .. maxS kernel");
            double beyond = 0; for (int i = 0; i < pw.Length; i++) if (t[i] > maxS + 1e-12) beyond += pw[i];
            Assert.Less(beyond / eAll, 1e-6, "nothing later than the longest delay");
            Assert.True(r.Records.Single().LossJ.Count == 0 || Math.Abs(r.Records.Single().LossJ.Values.Sum() / r.Input.TotalEnergyJ()) < 0.03, "loss budget small");
        }

        [Test(Reference = "λ^−n spectral weight", Tolerance = "25 %")]
        public void DelayedScatter_SpectralWeightFollowsALambdaPowerLaw()
        {
            var cr = Compiler.Compile(ScatterMirror(O("fraction", 0.02, "delay", O("kind", "uniform", "maxS", 40e-12), "spectralExponent", 8.0, "seed", 2)),
                Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 50e-15, 1e-6))));
            var term = cr.Elements[0].Post.OfType<UltrafastEngine.Optics.DelayedScatterTerm>().Single();
            double Avg(double lam0)
            {
                int n = 4000; var w = new double[n];
                for (int i = 0; i < n; i++) w[i] = W(lam0 * (1 + 0.02 * (i / (double)(n - 1) - 0.5)));
                var h = term.Evaluate(new UltrafastEngine.Physics.OmegaAxis(w, n / 2));
                double a = Math.Sqrt(1 - 0.02), s = 0;
                for (int i = 0; i < n; i++) s += (h.Xx[i] - a).Magnitude * (h.Xx[i] - a).Magnitude;
                return s / n;
            }
            double ratio = Avg(1000e-9) / Avg(1060e-9);
            Assert.Close(Math.Pow(1060.0 / 1000.0, 8), ratio, 0.25, 0, "intensity ~ lambda^-8");
        }

        [Test]
        public void DelayedScatter_OfAPlate_DefaultsToTheBulkBackscatterDelay()
        {
            var sys = SysOf(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3, "delayedScatter", O("fraction", 0.005)));
            var r = Go(sys, Gauss(Lam, 50e-15, 1e-6));
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            double maxS = 2 * fs.GroupIndexAt(W(Lam)) * 5e-3 / C;
            var pw = PulseMetrics.TemporalPower(r.Output); var t = r.Output.Grid.Time; double late = 0;
            for (int i = 0; i < pw.Length; i++) if (t[i] > maxS + 2e-12) late += pw[i];
            Assert.Less(late / pw.Sum(), 1e-5, "scatter stays within 0 .. 2 n_g d / c = " + (maxS * 1e12).ToString("F1") + " ps");
            Assert.True(r.Records.Single().Notes.Any(n => n.StartsWith("delayed scatter")), "note");
        }

        // ------------------------------------------------------------------------------------------------ detector: saturation and stitching

        private static double PostPulseSignal(JsonObject detector)
        {
            var pulse = Gauss(Lam, 50e-15, 1e-6);
            pulse.Set("pedestal", O("kind", "postPulse", "delayS", 20e-12, "relativeIntensity", 1e-6));
            var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -60e-12, 60e-12 }, "points", 6001, "noise", false, "dynamicRangeDb", 150.0);
            if (detector != null) foreach (var kv in detector) xc.Set(kv.Key, kv.Value);
            var sys = SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0));
            var r = Go(sys, pulse, null, O("instruments", new JsonArray(new JsonValue[] { xc })));
            var o = r.Instruments.Single();
            var d = o.Arrays.Single(a => a.Name.EndsWith("_delay_s")).Data; var sg = o.Arrays.Single(a => a.Name.EndsWith("_signal")).Data;
            double best = 0; for (int i = 0; i < d.Length; i++) if (d[i] > 15e-12 && d[i] < 25e-12) best = Math.Max(best, sg[i]);
            lastNotes = o.Notes;
            return best;
        }
        private static System.Collections.Generic.List<string> lastNotes;

        [Test]
        public void Detector_Saturation_ClipsThePeak_AndStitchingRestoresTheContrast()
        {
            double clean = PostPulseSignal(null);
            Assert.Greater(clean, 1e-8, "a post-pulse is seen");
            double clipped = PostPulseSignal(O("saturationLevel", 0.5));
            Assert.True(lastNotes.Any(n => n.Contains("saturate")), "note");
            Assert.Close(2.0, clipped / clean, 0.05, 0, "a detector clipped at half the peak doubles the apparent contrast");
            // the peak region is measured through a 1e-3 attenuator: no saturation, and dividing by the attenuation restores the scale
            double stitched = PostPulseSignal(O("saturationLevel", 0.5, "segments", new JsonArray(new JsonValue[] { O("fromS", -2e-12, "toS", 2e-12, "attenuation", 1e-3) })));
            Assert.Close(1.0, stitched / clean, 0.03, 0, "stitched contrast equals the unsaturated one");
            Assert.True(lastNotes.Any(n => n.Contains("attenuation-range stitching")), "stitching note");
        }

        [Test]
        public void Detector_Stitching_RaisesTheFloorByTheInverseAttenuation()
        {
            // the floor of the attenuated segment is the detector floor / attenuation
            var pulse = Gauss(Lam, 50e-15, 1e-6);
            var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -60e-12, 60e-12 }, "points", 6001, "noise", false, "noiseFloor", 1e-10,
                "segments", new JsonArray(new JsonValue[] { O("fromS", 30e-12, "toS", 60e-12, "attenuation", 1e-2) }));
            var r = Go(SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0)), pulse, null, O("instruments", new JsonArray(new JsonValue[] { xc })));
            var o = r.Instruments.Single();
            var d = o.Arrays.Single(a => a.Name.EndsWith("_delay_s")).Data; var sg = o.Arrays.Single(a => a.Name.EndsWith("_signal")).Data;
            double inSeg = 0, outSeg = 0; int ni = 0, no = 0;
            for (int i = 0; i < d.Length; i++) { if (d[i] > 35e-12) { inSeg += sg[i]; ni++; } else if (d[i] < -35e-12) { outSeg += sg[i]; no++; } }
            Assert.Close(100.0, (inSeg / ni) / (outSeg / no), 0.05, 0, "floor 1e-10 / 1e-2 = 1e-8 in the segment, 1e-10 elsewhere");
        }
    
        // ------------------------------------------------------------------------------------------------ analytic surface scatter

        private static UltrafastEngine.Optics.SurfaceScatterTerm SurfaceTerm(JsonObject scatter, JsonObject extra = null)
        {
            var m = O("id", "m", "type", "mirror", "coating", O("kind", "ideal", "R", 1.0, "T", 0.0), "delayedScatter", scatter);
            if (extra != null) foreach (var kv in extra) m.Set(kv.Key, kv.Value);
            var cr = Compiler.Compile(SysOf(null, m), Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", Gauss(Lam, 50e-15, 1e-6))));
            return cr.Elements[0].Post.OfType<UltrafastEngine.Optics.SurfaceScatterTerm>().Single();
        }

        /// <summary>TIS = (4 pi sigma / lambda)^2 at normal incidence; the share of it inside the mode (radius w) for a Gaussian roughness
        /// correlation l is 1 - exp(-(l/w)^2), obtained by integrating S(f) = pi sigma^2 l^2 exp(-pi^2 l^2 f^2) over f &lt; 1 / (pi w).</summary>
        [Test(Reference = "total integrated scatter TIS = (4π σ cos θ / λ)²", Tolerance = "1e-12, 0.1 %")]
        public void SurfaceScatter_LossIsTheTisOutsideTheMode_AndFollowsLambdaMinusTwo()
        {
            double sigma = 2e-9, ell = 500e-6, w = 1e-3;
            var t = SurfaceTerm(O("model", "surface", "roughnessRmsM", sigma, "correlationLengthM", ell, "beamRadiusM", w));
            // independent numerical integration of the roughness spectrum over the acceptance cone
            double fm = 1 / (Math.PI * w), inside = 0, total = 0, df = fm / 4000;
            for (int i = 0; i < 400000; i++) { double f = (i + 0.5) * df; double s = Math.PI * sigma * sigma * ell * ell * Math.Exp(-Math.Pow(Math.PI * ell * f, 2)) * 2 * Math.PI * f * df; total += s; if (f < fm) inside += s; }
            Assert.Close(inside / total, t.InModeShare, 1e-3, 0, "share of the scatter inside the mode");
            Assert.Close(1 - Math.Exp(-Math.Pow(ell / w, 2)), t.InModeShare, 1e-12, 0, "closed form");
            double LossAt(double lam) { var h = t.Evaluate(new UltrafastEngine.Physics.OmegaAxis(new[] { W(lam) }, 0)); return 1 - h.Xx[0].Magnitude * h.Xx[0].Magnitude; }
            double tis = Math.Pow(4 * Math.PI * sigma / Lam, 2);
            Assert.Close(tis * (1 - t.InModeShare), LossAt(Lam), 1e-3, 0, "power lost out of the mode = TIS (1 - g)");
            Assert.Close(Math.Pow(1060.0 / 1000.0, 2), LossAt(1000e-9) / LossAt(1060e-9), 1e-3, 0, "lambda^-2");
        }

        [Test]
        public void SurfaceScatter_WithADelay_ReturnsTheInModePartAfterTheKernel()
        {
            double sigma = 3e-9, ell = 800e-6, w = 1e-3;
            var t = SurfaceTerm(O("model", "surface", "roughnessRmsM", sigma, "correlationLengthM", ell, "beamRadiusM", w, "delay", O("kind", "uniform", "maxS", 40e-12), "seed", 4));
            int n = 6000; var om = new double[n]; for (int i = 0; i < n; i++) om[i] = W(Lam * (1 + 0.03 * (i / (double)(n - 1) - 0.5)));
            var h = t.Evaluate(new UltrafastEngine.Physics.OmegaAxis(om, n / 2));
            double tis = Math.Pow(4 * Math.PI * sigma / Lam, 2), a = Math.Sqrt(1 - tis), s = 0;
            for (int i = 0; i < n; i++) s += Math.Pow((h.Xx[i] - a).Magnitude, 2);
            Assert.Close(t.InModeShare * tis, s / n, 0.15, 0, "mean returned power g TIS (random amplitudes: 15 %)");
        }

        [Test]
        public void SurfaceScatter_InAnEngineRun_BooksTheLossAndNeedsItsInputs()
        {
            double sigma = 2e-9, ell = 200e-6, w = 1e-3;
            var r = Go(SysOf(null, O("id", "m", "type", "mirror", "coating", O("kind", "ideal", "R", 1.0, "T", 0.0), "beamRadiusM", w,
                "delayedScatter", O("model", "surface", "roughnessRmsM", sigma, "correlationLengthM", ell))), Gauss(Lam, 50e-15, 1e-6));
            double tis = Math.Pow(4 * Math.PI * sigma / Lam, 2), g = 1 - Math.Exp(-Math.Pow(ell / w, 2));
            Assert.Close(tis * (1 - g), r.Records[0].LossJ["scatter"] / r.Input.TotalEnergyJ(), 0.02, 0, "booked scatter loss ~ TIS (1 - g) (the pulse spectrum weights lambda^-2 by < 1 %)");
            Assert.Throws<UltrafastEngine.Models.ModelException>(() => Go(SysOf(null, O("id", "m", "type", "mirror", "delayedScatter", O("model", "surface", "roughnessRmsM", sigma))), Gauss(Lam, 50e-15, 1e-6)));
            // a plate has two surfaces
            var plate = Go(SysOf(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 1e-3, "beamRadiusM", w,
                "delayedScatter", O("model", "surface", "roughnessRmsM", sigma, "correlationLengthM", ell, "delay", O("kind", "uniform", "maxS", 10e-12)))), Gauss(Lam, 50e-15, 1e-6));
            Assert.True(plate.Records[0].Notes.Any(n => n.Contains("2 surfaces")), "two surfaces");
        }
    
        // ------------------------------------------------------------------------------------------------ cost: smooth elements, and cross-phase

        [Test]
        public void Ledger_SmoothElements_SkipTheExactStep_WithTheSameResult()
        {
            // a ghost plate makes a tail; the glass and the dispersion behind it are smooth: main and tail are propagated separately
            var sys = SysOf(null, O("id", "w", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3, "coating", O("kind", "ar", "R", 0.01), "ghosts", true),
                O("id", "g", "type", "bulk", "material", "FusedSilica", "lengthM", 0.05, "nonlinear", false), O("id", "d", "type", "dispersion", "gddFs2", -3e4));
            var pulse = Gauss(Lam, 100e-15, 1e-6);
            var fast = Go(sys, pulse, O("tail", true));
            var exact = Go(sys, pulse, O("tail", O("exactSteps", true)));
            Assert.Greater(fast.TailLedger.SeparateSteps, 0, "smooth steps were taken separately");
            Assert.Equal(0, exact.TailLedger.SeparateSteps);
            Assert.Greater(exact.TailLedger.ExactSteps, fast.TailLedger.ExactSteps, "fewer exact steps");
            var a = fast.TailLedger.Composite(fast.Output, fast.TailAt["end"]); var b = exact.TailLedger.Composite(exact.Output, exact.TailAt["end"]);
            Assert.Close(b.TotalEnergyJ(), a.TotalEnergyJ(), 1e-9, 0, "total energy");
            var pa = PulseMetrics.TemporalPower(a); var pb = PulseMetrics.TemporalPower(b);
            double d = 0, s2 = 0; for (int i = 0; i < pa.Length; i++) { d += Math.Pow(pa[i] - pb[i], 2); s2 += pb[i] * pb[i]; }
            Assert.Less(Math.Sqrt(d / s2), 1e-6, "power vs time of main + tail, relative rms difference");
            var (d0, r0) = Largest(b, 5e-12, 1e-9); var (d1, r1) = Largest(a, 5e-12, 1e-9);
            Assert.Close(d0, d1, 1e-6, 0, "ghost delay"); Assert.Close(r0, r1, 1e-6, 0, "ghost power");
        }

        /// <summary>First order in a weak tail that overlaps the main pulse: A = A_m + A_t in a Kerr medium gives the tail the change
        /// i gamma (2 |A_m|^2 A_t + A_m^2 A_t*). The reference is the full nonlinear solution of main + tail on one grid, minus that of the
        /// main pulse alone (which is the tail's output to first order in A_t).</summary>
        [Test(Reference = "cross-check: the full nonlinear difference field", Tolerance = "10 %")]
        public void Ledger_CrossPhase_MatchesTheFullNonlinearSolutionToFirstOrder()
        {
            double n2 = 1e-20, area = 1e-8, len = 0.01;
            var kerr = O("kerr", FlatMaterial(n2));
            var sys = SysOf(kerr, O("id", "k", "type", "bulk", "material", "kerr", "lengthM", len, "beam", O("kind", "constant", "areaM2", area)));
            var grid = O("auto", false, "n", 2048, "windowS", 20e-12);
            var phys = O("physics", O("raman", false, "selfSteepening", false));
            var p0 = Gauss(Lam, 100e-15, 2e-7);
            var p1 = Gauss(Lam, 100e-15, 2e-7); p1.Set("pedestal", O("kind", "postPulse", "delayS", 80e-15, "relativeIntensity", 1e-4, "phaseRad", 0.7));
            var r0 = Go(sys, p0, grid, phys); var r1 = Go(sys, p1, grid, phys);
            Assert.Equal(r0.Input.Grid.N, r1.Input.Grid.N);
            var led = new TailLedger(r0.Input.Grid, 8 * r0.Input.Grid.N * r0.Input.Grid.DTime, 1 << 22);
            // the replica = the difference of the two inputs (to first order in it: the part of p1 that p0 lacks), as a tail
            var diffIn = new Pulses.PulseField(r0.Input.Grid, r1.Input.Ax.Zip(r0.Input.Ax, (x, y) => x - y).ToArray(), new System.Numerics.Complex[r0.Input.Grid.N]);
            led.AddToTail(led.EmbedMain(diffIn));
            double gamma = 2 * Math.PI / Lam * n2 * len / area;
            Assert.Close(0.115, gamma * PulseMetrics.TemporalPower(r0.Input).Max(), 0.1, 0, "B-integral of the main pulse: about a tenth of a radian: " + gamma * PulseMetrics.TemporalPower(r0.Input).Max());
            var before = led.TailField();
            led.CrossPhase(r0.Input, r0.Output, gamma);
            var after = led.TailField();
            var formula = new System.Numerics.Complex[after.Ax.Length]; for (int i = 0; i < formula.Length; i++) formula[i] = after.Ax[i] - before.Ax[i];
            var diffOut = new Pulses.PulseField(r0.Input.Grid, r1.Output.Ax.Zip(r0.Output.Ax, (x, y) => x - y).ToArray(), new System.Numerics.Complex[r0.Input.Grid.N]);
            var brute = led.EmbedMain(diffOut); var inEmbedded = led.EmbedMain(diffIn);
            double num = 0, den = 0;
            for (int i = 0; i < formula.Length; i++) { var db = brute.Ax[i] - inEmbedded.Ax[i]; num += (formula[i] - db).Magnitude * (formula[i] - db).Magnitude; den += db.Magnitude * db.Magnitude; }
            Assert.Greater(den, 0, "the nonlinear solution changes the replica");
            Assert.Less(Math.Sqrt(num / den), 0.12, "first-order cross-phase vs the full nonlinear difference field: relative error ~ B = 0.11 (1 without the cross-phase)");
        }
    
        // ------------------------------------------------------------------------------------------------ detector response and presets

        private static double[] Spectrum(RunResult r, string name, string what) => r.Instruments.Single(i => i.Name == name).Arrays.Single(a => a.Name.EndsWith("_" + what)).Data;

        private static RunResult SpectrumRun(JsonValue detector, JsonObject extra = null)
        {
            var sp = O("kind", "spectrometer", "name", "sp", "normalize", "none"); if (detector != null) sp.Set("detector", detector);
            var run = O("instruments", new JsonArray(new JsonValue[] { sp }));
            if (extra != null) foreach (var kv in extra) run.Set(kv.Key, kv.Value);
            return Go(SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0)), Gauss(Lam, 30e-15, 1e-6), null, run);
        }

        [Test]
        public void Detector_SpectralResponse_MultipliesTheSpectrumByTheTable()
        {
            var plain = SpectrumRun(null);
            var resp = O("wavelengthsM", new[] { 900e-9, 1030e-9, 1200e-9 }, "values", new[] { 0.5, 0.9, 0.1 });
            var det = SpectrumRun(O("response", resp));
            var lam = Spectrum(plain, "sp", "wavelength_m"); var a = Spectrum(plain, "sp", "spectrum"); var b = Spectrum(det, "sp", "spectrum");
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] < 0.05 * a.Max()) continue; n++;
                double expect = lam[i] <= 1030e-9 ? 0.5 + (0.9 - 0.5) * (lam[i] - 900e-9) / (130e-9) : 0.9 + (0.1 - 0.9) * (lam[i] - 1030e-9) / (170e-9);
                Assert.Close(expect, b[i] / a[i], 1e-9, 0, "response at " + lam[i] * 1e9 + " nm");
            }
            Assert.Greater(n, 20, "points compared");
            Assert.True(det.Instruments.Single().Notes.Any(x => x.Contains("spectral response")), "note");
        }

        [Test]
        public void Detector_PhotonCounting_WeightsTheEnergySpectrumByLambda()
        {
            var plain = SpectrumRun(null); var ph = SpectrumRun(O("counting", "photons"));
            var lam = Spectrum(plain, "sp", "wavelength_m"); var a = Spectrum(plain, "sp", "spectrum"); var b = Spectrum(ph, "sp", "spectrum");
            int i1 = Array.IndexOf(a, a.Max()), i2 = i1 + 25;
            Assert.Close(lam[i2] / lam[i1], (b[i2] / a[i2]) / (b[i1] / a[i1]), 1e-9, 0, "photon counts ~ energy x lambda");
        }

        [Test]
        public void Detector_Preset_FillsTheInstrumentAndTheReadout_FromTheRunSpecOrAFile()
        {
            var seg = new JsonArray(new JsonValue[] { O("fromS", -2e-12, "toS", 2e-12, "attenuation", 1e-3) });
            JsonObject Xc(JsonValue detector, bool explicitKeys)
            {
                var xc = O("kind", "crossCorrelator", "name", "xc", "rangeS", new[] { -60e-12, 60e-12 }, "points", 3001, "noise", false);
                if (explicitKeys) { xc.Set("saturationLevel", 0.5); xc.Set("segments", seg); }
                if (detector != null) xc.Set("detector", detector);
                return xc;
            }
            double[] Run(JsonObject xc, JsonObject presets = null)
            {
                var pulse = Gauss(Lam, 50e-15, 1e-6); pulse.Set("pedestal", O("kind", "postPulse", "delayS", 20e-12, "relativeIntensity", 1e-6));
                var extra = O("instruments", new JsonArray(new JsonValue[] { xc })); if (presets != null) extra.Set("detectors", presets);
                var r = Go(SysOf(null, O("id", "d", "type", "dispersion", "gddFs2", 0.0)), pulse, null, extra);
                Assert.True(r.Warnings.All(w => !w.Contains("preset")), "no preset warning: " + string.Join("|", r.Warnings));
                return Spectrum(r, "xc", "signal");
            }
            var reference = Run(Xc(null, true));
            var presets = O("my-xc", O("instrument", O("saturationLevel", 0.5, "segments", seg)));
            var viaName = Run(Xc("my-xc", false), presets);
            Assert.Equal(reference.Length, viaName.Length);
            for (int i = 0; i < reference.Length; i++) Assert.Close(reference[i], viaName[i], 1e-12, 0, "scan point " + i + ": preset by name");
            string file = Path.Combine(Path.GetTempPath(), "uf_preset_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, presets["my-xc"].ToString());
            try
            {
                var viaFile = Run(Xc(O("preset", file), false));
                for (int i = 0; i < reference.Length; i++) Assert.Close(reference[i], viaFile[i], 1e-12, 0, "scan point " + i + ": preset from a file");
            }
            finally { File.Delete(file); }
            // the instrument's own keys win over the preset's, and the preset's detector read-out applies
            var sp = SpectrumRun("amp", O("detectors", O("amp", O("detector", O("calibration", 2.0)))));
            var plain = SpectrumRun(null);
            Assert.Close(2.0, Spectrum(sp, "sp", "spectrum").Max() / Spectrum(plain, "sp", "spectrum").Max(), 1e-9, 0, "the preset's calibration");
            var unknown = SpectrumRun("nosuch");
            Assert.True(unknown.Warnings.Any(w => w.Contains("detector preset \"nosuch\"")), "an unknown preset is reported");
        }
    }
}
