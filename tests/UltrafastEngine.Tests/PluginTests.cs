using System;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Physics;
using UltrafastEngine.Plugins;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The plugin system (docs/reference/plugins.md): registration, discovery, install, extension points.</summary>
    public class PluginTests
    {
        /// <summary>A test plugin: an element type ("halfEnergy": a flat 50 % loss) and an instrument ("peakCounter").</summary>
        private sealed class TestPlugin : IPlugin
        {
            public string Id => "test-plugin";
            public string Name => "Test plugin";
            public string Version => "1.0.0";
            public string Description => "adds halfEnergy and peakCounter";
            public void Register(PluginContext ctx)
            {
                ctx.RegisterElement("halfEnergy", (s, e, ce) => ce.Post.Add(new FilterTerm(new ConstantFilter(s.Num(e, "transmission", 0.5)), "test loss")),
                    new ComponentSpec("element", "halfEnergy", "A flat loss (test plugin).").Num("transmission", "", "Power transmission", "0.5"));
                ctx.RegisterInstrument("peakCounter", spec => new PeakCounter(),
                    new ComponentSpec("instrument", "peakCounter", "Peak power (test plugin)."));
            }
        }

        private sealed class PeakCounter : IInstrument
        {
            public string Kind => "peakCounter";
            public void Measure(InstrumentContext ctx, InstrumentOutput o) => o.Values.Set("peakPowerW", Results.PulseMetrics.TemporalPower(ctx.Pulse).Max());
        }

        [Test(Category = "plugins")]
        public void Plugin_RegistersElementAndInstrument_AndTheyRun()
        {
            PluginManager.Register(new TestPlugin());
            Assert.True(PluginManager.Has("test-plugin"), "registered");
            Assert.False(PluginManager.Register(new TestPlugin()), "a second registration of the same id is ignored");
            var sys = Sys(O("id", "loss", "type", "halfEnergy", "transmission", 0.25));
            var r = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-6), O("instruments", A(O("kind", "peakCounter", "name", "pk")))));
            Assert.Close(0.25e-6, r.Output.TotalEnergyJ(), 1e-9, 0, "plugin element applied");
            Assert.True(r.Instruments.First(i => i.Name == "pk").Values.GetDouble("peakPowerW", 0) > 0, "plugin instrument measured");
            Assert.Equal(0, r.Warnings.Count(w => w.Contains("unknown parameter")), "declared options accepted: " + string.Join("; ", r.Warnings));
            Assert.True(SchemaGenerator.SystemSchema().GetObject("definitions").Has("element.halfEnergy"), "plugin element in the schema");
            var bad = Validator.CheckSystem(Sys(O("id", "loss", "type", "halfEnergy", "transmision", 0.25)));
            Assert.True(bad.Any(w => w.Contains("did you mean \"transmission\"")), "plugin options are checked too");
        }

        [Test(Category = "plugins")]
        public void FirstPartyPlugins_AreRegistered_AndDiscoveryReusesThem()
        {
            Assert.True(PluginManager.Has("fitting") && PluginManager.Has("zemax"), "fitting and zemax registered by the runner");
            Assert.True(PluginManager.CliCommands.ContainsKey("zemax-demo"), "zemax plugin command");
            Assert.True(PluginManager.Modes.Any(m => m.Key == "fit"), "fit run mode");
            // the installable folder copied to bin/plugins loads (as the already-registered plugin)
            var folder = Path.Combine(AppContext.BaseDirectory, "plugins", "fitting");
            if (!Directory.Exists(folder)) Assert.Skip("bin/plugins not laid out");
            var info = PluginManager.LoadFolder(folder);
            Assert.True(info.Loaded, "loaded: " + info.Error);
            var m = PluginManifest.Load(folder);
            Assert.Equal("fitting", m.Id, "manifest id");
            Assert.True(m.Assemblies.ContainsKey(PluginManager.RuntimeFramework()), "assembly for this runtime");
        }

        [Test(Category = "plugins")]
        public void Install_AndRemove_InTheUserFolder()
        {
            var home = Path.Combine(Path.GetTempPath(), "uf-home-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var src = Path.Combine(home, "src", "demo");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "plugin.json"), "{\"id\": \"demo\", \"version\": \"0.1.0\", \"description\": \"python-only demo\", \"python\": {\"module\": \"demo_mod\"}}");
            var old = Environment.GetEnvironmentVariable("ULTRAFAST_HOME");
            try
            {
                Environment.SetEnvironmentVariable("ULTRAFAST_HOME", home);
                var dest = PluginManager.Install(src);
                Assert.True(File.Exists(Path.Combine(dest, "plugin.json")), "copied");
                Assert.True(dest.StartsWith(Path.Combine(home, "plugins")), "into ULTRAFAST_HOME/plugins");
                var info = PluginManager.LoadFolder(dest);
                Assert.True(info.Loaded, "a plugin without assemblies (Python-only) is listed: " + info.Error);
                Assert.True(PluginManager.Remove("demo"), "removed");
                Assert.False(Directory.Exists(dest), "gone");
            }
            finally { Environment.SetEnvironmentVariable("ULTRAFAST_HOME", old); try { Directory.Delete(home, true); } catch { } }
        }

        [Test]
        public void VersionComparison_OrdersPreReleases()
        {
            Assert.True(PluginManager.CompareVersions("2.0.0-alpha.2", "2.0.0-alpha.1") > 0, "alpha.2 > alpha.1");
            Assert.True(PluginManager.CompareVersions("2.0.0-beta.1", "2.0.0-alpha.9") > 0, "beta > alpha");
            Assert.True(PluginManager.CompareVersions("2.0.0", "2.0.0-rc.1") > 0, "release > rc");
            Assert.True(PluginManager.CompareVersions("2.1.0-alpha.1", "2.0.9") > 0, "minor wins");
            Assert.Equal(0, PluginManager.CompareVersions("2.0.0-alpha.2", "2.0.0-alpha.2"), "equal");
        }

        [Test(Category = "plugins")]
        public void TooNewPlugin_IsReportedNotLoaded()
        {
            var dir = Path.Combine(Path.GetTempPath(), "uf-new-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), "{\"id\": \"future\", \"engine\": \"9.0.0\", \"assemblies\": {\"net10.0\": \"x.dll\", \"net48\": \"x.dll\"}}");
            var info = PluginManager.LoadFolder(dir);
            Assert.False(info.Loaded, "not loaded");
            Assert.True(info.Error.Contains("9.0.0"), info.Error);
            Directory.Delete(dir, true);
        }

        [Test(Category = "instruments")]
        public void RepeatedInstruments_GetUniqueNames()
        {
            var sys = Sys(O("id", "a", "type", "probe"), O("id", "b", "type", "probe"));
            var r = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(
                O("kind", "spectrometer", "at", "a"), O("kind", "spectrometer", "at", "a"), O("kind", "spectrometer", "at", "b"),
                O("kind", "autocorrelator", "name", "ac"), O("kind", "autocorrelator", "name", "ac", "at", "a")))));
            var names = r.Instruments.Select(i => i.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count(), "unique: " + string.Join(", ", names));
            Assert.True(names.Contains("spectrometer_a") && names.Contains("spectrometer_a_2") && names.Contains("spectrometer_b"), string.Join(", ", names));
            Assert.True(names.Contains("ac") && names.Contains("ac_2"), "repeated explicit name suffixed");
            Assert.True(r.Warnings.Any(w => w.Contains("\"ac\" is used more than once")), "explicit duplicate warned");
            // arrays do not collide
            Assert.Equal(r.Arrays().Count(a => a.Name.StartsWith("spectrometer_a_wavelength")), 1, "own arrays");
            Assert.Equal(r.Arrays().Count(a => a.Name.StartsWith("spectrometer_a_2_wavelength")), 1, "own arrays (2)");
        }

        [Test(Category = "instruments")]
        public void Camera_UnifiedImaging_MatchesTheOldCameras()
        {
            var sys = Sys(O("id", "g", "type", "gap", "material", "Vacuum", "lengthM", 0.1, "beam", O("kind", "gaussian", "w0M", 300e-6, "wavelengthM", 1030e-9),
                            "transverse", O("model", "radial", "points", 64, "radiusM", 1.5e-3)));
            var r = Go(sys, Spec(Gauss(1030e-9, 300e-15, 1e-6), O("version", 2, "instruments", A(
                O("kind", "camera", "name", "near", "at", "g"),
                O("kind", "camera", "name", "far", "at", "g", "imaging", O("plane", "far", "focalLengthM", 1.0)),
                O("kind", "farFieldCamera", "name", "farOld", "at", "g", "focalLengthM", 1.0),
                O("kind", "camera", "name", "relay", "at", "g", "imaging", O("plane", "relay", "relay", A(O("gapM", 0.5), O("focalLengthM", 0.5), O("gapM", 0.5)))),
                O("kind", "camera", "name", "noisy", "at", "g", "sensor", O("pixels", 64, "bitDepth", 8, "backgroundCounts", 10.0, "readNoiseCounts", 1.0, "hotPixelFraction", 0.01))))));
            double V(string n, string k) => r.Instruments.First(i => i.Name == n).Values.GetDouble(k, double.NaN);
            Assert.Close(1030e-9 / (Math.PI * 300e-6), V("far", "radiusXM"), 0.005, 0, "far field radius lambda f / (pi w0)");
            Assert.Close(V("far", "radiusXM"), V("farOld", "radiusXM"), 1e-9, 0, "farFieldCamera alias == camera far");
            // a 2f relay (gap f, lens f, gap f) puts the far field on the sensor
            Assert.Close(V("far", "radiusXM") * 0.5, V("relay", "radiusXM"), 0.03, 0, "relay image = far field of f = 0.5 m");
            Assert.True(r.Instruments.First(i => i.Name == "noisy").Arrays.Any(a => a.Name.EndsWith("_counts")), "raw counts array");
            Assert.Equal("far", r.Instruments.First(i => i.Name == "far").Values.GetString("imagingPlane"), "imagingPlane reported");
        }
    }
}
