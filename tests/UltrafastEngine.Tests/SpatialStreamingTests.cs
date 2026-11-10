using System;
using System.Diagnostics;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// Large spatial runs (docs/reference/transverse.md#large-grids): streaming over frequency (transverse.frequencyBlock), keeping only the
    /// profiles that are read (outputs.keepProfiles), the cylindrical lens, and the 1920 x 1080 budget of the S2 scale spike.
    /// </summary>
    [TestArea("Large spatial runs: frequency streaming, profile retention, cylindrical lens, full-panel budget")]
    public class SpatialStreamingTests
    {
        private const double Lam = 1030e-9;

        private static JsonObject Grid(int block, int samples = 8, JsonObject initial = null)
        {
            var t = O("model", "cartesian", "nx", 256, "ny", 64, "halfWidthM", 2e-3, "halfWidthYM", 0.5e-3, "spectralSamples", samples, "frequencyBlock", block, "absorbingBoundary", true);
            if (initial != null) t.Set("initialProfile", initial);
            return t;
        }

        /// <summary>Source, a chromatic phase screen on a modulator, a gap, a lens, a gap; a camera at the end, a beam profile after the first gap.</summary>
        private static RunResult Chain(int block, string keep = "all")
        {
            var phase = new JsonArray();
            for (int i = 0; i < 64; i++) { var row = new JsonArray(); for (int j = 0; j < 16; j++) row.Add(8 * Math.Sin(i / 5.0) * Math.Cos(j / 3.0)); phase.Add(row); }
            var sys = Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", Grid(block, 8, O("kind", "gaussian", "radiusM", 0.6e-3))),
                          O("id", "mod", "type", "modulator", "pitchXM", 50e-6, "pitchYM", 50e-6, "phaseRad", phase, "designWavelengthM", Lam, "outsideTransmission", 1.0),
                          O("id", "g1", "type", "gap", "lengthM", 0.05, "material", "Vacuum", "transverse", Grid(block)),
                          O("id", "lens", "type", "lens", "focalLengthM", 0.2),
                          O("id", "g2", "type", "gap", "lengthM", 0.1, "material", "Vacuum", "transverse", Grid(block)));
            return Go(sys, Spec(Gauss(Lam, 30e-15, 1e-6), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end"), O("kind", "beamProfile", "name", "mid", "at", "g1")),
                                                         "outputs", O("keepProfiles", keep))));
        }

        private static double[] Map(RunResult r, string name) => r.Instruments.First(i => i.Name == name).Arrays.First(a => a.Name.EndsWith("fluence_map_j_m2")).Data;

        [Test(Reference = "linear propagation is independent per stored frequency, so propagating them in blocks (streaming) gives the all-at-once result", Tolerance = "1e-12 of the peak")]
        public void Streaming_InFrequencyBlocks_EqualsAllAtOnce()
        {
            var all = Chain(0);
            foreach (int b in new[] { 1, 3 })
            {
                var s = Chain(b);
                foreach (var name in new[] { "bp", "mid" })
                {
                    var a = Map(all, name); var c = Map(s, name); double peak = a.Max();
                    for (int i = 0; i < a.Length; i++) Assert.Close(a[i], c[i], 0, 1e-12 * peak, name + ", block " + b);
                }
                Assert.True(s.Records.First(x => x.Id == "g1").Notes.Any(n => n.Contains("streamed over frequency")), "streaming is noted");
                Assert.Close(all.Output.TotalEnergyJ(), s.Output.TotalEnergyJ(), 1e-12, 0, "energy");
            }
        }

        [Test(Reference = "outputs.keepProfiles = \"used\" drops the profiles no instrument reads; the instruments and the output are unchanged", Tolerance = "exact")]
        public void KeepProfilesUsed_DropsUnreadProfiles_AndChangesNoResult()
        {
            var all = Chain(0); var used = Chain(0, "used");
            foreach (var name in new[] { "bp", "mid" })
            {
                var a = Map(all, name); var b = Map(used, name);
                for (int i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i], name);
            }
            Assert.True(all.At["mod"].Profile != null && all.At["lens"].Profile != null, "all: every profile kept");
            Assert.True(used.At["lens"].Profile == null, "used: the lens output is not read");
            Assert.True(used.At["mod"].Profile != null, "used: the element before an instrument point is kept (instruments may read an element's input)");
            Assert.True(used.At["g1"].Profile != null && used.At["g2"].Profile != null && used.At["end"].Profile != null, "used: instrument points and the end kept");
            Assert.True(used.At["src"].Profile == null || ReferenceEquals(used.At["src"].Profile, used.At["mod"].Profile), "used: the source profile is not read");
        }

        [Test(Reference = "a prefix-cached study resumes from the nominal run's pulses, so with keepProfiles \"used\" the nominal run still keeps every profile: the cached sweep equals the uncached one (review finding H2, 2026-11-10)", Tolerance = "1e-12")]
        public void PrefixCachedStudy_WithKeepProfilesUsed_EqualsTheUncachedStudy()
        {
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 128, "ny", 1, "halfWidthM", 1e-3, "spectralSamples", 2); if (ip != null) t.Set("initialProfile", ip); return t; }
            var sys = Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "gaussian", "radiusM", 0.2e-3))),
                          O("id", "g1", "type", "gap", "lengthM", 0.01, "material", "Vacuum", "transverse", G()),
                          O("id", "mod", "type", "modulator", "cellsX", 64, "cellsY", 1, "pitchXM", 31.25e-6, "pitchYM", 1.0, "offsetXM", 0.5 * 15.625e-6, "fillFactor", 1.0),
                          O("id", "g2", "type", "gap", "lengthM", 0.01, "material", "Vacuum", "transverse", G()));
            var run = Spec(Gauss(Lam, 1e-12, 1e-6), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end")), "outputs", O("keepProfiles", "used")));
            JsonObject St(bool cache) => O("parameters", A(O("path", "element:mod/fillFactor", "values", A(0.5, 0.75, 1.0), "nominal", 1.0)),
                                           "outputs", A("record:mod/transmission", "instrument:bp/radiusXM"), "prefixCache", cache);
            var withCache = UltrafastEngine.Engine.Studies.Run(sys, run, UltrafastEngine.Engine.StudySpec.FromJson(St(true)));
            var without = UltrafastEngine.Engine.Studies.Run(sys, run, UltrafastEngine.Engine.StudySpec.FromJson(St(false)));
            Assert.True(withCache.Notes.Any(n => n.Contains("prefix cache")), "the prefix cache was used");
            for (int i = 0; i < 3; i++) for (int k = 0; k < 2; k++) Assert.Close(without.Outputs[i][k], withCache.Outputs[i][k], 1e-12, 0, "point " + i + ", output " + k);
            // fill 1/2 with two grid cells per pitch: each grid cell is half active, amplitude 1/2, so 1/4 of the power (docs: one complex number per cell)
            Assert.Close(0.25, without.Outputs[0][0], 1e-6, 0, "the modulator acted at the first point");
        }

        [Test(Reference = "one stored frequency: the bin nearest the energy centroid; for a two-colour spectrum with no light at the centroid, the nearest bin that carries light (review finding M1)", Tolerance = "exact")]
        public void OneStoredFrequency_IsTheCentroid_OrTheNearestLitBin()
        {
            var s = new double[101]; for (int i = 0; i < s.Length; i++) s[i] = Math.Exp(-Math.Pow((i - 40) / 4.0, 2));
            Assert.Equal(40, UltrafastEngine.Transverse.TransverseOps.SampleBins(s, 1)[0], "single line");
            var two = new double[101]; for (int i = 0; i < two.Length; i++) two[i] = Math.Exp(-Math.Pow((i - 20) / 3.0, 2)) + 0.5 * Math.Exp(-Math.Pow((i - 80) / 3.0, 2));
            int b = UltrafastEngine.Transverse.TransverseOps.SampleBins(two, 1)[0];
            Assert.True(two[b] >= 1e-3 * two.Max(), "the stored bin carries light: bin " + b);
            Assert.True(Math.Abs(b - 20) < 10 || Math.Abs(b - 80) < 10, "on one of the two lines: bin " + b);
        }

        [Test(Reference = "cylindrical lens (focalLengthXM = f, focalLengthYM = 1e30 m): x focuses to lambda f / (pi w), y diffracts freely to w sqrt(1 + (z / z_R)^2) with z_R = pi w^2 / lambda (Siegman, Lasers, ch. 17)", Tolerance = "2 %")]
        public void CylindricalLens_FocusesOneAxisOnly()
        {
            double w = 0.5e-3, f = 0.2;
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 256, "ny", 256, "halfWidthM", 2e-3, "spectralSamples", 1, "absorbingBoundary", true); if (ip != null) t.Set("initialProfile", ip); return t; }
            var r = Go(Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "gaussian", "radiusM", w))),
                           O("id", "cyl", "type", "lens", "focalLengthM", f, "focalLengthXM", f, "focalLengthYM", 1e30),
                           O("id", "z", "type", "gap", "lengthM", f, "material", "Vacuum", "transverse", G())),
                       Spec(Gauss(Lam, 1e-12, 1e-6), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "z")))));
            var v = r.Instruments.First(i => i.Name == "bp").Values;
            double zr = Math.PI * w * w / Lam;
            Assert.Close(Lam * f / (Math.PI * w), v.GetDouble("radiusXM", double.NaN), 0.02, 0, "x: focus");
            Assert.Close(w * Math.Sqrt(1 + (f / zr) * (f / zr)), v.GetDouble("radiusYM", double.NaN), 0.02, 0, "y: free diffraction");
        }

        /// <summary>Wall-time budget of the 1920 x 1080 tests: 4x the spike time on the development laptop (13 s); a shared CI runner (environment variable CI set) took
        /// 21-23 s for the same runs, so it gets 45 s. The budget is a regression guard against an order-of-magnitude slowdown, not a benchmark.</summary>
        public static double ScaleBudgetS() => Environment.GetEnvironmentVariable("CI") != null ? 45.0 : 13.0;

        [Test(Slow = true, Reference = "S2 scale spike (docs/design/notes/spikes.md): a 1920 x 1080 single-frequency run (modulator + 0.2 m gap + beam profile) took 3.3 s and 1.05 GB peak on the owner's laptop; a Gaussian overfilling the panel passes the Gaussian power on the panel (erf in x and y)", Tolerance = "budget 4x the spike time (13 s; 45 s when the CI variable is set); keepProfiles used holds at least one profile less than all; transmission 1e-3")]
        public void FullPanel_1920x1080_SingleFrequency_RunsWithinTheSpikeBudget()
        {
            int nx = 1920, ny = 1080; double pitch = 8e-6, dx = pitch, w = 6e-3;    // the grid (2048 x 1280 cells of 8 um, one per device cell, aligned) is larger than the panel (15.4 x 8.6 mm)
            var reg = UltrafastEngine.Models.ModelRegistry.CreateDefault();
            reg.RegisterCallback("panel", (UltrafastEngine.Models.FieldCallback)((x, y, z, o) => { for (int i = 0; i < o.Length; i++) o[i] = 6 * Math.PI * Math.Sin(2 * Math.PI * x[i] / 1e-3); }));
            JsonObject G(JsonObject ip = null) { var t = O("model", "cartesian", "nx", 2048, "ny", 1280, "halfWidthM", 1024 * dx, "halfWidthYM", 640 * dx, "spectralSamples", 1); if (ip != null) t.Set("initialProfile", ip); return t; }
            var sys = Sys(O("id", "src", "type", "gap", "lengthM", 0.0, "material", "Vacuum", "transverse", G(O("kind", "gaussian", "radiusM", w))),
                          O("id", "slm", "type", "modulator", "cellsX", nx, "cellsY", ny, "pitchXM", pitch, "phaseRad", "@panel", "offsetXM", 0.5 * pitch, "offsetYM", 0.5 * pitch),
                          O("id", "prop", "type", "gap", "lengthM", 0.2, "material", "Vacuum", "transverse", G()));
            (double s, long held, RunResult r) Run(string keep)
            {
                var spec = Spec(O("shape", "cw", "centerWavelengthM", 800e-9, "powerW", 1.0), O("instruments", A(O("kind", "beamProfile", "name", "bp", "at", "end")), "outputs", O("keepProfiles", keep)));
                GC.Collect(); long before = GC.GetTotalMemory(true);
                var sw = Stopwatch.StartNew();
                var res = Simulation.Run(sys, spec, reg);
                sw.Stop();
                long h = GC.GetTotalMemory(true) - before;
                GC.KeepAlive(res);
                return (sw.Elapsed.TotalSeconds, h, res);
            }
            var used = Run("used"); var all = Run("all");
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "1920 x 1080 panel on a 2048 x 1280 grid, cw: {0:F2} s; held after the run {1:F0} MB (used), {2:F0} MB (all)", used.s, used.held / 1e6, all.held / 1e6));
            Assert.Less(used.s, ScaleBudgetS(), "time");
            long profile = 2048L * 1280 * 16;
            Assert.Less(used.held + profile, all.held + 1, "used holds at least one profile (42 MB) less");
            // power of exp(-2 r^2 / w^2) inside |x| < a, |y| < b: erf(sqrt2 a / w) erf(sqrt2 b / w), relative to the power the grid holds
            double Erf(double z) { double t = 1 / (1 + 0.5 * Math.Abs(z)); double y = 1 - t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277))))))))); return z >= 0 ? y : -y; }
            double expect = Erf(Math.Sqrt(2) * nx * pitch / 2 / w) * Erf(Math.Sqrt(2) * ny * pitch / 2 / w) / (Erf(Math.Sqrt(2) * 1024 * dx / w) * Erf(Math.Sqrt(2) * 640 * dx / w));
            Assert.Close(expect, used.r.Records.First(x => x.Id == "slm").Values["transmission"], 1e-3, 0, "power on the panel");
        }
    }
}
