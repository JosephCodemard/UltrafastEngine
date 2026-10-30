using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Gain;
using UltrafastEngine.Materials;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Gaussian beam tracking: the thermal lens of gain media (docs/reference/gain.md#thermal-lens) and spatio-temporal
    /// couplings from angular dispersion (docs/reference/components.md#spatio-temporal-couplings).
    /// </summary>
    [TestArea("Gaussian beam tracking: thermal lenses and spatio-temporal couplings")]
    public class BeamTrackingTests
    {
        private const double Lam = 1030e-9, C = 2.99792458e8;
        private static ElementRecord Rec(RunResult r, string id) => r.Records.First(x => x.Id == id);
        private static RunSpec Tracked(double w0, JsonObject pulse = null) => Spec(pulse ?? Gauss(Lam, 2e-12, 1e-9), O("inputBeam", O("kind", "gaussian", "w0M", w0)));

        // =============================================================================================== thermal lens

        [Test(Category = "gain", Reference = "Innocenzi et al., Appl. Phys. Lett. 56, 1831 (1990): f = πK w_p² / (P_h dn/dT)", Tolerance = "5 %")]
        public void ThermalLens_SmallBeam_TrackedFocalLengthIsInnocenzis()
        {
            // Gaussian pump of 1/e^2 radius wp: f = pi K wp^2 / (P_h dn/dT) on the axis (Innocenzi et al., Appl. Phys. Lett.
            // 56, 1831 (1990)). A beam much smaller than the pump (0.1 mm vs 1 mm) sees that lens; tolerance 5 % (the
            // engine's on-axis fit is 4 % from the formula, the beam weighting adds ~ (w / wp)^2).
            double wp = 1e-3;
            var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.002, "slices", 8,
                            "pump", O("kind", "cw", "powerW", 40, "wavelengthM", 940e-9, "radiusM", wp, "profile", "gaussian"),
                            "thermalLens", O("rodRadiusM", 5e-3, "heatFraction", 0.1)));
            var v = Rec(Go(sys, Tracked(0.1e-3)), "g").Values;
            var m = GainMedium.Get("Yb:YAG");
            double f = Math.PI * m.ThermalConductivity * wp * wp / (v["heatW"] * m.DnDT);
            Assert.Close(f, v["trackedThermalLensFocalLengthM"], 0.05, 0, "tracked thermal focal length");
        }

        [Test(Category = "gain", Reference = "Gaussian-beam thin-lens law (Self, Appl. Opt. 22, 658 (1983))", Tolerance = "1e-12, 0.2 %")]
        public void ThermalLens_MeasuredFocalLength_FocusesTheTrackedBeamLikeAThinLens()
        {
            // thermalLens.focalLengthM = 0.5 m on a collimated 1 mm beam (z_R = 3.05 m) in a 1 mm thick rod: the new waist
            // lies at d = f / (1 + (f / z_R)^2) behind the lens with w0' = w0 / sqrt(1 + (z_R / f)^2) (Self, Appl. Opt. 22,
            // 658 (1983)); the half rod after the lens counts as 0.5 mm / n of free space.
            double w0 = 1e-3, f = 0.5, zr = Math.PI * w0 * w0 / Lam, L = 1e-3;
            double d = f / (1 + Math.Pow(f / zr, 2)), w1 = w0 / Math.Sqrt(1 + Math.Pow(zr / f, 2));
            var host = MaterialLibrary.BuiltIn("YAG");
            double n = host.IndexAt(2 * Math.PI * C / Lam);
            var sys = Sys(O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", L, "inversion", O("fraction", 0.0), "spm", false,
                            "thermalLens", O("focalLengthM", f)),
                          O("id", "gap", "type", "gap", "lengthM", d - L / 2 / n, "material", "Vacuum"));
            var r = Go(sys, Tracked(w0));
            Assert.Close(f, Rec(r, "g").Values["trackedThermalLensFocalLengthM"], 1e-12, 0, "the given focal length is used");
            Assert.Close(w1, Rec(r, "gap").Values["trackedBeamRadiusXM"], 0.002, 0, "waist radius");
            Assert.True(Math.Abs(Rec(r, "gap").Values["trackedCurvatureXM"]) > 100, "flat wavefront at the waist");
        }

        [Test(Category = "gain", Reference = "cross-model: the radial spatial gain model", Tolerance = "7 % (aberrated lens)")]
        public void ThermalLens_TrackedBeam_AgreesWithTheSpatialGainModel()
        {
            // Cross-model: a 0.5 mm beam in an end-pumped Yb:YAG rod (Gaussian pump 0.6 mm, 60 W) and 0.4 m of free space.
            // The radial model solves Delta T(r, z) on the grid and diffracts through it; the tracker applies the
            // intensity-weighted parabola of the same Delta T as thin lenses. The radii after 0.4 m agree within 7 %: the
            // lens is aberrated (M^2 = 1.08 in the spatial run), which a Gaussian beam parameter cannot carry.
            JsonObject Rod(bool spatial)
            {
                var o = O("id", "rod", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.004, "slices", 12,
                          "pump", O("kind", "cw", "powerW", 60, "wavelengthM", 940e-9, "radiusM", 0.6e-3, "profile", "gaussian", "passes", 2),
                          "thermalLens", O("rodRadiusM", 2e-3));
                if (spatial) { o.Set("beam", O("kind", "gaussian", "w0M", 0.5e-3)); o.Set("transverse", O("model", "radial", "points", 96, "radiusM", 2e-3)); }
                return o;
            }
            var pulse = O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 2e-12, "energyJ", 1e-4, "repRateHz", 10e3);
            var tracked = Go(Sys(Rod(false), O("id", "free", "type", "gap", "lengthM", 0.4)), Tracked(0.5e-3, pulse));
            var spatial = Go(Sys(Rod(true), O("id", "free", "type", "gap", "lengthM", 0.4, "transverse", O("model", "radial", "points", 256, "radiusM", 2.5e-3))),
                             Spec(pulse, O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "free")))));
            double wt = Rec(tracked, "free").Values["trackedBeamRadiusXM"], ws = spatial.Instruments.First(i => i.Name == "bp").Values.GetDouble("radiusXM", double.NaN);
            Assert.Close(ws, wt, 0.07, 0, "beam radius after 0.4 m: tracked vs spatial");
            Assert.Less(wt, 0.5e-3, "the thermal lens converges the beam");
        }

        // ================================================================================= spatio-temporal couplings

        private const double Lpm = 1200;
        private static double Period => 1e-3 / Lpm;
        private static double Beta => Math.Asin(Lam / Period - Math.Sin(Math.PI / 4));   // 45 deg incidence, first order

        private static RunResult Gratings(params JsonObject[] elements) => Go(Sys(elements), Tracked(2e-3, Gauss(Lam, 100e-15, 1e-6)));

        [Test(Reference = "grating equation; Hebling, Opt. Quantum Electron. 28, 1759 (1996)", Tolerance = "0.2 %")]
        public void Grating_AngularDispersionAndPulseFrontTilt_FollowTheGratingEquationAndHebling()
        {
            // d theta / d lambda = m / (period cos beta); pulse-front tilt tan(gamma) = lambda d theta / d lambda (Hebling,
            // Opt. Quantum Electron. 28, 1759 (1996)), i.e. dt/dx = (lambda / c) d theta / d lambda.
            var v = Rec(Gratings(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1)), "g1").Values;
            double ad = 1 / (Period * Math.Cos(Beta));
            Assert.Close(ad, Math.Abs(v["trackedAngularDispersionXRadPerM"]), 0.002, 0, "angular dispersion");
            Assert.Close(Lam / C * ad, Math.Abs(v["trackedPulseFrontTiltXSPerM"]), 0.002, 0, "pulse-front tilt");
            Assert.Close(Math.Atan(Lam * ad) * 180 / Math.PI, Math.Abs(v["trackedPulseFrontTiltXDeg"]), 0.002, 0, "tilt angle");
        }

        [Test(Reference = "Treacy grating pair (IEEE JQE 5, 454 (1969)), Kostenbauder ray-pulse matrices", Tolerance = "0.1 %, 0.2 %")]
        public void TwoGratings_GiveTreacysGdd_AndTheGratingPairsSpatialChirp()
        {
            // grating - slant gap b - parallel grating (opposite order) vs the gratingPair element (Treacy's exact phase,
            // single pass): GDD -k0 b (d beta / d omega)^2 = Treacy's second order; both leave the spatial chirp
            // b cos(alpha) / cos(beta) d beta / d omega and no angular dispersion.
            double G = 0.1, b = G / Math.Cos(Beta);
            var two = Gratings(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1),
                               O("id", "gap", "type", "gap", "lengthM", b, "material", "Vacuum"),
                               O("id", "g2", "type", "grating", "aoiDeg", Beta * 180 / Math.PI, "linesPerMm", Lpm, "order", -1));
            var pair = Gratings(O("id", "pair", "type", "gratingPair", "linesPerMm", Lpm, "aoiDeg", 45.0, "separationM", G, "passes", 1));
            Assert.Close(pair.FinalSystem.GddS2, two.FinalSystem.GddS2, 0.001, 0, "GDD vs Treacy");
            var v2 = Rec(two, "g2").Values; var vp = Rec(pair, "pair").Values;
            Assert.Close(vp["trackedSpatialChirpXMPerM"], v2["trackedSpatialChirpXMPerM"], 0.002, 0, "spatial chirp");
            Assert.Less(Math.Abs(v2["trackedAngularDispersionXRadPerM"]), 1e-6, "no angular dispersion after the pair");
        }

        [Test(Reference = "Akturk et al., Opt. Express 13, 8642 (2005): 2 φ2 E σ² / (w² + 2 E² σ²)", Tolerance = "0.5 %")]
        public void PulseFrontTilt_FromSpatialChirpAndGdd_MatchesTheGaussianResult()
        {
            // A collimated Gaussian beam (radius w) with spatial dispersion E = dx/d omega and GDD phi2: the mean arrival time
            // at x is phi2 times the local mean frequency, so dt/dx = 2 phi2 E sigma^2 / (w^2 + 2 E^2 sigma^2) with
            // sigma^2 = 4 ln2 / tau^2 (Akturk et al., Opt. Express 13, 8642 (2005), for this case).
            var r = Gratings(O("id", "pair", "type", "gratingPair", "linesPerMm", Lpm, "aoiDeg", 45.0, "separationM", 0.1, "passes", 1));
            var v = Rec(r, "pair").Values;
            double w0 = 2 * Math.PI * C / Lam, E = v["trackedSpatialChirpXMPerM"] / (-w0 / Lam), phi2 = r.FinalSystem.GddS2;
            double s2 = 4 * Math.Log(2) / Math.Pow(100e-15, 2), w = v["trackedBeamRadiusXM"];
            Assert.Close(2 * phi2 * E * s2 / (w * w + 2 * E * E * s2), v["trackedPulseFrontTiltXSPerM"], 0.005, 0, "pulse-front tilt");
        }

        [Test(Reference = "Snell's law at both prism faces (finite difference of n(λ))", Tolerance = "0.5 %")]
        public void Prism_AngularDispersion_MatchesSnellsLawThroughBothFaces()
        {
            // A 60 deg fused-silica prism at 55 deg incidence, built from two interfaces (signed angles: the exit face is
            // tilted the other way). Reference: the exit angle from Snell's law at both faces with n(lambda +- 0.1 nm).
            var fs = MaterialLibrary.BuiltIn("FusedSilica");
            double apex = 60 * Math.PI / 180, t1 = 55 * Math.PI / 180;
            Func<double, double> exit = lam => { double n = fs.IndexAt(2 * Math.PI * C / lam), t2 = Math.Asin(Math.Sin(t1) / n); return Math.Asin(n * Math.Sin(t2 - apex)); };
            double h = 0.1e-9, ad = (exit(Lam + h) - exit(Lam - h)) / (2 * h);
            double t2c = Math.Asin(Math.Sin(t1) / fs.IndexAt(2 * Math.PI * C / Lam));
            var r = Gratings(O("id", "f1", "type", "interface", "before", "Air", "after", "FusedSilica", "aoiDeg", 55.0),
                             O("id", "glass", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-6),
                             O("id", "f2", "type", "interface", "before", "FusedSilica", "after", "Air", "aoiDeg", (t2c - apex) * 180 / Math.PI));
            // the tracker's air index is not exactly 1: compare at 0.5 %
            Assert.Close(ad, Rec(r, "f2").Values["trackedAngularDispersionXRadPerM"], 0.005, 0, "prism angular dispersion");
        }

        [Test(Reference = "Martinez zero-dispersion stretcher: GDD = 0", Tolerance = "< 10⁻⁶ of k0 f F²")]
        public void ZeroDispersionStretcher_4f_LeavesNoGddAndNoCouplings()
        {
            // Martinez stretcher at zero dispersion: grating - f - lens - 2f - lens - f - grating (mirror image). The
            // telescope inverts the beam, so the second grating sits at -beta. GDD and couplings cancel (the lens terms
            // -k0 E^2 / f balance the gaps). All in vacuum: a lens in air has the power n_air / f, which leaves 400 fs^2.
            double f = 0.2, bdeg = Beta * 180 / Math.PI;
            var r = Gratings(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1),
                             O("id", "a", "type", "gap", "lengthM", f, "material", "Vacuum"),
                             O("id", "l1", "type", "lens", "focalLengthM", f, "medium", "Vacuum"),
                             O("id", "b", "type", "gap", "lengthM", 2 * f, "material", "Vacuum"),
                             O("id", "l2", "type", "lens", "focalLengthM", f, "medium", "Vacuum"),
                             O("id", "c", "type", "gap", "lengthM", f, "material", "Vacuum"),
                             O("id", "g2", "type", "grating", "aoiDeg", -bdeg, "linesPerMm", Lpm, "order", 1));
            double scale = 2 * Math.PI / Lam * f * Math.Pow(2 * Math.PI * C / Math.Pow(2 * Math.PI * C / Lam, 2) / (Period * Math.Cos(Beta)), 2);   // k0 f F^2
            Assert.Less(Math.Abs(r.FinalSystem.GddS2), 1e-6 * scale, "GDD of the 4f stretcher");
            Assert.True(Rec(r, "b").Values["trackedSpatialChirpXMPerM"] != 0, "spatially dispersed in the Fourier plane");
            var v = Rec(r, "g2").Values;
            Assert.False(v.ContainsKey("trackedAngularDispersionXRadPerM") && Math.Abs(v["trackedAngularDispersionXRadPerM"]) > 1e-3, "no angular dispersion at the output");
            Assert.False(v.ContainsKey("trackedSpatialChirpXMPerM") && Math.Abs(v["trackedSpatialChirpXMPerM"]) > 1e-6, "no spatial chirp at the output");
        }

        [Test(Reference = "Treacy, IEEE JQE 5, 454 (1969): TOD of the exact grating-pair phase", Tolerance = "0.5 %")]
        public void TwoGratings_ReproduceTreacysThirdOrderDispersion()
        {
            // the ray bundle has no knowledge of Treacy's formula: rays through two parallel gratings, phase from their optical paths
            double G = 0.1, b = G / Math.Cos(Beta);
            var two = Gratings(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1),
                               O("id", "gap", "type", "gap", "lengthM", b, "material", "Vacuum"),
                               O("id", "g2", "type", "grating", "aoiDeg", Beta * 180 / Math.PI, "linesPerMm", Lpm, "order", -1));
            var pair = Gratings(O("id", "pair", "type", "gratingPair", "linesPerMm", Lpm, "aoiDeg", 45.0, "separationM", G, "passes", 1));
            Assert.Close(pair.FinalSystem.TodS3, two.FinalSystem.TodS3, 0.005, 0, "TOD vs Treacy");
            Assert.Close(pair.FinalSystem.GddS2, two.FinalSystem.GddS2, 0.001, 0, "GDD vs Treacy");
        }

        [Test(Reference = "angular-dispersion GDD -k0 L (d theta / d omega)^2; Snell's law at the prism faces", Tolerance = "1 %")]
        public void PrismPair_InAir_GivesTheNegativeDispersionOfItsAngularDispersion()
        {
            // two opposed fused-silica prisms apex to apex (no glass path): the air path L gives -k0 L F^2 of negative GDD
            var fs = MaterialLibrary.BuiltIn("FusedSilica");
            double w0 = 2 * Math.PI * C / Lam, h = 1e-4 * w0, aoi = 55 * Math.PI / 180, apex = 60 * Math.PI / 180, L = 0.5;
            Func<double, double> exit = w => { double n = fs.IndexAt(w), t2 = Math.Asin(Math.Sin(aoi) / n); return Math.Asin(n * Math.Sin(t2 - apex)); };
            double F = (exit(w0 + h) - exit(w0 - h)) / (2 * h), t20 = Math.Asin(Math.Sin(aoi) / fs.IndexAt(w0)), out0 = exit(w0);
            var r = Gratings(O("id", "p1", "type", "prism", "material", "FusedSilica", "apexDeg", 60.0, "aoiDeg", 55.0, "fresnel", false, "medium", "Vacuum"),
                             O("id", "gap", "type", "gap", "lengthM", L, "material", "Vacuum"),
                             O("id", "p2", "type", "prism", "material", "FusedSilica", "apexDeg", 60.0, "aoiDeg", out0 * 180 / Math.PI, "fresnel", false, "medium", "Vacuum"));
            Assert.Close(-2 * Math.PI / Lam * L * F * F, r.FinalSystem.GddS2, 0.01, 0, "GDD");
            var v = Rec(r, "p2").Values;
            Assert.False(v.ContainsKey("trackedAngularDispersionXRadPerM") && Math.Abs(v["trackedAngularDispersionXRadPerM"]) > 1, "the second prism cancels the angular dispersion");
            Assert.Close(1, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 1e-9, 0, "no Fresnel loss when switched off");
        }

        [Test(Reference = "cross-model: spaceTime from the tracked beam vs the element's pulse-front tilt")]
        public void SpaceTime_OnA1DBeam_ShowsTheTrackedPulseFrontTilt()
        {
            var r = Go(Sys(O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1)),
                       Spec(Gauss(Lam, 100e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", 0.2e-3), "grid", O("windowS", 20e-12), "instruments", A(O("kind", "spaceTime", "name", "st", "at", "g1")))));
            var o = r.Instruments.First(i => i.Name == "st");
            var x = o.Arrays.First(a => a.Name.EndsWith("position_m")).Data; var t = o.Arrays.First(a => a.Name.EndsWith("arrival_s") || a.Name.Contains("arrival")).Data;
            int lo = Array.FindIndex(x, v => v > -0.1e-3), hi = Array.FindIndex(x, v => v > 0.1e-3);
            Assert.Close(Rec(r, "g1").Values["trackedPulseFrontTiltXSPerM"], (t[hi] - t[lo]) / (x[hi] - x[lo]), 0.03, 0, "slope of the arrival time");
        }

        [Test(Reference = "Treacy GDD and the tracker's spatial chirp, from a field propagated on a grid", Tolerance = "3 %")]
        public void AngularDispersionOnTheProfile_TwoGratingsOnAGrid_GiveTheTrackersChirpAndTreacysGdd()
        {
            // physics.spatioTemporal = "profile": each stored frequency is tilted; the gap diffracts them apart; nothing comes from the tracker
            double G = 0.02, b = G / Math.Cos(Beta), w = 1e-3;
            var grid = O("model", "cartesian", "nx", 1024, "ny", 1, "halfWidthM", 15e-3, "spectralSamples", 256);
            var src = (JsonObject)JsonValue.Parse(grid.ToJson(false)); src.Set("initialProfile", O("kind", "gaussian", "radiusM", w));
            JsonObject Spc(string mode) => O("inputBeam", O("kind", "gaussian", "w0M", w), "physics", O("spatioTemporal", mode), "grid", O("windowS", 200e-12, "n", 2048, "auto", false));
            var sys = Sys(O("id", "s", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", src),
                          O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1),
                          O("id", "gap", "type", "gap", "lengthM", b, "material", "Vacuum", "transverse", grid),
                          O("id", "g2", "type", "grating", "aoiDeg", Beta * 180 / Math.PI, "linesPerMm", Lpm, "order", -1));
            var r = Go(sys, Spec(Gauss(Lam, 1e-12, 1e-6), Spc("profile")));
            Assert.False(r.Warnings.Any(x => x.Contains("raise transverse.spectralSamples")), "samples are close enough");
            var p = r.Output.Profile; var B = p.Basis; int nf = p.Omega.Length, M = B.M;
            // the centroid of each frequency across the beam and the field there
            var om = new System.Collections.Generic.List<double>(); var xc = new System.Collections.Generic.List<double>(); var ph = new System.Collections.Generic.List<double>();
            var ax = r.Output.Ax; var g = r.Output.Grid;
            for (int f = 0; f < nf; f++)
            {
                double s = 0, sx = 0; int best = 0; double top = 0;
                for (int m = 0; m < M; m++) { double i = Math.Pow(p.Data[m * nf + f].Magnitude, 2); s += i; sx += i * B.X[m]; if (i > top) { top = i; best = m; } }
                if (Math.Abs(p.Omega[f] - g.OmegaCenter) > 4e12) continue;
                int bin = Array.IndexOf(g.OmegaAbsolute, g.OmegaAbsolute.OrderBy(v => Math.Abs(v - p.Omega[f])).First());
                om.Add(p.Omega[f] - g.OmegaCenter); xc.Add(sx / s); ph.Add((ax[bin] * p.Data[best * nf + f]).Phase);
            }
            double Slope(double[] u, double[] v) { double mu = u.Average(), mv = v.Average(); return u.Zip(v, (a, c) => (a - mu) * (c - mv)).Sum() / u.Sum(a => (a - mu) * (a - mu)); }
            double dxdw = Slope(om.ToArray(), xc.ToArray());
            double tracked = Rec(r, "g2").Values["trackedSpatialChirpXMPerM"] / (-(2 * Math.PI * C / Lam) / Lam);
            Assert.Close(tracked, dxdw, 0.03, 0, "dx / d omega of the field vs the tracker");
            var pair = Gratings(O("id", "pair", "type", "gratingPair", "linesPerMm", Lpm, "aoiDeg", 45.0, "separationM", G, "passes", 1));
            Assert.Less(Math.Abs(r.FinalSystem.GddS2), 1e-33, "no phase came from the tracker (below 1e-3 fs^2)");
            // the phase of the field on each colour's chief ray: a parabola of curvature GDD (group delay removed by the fit)
            var u = new double[ph.Count]; double acc = 0; for (int i = 0; i < u.Length; i++) { if (i > 0) { double d = ph[i] - ph[i - 1]; d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI)); acc += d; } u[i] = acc; }
            double[,] A2 = new double[3, 4];
            for (int i = 0; i < u.Length; i++) { double x = om[i] * 1e-12; var row = new[] { 1, x, x * x }; for (int a = 0; a < 3; a++) { for (int c = 0; c < 3; c++) A2[a, c] += row[a] * row[c]; A2[a, 3] += row[a] * u[i]; } }
            for (int a = 0; a < 3; a++) { for (int k = a + 1; k < 3; k++) { double f = A2[k, a] / A2[a, a]; for (int c = a; c < 4; c++) A2[k, c] -= f * A2[a, c]; } }
            double c2 = A2[2, 3] / A2[2, 2];
            Assert.Close(pair.FinalSystem.GddS2, 2 * c2 * 1e-24, 0.03, 0, "GDD of the field vs Treacy");
        }

        [Test]
        public void SpatioTemporalSwitch_TrackReportsWithoutPhase_OffForgetsTheCouplings()
        {
            JsonObject[] Els() => new[] { O("id", "g1", "type", "grating", "aoiDeg", -45.0, "linesPerMm", Lpm, "order", 1),
                                          O("id", "gap", "type", "gap", "lengthM", 0.1, "material", "Vacuum"),
                                          O("id", "g2", "type", "grating", "aoiDeg", Beta * 180 / Math.PI, "linesPerMm", Lpm, "order", -1) };
            RunResult Run(string mode) => Go(Sys(Els()), Spec(Gauss(Lam, 100e-15, 1e-6), O("inputBeam", O("kind", "gaussian", "w0M", 2e-3), "physics", O("spatioTemporal", mode))));
            var on = Run("auto"); var track = Run("track"); var off = Run("off");
            Assert.Less(on.FinalSystem.GddS2, -1e-26, "auto: the pair's negative GDD is applied");
            Assert.Close(0, track.FinalSystem.GddS2, 0, 1e-33, "track: no phase (round-off only)");
            Assert.True(Rec(track, "g2").Values.ContainsKey("trackedSpatialChirpXMPerM"), "track: couplings reported");
            Assert.False(Rec(off, "gap").Values.ContainsKey("trackedAngularDispersionXRadPerM"), "off: nothing tracked");
            Assert.Throws<Exception>(() => Run("sometimes"), "unknown mode");
        }
    }
}
