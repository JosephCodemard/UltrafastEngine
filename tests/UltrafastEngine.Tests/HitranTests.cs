using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Plugins.Hitran;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The hitran plugin: Faddeeva function, line files, Voigt lines (absorption + Kramers-Kronig dispersion), the
    /// air in the gaps (environment.air) and the water-line tail against a brute-force big window.</summary>
    [TestArea("HITRAN plugin: line shapes, files, partition sums, absorption in air")]
    public class HitranTests
    {
        private const double Lam = 1030e-9;
        private const double Kb = 1.380649e-23;

        private static double OmegaOfCm1(double nu) => 2 * Math.PI * C * 100.0 * nu;

        private static HitranLine Line(double nu, double s, double gAir = 0.1, double gSelf = 0.4, double e = 100, double nAir = 0.7, double d = 0)
            => new HitranLine { Molecule = 1, Isotopologue = 1, NuCm1 = nu, S = s, A = 0, GammaAir = gAir, GammaSelf = gSelf, ElowerCm1 = e, NAir = nAir, DeltaAir = d };

        private static GasSpecies Water(double x, params HitranLine[] lines) => new GasSpecies { Name = "H2O", Lines = lines.ToList(), MoleFraction = x, PartitionExponent = 1.5 };

        [Test(Reference = "Faddeeva function: w(0) = 1, erfcx, Dawson values", Tolerance = "1e-10, 1e-09")]
        public void Faddeeva_MatchesKnownValues()
        {
            var w0 = Faddeeva.W(Complex.Zero);
            Assert.Close(1.0, w0.Real, 1e-10, 1e-10, "w(0) real"); Assert.Close(0.0, w0.Imaginary, 0, 1e-10, "w(0) imag");
            // w(i y) = erfcx(y): erfcx(1) = 0.4275835761558070 (tables); erfcx(0.1) = exp(0.01) erfc(0.1) = 0.896456979969
            Assert.Close(0.4275835761558070, Faddeeva.W(new Complex(0, 1)).Real, 1e-9, 0, "erfcx(1)");
            Assert.Close(1.010050167084168 * 0.8875370839817152, Faddeeva.W(new Complex(0, 0.1)).Real, 1e-9, 0, "erfcx(0.1)");
            // on the real axis: Re w = exp(-x^2), Im w = 2/sqrt(pi) Dawson(x); Dawson(1) = 0.5380795069127684
            var w1 = Faddeeva.W(new Complex(1, 1e-13));
            Assert.Close(Math.Exp(-1), w1.Real, 1e-9, 0, "Re w(1)");
            Assert.Close(2 / Math.Sqrt(Math.PI) * 0.5380795069127684, w1.Imaginary, 1e-9, 0, "Im w(1)");
        }

        [Test]
        public void Faddeeva_IsContinuousAcrossTheAsymptoticSwitch()
        {
            var below = Faddeeva.W(new Complex(99.99, 0.5)); var above = Faddeeva.W(new Complex(100.01, 0.5));
            var z = new Complex(100.0, 0.5);
            var asym = Complex.ImaginaryOne / (Math.Sqrt(Math.PI) * z) * (1 + 0.5 / (z * z) + 0.75 / Complex.Pow(z, 4));
            Assert.Close(asym.Real, 0.5 * (below.Real + above.Real), 2e-4, 0, "Re across the switch");
            Assert.Close(asym.Imaginary, 0.5 * (below.Imaginary + above.Imaginary), 2e-4, 0, "Im across the switch");
        }

        [Test]
        public void ParFile_RoundTripsAHitranRecord()
        {
            var l = Line(9708.123456, 2.34e-22, 0.0990, 0.412, 1234.5678, 0.69, -0.003820);
            string rec = HitranFile.FormatPar(l);
            Assert.Equal(160, rec.Length);
            string path = Path.Combine(Path.GetTempPath(), "uf_h2o_" + Guid.NewGuid().ToString("N") + ".par");
            File.WriteAllText(path, rec + "\n" + HitranFile.FormatPar(Line(5000, 1e-20)) + "\n");
            try
            {
                var all = HitranFile.Read(path);
                Assert.Equal(2, all.Count);
                Assert.Close(9708.123456, all[0].NuCm1, 0, 1e-6, "nu"); Assert.Close(2.34e-22, all[0].S, 1e-3, 0, "S");
                Assert.Close(0.0990, all[0].GammaAir, 0, 1e-5, "gamma_air"); Assert.Close(0.412, all[0].GammaSelf, 0, 1e-5, "gamma_self");
                Assert.Close(1234.5678, all[0].ElowerCm1, 0, 1e-4, "E''"); Assert.Close(0.69, all[0].NAir, 0, 1e-3, "n_air"); Assert.Close(-0.003820, all[0].DeltaAir, 0, 1e-6, "delta_air");
                Assert.Equal(1, all[0].Molecule); Assert.Equal(1, all[0].Isotopologue);
                Assert.Equal(1, HitranFile.Read(path, 6500, 14000).Count);   // range filter
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void CsvFile_UsesHapiColumnNames()
        {
            string path = Path.Combine(Path.GetTempPath(), "uf_h2o_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path, "molec_id,local_iso_id,nu,sw,a,gamma_air,gamma_self,elower,n_air,delta_air\n1,1,9708.5,1.5e-22,0.1,0.09,0.45,300.5,0.65,-0.002\n");
            try
            {
                var l = HitranFile.Read(path).Single();
                Assert.Close(9708.5, l.NuCm1, 0, 1e-9, "nu"); Assert.Close(1.5e-22, l.S, 1e-9, 0, "sw"); Assert.Close(0.45, l.GammaSelf, 0, 1e-12, "gamma_self");
            }
            finally { File.Delete(path); }
        }

        [Test(Reference = "Doppler HWHM ν/c √(2kT ln2 / m)", Tolerance = "0.01 %, 1 %, 2 %")]
        public void Doppler_WidthMatchesTheKineticFormula()
        {
            // HWHM = nu / c sqrt(2 k T ln2 / m); H2O, 296 K, 9708 cm-1: about 0.0141 cm-1 (0.42 GHz), FWHM about 0.85 GHz
            double nu = 9708.0, m = 18.010565 * 1.66053906660e-27;
            double expect = nu * Math.Sqrt(2 * Kb * 296 * Math.Log(2) / (m * C * C));
            Assert.Close(expect, HitranAirIndex.DopplerHwhmCm1(nu, 296, 18.010565), 1e-4, 0, "Doppler HWHM");
            Assert.Close(0.0141, expect, 0.01, 0, "about 0.0141 cm-1");
            double fwhmGhz = 2 * expect * C * 100 / 1e9;
            Assert.Close(0.85, fwhmGhz, 0.02, 0, "FWHM in GHz");
        }

        [Test(Reference = "saturation vapour pressure of water, 2339 Pa at 20 °C", Tolerance = "1e-12, 0.5 %")]
        public void WaterVapour_FractionFollowsTheSaturationPressure()
        {
            // saturation vapour pressure of water at 20 C: 2339 Pa
            var air = new CiddorAirIndex(20, 101325, 1.0, 450);
            Assert.Close(2339.0 / 101325.0, air.WaterVaporMoleFraction / 1.0040, 0.005, 0, "x_w at 100 % RH, 20 C (enhancement factor 1.004)");
            Assert.Close(0.5 * air.WaterVaporMoleFraction, new CiddorAirIndex(20, 101325, 0.5, 450).WaterVaporMoleFraction, 1e-12, 0, "linear in RH");
        }

        /// <summary>A line pressure-broadened far beyond the Doppler width is a Lorentzian: peak absorption n_d S / (pi gamma_L)
        /// and dispersion extremum n_d S / (8 pi^2 nu gamma_L) at nu0 -+ gamma_L (normal dispersion below the line).</summary>
        [Test(Reference = "Lorentz line: peak n S / (π γ), dispersion extremum", Tolerance = "1 %")]
        public void LorentzLine_PeakAbsorptionAndDispersion_MatchTheAnalyticResult()
        {
            double nu0 = 9708.0, S = 1e-22, T = 296, p = 5 * 101325, x = 0.01;
            var line = Line(nu0, S, 0.1, 0.4, 0, 0.7);
            var idx = new HitranAirIndex(null, T, p, new[] { Water(x, line) }, cutoffCm1: 5000);
            double nd = x * p / (Kb * T) * 1e-6, gL = 0.1 * 5 * 0.99 + 0.4 * 5 * 0.01;          // cm-3, cm-1 (p_air + p_self in atm)
            var om = new[] { OmegaOfCm1(nu0 - gL), OmegaOfCm1(nu0), OmegaOfCm1(nu0 + gL) };
            var r = idx.Compute(om);
            Assert.Close(nd * S / (Math.PI * gL) * 100.0, r.AlphaPerM[1], 0.01, 0, "peak absorption (1/m)");
            double dnMax = nd * S / (8 * Math.PI * Math.PI * nu0 * gL);
            Assert.Close(dnMax, r.Dn[0], 0.01, 0, "normal dispersion below the line");
            Assert.Close(-dnMax, r.Dn[2], 0.01, 0, "anomalous dispersion above the line");
            Assert.Close(0.0, r.Dn[1], 0, 0.01 * dnMax, "no dispersion at the centre");
        }

        [Test(Reference = "Kramers-Kronig (numerical Hilbert transform)")]
        public void VoigtLine_DispersionIsTheHilbertTransformOfTheAbsorption()
        {
            // Kramers-Kronig: Dn(nu) = (1/pi) PV integral kappa(nu') / (nu' - nu) dnu' for a line far from nu = 0
            double nu0 = 9708.0, T = 296, p = 101325, x = 0.01, gL = 0.1 * 0.99 + 0.4 * 0.01;
            var idx = new HitranAirIndex(null, T, p, new[] { Water(x, Line(nu0, 1e-21)) }, cutoffCm1: 1e5);
            double h = gL / 16; int n = (int)(800 * gL / h);
            var nu = new double[n]; var om = new double[n];
            for (int i = 0; i < n; i++) { nu[i] = nu0 - 400 * gL + i * h; om[i] = OmegaOfCm1(nu[i]); }
            var r = idx.Compute(om);
            var kappa = new double[n];
            for (int i = 0; i < n; i++) kappa[i] = r.AlphaPerM[i] / 100.0 / (4 * Math.PI * nu[i]);
            double peak = r.Dn.Select(Math.Abs).Max();
            foreach (double dx in new[] { -2.0, -0.5, 1.0, 3.0 })
            {
                int k = (int)Math.Round((nu0 + dx * gL - nu[0]) / h);
                double s = 0;
                for (int j = 0; j < n; j++) if (j != k) s += kappa[j] / (nu[j] - nu[k]) * h;
                Assert.Close(r.Dn[k], s / Math.PI, 0, 0.03 * peak, "Kramers-Kronig at " + dx + " gamma_L");
            }
        }

        [Test]
        public void LineStrength_ScalesWithDensityAndTemperature()
        {
            double nu0 = 9708.0, S = 1e-22, E = 800.0;
            double Peak(double T, double p)
            {
                var idx = new HitranAirIndex(null, T, p, new[] { Water(0.01, Line(nu0, S, 0.1, 0.4, E, 0.7)) }, 5000);
                return idx.Compute(new[] { OmegaOfCm1(nu0) }).AlphaPerM[0];
            }
            double T1 = 296, T2 = 330, p = 5 * 101325;
            // Lorentz peak = n_d S(T) / (pi gamma_L(T)); every factor written out independently of the model code
            double c2 = 1.4387769;
            double S2 = S * Math.Pow(T1 / T2, 1.5) * Math.Exp(-c2 * E * (1 / T2 - 1 / T1)) * (1 - Math.Exp(-c2 * nu0 / T2)) / (1 - Math.Exp(-c2 * nu0 / T1));
            double g2 = Math.Pow(T1 / T2, 0.7) * (0.1 * 5 * 0.99 + 0.4 * 5 * 0.01), g1 = 0.1 * 5 * 0.99 + 0.4 * 5 * 0.01;
            double expectRatio = (p / (Kb * T2) * S2 / g2) / (p / (Kb * T1) * S / g1);
            Assert.Close(expectRatio, Peak(T2, p) / Peak(T1, p), 0.01, 0, "peak absorption ratio T2/T1");
            Assert.Close(2.0 * 1.0, Peak(T1, 2 * p) / Peak(T1, p) / (g1 / (0.1 * 10 * 0.99 + 0.4 * 10 * 0.01)) , 0.01, 0, "doubling the pressure doubles the density (and the width)");
        }

        // ------------------------------------------------------------------------------------------------ in the engine

        private static string WriteLines(params HitranLine[] lines)
        {
            string path = Path.Combine(Path.GetTempPath(), "uf_h2o_" + Guid.NewGuid().ToString("N") + ".par");
            File.WriteAllLines(path, lines.Select(HitranFile.FormatPar));
            return path;
        }

        private static SystemDescription GapSystem(JsonObject environment, double lengthM, JsonObject gapExtra = null)
        {
            var gap = O("id", "g", "type", "gap", "lengthM", lengthM);
            if (gapExtra != null) foreach (var kv in gapExtra) gap.Set(kv.Key, kv.Value);
            var o = O("format", "ultrafast-system", "version", 2, "name", "t");
            if (environment != null) o.Set("environment", environment);
            o.Set("elements", new JsonArray(new JsonValue[] { gap }));
            return SystemDescription.FromJson(o);
        }

        private static RunResult Go(SystemDescription s, JsonObject grid = null)
        {
            var o = O("format", "ultrafast-run", "version", 2);
            o.Set("pulse", Gauss(Lam, 100e-15, 1e-6));
            if (grid != null) o.Set("grid", grid);
            return Simulation.Run(s, Specs.RunSpec.FromJson(o));
        }

        [Test]
        public void EnvironmentAir_IsOffByDefault_AndPlainCiddorReproducesTheDefaultAir()
        {
            var plain = Go(GapSystem(null, 5.0));
            var humidNoApply = Go(GapSystem(O("air", O("humidity", 0.8, "temperatureC", 30.0)), 5.0));
            Assert.Close(plain.FinalArrivalS, humidNoApply.FinalArrivalS, 0, 1e-21, "environment.air without applyToGaps changes nothing");
            var applied = Go(GapSystem(O("air", O("humidity", 0.0, "temperatureC", 20.0, "applyToGaps", true)), 5.0));
            Assert.Close(plain.FinalArrivalS, applied.FinalArrivalS, 0, 1e-21, "default Ciddor air as the environment air = the built-in Air");
            Assert.True(applied.Notes.Any(n => n.Contains("environment.air is applied to every air gap")), "a run note says the environment air is active");
            var hot = Go(GapSystem(O("air", O("humidity", 0.8, "temperatureC", 35.0, "applyToGaps", true)), 5.0));
            Assert.True(Math.Abs(hot.FinalArrivalS - plain.FinalArrivalS) > 1e-16, "warm humid air changes the delay of the gap");
        }

        [Test]
        public void GapAir_OverridesTheEnvironmentForOneGap()
        {
            var env = O("air", O("temperatureC", 20.0, "applyToGaps", true));
            var a = Go(GapSystem(env, 5.0));
            var b = Go(GapSystem(env, 5.0, O("air", O("temperatureC", 35.0, "humidity", 0.8))));
            Assert.True(Math.Abs(a.FinalArrivalS - b.FinalArrivalS) > 1e-16, "the gap's own air is used");
            Assert.True(b.Records.Single().Notes.Any(n => n.StartsWith("air of this gap")), "note names the gap's air");
        }

        [Test(Reference = "Beer-Lambert: 1 − exp(−α L)", Tolerance = "1 %")]
        public void WaterLines_InAGap_AbsorbAsBeerLambertOnTheGrid()
        {
            double nu0 = 1e7 / (Lam * 1e9) , S = 3e-21;
            string file = WriteLines(Line(nu0, S), Line(nu0 + 1.7, 1e-21), Line(nu0 - 2.3, 2e-21));
            try
            {
                var air = O("kind", "hitran", "humidity", 0.5, "applyToGaps", true, "species", O("H2O", file), "rangeCm1", new[] { 9000.0, 10500.0 });
                double L = 10;
                var r = Go(GapSystem(O("air", air), L), O("windowS", 1e-9, "n", 262144));
                // expected: sum over the grid of S(omega) (1 - exp(-alpha L)) with alpha from the same model
                var idxModel = r.Compiled.Materials["Air"].Attenuation;
                var g = r.Input.Grid; var spec = r.Input.SpectralIntensity();
                var alpha = new double[g.N]; idxModel.Evaluate(g.OmegaAbsolute, alpha);
                // r.Input is on the run's grid: the engine may have enlarged it, so evaluate on the result's own grid
                double e0 = spec.Sum(), lost = 0;
                for (int i = 0; i < g.N; i++) lost += spec[i] * (1 - Math.Exp(-alpha[i] * L));
                double expect = lost / e0;
                double got = 1 - r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ();
                Assert.Greater(got, 1e-6, "the lines absorb something");
                Assert.Close(expect, got, 0.01, 1e-9, "energy lost = sum S (1 - exp(-alpha L))");
            }
            finally { File.Delete(file); }
        }

        /// <summary>The motivating case: lines a few GHz wide ring for hundreds of ps. The ledger (main window of a few ps, tail 2 ns)
        /// must reproduce the same system on one 2 ns grid.</summary>
        [Test(Reference = "cross-check: one 2 ns window", Tolerance = "1e-05, 0.1 %, 2 %")]
        public void WaterLines_TailLedger_ReproducesTheBruteForce2nsWindow()
        {
            double nu0 = 1e7 / (Lam * 1e9);
            string file = WriteLines(Line(nu0, 3e-21), Line(nu0 + 1.7, 1e-21), Line(nu0 - 2.3, 2e-21));
            try
            {
                var air = O("kind", "hitran", "humidity", 0.5, "applyToGaps", true, "species", O("H2O", file), "rangeCm1", new[] { 9000.0, 10500.0 });
                var sys = GapSystem(O("air", air), 10.0);
                var brute = Go(sys, O("windowS", 2e-9, "n", 524288, "auto", false, "snapCarrier", false));
                var led = Go(sys, O("tail", O("windowS", 2e-9), "snapCarrier", false));
                var comp = led.TailLedger.Composite(led.Output, led.TailAt["end"]);
                Assert.Less(led.Output.Grid.N, 8192, "the nonlinear (main) grid stays small");
                Assert.Close(brute.Output.TotalEnergyJ(), comp.TotalEnergyJ(), 1e-5, 0, "total energy");
                double tailPeakBrute = Peak(brute.Output, 100e-12, 900e-12), tailPeakLed = Peak(comp, 100e-12, 900e-12);
                Assert.Greater(tailPeakBrute, 0, "the brute-force run has a ringing tail");
                Assert.Close(tailPeakBrute, tailPeakLed, 0.02, 0, "free-induction-decay tail power between 100 and 900 ps");
                var pb = PulseMetrics.Summarize(brute.Output); var pl = PulseMetrics.Summarize(led.Output);
                Assert.Close(pb.PeakPowerW, pl.PeakPowerW, 1e-3, 0, "peak power of the main pulse");
            }
            finally { File.Delete(file); }
        }

        private static double Peak(Pulses.PulseField p, double t0, double t1)
        {
            var pw = PulseMetrics.TemporalPower(p); var t = p.Grid.Time; double m = 0;
            for (int i = 0; i < pw.Length; i++) if (t[i] >= t0 && t[i] <= t1) m = Math.Max(m, pw[i]);
            return m;
        }
    
        // ------------------------------------------------------------------------------------------------ partition sums (TIPS)

        [Test]
        public void PartitionTable_InterpolatesLinearly_AndRefusesToExtrapolate()
        {
            var t = new PartitionTable(new[] { 100.0, 200.0, 300.0 }, new[] { 10.0, 40.0, 90.0 });
            Assert.Close(25.0, t.At(150.0), 1e-12, 0, "midpoint of 10 and 40");
            Assert.Close(90.0, t.At(300.0), 1e-12, 0, "end point");
            Assert.Throws<JsonException>(() => t.At(350.0));
        }

        [Test]
        public void LineStrength_UsesThePartitionTable_WhenThereIsOne()
        {
            double nu0 = 9708.0, S = 1e-22, E = 300.0, p = 5 * 101325, T1 = 296, T2 = 330;
            Func<GasSpecies, double> peak = sp => new HitranAirIndex(null, T2, p, new[] { sp }, 5000).Compute(new[] { OmegaOfCm1(nu0) }).AlphaPerM[0];
            var plain = Water(0.01, Line(nu0, S, 0.1, 0.4, E, 0.7));
            var tab = Water(0.01, Line(nu0, S, 0.1, 0.4, E, 0.7));
            // a table with Q ~ T^3 (not the T^1.5 of the default): the strength must follow it
            var T = Enumerable.Range(0, 41).Select(i => 250.0 + 5 * i).ToArray();
            tab.Partition = new System.Collections.Generic.Dictionary<int, PartitionTable> { { 1, new PartitionTable(T, T.Select(x => 1e-3 * x * x * x).ToArray()) } };
            double ratio = peak(tab) / peak(plain);
            Assert.Close(Math.Pow(T1 / T2, 3.0 - 1.5), ratio, 0.002, 0, "line strength ~ Q(296) / Q(T) from the table (T^-3 vs T^-1.5)");
            // an isotopologue without a table row falls back to the power law
            var tab2 = Water(0.01, Line(nu0, S, 0.1, 0.4, E, 0.7)); tab2.Lines[0] = new HitranLine { Molecule = 1, Isotopologue = 2, NuCm1 = nu0, S = S, GammaAir = 0.1, GammaSelf = 0.4, ElowerCm1 = E, NAir = 0.7 };
            tab2.Partition = tab.Partition;
            var plain2 = Water(0.01, tab2.Lines[0]);
            Assert.Close(1.0, peak(tab2) / peak(plain2), 1e-9, 0, "isotopologue 2 has no table: power law");
        }

        [Test]
        public void PartitionFile_NextToTheLineFile_IsFoundAndNamed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf_hit_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllLines(Path.Combine(dir, "H2O.par"), new[] { HitranFile.FormatPar(Line(9708.0, 1e-21)) });
                File.WriteAllLines(Path.Combine(dir, "H2O.partition.csv"), new[] { "iso,T,Q" }.Concat(Enumerable.Range(0, 201).Select(i => "1," + (100 + 2 * i) + "," + (0.5 * (100 + 2 * i)).ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                var spec = new UltrafastEngine.Models.ModelSpec("hitran", O("kind", "hitran", "species", O("H2O", Path.Combine(dir, "H2O.par")), "rangeCm1", new[] { 9000.0, 10500.0 }), UltrafastEngine.Models.ModelSource.SystemData, "test");
                var m = HitranAirIndex.FromSpec(spec);
                Assert.True(m.Description.Contains("TIPS partition sums"), "the description says which partition function: " + m.Description);
                var noTable = new UltrafastEngine.Models.ModelSpec("hitran", O("kind", "hitran", "species", O("H2O", O("file", Path.Combine(dir, "H2O.par"), "partitionExponent", 1.5)), "rangeCm1", new[] { 9000.0, 10500.0 }), UltrafastEngine.Models.ModelSource.SystemData, "test");
                Assert.True(HitranAirIndex.FromSpec(noTable).Description.Contains("Q ~ T^1.5"), "an explicit partitionExponent keeps the power law");
            }
            finally { Directory.Delete(dir, true); }
        }
    
        // ------------------------------------------------------------------------------------------------ real lines against the reference implementation

        /// <summary>
        /// Opt-in (set ULTRAFAST_HITRAN_VALIDATE_DIR to a folder made by plugins/hitran/tools/make_hapi_reference.py; needs the network once):
        /// real H2O, CO2 and O2 lines at 250, 296 and 350 K against HAPI's Voigt absorption coefficient (TIPS partition sums). Skipped otherwise.
        /// </summary>
        [Test(Reference = "HAPI (HITRAN reference code), H2O / CO2 / O2 at 250-350 K", Tolerance = "0.7 % integrated, 3.9 % worst point")]
        public void AgainstHapi_RealLines_AgreeWithTheReferenceImplementation()
        {
            string dir = Environment.GetEnvironmentVariable("ULTRAFAST_HITRAN_VALIDATE_DIR");
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "cases.json"))) { Assert.Skip("set ULTRAFAST_HITRAN_VALIDATE_DIR (plugins/hitran/tools/make_hapi_reference.py)"); return; }
            foreach (var c in JsonValue.ParseFile(Path.Combine(dir, "cases.json")).AsArray())
            {
                var co = c.AsObject(); string tag = co.GetString("tag"), mol = co.GetString("molecule"); double x = co.GetDouble("moleFraction", 0);
                foreach (double T in co.GetDoubleArray("temperaturesK"))
                {
                    var rows = File.ReadAllLines(Path.Combine(dir, tag, "ref_" + (int)T + ".csv")).Select(l => l.Split(' ').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToArray();
                    var nu = rows.Select(r => r[0]).ToArray(); var k = rows.Select(r => r[1]).ToArray();
                    var spec = new UltrafastEngine.Models.ModelSpec("hitran", O("kind", "hitran", "temperatureC", T - 273.15, "pressurePa", 101325.0, "baseIndex", "vacuum", "cutoffCm1", 5.0,
                        "species", O(mol, O("file", co.GetString("lines"), "moleFraction", x)), "rangeCm1", new[] { nu[0] - 100, nu[nu.Length - 1] + 100 }), UltrafastEngine.Models.ModelSource.SystemData, "test");
                    var m = HitranAirIndex.FromSpec(spec);
                    Assert.True(m.Description.Contains("TIPS"), tag + ": the partition sums of the downloader are used: " + m.Description);
                    var a = m.Compute(nu.Select(OmegaOfCm1).ToArray()).AlphaPerM;
                    double kmax = k.Max(), sum = 0, sumRef = 0, worst = 0; int n = 0;
                    for (int i = 0; i < nu.Length; i++)
                    {
                        double mine = a[i] / 100 / x; sum += mine; sumRef += k[i];
                        if (k[i] < 0.05 * kmax) continue;
                        worst = Math.Max(worst, Math.Abs(mine / k[i] - 1)); n++;
                    }
                    Console.WriteLine("  AgainstHapi {0} {1} K: integrated ratio {2:F4}, worst point on a line {3:P2} ({4} points)", tag, T, sum / sumRef, worst, n);
                    Assert.Greater(n, 20, tag + " " + T + " K: points on the lines");
                    Assert.Close(1.0, sum / sumRef, 0.01, 0, tag + " " + T + " K: integrated absorption (same cutoff as HAPI: 5 cm-1 = 50 half widths)");
                    Assert.Less(worst, 0.04, tag + " " + T + " K: worst point on a line within 4 % of HAPI");
                }
            }
        }
    }
}
