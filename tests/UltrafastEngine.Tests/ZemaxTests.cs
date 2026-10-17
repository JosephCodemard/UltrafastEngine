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

        [Test]
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
    }
}
