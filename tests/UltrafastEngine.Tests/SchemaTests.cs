using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Option registry, JSON Schemas, unknown-key checks and migrations (docs/reference/schema.md).</summary>
    [TestArea("Schemas, validation and migrations")]
    public class SchemaTests
    {
        internal static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "UltrafastEngine.sln"))) d = d.Parent;
            return d?.FullName;
        }

        /// <summary>Every example and tutorial file uses only declared options (keeps the registry, hence the
        /// schemas and the reference, complete).</summary>
        [Test(Category = "schema")]
        public void ExamplesUseOnlyDeclaredOptions()
        {
            var root = RepoRoot();
            if (root == null) Assert.Skip("repository not found");
            var problems = new List<string>();
            int files = 0;
            foreach (var dir in new[] { Path.Combine(root, "docs") })   // examples live in docs/examples
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".ufs") || x.EndsWith(".json")))
                {
                    JsonObject o;
                    try { o = JsonValue.ParseFile(f).AsObject(); } catch { continue; }
                    string fmt = o.GetString("format", "");
                    try
                    {
                        if (fmt == "ultrafast-system") { files++; problems.AddRange(Validator.CheckSystem(SystemDescription.FromJson(o, Path.GetDirectoryName(f))).Select(w => Path.GetFileName(f) + ": " + w)); }
                        else if (fmt == "ultrafast-run") { files++; problems.AddRange(Validator.CheckRun(RunSpec.FromJson(o, Path.GetDirectoryName(f))).Select(w => Path.GetFileName(f) + ": " + w)); }
                    }
                    catch (Exception ex) { problems.Add(f + ": " + ex.Message); }
                }
            }
            Assert.Greater(files, 30, "example files found");
            Assert.True(problems.Count == 0, problems.Count + " undeclared options:\n  " + string.Join("\n  ", problems.Take(80)));
        }

        [Test(Category = "schema")]
        public void UnknownKey_IsReportedWithSuggestion()
        {
            var sys = Sys(O("id", "plate", "type", "bulk", "material", "FusedSilica", "lenghtM", 1e-3, "lengthM", 1e-3));
            var w = Validator.CheckSystem(sys);
            Assert.True(w.Any(x => x.Contains("\"lenghtM\"") && x.Contains("did you mean \"lengthM\"")), string.Join("; ", w));
            var run = Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "resolutonM", 1e-9))));
            var w2 = Validator.CheckRun(run);
            Assert.True(w2.Any(x => x.Contains("resolutonM") && x.Contains("resolutionM")), string.Join("; ", w2));
            var w3 = Validator.CheckRun(Spec(Gauss(800e-9, 30e-15, 1e-9), O("instruments", A(O("kind", "spectrometer", "resolutionM", "1 nm")))));
            Assert.True(w3.Any(x => x.Contains("should be a number")), "type check: " + string.Join("; ", w3));
            // the warning reaches the run
            var r = Go(sys, Spec(Gauss(800e-9, 30e-15, 1e-9)));
            Assert.True(r.Warnings.Any(x => x.Contains("lenghtM")), "run warning");
        }

        [Test(Category = "schema")]
        public void Schemas_AreGeneratedForEveryElementAndInstrument()
        {
            var s = SchemaGenerator.SystemSchema();
            var defs = s.GetObject("definitions");
            foreach (var t in Engine.Compiler.ElementTypes.Where(t => t != "sesam" && t != "custom"))
                Assert.True(defs.Has("element." + t) || defs.Keys.Any(k => k.Equals("element." + t, StringComparison.OrdinalIgnoreCase)), "element." + t + " in the system schema");
            var r = SchemaGenerator.RunSchema();
            var rd = r.GetObject("definitions");
            foreach (var k in Instruments.InstrumentFactory.Known.Where(k => !new[] { "sfgFrog", "powerMeter", "photodiode", "nearFieldCamera", "farFieldCamera", "thirdOrderCorrelator" }.Contains(k)))
                Assert.True(rd.Has("instrument." + k), "instrument." + k + " in the run schema");
            // plugin keys appear too (fitting plugin registered by the runner)
            Assert.True(r.GetObject("properties").Has("fit"), "fit (fitting plugin) in the run schema");
            Assert.True(rd.GetObject("instrument.spectrometer").GetObject("properties").Has("measured"), "measured (core) on instruments");
            // valid JSON round trip
            JsonValue.Parse(s.ToJson(true)); JsonValue.Parse(r.ToJson(true));
        }

        /// <summary>The committed schemas/ and docs/reference/options.md are what the engine generates
        /// (regenerate with ultrafast schema / ultrafast reference).</summary>
        private static readonly string[] Shipped = { "core", "fitting", "zemax", "ao" };

        [Test(Category = "schema")]
        public void CommittedSchemasAndReference_AreUpToDate()
        {
            var root = RepoRoot();
            if (root == null) Assert.Skip("repository not found");
            string Norm(string t) => t.Replace("\r\n", "\n").Trim();
            Assert.Equal(Norm(SchemaGenerator.SystemSchema(Shipped).ToJson(true)), Norm(File.ReadAllText(Path.Combine(root, "schemas", "ufs.schema.json"))), "schemas/ufs.schema.json is current (ultrafast schema system -o schemas/ufs.schema.json)");
            Assert.Equal(Norm(SchemaGenerator.RunSchema(Shipped).ToJson(true)), Norm(File.ReadAllText(Path.Combine(root, "schemas", "run.schema.json"))), "schemas/run.schema.json is current (ultrafast schema run -o schemas/run.schema.json)");
            Assert.Equal(Norm(ReferenceGenerator.Markdown(Shipped)), Norm(File.ReadAllText(Path.Combine(root, "docs", "reference", "options.md"))), "docs/reference/options.md is current (ultrafast reference -o docs/reference/options.md)");
            Assert.Equal(Norm(UltrafastEngine.Materials.CatalogueReference.Markdown()), Norm(File.ReadAllText(Path.Combine(root, "docs", "reference", "materials-catalogue.md"))), "docs/reference/materials-catalogue.md is current (ultrafast reference materials -o docs/reference/materials-catalogue.md)");
        }

        [Test(Category = "schema")]
        public void Migration_V1Cameras_BecomeTheUnifiedCamera()
        {
            var o = (JsonObject)JsonValue.Parse("{\"format\":\"ultrafast-run\",\"pulse\":{\"centerWavelengthM\":1e-6,\"durationFwhmS\":1e-13,\"energyJ\":1e-9},\"instruments\":[{\"kind\":\"farFieldCamera\",\"name\":\"ff\",\"focalLengthM\":0.5,\"pixels\":64},{\"kind\":\"nearFieldCamera\",\"propagateM\":-0.1}]}");
            var notes = new List<string>();
            Migrations.MigrateRun(o, notes);
            var i0 = o.GetArray("instruments")[0].AsObject();
            Assert.Equal("camera", i0.GetString("kind"), "kind");
            Assert.Equal("far", i0.GetObject("imaging").GetString("plane"), "plane");
            Assert.Close(0.5, i0.GetObject("imaging").GetDouble("focalLengthM", 0), 0, 0, "focal length moved");
            Assert.False(i0.Has("focalLengthM"), "moved, not copied");
            Assert.Close(-0.1, o.GetArray("instruments")[1].AsObject().GetObject("imaging").GetDouble("propagateM", 0), 0, 0, "propagateM moved");
            Assert.Equal(2, o.GetInt("version", 0), "version");
            Assert.Equal(2, notes.Count, "a note per camera");
            Assert.Throws<JsonException>(() => Migrations.MigrateRun((JsonObject)JsonValue.Parse("{\"format\":\"ultrafast-run\",\"version\":99}"), null), "newer versions refused");
        }

        [Test(Reference = "1.0.0 renames (the unit goes into the key): the old names are rewritten on load with a note, give an identical run, and the new names pass the strict key check", Tolerance = "exact")]
        public void UnitInKeyRenames_OldNamesStillWork_AndNewOnesAreDeclared()
        {
            var notes = new List<string>();
            var sys = (JsonObject)JsonValue.Parse(@"{""format"":""ultrafast-system"",""elements"":[
              {""id"":""f"",""type"":""fiber"",""lengthM"":0.1,""betas"":[-2e-26],""gamma"":1e-3},
              {""id"":""r"",""type"":""faradayRotator"",""verdetRadPerTM"":40,""fieldT"":1.1,""lengthM"":0.018}],
              ""materials"":{""m"":{""index"":{""kind"":""refractiveindex"",""page"":""x"",""dnDT"":1e-5}}}}");
            Migrations.MigrateSystem(sys, notes);
            var f = sys.GetArray("elements")[0].AsObject(); var r = sys.GetArray("elements")[1].AsObject();
            Assert.True(f.Has("betasSkPerM") && !f.Has("betas"), "fibre betas renamed");
            Assert.Close(-2e-26, f.GetDoubleArray("betasSkPerM")[0], 0, 0, "value kept");
            Assert.True(r.Has("fieldTesla") && !r.Has("fieldT"), "Faraday field renamed");
            Assert.True(sys.GetObject("materials").GetObject("m").GetObject("index").Has("dnDTPerK"), "thermo-optic coefficient renamed (nested)");
            Assert.Equal(3, notes.Count, "one note per renamed key");
            var run = (JsonObject)JsonValue.Parse(@"{""format"":""ultrafast-run"",""models"":{""x"":{""kind"":""dampedOscillator"",""tau1"":12.2e-15,""tau2"":32e-15}}}");
            Migrations.MigrateRun(run, null);
            var model = run.GetObject("models").GetObject("x");
            Assert.True(model.Has("tau1S") && model.Has("tau2S") && !model.Has("tau1"), "Raman time constants renamed");
            // a file that already uses the new name is left alone, and the new names are declared options (strict mode accepts them)
            var again = new List<string>(); Migrations.MigrateSystem(sys, again);
            Assert.Equal(0, again.Count, "nothing to rename twice");
            foreach (var (type, key) in new[] { ("fiber", "betasSkPerM"), ("faradayRotator", "fieldTesla") })
                Assert.True(OptionRegistry.Get("element", type).Params.Any(o => o.Name == key), type + "." + key + " is declared");
            // the same run from the old and the new name
            JsonObject Fib(string key) => O("id", "f", "type", "fiber", "lengthM", 0.5, key, A(-20e-27), "gamma", 1e-3);
            var a = Go(Sys(Fib("betas")), Spec(Gauss(1550e-9, 100e-15, 1e-9)));
            var b = Go(Sys(Fib("betasSkPerM")), Spec(Gauss(1550e-9, 100e-15, 1e-9)));
            Assert.Equal(a.Output.TotalEnergyJ(), b.Output.TotalEnergyJ(), "same run");
            Assert.Equal(a.Records[0].Values.Count, b.Records[0].Values.Count, "same record");
        }
    }
}
