using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Physics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using UltrafastEngine.Zemax;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Zemax plugin: export and replay")]
    public class ZemaxTests
    {
        private static ZemaxSample SampleFake(IOpticStudio os, ExportConfig cfg)
        {
            return Sampler.Sample(os, new SamplerOptions
            {
                WavelengthsUm = cfg.SampleWavelengthsUm(), CarrierUm = cfg.CarrierUm(), BatchSize = cfg.BatchSize,
                BeamWaist = cfg.BeamWaistM / os.LensUnitToMeters, BeamWaistToSurface1 = cfg.BeamWaistToSurface1M / os.LensUnitToMeters, BeamM2 = cfg.BeamM2,
                Log = _ => { }
            });
        }

        private static ExportConfig Cfg(string json) => ExportConfig.FromJson(JsonValue.Parse(json).AsObject());

        [Test(Reference = "Treacy, IEEE JQE 5, 454 (1969)", Tolerance = "0.5 %")]
        public void Treacy_GroupPathMatchesTreacyAndBeatsPhasePath()
        {
            var os = new FakeOpticStudio("treacy", 100);
            var cfg = Cfg("{ 'sampling': {'minWavelengthM': 0.98e-6, 'maxWavelengthM': 1.18e-6, 'count': 40, 'carrierWavelengthM': 1.03e-6}, 'beam': {'waistRadiusM': 2e-3} }".Replace('\'', '"'));
            var sample = SampleFake(os, cfg);
            Assert.True(os.OperandCalls > 0, "geometry read through merit-function operands");
            var sys = SystemBuilder.Build(sample, cfg);
            var r = Go(sys, Spec(Gauss(1.03e-6, 300e-15, 1e-9), O("physics", O("absoluteIndex", false))));
            double w0 = r.Compiled.Grid.OmegaCenter, lam = 2 * Math.PI * C / w0;
            double treacy = GratingPairTerm.TreacyGdd(lam, 1200e3, 40 * Math.PI / 180, 0.1, -1);
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            double slab = new BulkTerm(new Materials.Material { Name = "fs", Index = new Materials.SellmeierIndex(new[] { 0.6961663, 0.4079426, 0.8974794 }, new[] { 0.0684043 * 0.0684043, 0.1162414 * 0.1162414, 9.896161 * 9.896161 }) }, 0.01).DispersionAt(w0).GddS2;
            Assert.Close(treacy + slab, r.FinalSystem.GddS2, 0.005, 0, "group-path GDD = Treacy + slab");
            double phasePathGdd = sys.Source.GetObject("dispersionSummary").GetDouble("phasePathGddS2", 0);
            Assert.Greater(phasePathGdd, 0, "the grating-blind phase-path model gets the wrong sign");
            Assert.True(sys.Elements.Any(e => e.Type == "interface" && e.Json.GetString("mode") == "diffract"), "gratings exported as diffracting interfaces");
            Assert.True(sys.Elements.Any(e => e.Id.StartsWith("vignette")), "vignetted wavelengths exported");
        }

        [Test]
        public void Mpc_PassesBeamsMirrorsAndCoatings()
        {
            var os = new FakeOpticStudio("mpc");
            var cfg = Cfg(@"{ ""sampling"": {""minWavelengthM"": 0.99e-6, ""maxWavelengthM"": 1.07e-6, ""count"": 16, ""carrierWavelengthM"": 1.03e-6},
                ""beam"": {""waistRadiusM"": 0.2e-3},
                ""media"": [ {""matchMaterial"": ""ARGON"", ""material"": ""Ar1bar"", ""nonlinear"": true, ""definition"": {""gas"": {""species"": ""Ar"", ""pressureBar"": 1.0}}} ],
                ""coatings"": [ {""matchComment"": ""MPC_MIRROR"", ""coating"": {""kind"": ""gdd"", ""R"": 0.999, ""gddS2"": -150e-30}} ] }");
            var sample = SampleFake(os, cfg);
            var sys = SystemBuilder.Build(sample, cfg);
            var passes = sys.Elements.Where(e => e.Type == "bulk").ToList();
            var mirrors = sys.Elements.Where(e => e.Type == "interface" && e.Json.GetString("mode") == "reflect").ToList();
            Assert.Equal(9, passes.Count, "passes (entrance + 8 mirror-to-mirror/exit)");
            Assert.Equal(8, mirrors.Count, "mirror interfaces");
            Assert.True(mirrors.All(m => m.Json.GetString("coating") == "rule1"), "MPC mirror coating rule applied");
            Assert.True(sys.Materials.Has("Ar1bar"), "gas definition exported");
            var beam = passes[1].Json.GetObject("beam");
            Assert.Close(0.2e-3, beam.GetDoubleArray("radiusM").Min(), 0.01, 0, "waist reconstructed mid-pass from GBPS/GBPW");
            var run = Spec(Gauss(1.03e-6, 200e-15, 2e-4), O("physics", O("raman", false)));
            var r = Go(sys, run);
            var passRecs = r.Records.Where(x => x.Type == "bulk").ToList();
            Assert.Equal(9, passRecs.Count, "nonlinear passes executed");
            Assert.True(passRecs.All(p => p.BIntegralRad > 0), "every pass is nonlinear");
            Assert.Close(passRecs[1].BIntegralRad, passRecs[2].BIntegralRad, 0.05, 0, "identical full passes give similar B");
            Assert.Close(8 * -150e-30, r.Records.Where(x => x.Type == "interface").Sum(x => x.Own.GddS2), 1e-6, 0, "coating GDD per bounce");
            Assert.Greater(RmsSpectralWidth(r.Output), 1.5 * RmsSpectralWidth(r.Input), "spectral broadening in the cell");
        }

        [Test]
        public void Slab_InterfacesAoiCoatingAndFresnel()
        {
            var os = new FakeOpticStudio("slab");
            var cfg = Cfg(@"{ ""sampling"": {""minWavelengthM"": 1.0e-6, ""maxWavelengthM"": 1.06e-6, ""count"": 12, ""carrierWavelengthM"": 1.03e-6},
                ""coatings"": [ {""matchCoating"": ""AR1030"", ""coating"": {""kind"": ""ar"", ""R"": 0.002}} ] }");
            var sys = SystemBuilder.Build(SampleFake(os, cfg), cfg);
            var ifs = sys.Elements.Where(e => e.Type == "interface").ToList();
            Assert.Equal(2, ifs.Count, "front and back surface");
            Assert.Close(10 * Math.PI / 180, ifs[0].Json.GetDoubleArray("aoiRad")[5], 1e-9, 0, "angle of incidence from RAID");
            Assert.Equal("rule1", ifs[0].Json.GetString("coating"), "AR coating matched by Zemax coating name");
            var r = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", 1.03e-6, "durationFwhmS", 200e-15, "energyJ", 1e-9, "polarization", "45")));
            double n = FakeOpticStudio.SilicaIndex(1.03), ti = 10 * Math.PI / 180, tt = Math.Asin(Math.Sin(ti) / n);
            double rs = Math.Pow(Math.Sin(ti - tt) / Math.Sin(ti + tt), 2), rp = Math.Pow(Math.Tan(ti - tt) / Math.Tan(ti + tt), 2);
            double expected = 0.998 * (1 - 0.5 * (rs + rp));
            Assert.Close(expected, r.Output.TotalEnergyJ() / r.Input.TotalEnergyJ(), 2e-4, 0, "AR front + Fresnel back");
        }

        [Test]
        public void RecordReplay_ReproducesTheSampleExactly()
        {
            var cfg = Cfg(@"{ ""sampling"": {""minWavelengthM"": 0.98e-6, ""maxWavelengthM"": 1.08e-6, ""count"": 30, ""carrierWavelengthM"": 1.03e-6}, ""beam"": {""waistRadiusM"": 2e-3} }");
            var rec = new RecordingOpticStudio(new FakeOpticStudio("treacy"));
            var a = SampleFake(rec, cfg);
            string path = Path.Combine(Path.GetTempPath(), "uf_session_" + Guid.NewGuid().ToString("N") + ".json");
            rec.Save(path);
            var b = SampleFake(new ReplayOpticStudio(path), cfg);
            File.Delete(path);
            a.Created = b.Created = "x";
            Assert.Equal(a.ToJson().ToJson(false), b.ToJson().ToJson(false), "replayed sample");
            string sp = Path.Combine(Path.GetTempPath(), "uf_sample_" + Guid.NewGuid().ToString("N") + ".json");
            a.Save(sp);
            var c = ZemaxSample.Load(sp);
            File.Delete(sp);
            Assert.Equal(SystemBuilder.Build(a, cfg).Elements.Count, SystemBuilder.Build(c, cfg).Elements.Count, "rebuilt from a saved sample");
        }

        [Test]
        public void GaussianBeamProfile_ReconstructsAWaistInsideTheGap()
        {
            double lam = 1.03e-6, w0 = 100e-6, zr = Math.PI * w0 * w0 / lam, L = 0.5, zw = 0.2;
            Func<double, double> w = z => w0 * Math.Sqrt(1 + Math.Pow((z - zw) / zr, 2));
            var p = GaussianBeamProfile.Build(L, w(0), w(L), w0, 1.0, 1.0, lam);
            Assert.True(p.Method.StartsWith("GBPS+GBPW"), "analytic reconstruction used");
            Assert.Close(zw, p.WaistPositionM, 1e-6, 0, "waist position");
            Assert.Close(w(0.37), p.RadiusAt(0.37), 1e-6, 0, "radius inside the gap");
        }

        // ------------------------------------------------------------------------------------------ Zernike (WP Z2)

        private const string ZernikeCfg = @"{ ""sampling"": {""minWavelengthM"": 0.98e-6, ""maxWavelengthM"": 1.08e-6, ""count"": 16, ""carrierWavelengthM"": 1.03e-6}, ""zernike"": {""maxTerm"": 15 ZEXTRA} }";

        private static (SystemDescription Sys, ZemaxSample Sample, FakeOpticStudio Os) ZernikeExportOf(double[] nollWaves, string extra = "")
        {
            var os = new FakeOpticStudio("slab") { ZernikeNollWaves = nollWaves, EntrancePupilRadius = 5.0 };
            var cfg = Cfg(ZernikeCfg.Replace("ZEXTRA", extra));
            var sample = Sampler.Sample(os, cfg.ToSamplerOptions(os, _ => { }));
            return (SystemBuilder.Build(sample, cfg), sample, os);
        }

        [Test(Reference = "Noll, J. Opt. Soc. Am. 66, 207 (1976): Z4 = sqrt(3) (2 rho^2 - 1), Z6 = sqrt(6) rho^2 cos 2 theta", Tolerance = "1e-12")]
        public void Zernike_DefocusAndAstigmatismGiveTheAnalyticPhaseAtPupilPoints()
        {
            // OpticStudio (fake): piston 0.3, defocus Z4 = 0.25, astigmatism Z6 = -0.1 waves at the carrier over a 5 mm entrance pupil
            var (sys, sample, os) = ZernikeExportOf(new[] { 0.3, 0, 0, 0.25, 0, -0.1 });
            Assert.Equal(1, os.ZernikeCalls, "one Zernike read");
            Assert.Close(1.03, sample.Zernike.WavelengthUm, 0, 1e-15, "read at the carrier");
            Assert.Equal(15, sample.Zernike.NollWaves.Length, "Z1..maxTerm");
            var el = sys.Elements.Last();
            Assert.Equal("wavefront", el.Type, "the wavefront element is last (position end)");
            Assert.Close(5e-3, el.Json.GetDouble("radiusM", 0), 1e-15, 0, "radiusM = entrance pupil radius in metres");
            Assert.Close(1.03e-6, el.Json.GetDouble("referenceWavelengthM", 0), 1e-15, 0, "coefficients in waves at the carrier");
            Assert.Close(0, el.Json.GetDoubleArray("zernikeNollWaves")[0], 0, 0, "piston removed");
            // through the engine: the compiled element's optical path at pupil points against the analytic Zernike polynomials
            var r = Go(sys, Spec(Gauss(1.03e-6, 300e-15, 1e-9)));
            var ab = r.Compiled.Elements.Select(e => e.Active).OfType<Optics.SurfaceOpticsElement>().Single().Aberration;
            double lam = 1.03e-6, R = 5e-3;
            foreach (var (x, y) in new[] { (0.0, 0.0), (R, 0.0), (0.0, R), (3e-3, -2e-3), (-1.2e-3, 4.1e-3), (2.5e-3, 2.5e-3) })
            {
                double rho = Math.Sqrt(x * x + y * y) / R, th = Math.Atan2(y, x);
                double opticStudioWaves = 0.25 * Math.Sqrt(3) * (2 * rho * rho - 1) - 0.1 * Math.Sqrt(6) * rho * rho * Math.Cos(2 * th);
                double expected = -opticStudioWaves * lam; // engine W = opdSign (-1) x OpticStudio OPD
                Assert.Close(expected, ab.Opd(x, y, false), 1e-12, 1e-12 * lam, "W at (" + x + ", " + y + ")");
            }
        }

        [Test]
        public void Zernike_ExportOptionsSignPositionTiltAndRadius()
        {
            var (sys, _, _) = ZernikeExportOf(new[] { 0.3, 0.05, -0.02, 0.25 }, @", ""position"": ""start"", ""opdSign"": 1, ""removeTilt"": true, ""radiusM"": 2e-3");
            var el = sys.Elements[0];
            Assert.Equal("wavefront", el.Type, "position start");
            var z = el.Json.GetDoubleArray("zernikeNollWaves");
            Assert.Close(0.25, z[3], 0, 0, "opdSign +1 keeps the sign");
            Assert.Close(0, z[1] * z[1] + z[2] * z[2], 0, 0, "tilt removed");
            Assert.Close(2e-3, el.Json.GetDouble("radiusM", 0), 0, 0, "explicit radiusM");
            Assert.Throws<JsonException>(() => Cfg(@"{ ""zernike"": {""sampling"": 100} }"), "sampling must be a power of two from 32");
            Assert.Throws<JsonException>(() => Cfg(@"{ ""zernike"": {""opdSign"": 2} }"), "opdSign must be +1 or -1");
            // a failed read is a warning, not an error, and nothing is exported
            var os = new FakeOpticStudio("slab");
            var cfg = Cfg(ZernikeCfg.Replace("ZEXTRA", ""));
            var sample = Sampler.Sample(os, cfg.ToSamplerOptions(os, _ => { }));
            Assert.True(sample.Zernike == null && sample.Warnings.Any(w => w.Contains("Zernike")), "failed read warns");
            Assert.True(SystemBuilder.Build(sample, cfg).Elements.All(e => e.Type != "wavefront"), "no wavefront element");
        }

        [Test]
        public void Zernike_RecordReplayAndSavedSampleKeepTheCoefficients()
        {
            var cfg = Cfg(ZernikeCfg.Replace("ZEXTRA", ""));
            var rec = new RecordingOpticStudio(new FakeOpticStudio("slab") { ZernikeNollWaves = new[] { 0, 0, 0, 0.1, 0.02, 0, 0, 0.03 } });
            var a = Sampler.Sample(rec, cfg.ToSamplerOptions(rec, _ => { }));
            string path = Path.Combine(Path.GetTempPath(), "uf_session_" + Guid.NewGuid().ToString("N") + ".json");
            rec.Save(path);
            var b = Sampler.Sample(new ReplayOpticStudio(path), cfg.ToSamplerOptions(rec, _ => { }));
            File.Delete(path);
            a.Created = b.Created = "x";
            Assert.Equal(a.ToJson().ToJson(false), b.ToJson().ToJson(false), "replayed sample with Zernike");
            var c = ZemaxSample.FromJson(JsonValue.Parse(a.ToJson().ToJson(false)).AsObject());
            Assert.Close(0.03, c.Zernike.NollWaves[7], 0, 0, "Z8 survives the sample file");
            Assert.Close(5.0, c.Zernike.EntrancePupilRadius, 0, 0, "pupil radius survives the sample file");
        }
    }
}
