// CatalogueTests.cs -- the material catalogue (WP3): file lint, published reference values, user catalogues, run notes.
//
// The values themselves are pinned in CatalogueSnapshotTests. Here: every catalogue file has the required structure (source, status, units in
// key names), the verified index sets reproduce published indices, a user file replaces a built-in quantity with a notice, and a run lists
// the approximate / unverified catalogue values it used (and nothing it did not use or that the user overrode).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UltrafastEngine.Materials;
using UltrafastEngine.Serialization;
using UltrafastEngine.Models;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Material catalogue: values pinned, provenance, user catalogues")]
    public class CatalogueTests
    {
        private static readonly Regex UnitSuffix = new Regex(@"(S|M|Ev|J|K|Pa|C|M2|M3|M2PerW|PerCm|PerUm2|Um2|Um|A3|Percent|Ppm|PerBar273K|Bar|Cm2|PerK|WPerMK|PerPpm|JPerMolK|KgPerMol|GPerMol|GPerMolPerPpm|PerGPerMolFactor|PerV)$");
        private static readonly HashSet<string> Dimensionless = new HashSet<string>
        { "a", "b", "c0", "value", "fa", "fb", "fc", "orbitalL", "zEff", "coefficients", "peakIntensities", "relativeUncertainty", "nuclearSpinWeightEven", "nuclearSpinWeightOdd",
          "anisotropicFraction", "effectiveMassRatio", "borzsonyiCoefficients", "stressCr", "stressCphi", "correctionFactor", "polynomialCoefficients", "a", "b", "N2", "O2", "Ar" };

        private static void Lint(string where, JsonValue v, List<string> bad, bool unitParent = false)
        {
            if (v.Kind == JsonKind.Array)
            {
                int i = 0;
                foreach (var item in v.AsArray()) { if (item.Kind == JsonKind.Object) Lint(where + "[" + i + "]", item, bad); i++; }
            }
            else if (v.Kind == JsonKind.Object)
                foreach (var kv in v.AsObject())
                {
                    bool numeric = kv.Value.Kind == JsonKind.Number || (kv.Value.Kind == JsonKind.Array && kv.Value.AsArray().Any() && kv.Value.AsArray().All(x => x.Kind == JsonKind.Number));
                    bool unit = UnitSuffix.IsMatch(kv.Key);
                    // the children of an object whose own key carries the unit (dPmPerV: {d22: ...}) are exempt
                    if (numeric && !unitParent && !Dimensionless.Contains(kv.Key) && !unit)
                        bad.Add(where + "." + kv.Key + ": a number needs its unit in the key name (or belongs in the dimensionless list of this test)");
                    Lint(where + "." + kv.Key, kv.Value, bad, unit && kv.Value.Kind == JsonKind.Object);
                }
        }

        [Test(Category = "materials")]
        public void CatalogueFiles_HaveSourceStatusAndUnitsInTheirKeys()
        {
            var bad = new List<string>(Catalogue.Problems);
            int files = 0;
            foreach (var f in Catalogue.EmbeddedFiles())
            {
                files++;
                var o = JsonValue.Parse(f.Value).AsObject();
                var qs = o.GetObject("quantities");
                foreach (var q in qs)
                {
                    var qo = q.Value.AsObject();
                    if (qo.Has("ref")) continue;
                    if (qo.GetString("source", "").Trim().Length < 8) bad.Add(f.Key + ": " + q.Key + " has no real source");
                    var st = qo.GetString("status", "");
                    if (st != "verified" && st != "approximate" && st != "unverified") bad.Add(f.Key + ": " + q.Key + " status '" + st + "'");
                    if (st != "verified" && !qo.Has("ref") && !qo.Has("relativeUncertainty") && !qo.Has("uncertainty") && q.Key != "composition")
                        bad.Add(f.Key + ": " + q.Key + " is " + st + " and states no uncertainty");
                    Lint(f.Key + ":" + q.Key, qo, bad);
                }
            }
            Assert.True(files >= 40, "catalogue files found: " + files);
            Assert.True(bad.Count == 0, bad.Count + " problems:\n" + string.Join("\n", bad.Take(60)));
        }

        [Test(Category = "materials")]
        public void EveryBuiltInName_ResolvesToACatalogueEntry_AndBuilds()
        {
            foreach (var name in MaterialLibrary.BuiltInNames)
            {
                Assert.True(MaterialLibrary.IsBuiltIn(name), name);
                var m = MaterialLibrary.BuiltIn(name);
                Assert.True(m.Used.ContainsKey("index"), name + " does not record its catalogue index");
            }
            Assert.Equal("FusedSilica", MaterialLibrary.Canonical("f_silica"), "alias");
            Assert.Equal("Ar", MaterialLibrary.Canonical("Argon"), "gas alias");
            Assert.Equal("Air", MaterialLibrary.Canonical(""), "empty name is air");
        }

        [Test(Category = "materials")]
        public void VerifiedDispersionSets_RecordTheirCheck()
        {
            foreach (var e in Catalogue.Entries())
            {
                var q = e.TryQ("index");
                if (q == null || q.Status != CatalogueStatus.Verified) continue;
                string model = q.Text("model");
                if (model == "constant" || model == "ciddor") continue;
                Assert.True(q.Text("verification", "").Length > 0, e.Id + ": a verified index records the check against its source page");
                Assert.True(q.Text("verification").StartsWith("agrees"), e.Id + ": " + q.Text("verification"));
            }
        }

        [Test(Category = "materials", Reference = "Malitson, JOSA 55, 1205 (1965): n_d = 1.45846 at 587.56 nm; Schott N-BK7 data sheet: n_d = 1.5168 at 587.56 nm; Dodge 1984 MgF2 n_o = 1.3777 at 589.3 nm", Tolerance = "2e-5 / 1e-4 / 1e-4")]
        public void PublishedIndices_AreReproduced()
        {
            double N(string id, double um) { var n = new double[1]; CatalogueModels.Index(Catalogue.Get(id, "solid").Q("index")).Evaluate(new[] { 2 * Math.PI * 2.99792458e8 / (um * 1e-6) }, n); return n[0]; }
            Assert.Close(1.45846, N("FusedSilica", 0.58756), 2e-5, 0, "fused silica at the helium d line");
            Assert.Close(1.5168, N("N-BK7", 0.58756), 1e-4, 0, "N-BK7 n_d");
            Assert.Close(1.3777, N("MgF2", 0.5893), 1e-4, 0, "MgF2 ordinary at the sodium D line");
        }

        [Test(Category = "materials", Reference = "Schlarb and Sugg, Phys. Status Solidi B 182, K91 (1994): measured n of TGG at 20 C (refractiveindex.info main/Tb3Ga5O12/Schlarb)", Tolerance = "1e-3")]
        public void Tgg_Dispersion_FollowsTheMeasuredTable()
        {
            // the nine measured points (um, n relative to air); the fit's largest deviation is 6.6e-4. The previous two-point fit was 0.0106 away at 1.08 um.
            double[,] table = { { 0.40466, 2.0178 }, { 0.43583, 2.0057 }, { 0.54607, 1.9765 }, { 0.63300, 1.9656 }, { 1.07981, 1.9434 }, { 1.08445, 1.9432 }, { 1.14091, 1.9420 }, { 1.15228, 1.9421 }, { 1.17668, 1.9415 } };
            var ix = CatalogueModels.Index(Catalogue.Get("TGG", "solid").Q("index"));
            for (int i = 0; i < table.GetLength(0); i++)
            {
                var n = new double[1];
                ix.Evaluate(new[] { 2 * Math.PI * 2.99792458e8 / (table[i, 0] * 1e-6) }, n);
                Assert.Close(table[i, 1], n[0], 1e-3, 0, "TGG at " + table[i, 0] + " um");
            }
            Assert.True(Catalogue.Get("TGG", "solid").Q("index").Status == CatalogueStatus.Approximate, "a fit is approximate");
        }

        [Test(Category = "materials")]
        public void UserCatalogue_ReplacesAQuantityWithANotice_AndCanAddAMaterial()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-cat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "FusedSilica.json"), @"{ // only the quantity that changes
                  ""id"": ""FusedSilica"",
                  ""quantities"": { ""n2"": { ""valueM2PerW"": 2.19e-20, ""source"": ""my own Z-scan, 2026"", ""status"": ""verified"" } } }");
                File.WriteAllText(Path.Combine(dir, "MyGlass.json"), @"{ ""id"": ""MyGlass"", ""kind"": ""solid"", ""listOrder"": 900, ""quantities"": {
                  ""index"": { ""model"": ""constant"", ""value"": 1.6, ""source"": ""vendor data sheet"", ""status"": ""verified"" },
                  ""n2"": { ""valueM2PerW"": 5e-20, ""source"": ""vendor data sheet"", ""status"": ""approximate"", ""relativeUncertainty"": 0.5 } } }");
                Catalogue.Reload(dir);
                var m = MaterialLibrary.BuiltIn("FusedSilica");
                Assert.Close(2.19e-20, m.N2, 1e-15, 0, "user n2");
                Assert.True(m.Used["n2"].IsUser, "the quantity knows it comes from a user file");
                Assert.Close(0.18, m.RamanFraction, 1e-15, 0, "the other quantities stay the built-in ones");
                Assert.True(Catalogue.Notices.Any(n => n.Contains("FusedSilica.n2")), "a notice names the replaced quantity");
                Assert.True(MaterialLibrary.IsBuiltIn("MyGlass"), "a new material from a user file");
                Assert.Close(1.6, MaterialLibrary.BuiltIn("MyGlass").IndexAt(2 * Math.PI * 2.99792458e8 / 800e-9), 1e-12, 0, "new material index");
            }
            finally { Catalogue.Reload(null); Directory.Delete(dir, true); }
            Assert.Close(2.6e-20, MaterialLibrary.BuiltIn("FusedSilica").N2, 1e-15, 0, "back to the built-in value");
            Assert.True(!MaterialLibrary.IsBuiltIn("MyGlass"), "the user material is gone after the reload");
        }

        private static string Nl(string material, JsonObject extra = null)
        {
            var sys = Sys(O("id", "nl", "type", "bulk", "material", material, "lengthM", 0.01, "beam", O("kind", "constant", "areaM2", 1e-8)));
            var ex = extra ?? O();
            ex.Set("physics", O("raman", true));
            var res = Go(sys, Spec(Gauss(800e-9, 60e-15, 1e-7), ex));
            return string.Join("\n", res.Notes) + "\n" + res.SummaryJson().GetArray("unverifiedInputs").ToJson(false);
        }

        [Test(Category = "materials")]
        public void Run_ListsTheApproximateValuesItUsed_AndNotTheOnesTheUserOverrode()
        {
            string text = Nl("FusedSilica");
            Assert.True(text.Contains("APPROXIMATE catalogue value used: FusedSilica.n2"), "n2 of fused silica is approximate and used");
            Assert.True(text.Contains("FusedSilica.raman"), "the Raman response is used");
            Assert.True(!text.Contains("FusedSilica.index"), "the index is verified and is not listed");
            string over = Nl("FusedSilica", O("overrides", A(O("target", "material:FusedSilica", "property", "n2", "value", 2.2e-20))));
            Assert.True(!over.Contains("FusedSilica.n2"), "an overridden n2 is not reported");
            Assert.True(over.Contains("FusedSilica.raman"), "the other values still are");
            string gas = Nl("Ar");
            Assert.True(gas.Contains("Ar.n2"), "the electronic n2 of argon");
            Assert.True(!gas.Contains("Ar.rotation"), "an atomic gas has no rotational Raman");
            string air = Nl("Air");
            Assert.True(air.Contains("Air.dephasing.N2") && air.Contains("Air.rotation.O2"), "air reports the rotational data of N2 and O2");
        }

        [Test(Category = "materials")]
        public void LinearRun_ReportsOnlyTheDispersionItUsed()
        {
            var sys = Sys(O("id", "g", "type", "bulk", "material", "YVO4", "lengthM", 0.01));
            var res = Go(sys, Spec(Gauss(800e-9, 60e-15, 1e-7), O("physics", O("nonlinear", false))));
            var text = string.Join("\n", res.Notes);
            Assert.True(text.Contains("YVO4.index"), "an unverified vendor index is listed");
            Assert.True(!text.Contains("YVO4.n2"), "n2 is not used in a linear run");
        }

        // ---------------------------------------------------------------------------------------------- gain media, crystals, Ciddor, strict mode

        private static string Notes(UltrafastEngine.Results.RunResult r) => string.Join("\n", r.Notes) + "\n" + r.SummaryJson().GetArray("unverifiedInputs").ToJson(false);

        [Test(Category = "materials", Reference = "design: the generic defaults live in the catalogue with a status, and a run that uses one says so", Tolerance = "exact")]
        public void UserGainMedium_ThermalDefaults_AreCatalogueValues_ReportedUnlessBothAreGiven()
        {
            var reg = new UltrafastEngine.Models.ModelRegistry();
            JsonObject Def(params object[] extra)
            {
                var o = O("lifetimeS", 1e-3, "dopingDensityM3", 1e26, "emission", O("kind", "gaussians", "bands", A(O("wavelengthM", 1030e-9, "sigmaM2", 2e-24, "fwhmM", 10e-9))));
                for (int i = 0; i < extra.Length; i += 2) o.Set((string)extra[i], extra[i + 1] is double d ? (JsonValue)d : (JsonValue)(string)extra[i + 1]);
                return o;
            }
            var g = UltrafastEngine.Gain.GainMedium.FromJson(Def(), reg, "test");
            Assert.Close(10, g.ThermalConductivity, 0, 0, "default conductivity, from the catalogue");
            Assert.Close(7.8e-6, g.DnDT, 1e-12, 0, "default dn/dT, from the catalogue");
            Assert.True(g.Used.ContainsKey("thermal") && g.Used["thermal"].Path == "defaults:gainMedium.thermal" && g.Used["thermal"].Status == UltrafastEngine.Materials.CatalogueStatus.Unverified, "the run reports the default as unverified");
            var one = UltrafastEngine.Gain.GainMedium.FromJson(Def("thermalConductivity", 5.0), reg, "test");
            Assert.True(one.Used.ContainsKey("thermal"), "one value given: the other is still a default");
            var both = UltrafastEngine.Gain.GainMedium.FromJson(Def("thermalConductivity", 5.0, "dnDT", 1e-5), reg, "test");
            Assert.True(!both.Used.ContainsKey("thermal") && both.ThermalConductivity == 5.0 && both.DnDT == 1e-5, "both given: nothing is reported");
        }

        [Test(Category = "materials")]
        public void StrictMaterials_AllowList_AcceptsNamedValuesOnly()
        {
            string Refusal(string allow)
            {
                try { StrictRun("FusedSilica", O("strictMaterialsAllow", A(allow))); return ""; }
                catch (ModelException ex) { return ex.Message; }
            }
            string msg = Refusal("FusedSilica.n2");
            Assert.True(!msg.Contains("FusedSilica.n2 ") && !msg.Contains("[FusedSilica.n2]"), "the allowed value is no longer listed: " + msg);
            string all = Refusal("FusedSilica");
            Assert.True(!all.Contains("[FusedSilica."), "an entry name accepts all its quantities: " + all);
            Assert.True(Refusal("BaF2").Contains("FusedSilica.n2"), "an unrelated entry accepts nothing here");
        }

        [Test(Category = "materials")]
        public void SolidIonization_PartialOverride_NamesWhatTheRunGaveAndWhatCameFromTheCatalogue()
        {
            var sys = Sys(O("id", "nl", "type", "bulk", "material", "FusedSilica", "lengthM", 1e-4, "beam", O("kind", "constant", "areaM2", 1e-8), "ionization", O("model", "keldysh", "effectiveMass", 0.5)));
            var notes = string.Join("\n", Go(sys, Spec(Gauss(800e-9, 30e-15, 2e-9), O())).Notes);
            Assert.True(notes.Contains("the run gives effectiveMass") && notes.Contains("catalogue values"), "the note names the overridden field: " + notes);
        }

        [Test(Category = "materials")]
        public void GainMedium_ListsItsCatalogueValues_AndNotTheOnesTheElementOverrides()
        {
            const double lam = 1030e-9;
            string Run(JsonObject extra)
            {
                var el = O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.003, "inversion", O("fraction", 0.25), "spm", false, "modeRadiusM", 1e-3);
                if (extra != null) foreach (var kv in extra) el.Set(kv.Key, kv.Value);
                return Notes(Go(Sys(el), Spec(Gauss(lam, 10e-12, 1e-9))));
            }
            string t = Run(null);
            Assert.True(t.Contains("Yb:YAG.emission"), "the emission band is used");
            Assert.True(t.Contains("Yb:YAG.lifetime"), "the lifetime is used");
            Assert.True(t.Contains("Yb:YAG.doping"), "the doping density is used");
            Assert.True(!t.Contains("Yb:YAG.thermal"), "no pump, no thermal lens: the thermal data are not used");
            string o = Run(O("dopingDensityM3", 1e26));
            Assert.True(!o.Contains("Yb:YAG.doping"), "an element that gives its own doping does not use the catalogue's");
            var user = UltrafastEngine.Gain.GainMedium.FromJson(O("base", "Yb:YAG", "lifetimeS", 1e-3), new UltrafastEngine.Models.ModelRegistry(), "test");
            Assert.True(!user.Used.ContainsKey("lifetime") && user.Used.ContainsKey("emission"), "a medium built from a base keeps the quantities it did not change");
        }

        [Test(Category = "materials")]
        public void Crystal_ListsItsUnverifiedValues_AndVerifiedIndicesStayQuiet()
        {
            string Run(JsonObject extra)
            {
                var el = O("id", "bbo", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "interaction", O("process", "shg", "type", "I"),
                    "overlap", O("model", "planeWave", "radiusM", 100e-6), "chi3", false, "output", "sh");
                if (extra != null) foreach (var kv in extra) el.Set(kv.Key, kv.Value);
                return Notes(Go(Sys(el), Spec(Gauss(1030e-9, 3e-12, 1e-9), O("physics", O("selfSteepening", false)))));
            }
            string t = Run(null);
            Assert.True(t.Contains("BBO.d"), "the d tensor of BBO is unverified and used");
            Assert.True(!t.Contains("BBO.indexX") && !t.Contains("BBO.indexZ"), "the indices are verified against the database pages");
            Assert.True(!Run(O("deffPmPerV", 2.0)).Contains("BBO.d "), "a given deff replaces the catalogue's tensor");
            Assert.True(Run(null).Contains("BBO.n2"), "n2 of BBO is listed");
            Assert.True(Catalogue.Get("LBO", "crystal").Q("indexY").Status == CatalogueStatus.Unverified, "LBO (Kato 1994) has no database page and is not claimed verified");
        }

        [Test(Category = "materials", Reference = "Ciddor, Appl. Opt. 35, 1566 (1996) as published on refractiveindex.info (other/air/Ciddor: standard air, 15 C, 101.325 kPa, dry, 450 ppm CO2): n - 1 = 0.05792105 / (238.0185 - s^2) + 0.00167917 / (57.362 - s^2)", Tolerance = "1e-12")]
        public void CiddorAir_AtStandardConditions_EqualsTheDatabaseFormula()
        {
            var air = new CiddorAirIndex(15, 101325, 0, 450);
            foreach (double um in new[] { 0.3, 0.5, 0.6328, 0.8, 1.03, 1.55, 1.69 })
            {
                var n = new double[1]; air.Evaluate(new[] { 2 * Math.PI * 2.99792458e8 / (um * 1e-6) }, n);
                double s2 = 1 / (um * um);
                Assert.Close(1 + 0.05792105 / (238.0185 - s2) + 0.00167917 / (57.362 - s2), n[0], 1e-12, 0, "n of standard air at " + um + " um");
            }
            var q = Catalogue.Get("Air", "gas").Q("standardAirDispersion");
            Assert.Close(0.05792105, q.Number("k1PerUm2") * 1e-8, 1e-12, 0, "k1 equals the page's first coefficient");
            Assert.Close(0.00167917, q.Number("k3PerUm2") * 1e-8, 1e-12, 0, "k3 equals the page's third coefficient");
        }

        private static string StrictRun(string material, JsonObject physics, JsonObject extra = null)
        {
            var sys = Sys(O("id", "nl", "type", "bulk", "material", material, "lengthM", 0.01, "beam", O("kind", "constant", "areaM2", 1e-8)));
            var ex = extra ?? O();
            var ph = physics ?? O(); ph.Set("strictMaterials", true); ex.Set("physics", ph);
            return Notes(Go(sys, Spec(Gauss(800e-9, 60e-15, 1e-7), ex)));
        }

        [Test(Category = "materials")]
        public void StrictMaterials_RefusesUnverifiedValues_ListsThem_AndAcceptsOverridesAndVerifiedOnes()
        {
            string msg = null;
            try { StrictRun("FusedSilica", O()); } catch (ModelException ex) { msg = ex.Message; }
            Assert.True(msg != null, "n2 of fused silica is approximate: the strict run is refused");
            Assert.True(msg.Contains("FusedSilica.n2") && msg.Contains("approximate"), "the message names the value and its status: " + msg);
            Assert.True(msg.Contains("strictMaterials"), "and says how to switch it off");
            // overridden / disabled values are not counted: n2 overridden, Raman switched off, ionisation off -> only the verified index is left
            StrictRun("FusedSilica", O("raman", false), O("overrides", A(O("target", "material:FusedSilica", "property", "n2", "value", 2.2e-20))));
            // a linear run on a verified index
            var lin = Sys(O("id", "g", "type", "bulk", "material", "N-BK7", "lengthM", 0.01));
            Go(lin, Spec(Gauss(800e-9, 60e-15, 1e-7), O("physics", O("nonlinear", false, "strictMaterials", true))));
            Assert.Throws<UltrafastEngine.Models.ModelException>(() => Go(Sys(O("id", "g", "type", "bulk", "material", "YVO4", "lengthM", 0.01)),
                Spec(Gauss(800e-9, 60e-15, 1e-7), O("physics", O("nonlinear", false, "strictMaterials", true)))), "the vendor Sellmeier of YVO4 is unverified");
            // the environment variable does the same without touching the run spec
            Environment.SetEnvironmentVariable("ULTRAFAST_STRICT_MATERIALS", "1");
            try
            {
                Assert.Throws<UltrafastEngine.Models.ModelException>(() => Go(Sys(O("id", "g", "type", "bulk", "material", "YVO4", "lengthM", 0.01)),
                    Spec(Gauss(800e-9, 60e-15, 1e-7), O("physics", O("nonlinear", false)))), "ULTRAFAST_STRICT_MATERIALS=1");
            }
            finally { Environment.SetEnvironmentVariable("ULTRAFAST_STRICT_MATERIALS", null); }
        }

        [Test(Category = "materials")]
        public void StrictMaterials_AcceptsAUserValueMarkedVerified()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uf-cat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "YVO4.json"), @"{ ""id"": ""YVO4"", ""quantities"": {
                  ""index"": { ""model"": ""vendorSellmeier"", ""referenceMedium"": ""air"", ""a"": 3.778790, ""numeratorUm2"": 0.070479, ""poleUm2"": 0.045731, ""irTermPerUm2"": 0.009701,
                              ""source"": ""Shi et al., J. Synthetic Cryst. 30, 85 (2001), 20 C"", ""status"": ""verified"" } } }");
                Catalogue.Reload(dir);
                Go(Sys(O("id", "g", "type", "bulk", "material", "YVO4", "lengthM", 0.01)), Spec(Gauss(800e-9, 60e-15, 1e-7), O("physics", O("nonlinear", false, "strictMaterials", true))));
            }
            finally { Catalogue.Reload(null); Directory.Delete(dir, true); }
        }
    }
}
