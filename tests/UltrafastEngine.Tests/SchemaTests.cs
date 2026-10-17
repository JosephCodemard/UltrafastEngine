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
            foreach (var k in Instruments.InstrumentFactory.Known.Where(k => !new[] { "powerMeter", "photodiode", "nearFieldCamera", "farFieldCamera", "thirdOrderCorrelator" }.Contains(k)))
                Assert.True(rd.Has("instrument." + k), "instrument." + k + " in the run schema");
            // plugin keys appear too (fitting plugin registered by the runner)
            Assert.True(r.GetObject("properties").Has("fit"), "fit (fitting plugin) in the run schema");
            Assert.True(rd.GetObject("instrument.spectrometer").GetObject("properties").Has("measured"), "measured (fitting plugin) on instruments");
            // valid JSON round trip
            JsonValue.Parse(s.ToJson(true)); JsonValue.Parse(r.ToJson(true));
        }

        /// <summary>The committed schemas/ and docs/reference/options.md are what the engine generates
        /// (regenerate with ultrafast schema / ultrafast reference).</summary>
        private static readonly string[] Shipped = { "core", "fitting", "zemax" };

        [Test(Category = "schema")]
        public void CommittedSchemasAndReference_AreUpToDate()
        {
            var root = RepoRoot();
            if (root == null) Assert.Skip("repository not found");
            string Norm(string t) => t.Replace("\r\n", "\n").Trim();
            Assert.Equal(Norm(SchemaGenerator.SystemSchema(Shipped).ToJson(true)), Norm(File.ReadAllText(Path.Combine(root, "schemas", "ufs.schema.json"))), "schemas/ufs.schema.json is current (ultrafast schema system -o schemas/ufs.schema.json)");
            Assert.Equal(Norm(SchemaGenerator.RunSchema(Shipped).ToJson(true)), Norm(File.ReadAllText(Path.Combine(root, "schemas", "run.schema.json"))), "schemas/run.schema.json is current (ultrafast schema run -o schemas/run.schema.json)");
            Assert.Equal(Norm(ReferenceGenerator.Markdown(Shipped)), Norm(File.ReadAllText(Path.Combine(root, "docs", "reference", "options.md"))), "docs/reference/options.md is current (ultrafast reference -o docs/reference/options.md)");
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
    }
}
