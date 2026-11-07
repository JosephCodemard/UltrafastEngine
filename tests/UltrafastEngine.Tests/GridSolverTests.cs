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
    }
}
