using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Vector Kerr and Raman physics for nonlinear ellipse rotation (NER): the B-integral of elliptical and circular
    /// light, the rotation angle against the analytic result, the anisotropic Raman share, and circular vs linear
    /// self-phase modulation. Analytic results for an instantaneous isotropic chi(3) (Maker-Terhune A = 2/3, B = 1/3 per
    /// unit of the linear index): rotation theta = B_lin sin(2 chi) x Bcoef, Bcoef = 1/3; with a slow Raman response
    /// whose anisotropic share is b, Bcoef = (1 - fR) / 3 + fR b / 2, and circular light sees
    /// (1 - fR) 2/3 + fR (1 - b/2) of the linear index.
    /// </summary>
    public class NerTests
    {
        private const double Lam = 1030e-9, N2 = 3e-20;

        /// <summary>A dispersionless Kerr plate (constant index, constant beam), optional Raman.</summary>
        private static SystemDescription Plate(JsonObject raman = null, double fR = 0, double L = 5e-3)
        {
            var mat = FlatMaterial(N2);
            if (raman != null) { mat.Set("raman", raman); mat.Set("ramanFraction", fR); }
            var j = O("format", "ultrafast-system", "version", 2, "name", "ner");
            j.Set("materials", O("Kerr", mat));
            j.Set("elements", A(O("id", "plate", "type", "bulk", "material", "Kerr", "lengthM", L, "beam", O("kind", "constant", "radiusM", 100e-6))));
            return SystemDescription.FromJson(j);
        }

        /// <summary>A Gaussian pulse with polarisation ellipse angle chi (tan chi = ellipticity) along x.</summary>
        private static RunResult Run(SystemDescription sys, double chi, double energy, double fwhm = 1e-12)
        {
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", fwhm, "energyJ", energy,
                "polarization", O("angleDeg", 0.0, "ellipticity", Math.Tan(chi)));
            return Go(sys, Spec(pulse, O("physics", O("selfSteepening", false, "raman", true), "grid", O("auto", false, "n", 4096, "windowS", 12 * fwhm))));
        }

        private static int PeakIndex(Complex[] ax, Complex[] ay)
        {
            int k = 0; double m = -1;
            for (int i = 0; i < ax.Length; i++) { double v = ax[i].Magnitude * ax[i].Magnitude + ay[i].Magnitude * ay[i].Magnitude; if (v > m) { m = v; k = i; } }
            return k;
        }

        private static double Azimuth(Complex ex, Complex ey)
        {
            double xx = ex.Magnitude * ex.Magnitude, yy = ey.Magnitude * ey.Magnitude;
            return 0.5 * Math.Atan2(2 * (ex * Complex.Conjugate(ey)).Real, xx - yy);
        }

        /// <summary>Rotation of the polarisation ellipse at the temporal peak (rad).</summary>
        private static double Rotation(RunResult r)
        {
            var ix = r.Input.TemporalAx(); var iy = r.Input.TemporalAy(); var ox = r.Output.TemporalAx(); var oy = r.Output.TemporalAy();
            int k = PeakIndex(ix, iy);
            return Math.Abs(Azimuth(ox[k], oy[k]) - Azimuth(ix[k], iy[k]));
        }

        /// <summary>Nonlinear phase of the x component at the temporal peak (rad).</summary>
        private static double PeakPhase(RunResult r)
        {
            var ix = r.Input.TemporalAx(); var iy = r.Input.TemporalAy(); var ox = r.Output.TemporalAx();
            int k = PeakIndex(ix, iy);
            return Math.Abs((ox[k] / ix[k]).Phase);
        }

        private static double B(RunResult r) => r.Records.Sum(x => x.BIntegralRad);

        [Test(Category = "ner")]
        public void BIntegral_CircularIsTwoThirdsOfLinear_EllipticalInBetween()
        {
            var sys = Plate();
            double bl = B(Run(sys, 0, 2e-6));
            double bc = B(Run(sys, Math.PI / 4, 2e-6));
            double be = B(Run(sys, 0.3, 2e-6));
            Assert.Close(2.0 / 3.0, bc / bl, 0.005, 0, "circular / linear B = 2/3 (was 5/6)");
            // power-weighted index of an ellipse: 1 - sin^2(2 chi) / 3
            Assert.Close(1 - Math.Pow(Math.Sin(0.6), 2) / 3, be / bl, 0.005, 0, "elliptical B");
        }

        [Test(Category = "ner")]
        public void Circular_AtOneAndAHalfTimesTheEnergy_MatchesLinear()
        {
            var sys = Plate();
            var lin = Run(sys, 0, 20e-6, 200e-15); var cir = Run(sys, Math.PI / 4, 30e-6, 200e-15);
            Assert.Greater(B(lin), 2, "strong SPM");
            var sl = lin.Output.SpectralIntensity(); var sc = cir.Output.SpectralIntensity();
            double ml = sl.Max(), mc = sc.Max(), dev = 0;
            for (int i = 0; i < sl.Length; i++) dev = Math.Max(dev, Math.Abs(sl[i] / ml - sc[i] / mc));
            Assert.Less(dev, 1e-3, "circular at 1.5x energy has the linear spectrum");
            Assert.Close(B(lin), B(cir), 0.005, 0, "same B");
        }

        [Test(Category = "ner")]
        public void Rotation_InstantaneousKerr_IsBsin2chiOverThree()
        {
            var sys = Plate();
            double bl = B(Run(sys, 0, 3e-6));
            foreach (var chi in new[] { 0.1, 0.25, 0.5 })
            {
                var r = Run(sys, chi, 3e-6);
                double rot = Rotation(r);
                Assert.Close(bl * Math.Sin(2 * chi) / 3, rot, 0.01, 0, "theta = B sin(2 chi) / 3 at chi = " + chi);
            }
        }

        [Test(Category = "ner")]
        public void Rotation_WithAnisotropicRaman_UsesTheAnisotropicShare()
        {
            // a 1 ps pulse: the silica Raman response (~100 fs) follows the intensity, so the steady-state tensor applies
            const double fR = 0.18, b = 0.25, chi = 0.25;
            var iso = Plate(O("kind", "silica", "anisotropicFraction", 0.0), fR);
            var ani = Plate(O("kind", "silica"), fR);   // fused-silica default: 25 % anisotropic
            double bl = B(Run(ani, 0, 3e-6));
            double RotOf(SystemDescription s) => Rotation(Run(s, chi, 3e-6));
            Assert.Close(bl * Math.Sin(2 * chi) * (1 - fR) / 3, RotOf(iso), 0.02, 0, "isotropic Raman adds no rotation");
            Assert.Close(bl * Math.Sin(2 * chi) * ((1 - fR) / 3 + fR * b / 2), RotOf(ani), 0.02, 0, "anisotropic Raman adds fR b / 2");
        }

        [Test(Category = "ner")]
        public void CircularLight_SeesLessRamanThanLinear()
        {
            const double fR = 0.18, b = 0.25;
            var sys = Plate(O("kind", "silica"), fR);
            var lin = Run(sys, 0, 2e-6); var cir = Run(sys, Math.PI / 4, 2e-6);
            double ratio = PeakPhase(cir) / PeakPhase(lin);
            Assert.Close((1 - fR) * 2.0 / 3.0 + fR * (1 - b / 2), ratio, 0.01, 0, "circular / linear nonlinear phase with anisotropic Raman");
            // the linear-polarisation result does not depend on the split (scalar runs unchanged)
            var linIso = Run(Plate(O("kind", "silica", "anisotropicFraction", 0.0), fR), 0, 2e-6);
            Assert.Close(PeakPhase(linIso), PeakPhase(lin), 1e-9, 0, "linear light unchanged by the split");
            Assert.Close(RmsSpectralWidth(linIso.Output), RmsSpectralWidth(lin.Output), 1e-9, 0, "linear spectrum unchanged");
        }

        [Test(Category = "ner")]
        public void LinAgrawal_AnisotropicPart_IsFcHaPlusFbHb()
        {
            var la = new UltrafastEngine.Materials.LinAgrawalRamanResponse();
            Assert.Close(0.25, la.AnisotropicFraction, 1e-12, 0, "fb + fc");
            var t = Enumerable.Range(0, 20000).Select(i => i * 0.1e-15).ToArray();
            var h = la.Evaluate(t); var hb = la.EvaluateAnisotropic(t);
            double sh = h.Sum() * 0.1e-15, sb = hb.Sum() * 0.1e-15;
            Assert.Close(1.0, sh, 0.01, 0, "unit integral");
            Assert.Close(0.25, sb / sh, 0.01, 0, "anisotropic share of the integral");
        }

        [Test(Category = "ner", Slow = true)]
        public void SpatialModel_OnAxisRotationWithAnisotropicRaman_MatchesTheOneDModel()
        {
            // a radial grid with the anisotropic silica Raman response: on axis the rotation equals the 1-D vector
            // model at the on-axis intensity (A_eff = pi w^2 / 2)
            double w = 0.5e-3, L = 2e-3, E = 2e-4, chi = 0.3;
            JsonObject Mat() { var m = FlatMaterial(1e-19); m.Set("raman", O("kind", "silica")); m.Set("ramanFraction", 0.18); return m; }
            SystemDescription S(JsonObject beam, JsonObject transverse)
            {
                var el = O("id", "k", "type", "bulk", "material", "Kerr", "lengthM", L, "beam", beam);
                if (transverse != null) el.Set("transverse", transverse);
                var j = O("format", "ultrafast-system", "version", 2, "name", "ner-spatial");
                j.Set("materials", O("Kerr", Mat())); j.Set("elements", A(el));
                return SystemDescription.FromJson(j);
            }
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 1e-12, "energyJ", E, "polarization", O("angleDeg", 0.0, "ellipticity", Math.Tan(chi)));
            var extra = O("physics", O("selfSteepening", false, "raman", true));
            var r = Go(S(O("kind", "gaussian", "w0M", w), O("model", "radial", "points", 32, "radiusM", 2.5 * w)), Spec(pulse, extra));
            var r1 = Go(S(O("kind", "constant", "areaM2", Math.PI * w * w / 2), null), Spec(pulse, extra));
            var g = r.Output.Grid; int ic = g.CarrierIndex;
            double psiIn = Azimuth(r.Input.Ax[ic], r.Input.Ay[ic]);
            int ic1 = r1.Output.Grid.CarrierIndex;
            double rot1 = Azimuth(r1.Output.Ax[ic1], r1.Output.Ay[ic1]) - Azimuth(r1.Input.Ax[ic1], r1.Input.Ay[ic1]);
            var prof = r.Output.Profile;
            int f = Enumerable.Range(0, prof.Omega.Length).OrderBy(k => Math.Abs(prof.Omega[k] - g.OmegaAbsolute[ic])).First();
            int i = Enumerable.Range(0, g.N).OrderBy(k => Math.Abs(g.OmegaAbsolute[k] - prof.Omega[f])).First();
            int nf = prof.Omega.Length;
            int m0 = Enumerable.Range(0, prof.Basis.M).OrderBy(m => prof.Basis.Radius[m]).First();
            double rot0 = Azimuth(r.Output.Ax[i] * prof.Data[m0 * nf + f], r.Output.Ay[i] * prof.DataY[m0 * nf + f]) - psiIn;
            Assert.Greater(Math.Abs(rot1), 0.02, "a measurable rotation");
            Assert.Close(rot1, rot0, 0.05, 0, "on-axis rotation (spatial, vector Raman) = 1-D model");
        }
    }
}
