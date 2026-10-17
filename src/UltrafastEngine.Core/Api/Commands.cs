using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Materials;
using UltrafastEngine.Retrieval;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine
{
    /// <summary>
    /// JSON-in / JSON-out entry points shared by the CLI, the C API (uf_command) and the Python package: studies,
    /// pulse retrieval and the material library. A request is {"command": "...", ...}; the reply is a JSON object.
    /// </summary>
    public static class Commands
    {
        public static JsonObject Execute(JsonObject request, string baseDirectory = null)
        {
            string cmd = request.GetRequiredString("command", "request");
            switch (cmd.ToLowerInvariant())
            {
                case "study": return Study(request, baseDirectory);
                case "retrieve": return Retrieve(request, baseDirectory);
                case "materials.list": return MaterialsList();
                case "materials.show": return MaterialsShow(request.GetRequiredString("name", "materials.show"), baseDirectory);
                case "materials.import": return MaterialsImport(request, baseDirectory);
                default: throw new JsonException("unknown command \"" + cmd + "\" (study, retrieve, materials.list, materials.show, materials.import)");
            }
        }

        private static string Abs(string p, string b) => p == null ? null : Path.IsPathRooted(p) || b == null ? p : Path.Combine(b, p);

        // ------------------------------------------------------------------------------------------ study

        /// <summary>{"command": "study", "system": path | object, "run": path | object, "study": {...} (or the study keys inline), "output": dir}</summary>
        public static JsonObject Study(JsonObject r, string baseDirectory)
        {
            SystemDescription sys; RunSpec run;
            var sv = r["system"] ?? throw new JsonException("study: \"system\" (file or object) is required");
            var rv = r["run"] ?? throw new JsonException("study: \"run\" (file or object) is required");
            if (sv.Kind == JsonKind.String) sys = SystemDescription.Load(Abs(sv.AsString(), baseDirectory)); else sys = SystemDescription.FromJson(sv.AsObject(), baseDirectory);
            if (rv.Kind == JsonKind.String) run = RunSpec.Load(Abs(rv.AsString(), baseDirectory)); else run = RunSpec.FromJson(rv.AsObject(), baseDirectory);
            var so = r.GetObject("study") ?? r;
            var spec = StudySpec.FromJson(so);
            var res = Studies.Run(sys, run, spec);
            var outDir = r.GetString("output");
            if (outDir != null) res.Write(Abs(outDir, baseDirectory));
            return res.ToJson();
        }

        // ------------------------------------------------------------------------------------------ retrieval

        /// <summary>{"command": "retrieve", "method": "pcgpa" | "epie" | "dscan", "trace": path | {arrays}, "kind": "shg-frog",
        /// "fundamental": spectrum CSV (d-scan), "material", "instrument": name of the trace in a results file, "options": {...}, "output": dir,
        /// "energyJ": for the pulse}</summary>
        public static JsonObject Retrieve(JsonObject r, string baseDirectory)
        {
            string method = r.GetString("method", "pcgpa");
            string kind = r.GetString("kind", method == "dscan" ? "dscan" : "shg-frog");
            TraceData t;
            var tv = r["trace"] ?? throw new JsonException("retrieve: \"trace\" is required");
            if (tv.Kind == JsonKind.String) t = TraceData.Load(Abs(tv.AsString(), baseDirectory), kind, r.GetString("instrument", null));
            else
            {
                var tmp = Path.Combine(Path.GetTempPath(), "uf_trace_" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(tmp, tv.ToJson(false));
                try { t = TraceData.Load(tmp, kind); } finally { File.Delete(tmp); }
            }
            t.Kind = kind;
            if (r.Has("material")) t.Material = r.GetString("material");
            var fcsv = r.GetString("fundamental");
            if (fcsv != null)
            {
                Pulses.PulseSpec.LoadCsv(Abs(fcsv, baseDirectory), out var wl, out var inten, out _, r.GetString("wavelengthUnit", "nm"));
                t.FundamentalOmega = wl.Select(l => 2 * Math.PI * Numerics.PhysicalConstants.SpeedOfLightMPerS / l).ToArray();
                t.FundamentalIntensity = inten.Select((v, i) => v * wl[i] * wl[i]).ToArray();
            }
            var res = Retrievals.Run(method, t, r.GetObject("options"));
            var o = res.ToJson();
            o.Set("pulse", res.ToPulseJson(r.GetDouble("energyJ", 1e-9)));
            var outDir = r.GetString("output");
            if (outDir != null)
            {
                var d = Abs(outDir, baseDirectory); Directory.CreateDirectory(d);
                res.WriteSpectrumCsv(Path.Combine(d, "retrieved_spectrum.csv"));
                File.WriteAllText(Path.Combine(d, "retrieval.json"), o.ToJson(true));
                var arrays = new List<NdArray>
                {
                    new NdArray("time_s", res.Time), new NdArray("field_re", res.Field.Select(z => z.Real).ToArray()), new NdArray("field_im", res.Field.Select(z => z.Imaginary).ToArray()),
                    new NdArray("omega_rad_s", res.Omega), new NdArray("spectrum_re", res.Spectrum.Select(z => z.Real).ToArray()), new NdArray("spectrum_im", res.Spectrum.Select(z => z.Imaginary).ToArray()),
                    new NdArray("error_history", res.ErrorHistory ?? new double[0]),
                    new NdArray("measured_trace", t.Trace, t.RowCount, t.ColumnCount), new NdArray("retrieved_trace", res.RetrievedTrace, t.RowCount, t.ColumnCount),
                    new NdArray("rows", t.Rows), new NdArray("omega_columns_rad_s", t.Omega)
                };
                NpzWriter.Write(Path.Combine(d, "retrieval.npz"), arrays);
            }
            return o;
        }

        // ------------------------------------------------------------------------------------------ materials

        public static JsonObject MaterialsList()
        {
            var user = UserMaterialsFolder();
            var userPages = Directory.Exists(user) ? Directory.GetFiles(user, "*.yml").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray() : new string[0];
            return new JsonObject()
                .Set("builtIn", JsonValue.FromArray(MaterialLibrary.BuiltInNames))
                .Set("gases", JsonValue.FromArray(GasData.Table.Keys.Concat(new[] { "Air" })))
                .Set("molecularGases", JsonValue.FromArray(GasData.Table.Values.Where(g => g.Molecular).Select(g => g.Species)))
                .Set("birefringent", JsonValue.FromArray(Optics.BirefringentMaterials.Names))
                .Set("refractiveIndexInfoBundled", JsonValue.FromArray(RefractiveIndexInfoPage.Bundled))
                .Set("refractiveIndexInfoUser", JsonValue.FromArray(userPages))
                .Set("userFolder", user);
        }

        public static JsonObject MaterialsShow(string name, string baseDirectory)
        {
            var o = new JsonObject().Set("name", name);
            Material m = null;
            try { m = MaterialLibrary.BuiltIn(name); } catch (Models.ModelException) { }
            if (m == null)
            {
                var page = RefractiveIndexInfoPage.Load(name, baseDirectory);
                m = new Material { Name = name, Index = new RefractiveIndexInfoIndex(page) };
                o.Set("references", page.References).Set("comments", page.Comments);
            }
            o.Set("index", m.Index.Description).Set("n2", m.N2).Set("isGas", m.IsGas);
            if (m.Rotational != null) o.Set("rotationalRaman", m.Rotational.Description).Set("rotationalAdiabaticN2", m.Rotational.AdiabaticN2);
            var table = new JsonArray();
            foreach (var l in new[] { 400e-9, 515e-9, 800e-9, 1030e-9, 1550e-9, 2000e-9 })
            {
                double w = 2 * Math.PI * Numerics.PhysicalConstants.SpeedOfLightMPerS / l;
                double n = m.IndexAt(w), ng = m.GroupIndexAt(w);
                double gvd = new Physics.BulkTerm(m, 1.0).DispersionAt(w).GddS2;
                table.Add(new JsonObject().Set("wavelengthM", l).Set("n", n).Set("groupIndex", ng).Set("gvdFs2PerMm", gvd * 1e30 * 1e-3));
            }
            return o.Set("table", table);
        }

        /// <summary>The user material folder ($ULTRAFAST_HOME/materials, default ~/.ultrafast/materials): refractiveindex.info pages
        /// copied there by "ultrafast materials import" are found by name.</summary>
        public static string UserMaterialsFolder()
        {
            string home = Environment.GetEnvironmentVariable("ULTRAFAST_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ultrafast");
            return Path.Combine(home, "materials");
        }

        /// <summary>{"command": "materials.import", "file": "page.yml", "name": "MyGlass"} -> copies the page into the user folder and
        /// returns the material definition to paste into a system file.</summary>
        public static JsonObject MaterialsImport(JsonObject r, string baseDirectory)
        {
            string file = Abs(r.GetRequiredString("file", "materials.import"), baseDirectory);
            var page = RefractiveIndexInfoPage.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));   // validates
            string name = r.GetString("name", Path.GetFileNameWithoutExtension(file));
            var dir = UserMaterialsFolder(); Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, name + ".yml");
            File.Copy(file, dest, true);
            var idx = new JsonObject().Set("kind", "refractiveindex").Set("page", name);
            var def = new JsonObject().Set("index", idx);
            var range = page.Range;
            return new JsonObject().Set("name", name).Set("copiedTo", dest).Set("material", def)
                .Set("wavelengthRangeUm", range != null ? JsonValue.FromArray(range) : JsonValue.Null).Set("isGas", page.IsGas).Set("hasK", page.HasK).Set("references", page.References);
        }
    }
}
