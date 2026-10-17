using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Instruments;
using UltrafastEngine.Models;
using UltrafastEngine.Physics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    public class PolarizationTests
    {
        private static RunResult Through(string pol, params JsonObject[] els) =>
            Go(Sys(els), Spec(O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 50e-15, "energyJ", 1e-9, "polarization", pol)));

        [Test]
        public void Waveplates_RotateAndCirculariseAsExpected()
        {
            var hwp = Through("x", O("id", "hwp", "type", "waveplate", "plate", "half", "axisDeg", 22.5));
            var s = PulseMetrics.Summarize(hwp.Output);
            Assert.Close(1.0, s.S2, 1e-3, 0, "HWP at 22.5 deg: x -> +45 deg (S2 ~ 1; zero-order plate retardance varies across the spectrum)");
            var qwp = Through("x", O("id", "qwp", "type", "waveplate", "plate", "quarter", "axisDeg", 45.0));
            Assert.Close(1.0, Math.Abs(PulseMetrics.Summarize(qwp.Output).S3), 1e-3, 0, "QWP at 45 deg: circular");
            var rcp = Through("rcp");
            Assert.Close(1.0, PulseMetrics.Summarize(rcp.Output).S3, 1e-9, 0, "rcp input has S3 = +1");
            var pol = Through("x", O("id", "p", "type", "polarizer", "axisDeg", 90.0, "extinctionRatio", 1e4));
            Assert.Close(1e-4, pol.Output.TotalEnergyJ() / pol.Input.TotalEnergyJ(), 1e-6, 0, "crossed polariser leaks 1/ER");
            Assert.True(pol.Records[0].LossJ.ContainsKey("polarization"), "loss attributed to polarization");
        }

        [Test]
        public void BrewsterWindow_TransmitsPOnly()
        {
            double th = Math.Atan(1.5);
            var mats = O("glass", O("index", 1.5));
            var s = O("format", "ultrafast-system", "materials", mats);
            // fold in the x-z plane (default): s is y, so x is p-polarised
            s.Set("elements", A(O("id", "w1", "type", "interface", "before", "Vacuum", "after", "glass", "aoiRad", th)));
            var sys = SystemDescription.FromJson(s);
            var px = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 50e-15, "energyJ", 1e-9, "polarization", "x")));
            var py = Go(sys, Spec(O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 50e-15, "energyJ", 1e-9, "polarization", "y")));
            Assert.Close(1.0, px.Output.TotalEnergyJ() / px.Input.TotalEnergyJ(), 1e-10, 0, "p transmission at Brewster");
            double rs = Math.Pow((Math.Cos(th) - 1.5 * Math.Cos(Math.Asin(Math.Sin(th) / 1.5))) / (Math.Cos(th) + 1.5 * Math.Cos(Math.Asin(Math.Sin(th) / 1.5))), 2);
            Assert.Close(1 - rs, py.Output.TotalEnergyJ() / py.Input.TotalEnergyJ(), 1e-9, 0, "s transmission at Brewster");
            Assert.True(px.Records[0].LossJ.Count == 0 || px.Records[0].LossJ.Values.All(v => Math.Abs(v) < 1e-20), "no loss for p");
        }

        [Test]
        public void BasisTracker_PeriscopeRotatesTheTransportedBasis()
        {
            var t = new BasisTracker();
            double r = 1 / Math.Sqrt(2);
            var b1 = t.Surface(new Vec3(0, 0, 1), new Vec3(0, 1, 0), new Vec3(0, -r, r));
            Assert.Close(1, Math.Abs(b1[0]), 1e-12, 0, "x is s-polarised at the first mirror");
            var b2 = t.Surface(new Vec3(0, 1, 0), new Vec3(1, 0, 0), new Vec3(r, -r, 0));
            Assert.Close(1, Math.Abs(b2[2]), 1e-12, 0, "transported x is p-polarised at the second mirror");
            Assert.Close(1, Math.Abs(t.U.Y), 1e-12, 0, "transported x now points along global y (image rotation)");
            Assert.Close(1, t.K.X, 1e-12, 0, "beam travels along +x");
        }
    }

    public class LossAndOverrideTests
    {
        [Test]
        public void LossBudget_ClipFresnelScatterAddUp()
        {
            var mats = O("glass", O("index", 1.5));
            var s = O("format", "ultrafast-system", "materials", mats);
            double a = 1e-3, w = 1e-3;
            s.Set("elements", A(
                O("id", "ap", "type", "aperture", "radiusM", a, "beamRadiusM", w),
                O("id", "s1", "type", "interface", "before", "Vacuum", "after", "glass", "roughnessRmsM", 5e-9),
                O("id", "s2", "type", "interface", "before", "glass", "after", "Vacuum"),
                O("id", "f", "type", "filter", "filter", O("kind", "gaussian", "centerWavelengthM", 800e-9, "fwhmM", 40e-9))));
            var r = Go(SystemDescription.FromJson(s), Spec(Gauss(800e-9, 30e-15, 1e-6)));
            Assert.Close(1 - Math.Exp(-2 * a * a / (w * w)), r.Records[0].EnergyOutJ / r.Records[0].EnergyInJ, 1e-12, 0, "Gaussian clip");
            double total = r.Records.Sum(x => x.LossJ.Values.Sum());
            Assert.Close(r.Input.TotalEnergyJ() - r.Output.TotalEnergyJ(), total, 1e-9, 0, "loss budget closes");
            var budget = r.SummaryJson().GetObject("lossBudget");
            foreach (var m in new[] { "clip", "fresnel", "scatter", "filter" }) Assert.True(budget.Has(m), "budget has " + m);
            Assert.Close(0.04, budget.GetObject("fresnel").GetDouble("energyJ", 0) / r.Records[1].EnergyInJ / 2, 0.03, 0, "~4 % per uncoated surface");
        }

        [Test]
        public void Overrides_PriorityAndProvenance()
        {
            var s = O("format", "ultrafast-system", "materials", O());
            s.Set("elements", A(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-3, "beam", O("kind", "constant", "radiusM", 1e-4))));
            s.Set("overrides", A(O("target", "element:plate", "property", "n2", "value", 1e-20)));
            var sys = SystemDescription.FromJson(s);
            var extra = O(); extra.Set("overrides", A(O("target", "element:plate", "property", "n2", "value", 3e-20), O("target", "element:typo", "property", "n2", "value", 1.0)));
            var c = Engine.Compiler.Compile(sys, Spec(Gauss(800e-9, 30e-15, 1e-9), extra));
            Assert.Close(3e-20, c.Elements[0].Nonlinear.Medium.N2, 0, 0, "run override wins");
            var p = c.Provenance.Entries.First(e => e.Target == "element:plate" && e.Property == "n2");
            Assert.Equal(ModelSource.RunOverride, p.Source, "n2 provenance");
            Assert.True(c.Warnings.Any(w => w.Contains("element:typo")), "unused override reported");
            // without overrides the built-in value is used and reported as a material default
            var c2 = Engine.Compiler.Compile(SystemDescription.FromJson(O("format", "ultrafast-system", "elements", A(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-3, "beam", O("kind", "constant", "radiusM", 1e-4))))), Spec(Gauss(800e-9, 30e-15, 1e-9)));
            Assert.Equal(ModelSource.MaterialDefault, c2.Provenance.Entries.First(e => e.Property == "n2").Source, "default provenance");
        }

        [Test]
        public void Overrides_MaterialIndexOverrideBeatsTracedData()
        {
            var wl = new[] { 1000e-9, 1030e-9, 1060e-9, 1090e-9 };
            var s = O("format", "ultrafast-system", "sampling", O("wavelengthsM", wl));
            s.Set("elements", A(O("id", "g", "type", "bulk", "material", "N-BK7", "lengthM", 0.01, "groupDelayS", new[] { 1e-10, 1e-10, 1e-10, 1e-10 })));
            var sys = SystemDescription.FromJson(s);
            var c1 = Engine.Compiler.Compile(sys, Spec(Gauss(1030e-9, 100e-15, 1e-9)));
            Assert.True(c1.Elements[0].Pre[0] is SampledGroupDelayTerm, "traced data used by default");
            var extra = O(); extra.Set("overrides", A(O("target", "material:N-BK7", "property", "index", "model", O("kind", "constant", "value", 1.6))));
            var c2 = Engine.Compiler.Compile(sys, Spec(Gauss(1030e-9, 100e-15, 1e-9), extra));
            Assert.True(c2.Elements[0].Pre[0] is BulkTerm, "material override -> material model");
            Assert.True(c2.Provenance.Entries.Any(e => e.Target == "material:N-BK7" && e.Property == "index" && e.Source == ModelSource.RunOverride), "material provenance");
        }

        [Test]
        public void SystemFile_RoundTripAndValidation()
        {
            var sys = PropagationTests.FlatKerrSystem(0.01);
            string path = Path.Combine(Path.GetTempPath(), "uf_" + Guid.NewGuid().ToString("N") + ".ufs");
            sys.Save(path);
            var back = SystemDescription.Load(path);
            File.Delete(path);
            Assert.Equal(sys.ToJson().ToJson(false), back.ToJson().ToJson(false), "round trip");
            Assert.True(Simulation.Validate(back).Ok, "valid system");
            var bad = Sys(O("id", "a", "type", "bulk", "material", "Unobtainium", "lengthM", 1.0), O("id", "a", "type", "warpDrive"));
            var rep = Simulation.Validate(bad);
            Assert.False(rep.Ok, "invalid system");
            Assert.True(rep.Errors.Any(e => e.Contains("duplicate")), "duplicate id reported");
            Assert.True(rep.Errors.Any(e => e.Contains("warpDrive")), "unknown type reported");
        }
    }

    public class InstrumentTests
    {
        private static InstrumentOutput Measure(string kind, RunResult r, JsonObject spec = null)
        {
            spec = spec ?? O(); spec.Set("kind", kind);
            var inst = InstrumentFactory.Create(spec);
            var o = new InstrumentOutput { Name = kind, Kind = kind, Location = "end" };
            inst.Measure(new InstrumentContext { Pulse = r.Output, Location = "end", Run = r, Spec = spec }, o);
            return o;
        }

        private static RunResult Free(JsonObject pulse) => Go(Sys(O("id", "p", "type", "probe")), Spec(pulse));

        [Test]
        public void Autocorrelator_DeconvolutionFactors()
        {
            var g = Measure("autocorrelator", Free(Gauss(800e-9, 40e-15, 1e-9)));
            Assert.Close(40e-15 * Math.Sqrt(2), g.Values.GetDouble("acFwhmS", 0), 0.01, 0, "Gaussian AC = sqrt2 x FWHM");
            var s = Measure("autocorrelator", Free(O("shape", "sech2", "centerWavelengthM", 800e-9, "durationFwhmS", 40e-15, "energyJ", 1e-9)));
            Assert.Close(40e-15, s.Values.GetDouble("pulseFwhmSech2S", 0), 0.01, 0, "sech2 deconvolution");
            var iac = Measure("autocorrelator", Free(Gauss(800e-9, 20e-15, 1e-9)), O("type", "interferometric"));
            Assert.Close(8.0, iac.Values.GetDouble("peakToBackground", 0), 0.02, 0, "IAC 8:1");
        }

        [Test]
        public void Spectrometer_CompressorPolarimeterEnergy()
        {
            var r = Free(O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", 2e-6, "repRateHz", 1e5, "chirp", O("gddS2", 20000e-30), "polarization", "rcp"));
            var sp = Measure("spectrometer", r, O("resolutionM", 0.0));
            double dnu = 0.441 / 100e-15, dl = 1030e-9 * 1030e-9 * dnu / C;
            Assert.Close(dl, sp.Values.GetDouble("fwhmM", 0), 0.01, 0, "spectral FWHM");
            Assert.Close(1030e-9, sp.Values.GetDouble("peakWavelengthM", 0), 2e-4, 0, "peak wavelength");
            var cp = Measure("compressor", r);
            Assert.Close(-20000e-30, cp.Values.GetDouble("compressorGddS2", 0), 0.01, 0, "compressor finds -GDD");
            Assert.Close(100e-15, cp.Values.GetDouble("compressedFwhmS", 0), 0.01, 0, "compressed to the transform limit");
            var pm = Measure("polarimeter", r);
            Assert.Close(1.0, pm.Values.GetDouble("s3", 0), 1e-9, 0, "S3");
            var em = Measure("energyMeter", r);
            Assert.Close(0.2, em.Values.GetDouble("averagePowerW", 0), 1e-9, 0, "average power");
        }

        [Test]
        public void DScan_FindsGlassThatCompensatesTheChirp()
        {
            // pre-chirp equal to -(GDD of 1.5 mm fused silica at 800 nm): the optimum insertion is +1.5 mm
            var fs = Materials.MaterialLibrary.BuiltIn("FusedSilica");
            var d = new BulkTerm(fs, 1.5e-3).DispersionAt(W(800e-9));
            var r = Free(O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 20e-15, "energyJ", 1e-9, "chirp", O("gddS2", -d.GddS2, "todS3", -d.TodS3)));
            var o = Measure("dscan", r, O("insertionM", new[] { 0.0, 3e-3 }, "steps", 121));
            Assert.Close(1.5e-3, o.Values.GetDouble("optimalInsertionM", 0), 0.02, 0, "optimal insertion");
            var frog = Measure("frog", Free(Gauss(800e-9, 30e-15, 1e-9)), O("points", 128));
            Assert.Close(30e-15 * Math.Sqrt(2), frog.Values.GetDouble("delayMarginalFwhmS", 0), 0.05, 0, "SHG-FROG delay marginal = intensity AC");
        }
    }
}
