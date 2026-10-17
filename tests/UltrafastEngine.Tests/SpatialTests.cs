using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    public class SpatialTests
    {
        private const double Lam = 1030e-9;

        [Test]
        public void RadialBasis_QuadratureAndTransformAreExact()
        {
            var b = new RadialBasis(64, 3e-3);
            double w = 0.8e-3, s = 0;
            for (int m = 0; m < b.M; m++) s += b.Weights[m] * Math.Exp(-2 * b.Radius[m] * b.Radius[m] / (w * w));
            Assert.Close(Math.PI * w * w / 2, s, 1e-6, 0, "integral of a Gaussian");
            var f = new Complex[b.M];
            for (int m = 0; m < b.M; m++) f[m] = new Complex(Math.Exp(-b.Radius[m] * b.Radius[m] / (w * w)), 0.3 * b.Radius[m] / w);
            var g = (Complex[])f.Clone();
            b.ToK(g, 1); b.ToSpace(g, 1);
            double err = f.Zip(g, (x, y) => (x - y).Magnitude).Max();
            Assert.True(err < 1e-12, "Hankel transform round trip error " + err);
        }

        private static JsonObject GaussBeam(double w0, double zWaist) => O("kind", "gaussian", "w0M", w0, "zWaistM", zWaist, "wavelengthM", Lam);

        private static double BeamRadiusAfter(string model, double lengthM, double w0, int points = 64)
        {
            var tv = model == "radial" ? O("model", "radial", "points", points, "radiusM", 6 * w0 * Math.Sqrt(1 + Math.Pow(lengthM / (Math.PI * w0 * w0 / Lam), 2)) / 1.0)
                                       : O("model", "cartesian", "nx", points, "ny", points, "halfWidthM", 5 * w0 * Math.Sqrt(1 + Math.Pow(lengthM / (Math.PI * w0 * w0 / Lam), 2)));
            var sys = Sys(O("id", "free", "type", "gap", "material", "Vacuum", "lengthM", lengthM, "beam", GaussBeam(w0, 0), "transverse", tv));
            var r = Go(sys, Spec(Gauss(Lam, 200e-15, 1e-6)));
            Assert.Close(r.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-9, 0, model + " energy");
            Assert.Close(1.0, r.Records[0].BeamQualityM2, 0.02, 0, model + " M^2 of a Gaussian");
            return r.Records[0].BeamRadiusXM;
        }

        [Test]
        public void Diffraction_GaussianBeamRadiusMatchesAnalytic_Radial()
        {
            double w0 = 100e-6, zr = Math.PI * w0 * w0 / Lam;
            double w = BeamRadiusAfter("radial", 2 * zr, w0);
            Assert.Close(w0 * Math.Sqrt(5), w, 0.005, 0, "w(2 zR)");
        }

        [Test]
        public void Diffraction_GaussianBeamRadiusMatchesAnalytic_Cartesian()
        {
            double w0 = 100e-6, zr = Math.PI * w0 * w0 / Lam;
            double w = BeamRadiusAfter("cartesian", 1.5 * zr, w0, 64);
            Assert.Close(w0 * Math.Sqrt(1 + 2.25), w, 0.01, 0, "w(1.5 zR)");
        }

        [Test]
        public void LensAndGap_FocusAGaussianBeamAsGaussianOpticsPredicts()
        {
            // collimated w = 1 mm -> f = 0.5 m lens -> 0.5 m: waist w_f = lambda f / (pi w)
            double w = 1e-3, f = 0.5;
            var tvIn = O("model", "radial", "points", 64, "radiusM", 3.5e-3);
            var tvFocus = O("model", "radial", "points", 224, "radiusM", 3.5e-3); // ~20 um spacing for a 164 um focus
            var sys = Sys(
                O("id", "start", "type", "gap", "material", "Vacuum", "lengthM", 0.0, "beam", GaussBeam(w, 0), "transverse", tvIn),
                O("id", "lens", "type", "lens", "focalLengthM", f, "medium", "Vacuum"),
                O("id", "toFocus", "type", "gap", "material", "Vacuum", "lengthM", f, "transverse", tvFocus));
            var r = Go(sys, Spec(Gauss(Lam, 300e-15, 1e-6)));
            double wf = Lam * f / (Math.PI * w);
            Assert.Close(wf, r.Records[2].BeamRadiusXM, 0.02, 0, "focal spot radius");
            Assert.True(r.Records[2].Notes.Any(n => n.Contains("carried exactly")), "profile transported exactly");
        }

        [Test(Category = "physics")]
        public void CarriedProfile_ThroughFreeSpace_ConservesEnergyAfterSpectralBroadening()
        {
            // a self-focused, spectrally broadened beam: the profile is stored at sampled frequencies and must carry the
            // exact energy through the following (linear) spatial elements
            var tv = O("model", "radial", "points", 48, "radiusM", 0.4e-3);
            var sys = Sys(
                O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 5e-3, "beam", GaussBeam(100e-6, 0), "transverse", tv),
                O("id", "gap1", "type", "gap", "material", "Vacuum", "lengthM", 5e-3, "transverse", tv),
                O("id", "gap2", "type", "gap", "material", "Vacuum", "lengthM", 5e-3, "transverse", O("model", "radial", "points", 64, "radiusM", 0.6e-3)));
            var r = Go(sys, Spec(Gauss(Lam, 200e-15, 3e-6)));
            Assert.Greater(RmsSpectralWidth(r.Output) / RmsSpectralWidth(r.Input), 1.03, "spectrum broadened");
            Assert.Close(r.Records[0].EnergyOutJ, r.Output.TotalEnergyJ(), 1e-9, 0, "free space conserves energy");
        }

        [Test]
        public void ThinKerrLens_PhaseProfileMatchesAnalytic()
        {
            // L << zR: phase(r, t=0) - phase_linear(r, t=0) = k0 n2 I0 L exp(-2 r^2/w^2); the linear reference removes
            // the (small, r-independent) Gouy phase L/zR, which is of the same order as the Kerr phase here
            double w = 1e-3, L = 1e-3, n2 = 1e-19, peak = 1e7;
            var grid = FrequencyGrid.Create(256, 10e-15, U.W(Lam));
            var basis = new RadialBasis(48, 3.5e-3);
            int n = grid.N;
            double i0 = 2 * peak / (Math.PI * w * w);
            double sig = 300e-15 / (2 * Math.Sqrt(2 * Math.Log(2)));
            Complex[] Run(double n2v)
            {
                var med = new NonlinearMedium { N2 = n2v, EnableSelfSteepening = false, EnableRaman = false };
                var prob = new SpatialEnvelopeProblem(grid, basis, med, L, new double[n], Enumerable.Repeat(1.0, n).ToArray(), _ => 1.0, false, 0, 0, "t");
                var E = new Complex[basis.M * n];
                for (int m = 0; m < basis.M; m++)
                {
                    var row = new Complex[n];
                    for (int i = 0; i < n; i++) row[i] = Math.Sqrt(i0) * Math.Exp(-basis.Radius[m] * basis.Radius[m] / (w * w)) * Math.Exp(-grid.Time[i] * grid.Time[i] / (4 * sig * sig));
                    Array.Copy(SpectralTransform.TimeToFreq(row), 0, E, m * n, n);
                }
                basis.ToK(E, n);
                var o = n2v == 0 ? E : Steppers.Propagate(prob, E, new StepperSettings { Tolerance = 1e-9 }, new StepStatistics());
                if (n2v == 0)
                {
                    // linear: one exact step of the diffraction operator
                    var lin = new SpatialEnvelopeProblem(grid, basis, null, L, new double[n], Enumerable.Repeat(1.0, n).ToArray(), _ => 1.0, false, 0, 0, "t");
                    o = Steppers.Propagate(lin, E, new StepperSettings(), new StepStatistics());
                }
                basis.ToSpace(o, n);
                return o;
            }
            var nl = Run(n2); var li = Run(0);
            double k0 = U.W(Lam) / C;
            for (int m = 0; m < basis.M; m += 6)
            {
                if (basis.Radius[m] > 1.5 * w) break;
                var a = SpectralTransform.FreqToTime(nl.Skip(m * n).Take(n).ToArray());
                var b = SpectralTransform.FreqToTime(li.Skip(m * n).Take(n).ToArray());
                double expected = k0 * n2 * i0 * L * Math.Exp(-2 * basis.Radius[m] * basis.Radius[m] / (w * w));
                Assert.Close(expected, (a[n / 2] / b[n / 2]).Phase, 0.002, 1e-6, "Kerr phase at r = " + basis.Radius[m]);
            }
        }
    }
}
