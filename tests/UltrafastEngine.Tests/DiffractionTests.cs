using System;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using UltrafastEngine.Transverse;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Diffraction by apertures (docs/reference/transverse.md#diffraction-and-apertures, docs/examples/30-iris-diffraction):
    /// beams of different shapes through a round iris and a square aperture, against the Fresnel diffraction integral
    /// (Born &amp; Wolf, Principles of Optics, ch. 8) and its Fraunhofer limit.
    /// </summary>
    public class DiffractionTests
    {
        private const double Lam = 1030e-9, A = 1e-3, E = 1e-3;
        private static double K => 2 * Math.PI / Lam;
        private static double ZAt(double fresnelNumber) => A * A / (Lam * fresnelNumber);

        /// <summary>A 1 ps pulse (narrowband: the Fresnel number varies by 0.15 % over the spectrum) of 1 mJ.</summary>
        private static RunSpec Pulse(JsonObject extra = null) => Spec(Gauss(Lam, 1e-12, E), extra);

        private static JsonObject Radial(int points, double radiusM, JsonObject initial = null)
        {
            var t = O("model", "radial", "points", points, "radiusM", radiusM, "absorbingBoundary", true);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }

        private static JsonObject Cartesian(int points, double halfWidthM, JsonObject initial = null)
        {
            var t = O("model", "cartesian", "nx", points, "ny", points, "halfWidthM", halfWidthM, "absorbingBoundary", true, "spectralSamples", 4);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }

        /// <summary>Source (zero-length gap), aperture, then gaps to the distances <paramref name="z"/> (cumulative).</summary>
        private static RunResult Iris(JsonObject transverse, JsonObject initial, JsonObject aperture, params double[] z)
        {
            var els = new System.Collections.Generic.List<JsonObject>();
            var src = (JsonObject)JsonValue.Parse(transverse.ToJson(false)); src.Set("initialProfile", initial);
            els.Add(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src));
            aperture.Set("id", "iris"); aperture.Set("type", "aperture"); els.Add(aperture);
            double prev = 0;
            for (int i = 0; i < z.Length; i++) { els.Add(O("id", "z" + i, "type", "gap", "lengthM", z[i] - prev, "material", "Vacuum", "transverse", transverse)); prev = z[i]; }
            return Go(Sys(els.ToArray()), Pulse());
        }

        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);

        /// <summary>Paraxial Fresnel integral of a round field A(ρ) cut at the iris radius: fluence (J/m^2) at radius r.</summary>
        private static double FresnelRadial(Func<double, double> amp, double z, double r, int n = 4000)
        {
            Complex s = 0; double d = A / n;
            for (int i = 0; i < n; i++)
            {
                double rho = (i + 0.5) * d;
                s += amp(rho) * Complex.FromPolarCoordinates(1, K * rho * rho / (2 * z)) * Bessel.J0(K * r * rho / z) * rho * d;
            }
            return Math.Pow((K / z * s).Magnitude, 2);
        }

        /// <summary>One axis of the separable Fresnel integral of a field cut to |ξ| ≤ A: amplitude factor at x.</summary>
        private static Complex FresnelLine(Func<double, double> amp, double z, double x, int n = 4000)
        {
            Complex s = 0; double d = 2 * A / n;
            for (int i = 0; i < n; i++)
            {
                double xi = -A + (i + 0.5) * d;
                s += amp(xi) * Complex.FromPolarCoordinates(1, K * (x - xi) * (x - xi) / (2 * z)) * d;
            }
            return Math.Sqrt(K / (2 * Math.PI * z)) * s;
        }

        private static (double[] R, double[] F) Profile(RunResult r, string name)
        {
            var o = r.Instruments.First(i => i.Name == name);
            var R = o.Arrays.First(a => a.Name.EndsWith("radius_m") || a.Name.EndsWith("x_m")).Data;
            var F = o.Arrays.First(a => a.Name.EndsWith("fluence_j_m2") || a.Name.EndsWith("fluence_x_j_m2")).Data;
            return (R, F);
        }

        [Test]
        public void UniformBeamThroughRoundIris_OnAxisFluenceFollowsFresnelZones()
        {
            // Plane wave through a round aperture: on the axis I(z) = 4 I0 sin^2(pi N / 2), N = a^2 / (lambda z) (Born & Wolf
            // 8.8.1): four times the incident fluence at N = 1 and 3, dark at N = 2. A 3 mm tophat is cut by the 1 mm iris.
            double f0 = E / (Math.PI * 9e-6);
            var r = Iris(Radial(1024, 12e-3), O("kind", "tophat", "radiusM", 3e-3), O("radiusM", A), ZAt(3), ZAt(2), ZAt(1));
            Assert.Close(1.0 / 9, Rec(r, "iris").Values["transmission"], 0.003, 0, "geometric transmission (a / R)^2");
            Assert.Close(4 * f0, Rec(r, "z0").Values["onAxisFluenceOutJPerM2"], 0.015, 0, "N = 3: bright");
            Assert.Close(0, Rec(r, "z1").Values["onAxisFluenceOutJPerM2"], 0, 0.004 * 4 * f0, "N = 2: the dark centre");
            Assert.Close(4 * f0, Rec(r, "z2").Values["onAxisFluenceOutJPerM2"], 0.015, 0, "N = 1: bright");
        }

        [Test]
        public void GaussianAndFlatTopThroughRoundIris_ProfilesMatchTheFresnelIntegral()
        {
            // Round beams clipped by a 1 mm iris, at the N = 1 distance: the radial fluence against the Fresnel integral of
            // the clipped field. Tolerance 1 % of the peak (observed 0.02-0.16 % on 2048 points; 1024 here).
            foreach (var (shape, ampOf, label) in new (JsonObject, Func<double, double>, string)[]
            {
                (O("kind", "gaussian", "radiusM", 2e-3), rho => Math.Exp(-rho * rho / 4e-6), "Gaussian w = 2 mm"),
                (O("kind", "flatTop", "radiusM", 1.5e-3), rho => Math.Exp(-Math.Pow(rho * rho / 2.25e-6, 10)), "flat-top w = 1.5 mm"),
            })
            {
                // energy normalisation of the incident field
                double norm = 0; int nr = 20000; for (int i = 0; i < nr; i++) { double rho = (i + 0.5) * 10e-3 / nr; norm += ampOf(rho) * ampOf(rho) * 2 * Math.PI * rho * 10e-3 / nr; }
                double inside = 0; for (int i = 0; i < nr; i++) { double rho = (i + 0.5) * A / nr; inside += ampOf(rho) * ampOf(rho) * 2 * Math.PI * rho * A / nr; }
                Func<double, double> amp = rho => Math.Sqrt(E / norm) * ampOf(rho);
                var sys = Sys(
                    O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Radial(1024, 12e-3, shape)),
                    O("id", "iris", "type", "aperture", "radiusM", A),
                    O("id", "z", "type", "gap", "lengthM", ZAt(1), "material", "Vacuum", "transverse", Radial(1024, 12e-3)));
                var r = Go(sys, Pulse(O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
                Assert.Close(inside / norm, Rec(r, "iris").Values["transmission"], 0.003, 0, label + ": transmitted energy");
                var (R, F) = Profile(r, "bp");
                double peak = 0, err = 0;
                for (int m = 0; m < R.Length && R[m] < 3e-3; m += 4)
                {
                    double fa = FresnelRadial(amp, ZAt(1), R[m]);
                    peak = Math.Max(peak, fa); err = Math.Max(err, Math.Abs(F[m] - fa));
                }
                Assert.Less(err / peak, 0.01, label + ": max |F - F_Fresnel| / peak");
            }
        }

        [Test]
        public void SquareBeamThroughSquareAperture_CutMatchesTheSeparableFresnelIntegral()
        {
            // A square flat-top (separable super-Gaussian of order 10) through a 2 x 2 mm aperture on a 512^2 grid: the cut
            // y = 0 against the product of two one-dimensional Fresnel integrals, at N = 2 (half-width) and in the far zone.
            // Tolerance 2 % of the peak (observed 0.7 % / 0.4 %; the edge spans 21 pixels).
            Func<double, double> a1 = x => Math.Exp(-Math.Pow(x * x / 2.25e-6, 10));
            double n1 = 0; int nn = 40000; for (int i = 0; i < nn; i++) { double x = -10e-3 + (i + 0.5) * 20e-3 / nn; n1 += a1(x) * a1(x) * 20e-3 / nn; }
            double z1 = ZAt(2), z2 = 5.0;
            var sys = Sys(
                O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Cartesian(512, 12e-3, O("kind", "flatTop", "geometry", "square", "radiusM", 1.5e-3))),
                O("id", "iris", "type", "aperture", "halfWidthXM", A, "halfWidthYM", A),
                O("id", "z1", "type", "gap", "lengthM", z1, "material", "Vacuum", "transverse", Cartesian(512, 12e-3)),
                O("id", "z2", "type", "gap", "lengthM", z2 - z1, "material", "Vacuum", "transverse", Cartesian(512, 12e-3)));
            var r = Go(sys, Pulse(O("instruments", A(O("kind", "beamProfile", "name", "b1", "at", "z1"), O("kind", "beamProfile", "name", "b2", "at", "z2")))));
            Assert.False(r.Warnings.Any(w => w.Contains("under-resolved")), "the aperture is resolved");
            foreach (var (name, z) in new[] { ("b1", z1), ("b2", z2) })
            {
                var (X, F) = Profile(r, name);
                var uy = FresnelLine(a1, z, 0).Magnitude;
                double peak = 0, err = 0;
                for (int m = 0; m < X.Length; m += 2)
                {
                    if (Math.Abs(X[m]) > 8e-3) continue;
                    double fa = E / (n1 * n1) * Math.Pow(FresnelLine(a1, z, X[m]).Magnitude * uy, 2);
                    peak = Math.Max(peak, fa); err = Math.Max(err, Math.Abs(F[m] - fa));
                }
                Assert.Less(err / peak, 0.02, name + ": max |F - F_Fresnel| / peak");
            }
        }

        [Test]
        public void FarField_AiryAndSincZerosAreWhereFraunhoferPutsThem()
        {
            // Fraunhofer limit (N = 0.05): the first dark ring of a round aperture of radius a at r = 0.6098 lambda z / a
            // (zero of J1: 3.8317 / 2 pi), the first zero of a square aperture of half-width a at x = lambda z / 2a.
            double a = 0.5e-3, z = a * a / (Lam * 0.05);
            var rr = Go(Sys(
                O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Radial(1024, 25e-3, O("kind", "tophat", "radiusM", 1e-3))),
                O("id", "iris", "type", "aperture", "radiusM", a),
                O("id", "z", "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", Radial(1024, 25e-3))),
                Pulse(O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            var (R, F) = Profile(rr, "bp");
            int i = 1; while (i < R.Length - 1 && !(F[i] <= F[i - 1] && F[i] <= F[i + 1] && F[i] < 0.05 * F[0])) i++;
            Assert.Close(3.8317 / (2 * Math.PI) * Lam * z / a, R[i], 0.02, 0, "first Airy zero");
            var rs = Go(Sys(
                O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Cartesian(512, 12e-3, O("kind", "tophat", "geometry", "square", "radiusM", 1e-3))),
                O("id", "iris", "type", "aperture", "halfWidthXM", a, "halfWidthYM", a),
                O("id", "z", "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", Cartesian(512, 12e-3))),
                Pulse(O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            var (X, G) = Profile(rs, "bp");
            int c = Array.IndexOf(X, X.OrderBy(Math.Abs).First()), k = c + 1;
            while (k < X.Length - 1 && !(G[k] <= G[k - 1] && G[k] <= G[k + 1] && G[k] < 0.05 * G[c])) k++;
            // the minimum lies between samples (dx = 47 um): parabolic refinement
            double den = G[k - 1] - 2 * G[k] + G[k + 1], xk = X[k] + (den > 0 ? 0.5 * (G[k - 1] - G[k + 1]) / den * (X[1] - X[0]) : 0);
            Assert.Close(Lam * z / (2 * a), xk, 0.02, 0, "first sinc^2 zero");
        }

        [Test]
        public void HardAperture_EdgeCellsCarryTheirAreaFraction_AndCoarseIrisesWarn()
        {
            // Gaussian w = 2 mm through a = 0.5 mm: T = 1 - exp(-2 a^2 / w^2) = 0.1175. Area-averaged edge cells keep T within
            // ~ dx / 3a of it even on a coarse grid (a 0/1 mask is off by up to 2 dx / a); 6 samples across the radius warn.
            double w = 2e-3, a = 0.5e-3, t = 1 - Math.Exp(-2 * a * a / (w * w));
            foreach (int pts in new[] { 128, 512 })
            {
                var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Radial(pts, 8e-3, O("kind", "gaussian", "radiusM", w))),
                               O("id", "iris", "type", "aperture", "radiusM", a)), Pulse());
                double samples = Rec(r, "iris").Values["apertureSamples"];
                double dx = 8e-3 / pts;
                Assert.Close(t, Rec(r, "iris").Values["transmission"], 0.5 * dx / a, 0, pts + " points: transmission");
                Assert.Equal(samples < 10, r.Warnings.Any(x => x.Contains("under-resolved")), pts + " points: warning when under-resolved (" + samples + " samples)");
            }
        }

        [Test]
        public void AbsorbingBoundary_RemovesTheWrappedLight_AndTheLossBudgetCloses()
        {
            // Without the absorber the light diffracted to large angles wraps around the cartesian window and lands on the
            // pattern (19 % error at z = 5 m in the square case); with it the centre fluence is right and the absorbed energy
            // is booked as "grid boundary", so input - output = sum of losses.
            Func<double, double> a1 = x => Math.Abs(x) <= 3e-3 ? 1.0 : 0.0;
            double z = 5.0, f0 = E / 36e-6, uy = FresnelLine(a1, z, 0).Magnitude;
            double expect = f0 * Math.Pow(uy, 4);
            foreach (bool absorb in new[] { false, true })
            {
                var tv = Cartesian(512, 12e-3); tv.Set("absorbingBoundary", absorb);
                var src = (JsonObject)JsonValue.Parse(tv.ToJson(false)); src.Set("initialProfile", O("kind", "tophat", "geometry", "square", "radiusM", 3e-3));
                var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src),
                               O("id", "iris", "type", "aperture", "halfWidthXM", A, "halfWidthYM", A),
                               O("id", "z", "type", "gap", "lengthM", z, "material", "Vacuum", "transverse", tv)), Pulse());
                var rec = Rec(r, "z");
                double rel = Math.Abs(rec.Values["onAxisFluenceOutJPerM2"] / expect - 1);
                if (absorb)
                {
                    Assert.Less(rel, 0.02, "with the absorber: centre fluence vs Fresnel");
                    Assert.True(rec.LossJ.ContainsKey("grid boundary"), "absorbed energy booked");
                    double booked = r.Records.Sum(x => x.LossJ.Values.Sum());
                    Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), booked, 1e-6, 0, "loss budget");
                }
                else Assert.Greater(rel, 0.05, "without the absorber the wrapped light spoils the centre");
            }
        }

        [Test]
        public void LinearSpatialGap_PropagatesTheProfileOnly_AndEqualsTheFullFieldPropagation()
        {
            // A linear gap takes the profile-only path; the same gap with a (negligible) Kerr medium takes the full M x N
            // path. The fluence profiles must agree to the stored-frequency interpolation (1e-4 of the peak).
            JsonObject Gap(bool nonlinear) => nonlinear
                ? O("id", "z", "type", "bulk", "material", "kerr", "lengthM", ZAt(1), "nonlinear", true, "transverse", O("model", "radial", "points", 256, "radiusM", 8e-3))
                : O("id", "z", "type", "bulk", "material", "kerr", "lengthM", ZAt(1), "nonlinear", false, "transverse", O("model", "radial", "points", 256, "radiusM", 8e-3));
            double[] Run(bool nonlinear)
            {
                var o = O("format", "ultrafast-system", "version", 1, "name", "t", "materials", O("kerr", O("index", O("kind", "constant", "value", 1.0), "n2", 1e-30)));
                o.Set("elements", A(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", O("model", "radial", "points", 256, "radiusM", 8e-3, "initialProfile", O("kind", "gaussian", "radiusM", 2e-3))),
                                    O("id", "iris", "type", "aperture", "radiusM", A), Gap(nonlinear)));
                var r = Go(SystemDescription.FromJson(o), Spec(Gauss(Lam, 1e-12, 1e-9), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
                Assert.Equal(nonlinear, !Rec(r, "z").Representation.Contains("linear: the profile"), "path taken (nonlinear = " + nonlinear + ")");
                return Profile(r, "bp").F;
            }
            var lin = Run(false); var full = Run(true);
            double peak = lin.Max(), err = lin.Zip(full, (x, y) => Math.Abs(x - y)).Max();
            Assert.Less(err / peak, 1e-4, "profile-only vs full propagation");
        }

        [Test]
        public void SquareShapeOnARadialGrid_IsRejected_AndUnknownKindsAreErrors()
        {
            Assert.Throws<Exception>(() => Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "transverse", Radial(64, 8e-3, O("kind", "flatTop", "geometry", "square", "radiusM", 1e-3)))), Pulse()), "square on a radial grid");
            Assert.Throws<Exception>(() => Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "transverse", Radial(64, 8e-3, O("kind", "donut", "radiusM", 1e-3)))), Pulse()), "unknown kind");
        }
    }
}
