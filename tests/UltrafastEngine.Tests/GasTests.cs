using System;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Stage 1.4: molecular gases (rotational Raman), gas indices, the refractiveindex.info reader.</summary>
    [TestArea("Gases: rotational Raman, indices, the refractiveindex.info database")]
    public class GasTests
    {
        private const double Lam = 1030e-9, Kb = 1.380649e-23, Eps0 = 8.8541878128e-12;

        [Test(Reference = "isolated-molecule alignment, high-T limit: n2 = N Δα² / (4 c ε0² 15 kT)", Tolerance = "3 %")]
        public void RotationalRaman_AdiabaticLimitMatchesIsolatedMoleculeTheory()
        {
            // high-temperature limit of the isolated-molecule alignment: d<cos^2> = F / (15 kT) -> n2 = N da^2 / (4 c eps0^2 15 kT)
            foreach (var sp in new[] { "N2", "O2" })
            {
                var r = RotationalRamanResponse.For(new[] { (sp, 1.0) }, 1.0, 293.15);
                var g = GasData.Get(sp);
                double N = 1e5 / (Kb * 293.15), da = 4 * Math.PI * Eps0 * g.AnisotropyA3 * 1e-30;
                double classical = N * da * da / (4 * C * Eps0 * Eps0) / (15 * Kb * 293.15);
                Assert.Close(classical, r.AdiabaticN2, 0.03, 0, sp + " adiabatic orientational n2");
            }
            // N2 at 1 atm: the orientational n2 for long pulses is a few times the electronic one (Nibbering 1997)
            var n2 = MaterialLibrary.BuiltIn("N2");
            Assert.Greater(n2.Rotational.AdiabaticN2 / n2.N2, 2.0, "N2: rotational > electronic for long pulses");
        }

        [Test(Reference = "rotational revival period 1 / (2 B c)", Tolerance = "5 %")]
        public void RotationalRaman_RevivalsAtTheRotationalPeriod()
        {
            var r = RotationalRamanResponse.For(new[] { ("N2", 1.0) }, 1.0, 293.15);
            double trev = 1 / (2 * 1.98957 * 2.99792458e10);  // 1/(2Bc) = 8.38 ps
            double Max(double a, double b) { double m = 0; for (double t = a; t < b; t += 2e-15) m = Math.Max(m, Math.Abs(r.KernelAt(t))); return m; }
            double atRevival = Max(trev - 0.3e-12, trev + 0.3e-12), between = Max(0.6 * trev, 0.7 * trev), half = Max(0.5 * trev - 0.3e-12, 0.5 * trev + 0.3e-12);
            Assert.Greater(atRevival, 10 * between, "full revival at 1/(2Bc)");
            Assert.Greater(half, 5 * between, "half revival");
            // causal convolution: response to a short impulse reproduces the kernel
            int n = 8192; double dt = 5e-15;
            var x = new double[n]; x[100] = 1 / dt;
            var y = r.Convolve(x, dt);
            Assert.Close(r.KernelAt(10 * dt), y[110], 0.05, 0, "impulse response (10 dt after the impulse)");
            Assert.Less(Math.Abs(y[50]), 1e-9 * Math.Abs(y[110]), "causal (FFT round-off only)");
        }

        [Test(Reference = "first-order alignment: the index change at the revival dn(t) = (R * I)(t), R the rigid-rotor kernel (quadrature of KernelAt); linear (zero-padded) convolution: the window does not need to hold the revival", Tolerance = "probe phase 2 % of its swing; window independence 1e-6 relative")]
        public void RotationalRaman_RevivalActsOnAProbeAtTheRevival_AndTheWindowNeedsNoRoomForIt()
        {
            // N2, 1 bar, 1 m, 100 fs pump at 800 nm (1e16 W/m^2) and a weak replica (1e-3) at the full revival 1/(2Bc) = 8.38 ps. The rotational
            // kernel is applied as a zero-padded linear convolution (Gases.cs, IPaddedKernelResponse), so light anywhere in the window sees the
            // whole history of the index change and nothing wraps; the planner therefore adds no room for the 8.4 ps revival (or for the
            // dephasing time T2, hundreds of ps) and short-pulse runs in air and N2 keep their grids. The probe only has to be in the window,
            // which the planner already holds (satellites).
            double trev = 1 / (2 * 1.98957 * 2.99792458e10), area = 1e-8, lam = 800e-9, L = 1.0;
            SystemDescription S(bool rot)
            {
                var gas = O("gas", O("species", "N2", "pressureBar", 1.0));
                if (!rot) gas.Set("rotationalRaman", false);
                var o = O("format", "ultrafast-system", "version", 2, "name", "n2", "materials", O("gas", gas));
                o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "gas", "lengthM", L, "beam", O("kind", "constant", "areaM2", area))));
                return SystemDescription.FromJson(o);
            }
            var pump = O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", 100e-15, "peakPowerW", 1e8, "polarization", "x",
                         "pedestal", O("kind", "postPulse", "delayS", trev, "relativeIntensity", 1e-3));
            var phys = O("selfSteepening", false);
            var on = Simulation.Run(S(true), Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", pump, "physics", phys)));
            var g = on.Output.Grid;
            Assert.Greater(g.N * g.DTime / 2, 1.1 * trev, "the planned window holds the probe at the revival");
            var off = Simulation.Run(S(false), Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", pump, "physics", phys, "grid", O("n", g.N, "windowS", g.N * g.DTime, "auto", false))));
            // the probe peak, and its phase change by the pump's revival (on - off; the probe's own response is 1e-3 of it)
            var ain = on.Input.TemporalAx(); var aon = on.Output.TemporalAx(); var aoffIn = off.Input.TemporalAx(); var aoff = off.Output.TemporalAx();
            int ip = -1; double best = 0;
            for (int i = 0; i < g.N; i++) if (g.Time[i] > 0.5 * trev && ain[i].Magnitude > best) { best = ain[i].Magnitude; ip = i; }
            double Arg(System.Numerics.Complex z) => Math.Atan2(z.Imaginary, z.Real);
            double Dphi(int j) => Math.IEEERemainder(Arg(aon[j] / ain[j]) - Arg(aoff[j] / aoffIn[j]), 2 * Math.PI);
            // reference: dn(tp) = integral R(tp - t') I(t') dt' for the Gaussian pump (I0 = P / A), on a 0.25 fs quadrature
            var r = MaterialLibrary.BuiltIn("N2").Rotational as RotationalRamanResponse;
            double tau = 100e-15 / (2 * Math.Sqrt(Math.Log(2))), i0 = 1e8 / area, h = 0.25e-15;
            Func<double, double> Ref = tq => { double d = 0; for (double t = -6 * tau; t <= 6 * tau; t += h) d += r.KernelAt(tq - t) * i0 * Math.Exp(-t * t / (tau * tau)) * h; return 2 * Math.PI / lam * L * d; };
            // across the probe (+-100 fs, 25 fs steps): the revival swings the phase through about 3 rad
            double worst = 0, swing = 0;
            for (int k = -12; k <= 12; k += 3) { double e = Ref(g.Time[ip + k]); swing = Math.Max(swing, Math.Abs(e)); worst = Math.Max(worst, Math.Abs(Dphi(ip + k) - e)); }
            Assert.Greater(swing, 1.0, "a clear phase at the revival");
            Assert.Less(worst, 0.02 * swing, "probe phase across the revival = k0 L (R * I)(t)");
            // window independence: a window that ends before the revival (4 ps) and one four times larger, same time step, single pulse
            var single = O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", 100e-15, "peakPowerW", 3e8, "polarization", "x");
            double dt = g.DTime;
            RunResult W(int n) => Simulation.Run(S(true), Specs.RunSpec.FromJson(O("format", "ultrafast-run", "version", 2, "pulse", single, "physics", phys, "grid", O("n", n, "windowS", n * dt, "auto", false))));
            int n4 = GridPlannerNext(4e-12 / dt);
            var shortW = W(n4); var longW = W(4 * n4);
            Assert.Less(shortW.Output.Grid.N * dt, trev, "the short window ends before the revival");
            double sS = SpectralCentroid(shortW.Output) - SpectralCentroid(shortW.Input), sL = SpectralCentroid(longW.Output) - SpectralCentroid(longW.Input);
            double bS = RmsSpectralWidth(shortW.Output) - RmsSpectralWidth(shortW.Input), bL = RmsSpectralWidth(longW.Output) - RmsSpectralWidth(longW.Input);
            Assert.Greater(Math.Abs(bL), 0.05 * RmsSpectralWidth(longW.Input), "noticeable broadening");
            Assert.Close(sL, sS, 1e-6, 0, "centroid shift: the same with a window shorter than the revival");
            Assert.Close(bL, bS, 1e-6, 0, "rms broadening: the same with a window shorter than the revival");
        }

        private static int GridPlannerNext(double v) => UltrafastEngine.Engine.GridPlanner.NextPow2((long)Math.Ceiling(v));

        [Test(Reference = "rotational n2 of circular light = 1/4 of linear (tensor Raman, Hellwarth)", Tolerance = "3 %, 5 %")]
        public void RotationalRaman_LongPulseSpmUsesTheTotalN2_AndCircularSeesAQuarter()
        {
            SystemDescription S(JsonObject material)
            {
                var o = O("format", "ultrafast-system", "version", 2, "name", "g", "materials", O("gas", material));
                o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "gas", "lengthM", 1.0, "beam", O("kind", "constant", "areaM2", 2e-8))));
                return SystemDescription.FromJson(o);
            }
            var run = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false));
            run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 5e-12, "energyJ", 1.5e-3));
            var n2gas = MaterialLibrary.BuiltIn("N2");
            double ntot = n2gas.N2 + n2gas.Rotational.AdiabaticN2;
            var rRot = Simulation.Run(S(O("gas", O("species", "N2", "pressureBar", 1.0))), Specs.RunSpec.FromJson(run));
            var rIns = Simulation.Run(S(O("gas", O("species", "N2", "pressureBar", 1.0), "rotationalRaman", false, "n2", ntot)), Specs.RunSpec.FromJson(run));
            double b1 = RmsSpectralWidth(rRot.Output) / RmsSpectralWidth(rRot.Input), b2 = RmsSpectralWidth(rIns.Output) / RmsSpectralWidth(rIns.Input);
            Assert.Greater(b2, 1.5, "noticeable SPM");
            Assert.Close(b2, b1, 0.05, 0, "5 ps pulse: SPM as with n2_electronic + n2_rotational");
            // pure orientational response: circular light sees 1/4 of linear
            var pure = O("gas", O("species", "N2", "pressureBar", 1.0), "n2", 0.0);
            var lin = Simulation.Run(S(pure), Specs.RunSpec.FromJson(run));
            var circRun = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false));
            circRun.Set("pulse", O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 5e-12, "energyJ", 1.5e-3, "polarization", "rcp"));
            var circ = Simulation.Run(S(pure), Specs.RunSpec.FromJson(circRun));
            Assert.Close(0.25, circ.Records[0].BIntegralRad / lin.Records[0].BIntegralRad, 0.03, 0, "circular / linear orientational Kerr");
        }

        [Test(Reference = "linearity of every chi(3) response in n2Scale (the scale multiplies the third-order susceptibility)", Tolerance = "1e-6")]
        public void N2Scale_In1D_ScalesTheRotationalRamanToo()
        {
            // bug (D1): the 1-D path scaled only the Kerr n2, so air / N2 kept its full rotational Raman
            double B(JsonObject gas, double scale)
            {
                var o = O("format", "ultrafast-system", "version", 2, "name", "g", "materials", O("gas", gas));
                o.Set("elements", A(O("id", "cell", "type", "bulk", "material", "gas", "lengthM", 1.0, "beam", O("kind", "constant", "areaM2", 2e-8), "n2Scale", scale)));
                var run = O("format", "ultrafast-run", "version", 2, "physics", O("selfSteepening", false));
                run.Set("pulse", O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 5e-12, "energyJ", 1.5e-3));
                return Simulation.Run(SystemDescription.FromJson(o), Specs.RunSpec.FromJson(run)).Records[0].BIntegralRad;
            }
            var pureRot = O("gas", O("species", "N2", "pressureBar", 1.0), "n2", 0.0);   // orientational response only
            var full = O("gas", O("species", "N2", "pressureBar", 1.0));               // electronic Kerr + orientational
            foreach (var (name, gas) in new[] { ("orientational only", pureRot), ("Kerr + orientational", full) })
            {
                double b1 = B(gas, 1.0), b05 = B(gas, 0.5), b0 = B(gas, 0.0);
                Assert.Greater(b1, 1e-4, name + ": reference B (b1=" + b1 + ", b05=" + b05 + ")");
                Assert.Close(0.5 * b1, b05, 1e-6, 0, name + ": n2Scale 0.5 halves B");
                Assert.Less(b0, 1e-9 * b1, name + ": n2Scale 0 removes every third-order term");
            }
        }

        [Test(Reference = "published refractivities of the gases", Tolerance = "1e-09, 2 %")]
        public void Gases_IndicesOfNewSpecies()
        {
            double w = W(633e-9);
            Assert.Close(2.71e-4, MaterialLibrary.Gas("O2", 1.01325, 273.15).IndexAt(w) - 1, 0.02, 0, "O2 at STP");
            Assert.Close(4.49e-4, MaterialLibrary.Gas("CO2", 1.01325, 273.15).IndexAt(w) - 1, 0.02, 0, "CO2 at STP");
            Assert.Close(1.39e-4, MaterialLibrary.Gas("H2", 1.01325, 273.15).IndexAt(w) - 1, 0.02, 0, "H2 at STP");
            var h2 = MaterialLibrary.Gas("H2", 2.0, 293.15);
            Assert.Close(2.0 * 273.15 / 293.15 / 1.01325, (h2.IndexAt(w) - 1) / (MaterialLibrary.Gas("H2", 1.01325, 273.15).IndexAt(w) - 1), 1e-9, 0, "density scaling");
            Assert.True(MaterialLibrary.BuiltIn("Ar").Rotational == null, "atoms have no rotational response");
            Assert.True(MaterialLibrary.BuiltIn("Air").Rotational != null, "air: N2 + O2 rotational");
        }

        [Test(Reference = "refractiveindex.info formulas and tables", Tolerance = "1e-12, 1e-09, 2 %")]
        public void RefractiveIndexInfo_FormulasAndTablesMatchTheLibrary()
        {
            var pages = RefractiveIndexInfoPage.Bundled.ToList();
            Assert.True(pages.Contains("main/SiO2/Malitson"), "bundled fused silica");
            Assert.Greater(pages.Count, 15, "bundled subset");
            var fs = new RefractiveIndexInfoIndex(RefractiveIndexInfoPage.Load("main/SiO2/Malitson"));
            var n = new double[1];
            foreach (var l in new[] { 0.4e-6, 0.8e-6, 1.55e-6 })
            {
                fs.Evaluate(new[] { W(l) }, n);
                double lu = l * 1e6, l2 = lu * lu;
                double malitson = Math.Sqrt(1 + 0.6961663 * l2 / (l2 - 0.0684043 * 0.0684043) + 0.4079426 * l2 / (l2 - 0.1162414 * 0.1162414) + 0.8974794 * l2 / (l2 - 9.896161 * 9.896161));
                Assert.Close(malitson, n[0], 1e-12, 0, "formula 1 at " + lu + " um");
            }
            // every bundled page evaluates to a sensible index at 1 um
            foreach (var pg in pages)
            {
                var idx = new RefractiveIndexInfoIndex(RefractiveIndexInfoPage.Load(pg));
                idx.Evaluate(new[] { W(1.0e-6) }, n);
                Assert.True(n[0] > 1.0 && n[0] < 2.5, pg + ": n(1 um) = " + n[0]);
            }
            // gas page: density scaling to another pressure
            var ar = RefractiveIndexInfoPage.Load("main/Ar/Peck-15C");
            var a1 = new RefractiveIndexInfoIndex(ar); var a3 = new RefractiveIndexInfoIndex(ar, 3.0 * 1.01325, 288.15);
            var v1 = new double[1]; var v3 = new double[1];
            a1.Evaluate(new[] { W(800e-9) }, v1); a3.Evaluate(new[] { W(800e-9) }, v3);
            Assert.Close(3.0, (v3[0] - 1) / (v1[0] - 1), 1e-9, 0, "gas density scaling from SPECS");
            Assert.Close(MaterialLibrary.BuiltIn("Ar").IndexAt(W(800e-9)) - 1, (v1[0] - 1) * 288.15 / 293.15 * 1.0 / 1.01325, 0.02, 0, "agrees with the built-in argon (Borzsonyi) to 2 %");
            // tabulated nk parsing (inline page) and formula 6 / 7 / 8 / 9 sanity
            var yaml = "REFERENCES: \"test\"\nDATA:\n  - type: tabulated nk\n    data: |\n        0.5 1.5 0.01\n        1.0 1.4 0.02\nSPECS:\n    temperature: 20 °C\n";
            var t = RefractiveIndexInfoPage.Parse(yaml, "t");
            Assert.Close(1.45, t.IndexAt(0.75), 1e-12, 0, "tabulated n");
            Assert.Close(0.015, t.KAt(0.75), 1e-12, 0, "tabulated k");
            Assert.Close(293.15, t.Spec("temperature"), 1e-9, 0, "SPECS temperature");
            Assert.Close(1.5, RefractiveIndexInfoPage.Formula(8, new[] { (1.5 * 1.5 - 1) / (1.5 * 1.5 + 2), 0, 0, 0 }, 1.0), 1e-12, 0, "formula 8 (retro)");
        }

        [Test]
        public void RefractiveIndexInfo_AsAMaterialInARun()
        {
            var o = O("format", "ultrafast-system", "version", 2, "name", "rii", "materials", O("fsRii", O("index", O("kind", "refractiveindex", "page", "main/SiO2/Malitson"))));
            o.Set("elements", A(O("id", "b", "type", "bulk", "material", "fsRii", "lengthM", 0.01)));
            var s = SystemDescription.FromJson(o);
            var r = Simulation.Run(s, Spec(Gauss(800e-9, 20e-15, 1e-9)));
            // the library's fused silica is the same Malitson fit, but referenced to air: GDD agrees to 1e-3
            double gdd = r.Records[0].Own.GddS2;
            var lib = new Physics.BulkTerm(MaterialLibrary.BuiltIn("FusedSilica"), 0.01).DispersionAt(W(800e-9)).GddS2;
            Assert.Close(lib, gdd, 2e-3, 0, "GDD of 1 cm fused silica from the refractiveindex.info page");
        }
    }
}
