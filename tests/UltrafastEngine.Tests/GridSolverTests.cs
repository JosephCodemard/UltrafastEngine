// GridSolverTests.cs -- grid, carrier and solver behaviour against analytic references: the pulse sits at the requested carrier
// (not the carrier snapped onto the frequency lattice), pure SPM gives gamma P L whatever the grid, the angular dispersion of a
// grating is the grating equation to rounding error, and a pulse that broadens to zero frequency is reported.

using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Grid, carrier and solver accuracy")]
    public class GridSolverTests
    {
        private const double C = 2.99792458e8;

        private static JsonObject GridN(int n) => O("grid", O("n", n));

        [Test(Reference = "definition of the carrier: the input spectrum is centred on the requested wavelength", Tolerance = "1e-6 relative")]
        public void Pulse_IsCentredOnTheRequestedCarrier_NotTheSnappedOne()
        {
            // 10 fs at 1030 nm / 2 um: the lattice step is 8-31 nm, so a snapped carrier used to move the spectrum by up to 1.5 nm
            foreach (double lam in new[] { 515e-9, 1030e-9, 2000e-9 })
                foreach (int n in new[] { 0, 1024, 4096 })
                {
                    var r = Go(Sys(O("id", "p", "type", "probe")), Spec(Gauss(lam, 10e-15, 1e-9), n == 0 ? null : GridN(n)));
                    double wc = SpectralCentroid(r.Input), w0 = 2 * Math.PI * C / lam;
                    Assert.Close(w0, wc, 1e-6, 0, "spectral centre at " + lam * 1e9 + " nm, n = " + n);
                }
        }

        /// <summary>Phase of the output relative to the input at time sample i (rad), global phase removed at sample j.</summary>
        private static double RelPhase(RunResult r, int i, int j)
        {
            var a = r.Input.TemporalAx(); var b = r.Output.TemporalAx();
            return Math.Atan2((b[i] / a[i] * Complex.Conjugate(b[j] / a[j])).Imaginary, (b[i] / a[i] * Complex.Conjugate(b[j] / a[j])).Real);
        }

        [Test(Reference = "pure SPM in a dispersion-free fibre: A_out = A_in exp(i gamma |A_in|^2 L)", Tolerance = "2e-5 relative")]
        public void PureSpm_PhaseIsGammaPL_OnEveryGrid_At515_1030_And2000nm()
        {
            // B = 0.3 at the peak, 10 fs (40 fs at 2 um: a 1.5-cycle pulse reaches zero frequency, see the last test). gamma is given at the requested wavelength; before the fix the Kerr term used the snapped
            // carrier, so the phase was off by (w_snap / w_req - 1) = 1e-3 .. 4e-3 and varied from grid to grid.
            foreach (double lam in new[] { 515e-9, 1030e-9, 2000e-9 })
                foreach (int n in new[] { 0, 1024, 8192 })
                {
                    double tau = lam > 1.5e-6 ? 40e-15 : 10e-15, p0 = 1e3, len = 1.0, gamma = 0.3 / (p0 * len);
                    var sys = Sys(O("id", "f", "type", "fiber", "lengthM", len, "betas", A(0.0), "gamma", gamma, "raman", "none"));
                    var r = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", lam, "durationFwhmS", tau, "peakPowerW", p0), n == 0 ? O("physics", O("selfSteepening", false)) : O("physics", O("selfSteepening", false), "grid", O("n", n))));
                    var pin = PulseMetrics.TemporalPower(r.Input);
                    int ip = Array.IndexOf(pin, pin.Max());
                    int iw = Enumerable.Range(0, pin.Length).Where(k => pin[k] > 0.3 * pin.Max()).OrderBy(k => Math.Abs(pin[k] - 0.3 * pin.Max())).First();
                    double expected = gamma * len * (pin[ip] - pin[iw]);
                    Assert.Close(expected, RelPhase(r, ip, iw), 2e-5, 0, "SPM phase at " + lam * 1e9 + " nm, n = " + n);
                }
        }

        [Test(Reference = "grating equation: d theta / d lambda = m / (period cos beta) (Treacy, IEEE JQE 5, 454 (1969), eq. 2)", Tolerance = "1e-6 relative")]
        public void Grating_TrackedAngularDispersion_IsTheGratingEquation_OnAnyGrid()
        {
            double lam = 1030e-9, lpm = 1200, period = 1e-3 / lpm;
            double beta = Math.Asin(lam / period - Math.Sin(Math.PI / 4));
            double ad = 1 / (period * Math.Cos(beta));
            foreach (int n in new[] { 0, 512, 4096 })
            {
                var spec = Spec(Gauss(lam, 100e-15, 1e-9), n == 0 ? O("inputBeam", O("kind", "gaussian", "w0M", 2e-3)) : O("inputBeam", O("kind", "gaussian", "w0M", 2e-3), "grid", O("n", n, "windowS", 7e-12 * (n / 512.0))));
                var r = Go(Sys(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", lpm, "order", 1)), spec);
                Assert.Close(ad, Math.Abs(r.Records.First(x => x.Id == "g1").Values["trackedAngularDispersionXRadPerM"]), 1e-6, 0, "angular dispersion, n = " + n);
            }
        }

        [Test(Reference = "energy conservation of the envelope model: a pulse broadened to zero frequency loses energy there, and says so", Tolerance = "exact booking")]
        public void PulseBroadenedToZeroFrequency_IsReportedAndBooked()
        {
            // 1.5 cycles (10 fs at 2 um), B = 3: the SPM spectrum reaches omega = 0, where an envelope model has no index and no field
            var sys = Sys(O("id", "f", "type", "fiber", "lengthM", 1.0, "betas", A(0.0), "gamma", 3e-3, "raman", "none"));
            var r = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", 2e-6, "durationFwhmS", 10e-15, "peakPowerW", 1e3), O("physics", O("selfSteepening", false))));
            Assert.True(r.Warnings.Any(w => w.Contains("zero frequency")), "a warning about the spectrum reaching zero frequency");
            var rec = r.Records.First(x => x.Id == "f");
            Assert.True(rec.LossJ.ContainsKey("spectrum at non-positive frequency"), "the lost energy is booked under its own mechanism");
            Assert.True(!rec.LossJ.ContainsKey("numerical") || rec.LossJ["numerical"] < 0.05 * rec.LossJ["spectrum at non-positive frequency"], "not as numerical error");
            // 100 fs at 2 um with the same B: no warning
            var r2 = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", 2e-6, "durationFwhmS", 100e-15, "peakPowerW", 1e2), O("physics", O("selfSteepening", false))));
            Assert.False(r2.Warnings.Any(w => w.Contains("zero frequency")), "no warning for a many-cycle pulse");
        }

        [Test(Reference = "elliptical Gaussian through a circular aperture: 2-D quadrature; small aperture T = 2 a^2 / (wx wy); round beam 1 - exp(-2 a^2 / w^2)", Tolerance = "1e-6 (quadrature), 1e-5 (small-aperture limit)")]
        public void CircularAperture_OnAnEllipticalBeam_IsTheTwoDimensionalIntegral()
        {
            // the engine used the geometric-mean radius, 1 - exp(-2 a^2 / (wx wy)): 0.8816 instead of 0.7897 for 0.6 x 1.2 mm and a = 0.8 mm
            double wx = 0.6e-3, wy = 1.2e-3, a = 0.8e-3;
            var ap = new UltrafastEngine.Physics.CircularAperture(a);
            int n = 4000; double h = 2 * a / n, sum = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double x = -a + (i + 0.5) * h, y = -a + (j + 0.5) * h;
                    if (x * x + y * y <= a * a) sum += Math.Exp(-2 * x * x / (wx * wx) - 2 * y * y / (wy * wy));
                }
            double reference = sum * h * h * 2 / (Math.PI * wx * wy);
            Assert.Close(reference, ap.PowerTransmission(wx, wy, 0, 0), 2e-4, 0, "ellipse 0.6 x 1.2 mm (the midpoint quadrature's own error is the tolerance)");
            Assert.Close(1 - Math.Exp(-2 * a * a / (wx * wx)), ap.PowerTransmission(wx, wx, 0, 0), 1e-12, 0, "round beam");
            var small = new UltrafastEngine.Physics.CircularAperture(1e-3 * wx);
            Assert.Close(2 * small.Radius * small.Radius / (wx * wy), small.PowerTransmission(wx, wy, 0, 0), 1e-5, 0, "small-aperture limit: pi a^2 x peak intensity");
        }

        [Test(Reference = "decentred Gaussian through a circular aperture: round beam = noncentral chi-square probability (Poisson mixture of regularised incomplete gamma functions, i.e. 1 - Marcum Q1(2d/w, 2a/w)); elliptical beam = 2-D quadrature; small aperture T = 2 a^2/(wx wy) exp(-2 d^2/w^2)", Tolerance = "1e-6 (round), 2e-4 (quadrature), 1e-3 (limit)")]
        public void CircularAperture_OnADecentredBeam_IsTheOffsetGaussianIntegral()
        {
            // round beam: |X - c| < a for X ~ N(0, (w/2)^2 I) is a noncentral chi-square with 2 degrees of freedom
            double w = 1e-3, a = 1e-3, d = 0.7e-3, lam = 2 * d * d / (w * w), t = 2 * a * a / (w * w), cdf = 0, pois = Math.Exp(-lam);
            for (int j = 0; j < 80; j++)
            {
                double tail = 0, term = 1;                                  // sum_{i<=j} t^i / i!
                for (int i = 0; i <= j; i++) { if (i > 0) term *= t / i; tail += term; }
                cdf += pois * (1 - Math.Exp(-t) * tail);
                pois *= lam / (j + 1);
            }
            var ap = new UltrafastEngine.Physics.CircularAperture(a);
            Assert.Close(cdf, ap.PowerTransmission(w, w, d, 0), 1e-6, 0, "round beam, d = 0.7 w");
            Assert.Close(cdf, ap.PowerTransmission(w, w, d * 0.6, d * 0.8), 1e-6, 0, "the direction of the decentre does not matter for a round beam");
            Assert.Close(ap.PowerTransmission(w, w, 0, 0), ap.PowerTransmission(w, w, 1e-9 * w, 0), 1e-6, 0, "continuous at zero decentre");
            // elliptical beam, decentred in both directions: 2-D midpoint quadrature over the disk
            double wx = 0.6e-3, wy = 1.2e-3, dx = 0.5e-3, dy = -0.3e-3, a2 = 0.8e-3;
            var ap2 = new UltrafastEngine.Physics.CircularAperture(a2);
            int n = 3000; double h = 2 * a2 / n, sum = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double x = -a2 + (i + 0.5) * h, y = -a2 + (j + 0.5) * h;
                    if (x * x + y * y <= a2 * a2) sum += Math.Exp(-2 * (x - dx) * (x - dx) / (wx * wx) - 2 * (y - dy) * (y - dy) / (wy * wy));
                }
            Assert.Close(sum * h * h * 2 / (Math.PI * wx * wy), ap2.PowerTransmission(wx, wy, dx, dy), 2e-4, 0, "ellipse 0.6 x 1.2 mm, decentre (0.5, -0.3) mm");
            // a small aperture far out in the wing: pi a^2 times the intensity there
            var small = new UltrafastEngine.Physics.CircularAperture(0.01 * w);
            Assert.Close(2 * small.Radius * small.Radius / (w * w) * Math.Exp(-2 * 1.2 * 1.2), small.PowerTransmission(w, w, 1.2 * w, 0), 1e-3, 0, "small-aperture limit at 1.2 w");
            Assert.True(ap.PowerTransmission(w, w, 5 * w, 0) < 1e-6, "an aperture five radii away passes almost nothing");
        }

        private static JsonObject Quartz() => O("name", "quartz (Ghosh 1999)",
            "no", O("kind", "sellmeier", "A", 1.28604141, "B", new[] { 1.07044083, 1.10202242 }, "C", new[] { 1.00585997e-2, 100.0 }),
            "ne", O("kind", "sellmeier", "A", 1.28851804, "B", new[] { 1.09509924, 1.15662475 }, "C", new[] { 1.02101864e-2, 100.0 }),
            "pointGroup", "general", "deffPmPerV", 0.3);

        private static (double eta, double dk) Shg(double length)
        {
            var sys = Sys(O("id", "q", "type", "crystal", "crystal", Quartz(), "lengthM", length, "cut", O("thetaDeg", 90, "phiDeg", 0),
                "interaction", O("process", "shg", "type", "0"), "overlap", O("model", "planeWave", "radiusM", 1e-3), "chi3", false));
            var r = Go(sys, Spec(Gauss(694.3e-9, 5e-12, 1e-6)));
            var v = r.Records.First(x => x.Id == "q").Values;
            return (v["conversionEfficiency"], v["deltaK0PerM"]);
        }

        [Test(Reference = "plane-wave SHG at low conversion: eta(L) ~ sin^2(dk L / 2) (Armstrong et al., Phys. Rev. 127, 1918 (1962), eq. 2.14 limit)", Tolerance = "4 % (dispersion of the 5 ps pulse and finite bandwidth)")]
        public void Shg_StronglyPhaseMismatchedThickCrystal_FollowsTheSincLaw_AtTheDefaultStepper()
        {
            // 0.8 mm of quartz, dk L = 370 rad (coherence length 6.9 um). The adaptive step used to span 18 rad of mismatch phase and the
            // second-harmonic energy came out 150-960 x the law. eta(L_m) at the maxima L_m = (2m + 1) pi / dk equals eta(L_0).
            var (e0, dk) = Shg(Math.PI / 4.6e5);
            double l0 = Math.PI / Math.Abs(dk);
            var (eRef, dkRef) = Shg(l0);
            foreach (int m in new[] { 57, 58, 59 })
            {
                double L = (2 * m + 1) * l0;
                var (e, _) = Shg(L);
                Assert.Close(eRef, e, 0.04, 0, "eta at the " + m + "th maximum (L = " + L * 1e3 + " mm)");
                var (eMin, _) = Shg(2 * m * l0 + 0.0);
                Assert.Less(eMin, 0.02 * eRef, "eta at the nodes (L = 2 m l_c)");
            }
        }

        private static UltrafastEngine.Systems.SystemDescription SlowResponseCell(double tau)
        {
            var mat = O("index", O("kind", "expression", "expr", "1.6137"), "n2", 3e-19, "ramanFraction", 1.0,
                        "raman", O("kind", "expression", "expr", "exp(-t/" + tau.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")/" + tau.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "*step(t)"));
            var o = O("format", "ultrafast-system", "version", 2, "name", "cell", "materials", O("slow", mat));
            o.Set("elements", new JsonArray(new JsonValue[] { O("id", "cell", "type", "bulk", "material", "slow", "lengthM", 0.01, "beam", O("kind", "constant", "radiusM", 50e-6)) }));
            return UltrafastEngine.Systems.SystemDescription.FromJson(o);
        }

        private static double TotalVariation(UltrafastEngine.Pulses.PulseField a, UltrafastEngine.Pulses.PulseField b)
        {
            // spectra on a common 0.5 THz bin axis (the grids differ in step)
            double bin = 2 * Math.PI * 0.5e12; int nb = 400;
            Func<UltrafastEngine.Pulses.PulseField, double[]> hist = p =>
            {
                var h = new double[nb]; var s = p.SpectralIntensity(); double w0 = p.Grid.ReportOmega, tot = 0;
                for (int i = 0; i < s.Length; i++) { int k = (int)Math.Floor((p.Grid.OmegaAbsolute[i] - w0) / bin) + nb / 2; if (k >= 0 && k < nb) h[k] += s[i]; tot += s[i]; }
                return h.Select(v => v / tot).ToArray();
            };
            var ha = hist(a); var hb = hist(b);
            return 0.5 * Enumerable.Range(0, nb).Sum(k => Math.Abs(ha[k] - hb[k]));
        }

        [Test(Reference = "periodic time grid: the response to a pulse must fit the window (convergence against a window four times larger)", Tolerance = "total variation of the spectra 0.02")]
        public void DelayedResponseLongerThanTheWindow_IsWarnedAbout_AndTheAutoGridHoldsIt()
        {
            // tau = 18 ps (CS2-like), 6 ps pulse, B ~ 30 rad. The planner knew only the pulse and the dispersion: 120 ps was "adequate" and the
            // spectrum was wrong; the memory of the response (9.2 tau at 1e-4) is now part of the required window.
            double tau = 18e-12, fwhm = 6e-12;
            var sys = SlowResponseCell(tau);
            var pulse = O("shape", "gaussian", "centerWavelengthM", 694.3e-9, "durationFwhmS", fwhm, "energyJ", 3e-9, "polarization", "x");
            var small = Go(sys, Spec(pulse, O("physics", O("selfSteepening", false), "grid", O("n", 1 << 14, "windowS", 120e-12, "auto", false))));
            Assert.True(small.Warnings.Any(w => w.Contains("too small")), "the 120 ps window is reported as too small for an 18 ps response");
            var auto = Go(sys, Spec(pulse, O("physics", O("selfSteepening", false))));
            Assert.Greater(auto.Output.Grid.N * auto.Output.Grid.DTime, 9 * tau, "the planned window holds the response");
            var big = Go(sys, Spec(pulse, O("physics", O("selfSteepening", false), "grid", O("n", 1 << 16, "windowS", 600e-12, "auto", false))));
            Assert.Less(TotalVariation(auto.Output, big.Output), 0.02, "the auto grid agrees with a 600 ps window");
        }

        private static string R(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A Kerr cell (constant index, 1 cm) whose material Raman response is h(t) = 0.7 exp(-t/tau)/tau + 0.3 g(t - tRev), g a
        /// normalised Gaussian of rms width sigma: a fast response and one narrow revival.</summary>
        private static UltrafastEngine.Systems.SystemDescription RevivalCell(double tau, double tRev, double sigma, double fR)
        {
            string h = "step(t)*(0.7*exp(-t/" + R(tau) + ")/" + R(tau) + " + 0.3*exp(-0.5*((t-" + R(tRev) + ")/" + R(sigma) + ")^2)/(" + R(sigma) + "*sqrt(2*pi)))";
            var mat = O("index", O("kind", "expression", "expr", "1.6137"), "n2", 3e-19, "ramanFraction", fR, "raman", O("kind", "expression", "expr", h));
            var o = O("format", "ultrafast-system", "version", 2, "name", "revival", "materials", O("rev", mat));
            o.Set("elements", new JsonArray(new JsonValue[] { O("id", "cell", "type", "bulk", "material", "rev", "lengthM", 0.01, "beam", O("kind", "constant", "radiusM", 50e-6)) }));
            return UltrafastEngine.Systems.SystemDescription.FromJson(o);
        }

        /// <summary>Relative difference of two numbers (0 when both are 0).</summary>
        private static double Rel(double a, double b) => a == b ? 0 : Math.Abs(a - b) / Math.Max(Math.Abs(a), Math.Abs(b));
        /// <summary>The two spectral observables the convergence tests compare: the shift of the spectral centroid and the growth of the rms width (rad/s).</summary>
        private static (double Shift, double Broadening) SpectralChange(RunResult r) =>
            (SpectralCentroid(r.Output) - SpectralCentroid(r.Input), RmsSpectralWidth(r.Output) - RmsSpectralWidth(r.Input));

        [Test(Reference = "periodic time grid: a delayed response with a revival at 10.1 ps must fit the window (convergence against a window four times larger at the same time step)", Tolerance = "memory within 2 % of the revival; centroid shift and rms broadening 1e-6 relative")]
        public void DelayedResponseWithANarrowRevival_TheWindowHoldsTheRevival()
        {
            // h = 0.7 fast (100 fs) + 0.3 in a 20 fs rms revival at 10.1 ps. The original memory scan (a geometric time axis, step 1.9 % of t:
            // samples at 10.0 and 10.19 ps) stepped over the revival and gave 0.98 ps; the planner then kept a 6.2 ps window (N = 512, dt = 12 fs), the kernel
            // was cut at half of it and renormalised, so the pulse saw the fast part 1/0.7 times too strongly: the Raman red shift came out
            // 42 % too large and the rms broadening 15 % too large. The dense scan (linear inside each decade) finds the revival.
            double tau = 100e-15, tRev = 10.1e-12, sigma = 20e-15;
            var sys = RevivalCell(tau, tRev, sigma, 0.5);
            var h = new UltrafastEngine.Materials.FunctionDelayedResponse("revival", t => t.Select(x => x < 0 ? 0 : 0.7 * Math.Exp(-x / tau) / tau + 0.3 * Math.Exp(-0.5 * Math.Pow((x - tRev) / sigma, 2)) / (sigma * Math.Sqrt(2 * Math.PI))).ToArray());
            Assert.Less(UltrafastEngine.Engine.GridPlanner.GeometricMemoryS(h, 1e-4), 1.5e-12, "the original geometric scan misses the revival (the old failure)");
            var pulse = O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 200e-15, "energyJ", 1.8e-7, "polarization", "x");
            var phys = O("selfSteepening", false);
            var auto = Go(sys, Spec(pulse, O("physics", phys)));
            double win = auto.Output.Grid.N * auto.Output.Grid.DTime;
            Assert.Close(tRev, UltrafastEngine.Engine.GridPlanner.ResponseMemoryS(h), 0.02, 0, "the memory ends at the revival");
            Assert.Greater(win, 2 * tRev, "the planned window holds the revival (the kernel is sampled to +T/2)");
            // the reference: four times the planned window at the same time step
            var big = Go(sys, Spec(pulse, O("physics", phys, "grid", O("n", 4 * auto.Output.Grid.N, "windowS", 4 * win, "auto", false))));
            var small = Go(sys, Spec(pulse, O("physics", phys, "grid", O("n", 512, "windowS", 512 * auto.Output.Grid.DTime, "auto", false))));   // the old plan
            var (sA, bA) = SpectralChange(auto); var (sB, bB) = SpectralChange(big); var (sS, bS) = SpectralChange(small);
            Assert.Greater(auto.Records[0].BIntegralRad, 2, "strongly nonlinear");
            Assert.Less(Rel(sA, sB), 1e-6, "converged: the centroid shift equals that of a window four times larger");
            Assert.Less(Rel(bA, bB), 1e-6, "converged: the rms broadening equals that of a window four times larger");
            Assert.Greater(Rel(sS, sB), 0.2, "the window the old planner chose: the delayed part 1/0.7 too strong");
            Assert.True(small.Warnings.Any(w => w.Contains("delayed response of element 'cell'") && w.Contains("needs a window of at least")), "a manual window that cuts the response is warned about, with the size it needs");
            // grid.maxN too small for the window at the resolution the spectrum needs: the planner limits the window and says what it needs
            var capped = Go(sys, Spec(pulse, O("physics", phys, "grid", O("maxN", 64))));
            Assert.Less(capped.Output.Grid.N * capped.Output.Grid.DTime, 2 * tRev, "maxN = 64 limits the window");
            Assert.True(capped.Warnings.Any(w => w.Contains("delayed response of element 'cell'") && w.Contains("grid.maxN = 64") && w.Contains("N = ")), "the warning gives the window and N it needs");
        }

        [Test(Reference = "no change for smooth responses: the memory of every built-in delayed response and of every catalogue material is the value of the original scan, so their grids are unchanged", Tolerance = "exact (bit-identical)")]
        public void ResponseMemory_OfSmoothResponses_IsTheOriginalValue()
        {
            var reg = Models.ModelRegistry.CreateDefault();
            var list = new System.Collections.Generic.List<Models.IDelayedResponse>();
            foreach (var kind in new[] { "silica", "linAgrawal", "hollenbeck", "orientational" })
                list.Add(reg.Create<Models.IDelayedResponse>("delayedResponse", Models.ModelSpec.FromJson(O("kind", kind), Models.ModelSource.EngineDefault, "test")));
            list.Add(new UltrafastEngine.Materials.DampedOscillatorResponse(62.5e-15, 77e-15));
            foreach (var name in UltrafastEngine.Materials.MaterialLibrary.BuiltInNames)
            {
                UltrafastEngine.Materials.Material m;
                try { m = UltrafastEngine.Materials.MaterialLibrary.BuiltIn(name); } catch { continue; }
                if (m?.Raman != null) list.Add(m.Raman);
            }
            Assert.Greater(list.Count, 6, "responses checked");
            foreach (var r in list)
            {
                double old = UltrafastEngine.Engine.GridPlanner.GeometricMemoryS(r, 1e-4), now = UltrafastEngine.Engine.GridPlanner.ResponseMemoryS(r);
                Assert.True(old > 0 && now == old, r.Description + ": memory " + now + " s, original " + old + " s");
            }
        }

        /// <summary>A Kerr cell (1 cm, n2 = 0) with a user time response that convolves the intensity with exp(-t/tau)/tau on the periodic
        /// window (an FFT, as a callback might): dn = n2d (h * I).</summary>
        private static Models.ModelRegistry SlowCallback(double tau, double n2d)
        {
            var reg = Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("slowKerr", (Models.TimeResponseCallback)((ctx, re, im) =>
            {
                int n = ctx.N; double dt = ctx.T[1] - ctx.T[0];
                var hk = new Complex[n]; var ik = new Complex[n];
                for (int j = 0; j < n; j++) { hk[j] = Math.Exp(-j * dt / tau) / tau * dt; ik[j] = ctx.Intensity[j]; }
                var H = UltrafastEngine.Numerics.ComplexFft.Forward(hk); var I = UltrafastEngine.Numerics.ComplexFft.Forward(ik);
                for (int k = 0; k < n; k++) H[k] *= I[k];
                var y = UltrafastEngine.Numerics.ComplexFft.Inverse(H);
                for (int i = 0; i < n; i++) re[i] = n2d * y[i].Real;
            }));
            return reg;
        }

        [Test(Reference = "periodic time grid: a user time response that declares its memory (memoryS) gets a window that holds it (convergence against a window four times larger at the same time step)", Tolerance = "centroid shift and rms broadening 1e-6 relative")]
        public void TimeResponseCallback_DeclaredMemory_IsHeldByTheWindow()
        {
            // a callback convolving I with exp(-t/5 ps) on the window: the engine cannot see inside it. Without memoryS the planner sizes the
            // window for the 300 fs pulse alone (12.4 ps) and the slow index change wraps around onto the pulse's leading edge: the centroid
            // shift came out 9 % and the rms broadening 16 % off. With memoryS = 9.2 tau (1e-4 of the weight left) the window is 199 ps.
            double tau = 5e-12, n2d = 3e-19;
            var mat = O("index", O("kind", "expression", "expr", "1.6137"), "n2", 0.0);
            var o = O("format", "ultrafast-system", "version", 2, "name", "slow", "materials", O("slow", mat));
            o.Set("elements", new JsonArray(new JsonValue[] { O("id", "cell", "type", "bulk", "material", "slow", "lengthM", 0.01, "beam", O("kind", "constant", "radiusM", 50e-6)) }));
            var sys = UltrafastEngine.Systems.SystemDescription.FromJson(o);
            var pulse = O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 300e-15, "energyJ", 4e-7, "polarization", "x");
            JsonObject Run(JsonObject model, JsonObject grid)
            {
                var r = O("physics", O("selfSteepening", false));
                if (grid != null) r.Set("grid", grid);
                r.Set("overrides", A(O("target", "element:cell", "property", "timeResponse", "mode", "add", "model", model)));
                return r;
            }
            var declared = O("kind", "callback", "name", "slowKerr", "memoryS", 9.2 * tau);
            var silent = O("kind", "callback", "name", "slowKerr");
            var auto = Go(sys, Spec(pulse, Run(declared, null)), SlowCallback(tau, n2d));
            var bare = Go(sys, Spec(pulse, Run(silent, null)), SlowCallback(tau, n2d));
            var big = Go(sys, Spec(pulse, Run(silent, O("n", 4 * auto.Output.Grid.N, "windowS", 4 * auto.Output.Grid.N * auto.Output.Grid.DTime, "auto", false))), SlowCallback(tau, n2d));
            double win = auto.Output.Grid.N * auto.Output.Grid.DTime;
            var (sA, bA) = SpectralChange(auto); var (sB, bB) = SpectralChange(big); var (s0, b0) = SpectralChange(bare);
            Assert.Greater(win, 2 * 9.2 * tau, "the planned window holds the declared memory");
            Assert.Less(Rel(sA, sB), 1e-6, "converged: the centroid shift equals that of a window four times larger");
            Assert.Less(Rel(bA, bB), 1e-6, "converged: the rms broadening equals that of a window four times larger");
            Assert.Greater(Rel(s0, sB), 0.05, "without memoryS the window is sized for the pulse and the response wraps (centroid shift)");
            Assert.Greater(Rel(b0, bB), 0.05, "without memoryS: rms broadening");
        }

        [Test(Reference = "self-steepening switch: with it off the pulse stays symmetric in time (centroid shift 0); with it on the centroid moves (Agrawal, Nonlinear Fiber Optics, sec. 4.4)", Tolerance = "off: centroid shift < 1 % of the on value")]
        public void Fiber_ElementLevelSelfSteepeningFalse_SwitchesItOff_AndBulkToo()
        {
            // 20 fs, B = 8 at 1030 nm: the shock moves the temporal centroid by tens of attoseconds to femtoseconds
            Func<JsonObject, JsonObject, double> shift = (element, physics) =>
            {
                var r = Go(Sys(element), Spec(O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 20e-15, "peakPowerW", 1e5), O("physics", physics)));
                return TemporalCentroid(r.Output) - TemporalCentroid(r.Input);
            };
            var fib = O("id", "f", "type", "fiber", "lengthM", 0.08, "betas", A(0.0), "gamma", 1e-3, "raman", "none");
            var fibOff = O("id", "f", "type", "fiber", "lengthM", 0.08, "betas", A(0.0), "gamma", 1e-3, "raman", "none", "selfSteepening", false);
            double on = shift(fib, O("selfSteepening", true)), runOff = shift(fib, O("selfSteepening", false)), elOff = shift(fibOff, O("selfSteepening", true));
            Assert.Greater(Math.Abs(on), 1e-16, "self-steepening moves the pulse");
            Assert.Less(Math.Abs(runOff), 0.01 * Math.Abs(on), "off by the run setting");
            Assert.Close(runOff, elOff, 0, 1e-18, "off by the element's own switch");
            Func<bool, JsonObject> bulk = ss => O("id", "b", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-3, "n2", 3e-20, "beam", O("kind", "constant", "radiusM", 2e-6), "selfSteepening", ss);
            double bulkOn = shift(bulk(true), O("selfSteepening", false)), bulkOff = shift(bulk(false), O("selfSteepening", true));
            Assert.Greater(Math.Abs(bulkOn), 100 * Math.Abs(bulkOff), "bulk element switch (on in the element, off in the run, versus the reverse)");
        }

        [Test(Reference = "definition D = -2 pi c beta2 / lambda^2 (Agrawal, Nonlinear Fiber Optics); beta2(w) = b3 (w - wz) + b4 (w - wz)^2 / 2", Tolerance = "2e-4 relative to beta2 (a linear interpolation of the 53-point table gave ~1e-3)")]
        public void FiberDispersionTable_Coarse_ReproducesBeta2_BetweenThePoints()
        {
            double wz = 2 * Math.PI * C / 1.55e-6, b3 = 1e-40, b4 = 3e-55;
            Func<double, double> beta2 = w => b3 * (w - wz) + b4 * (w - wz) * (w - wz) / 2;
            var wl = Enumerable.Range(0, 53).Select(i => 1000e-9 + i * (2000e-9 / 52)).ToArray();   // 53 points, 1.0 - 3.0 um
            var d = wl.Select(l => -2 * Math.PI * C * beta2(2 * Math.PI * C / l) / (l * l) * 1e6).ToArray();   // s/m^2 -> ps/(nm km)
            foreach (double lam in new[] { 1.60e-6, 1.43e-6, 2.17e-6 })
            {
                var fib = O("id", "f", "type", "fiber", "lengthM", 1.0, "gamma", 0.0, "raman", "none", "nonlinear", false,
                            "dispersion", O("wavelengthsM", new JsonArray(wl.Select(x => (JsonValue)x).ToArray()), "dPsNmKm", new JsonArray(d.Select(x => (JsonValue)x).ToArray())));
                var r = Go(Sys(fib), Spec(Gauss(lam, 100e-15, 1e-12)));
                var rec = r.Records.First(x => x.Id == "f");
                double w = 2 * Math.PI * C / rec.CarrierWavelengthM;
                Assert.Close(beta2(w), rec.Values["beta2S2PerM"], 2e-4, 0, "beta2 at " + lam * 1e9 + " nm");
                Assert.Close(beta2(w) * 1.0, rec.Own.GddS2, 2e-3, 0, "GDD of 1 m at " + lam * 1e9 + " nm");
            }
        }
    }
}
