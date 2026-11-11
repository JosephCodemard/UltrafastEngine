using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Materials
{
    /// <summary>
    /// Built-in materials and the resolver that turns system-file / run-spec material definitions (and
    /// overrides) into <see cref="Material"/>s.
    ///
    /// Built-in nonlinear coefficients are typical literature values near 800 nm - 1 um and are meant as
    /// sensible defaults, not reference data: override n2 / Raman for quantitative work (docs/reference/materials.md
    /// lists the sources and their spread).
    /// </summary>
    public static class MaterialLibrary
    {
        /// <summary>Names of the built-in materials and gases, in catalogue order (the Raman models are not materials).</summary>
        public static IReadOnlyList<string> BuiltInNames => Catalogue.Entries().Where(e => e.Kind != "raman").Select(e => e.Id).ToArray();

        public static string Canonical(string name) => name == null ? null : Catalogue.Find(name) is CatalogueEntry e && e.Kind != "raman" ? e.Id : name.Trim();

        public static bool IsBuiltIn(string name) => name != null && Catalogue.Find(name) is CatalogueEntry e && e.Kind != "raman";

        /// <summary>Electronic nonlinear indices of gases at 1 bar, 273 K, ~800 nm (m^2/W); scaled with density. From the catalogue (Data/materials/gases),
        /// where each value carries its source and status.</summary>
        public static IReadOnlyDictionary<string, double> GasN2PerBar => GasData.N2PerBar;

        /// <summary>A built-in material by name (or alias, e.g. Zemax's "F_SILICA"). Gases at 1 bar / 293 K.</summary>
        public static Material BuiltIn(string name, double gasPressureBar = 1.0, double gasTemperatureK = 293.15)
        {
            var e = Catalogue.Find(name);
            if (e == null || e.Kind == "raman")
                throw new ModelException("Unknown material \"" + name + "\". Built-in materials: " + string.Join(", ", BuiltInNames) + ". Define it in the system file's \"materials\" section (e.g. as an index table), or add a catalogue file (docs/reference/materials-catalogue.md).");
            Material m;
            if (e.Kind == "gas") m = string.Equals(e.Id, "Air", StringComparison.OrdinalIgnoreCase) ? Air(e) : Gas(e.Id, gasPressureBar, gasTemperatureK);
            else m = Solid(e);
            foreach (var p in new[] { "index", "n2", "raman" })
                if (!m.Provenance.ContainsKey(p)) m.Provenance[p] = (p == "index" ? m.Index.Description : p == "n2" ? m.N2.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W" : (m.Raman?.Description ?? "none"), ModelSource.MaterialDefault, "built-in library");
            return m;
        }

        /// <summary>A solid or liquid of the catalogue: index (air-referenced where the data are), n2, delayed response, cubic anisotropy.</summary>
        private static Material Solid(CatalogueEntry e)
        {
            var qi = e.Q("index");
            var rel = CatalogueModels.Index(qi);
            var m = new Material { Name = e.Id, Notes = e.Notes };
            if (CatalogueModels.IsAirReferenced(qi)) { m.Index = new AirReferencedIndex(rel, new CiddorAirIndex()); m.IndexReference = "air"; }
            else m.Index = rel;
            m.Used["index"] = qi;
            var n2 = e.Q("n2");
            m.N2 = n2.Number("valueM2PerW"); m.Used["n2"] = n2;
            var rq = e.TryQ("raman");
            if (rq != null)
            {
                var re = Catalogue.Get(rq.Text("ref"), "raman");
                m.Raman = CatalogueModels.Raman(re);
                var own = e.TryQ("ramanFraction");
                m.RamanFraction = own != null ? own.Number("value") : CatalogueModels.RamanFractionOf(re);
                m.Used["raman"] = re.Q("response");
                var fq = own ?? re.TryQ("ramanFraction");
                if (fq != null) m.Used["ramanFraction"] = fq;
            }
            var ka = e.TryQ("kerrAnisotropy");
            if (ka != null) { m.KerrAnisotropy = ka.Number("value"); m.Used["kerrAnisotropy"] = ka; }
            return m;
        }

        /// <summary>Dry air at 20 C, 101325 Pa (Ciddor 1996): electronic n2 plus the rotational Raman response of its N2 and O2.</summary>
        private static Material Air(CatalogueEntry e)
        {
            var m = new Material { Name = "Air", Index = new CiddorAirIndex(), IsGas = true, N2 = GasN2PerBar["Air"] * 1.01325 * 273.0 / 293.15,
                Notes = "Ciddor 1996 at 20 C, 101325 Pa, 0 % RH, 450 ppm CO2; electronic n2 + rotational Raman of N2 and O2",
                Rotational = RotationalRamanResponse.For(GasData.AirComposition, 1.01325, 293.15), GasSpecies = "Air", GasPressureBar = 1.01325, GasTemperatureK = 293.15 };
            m.Used["index"] = e.Q("index"); m.Used["n2"] = e.Q("n2Electronic");
            foreach (var (sp, _) in GasData.AirComposition) UseRotation(m, sp);
            return m;
        }

        private static void UseRotation(Material m, string species)
        {
            var g = GasData.Get(species);
            if (g == null || !g.Molecular) return;
            m.Used["rotation:" + species] = g.Entry.Q("rotation");
            m.Used["dephasing:" + species] = g.Entry.Q("dephasing");
        }

        /// <summary>The Raman fraction that belongs to a built-in Raman model kind (NaN: none implied).</summary>
        public static double DefaultRamanFraction(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "silica": case "blowwood": return CatalogueModels.RamanFractionOf(Catalogue.Get("blowWood", "raman"));
                case "hollenbeck": return HollenbeckCantrellRamanResponse.DefaultFraction;
                case "linagrawal": return LinAgrawalRamanResponse.DefaultFraction;
                default: return double.NaN;
            }
        }

        /// <summary>BaF2 Kerr data (catalogue entry BaF2; UNVERIFIED typical values): n2 along [100] and cubic anisotropy sigma.</summary>
        public static double BaF2N2PerW => Catalogue.Get("BaF2", "solid").Q("n2").Number("valueM2PerW");
        public static double BaF2Sigma => Catalogue.Get("BaF2", "solid").Q("kerrAnisotropy").Number("value");

        /// <summary>Liquid CS2 nonlinearity (catalogue entry CS2; UNVERIFIED typical values): total n2 for ps pulses, delayed (orientational) share, rise and decay times.</summary>
        public static double CS2N2TotalPerW => Catalogue.Get("CS2", "solid").Q("n2").Number("valueM2PerW");
        public static double CS2OrientationalFraction => Catalogue.Get("CS2", "solid").Q("ramanFraction").Number("value");
        public static double CS2RiseS => Catalogue.Get("orientational", "raman").Q("response").Number("riseS");
        public static double CS2DecayS => Catalogue.Get("orientational", "raman").Q("response").Number("decayS");

        /// <summary>A gas at pressure and temperature: index (Borzsonyi fits, or dispersion formulas for O2, CO2, H2), electronic
        /// n2 scaled with density, and for molecules the rotational Raman response (docs/reference/gases.md).</summary>
        public static Material Gas(string species, double pressureBar, double temperatureK)
        {
            var g = GasData.Get(species);
            string sp = g?.Species ?? species;
            if (string.Equals(sp, "air", StringComparison.OrdinalIgnoreCase))
            {
                var am = BuiltIn("Air");   // keeps its catalogue quantities (Used); only the state changes below
                am.Index = new CiddorAirIndex(temperatureK - 273.15, pressureBar * 1e5, 0, 450);
                double f = pressureBar / 1.01325 * 293.15 / temperatureK;
                am.N2 *= f; am.Rotational = RotationalRamanResponse.For(GasData.AirComposition, pressureBar, temperatureK);
                am.GasPressureBar = pressureBar; am.GasTemperatureK = temperatureK; am.Name = "Air" + (pressureBar != 1.01325 ? "@" + pressureBar.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + "bar" : "");
                return am;
            }
            Models.IRefractiveIndex idx; double density;
            if (GasFormulaIndex.Knows(sp)) { var gi = new GasFormulaIndex(sp, pressureBar, temperatureK); idx = gi; density = gi.DensityFactor; }
            else { var gi = new GasIndex(sp, pressureBar, temperatureK); idx = gi; density = gi.DensityFactor; }
            double n2 = GasN2PerBar.TryGetValue(sp, out var v) ? v * density : 0.0;
            var rot = g != null && g.Molecular ? RotationalRamanResponse.For(new[] { (sp, 1.0) }, pressureBar, temperatureK) : null;
            var mat = new Material
            {
                Name = sp + (pressureBar != 1.0 ? "@" + pressureBar.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + "bar" : ""),
                Index = idx, IsGas = true, N2 = n2, Rotational = rot, GasSpecies = sp, GasPressureBar = pressureBar, GasTemperatureK = temperatureK,
                Notes = "electronic n2 scaled with density from " + (GasN2PerBar.TryGetValue(sp, out var b) ? b.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) : "?") + " m^2/W at 1 bar, 273 K" + (rot != null ? "; plus rotational Raman (adiabatic n2 " + rot.AdiabaticN2.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W)" : "")
            };
            var ge = Catalogue.Find(sp, "gas");
            if (ge != null) { mat.Used["index"] = ge.Q("index"); if (ge.Has("n2Electronic")) mat.Used["n2"] = ge.Q("n2Electronic"); UseRotation(mat, sp); }
            return mat;
        }

        /// <summary>
        /// Build a material from a JSON definition:
        /// <code>
        /// { "base": "FusedSilica",                       // optional: start from a built-in (or another defined material)
        ///   "index": {...model...}, "indexReference": "air" | "vacuum",
        ///   "n2": 2.6e-20, "ramanFraction": 0.18, "raman": {"kind":"silica"},
        ///   "attenuation": {...}, "lidt": {"fluenceJPerCm2": 1.0, "pulseDurationS": 1e-12},
        ///   "gas": {"species":"Ar","pressureBar":2.0,"temperatureK":293} }
        /// </code>
        /// </summary>
        public static Material FromJson(string name, JsonObject o, ModelRegistry reg, Func<string, Material> lookup, ModelSource source, string origin)
        {
            Material m;
            var gas = o.GetObject("gas");
            if (gas != null)
                m = Gas(gas.GetRequiredString("species", origin), gas.GetDouble("pressureBar", 1.0), gas.GetDouble("temperatureK", 293.15));
            else if (o.Has("base"))
                m = (lookup?.Invoke(o.GetString("base")) ?? BuiltIn(o.GetString("base"))).Clone();
            else if (IsBuiltIn(name) && !o.Has("index"))
                m = BuiltIn(name);
            else
                m = new Material { Index = new ConstantIndex(1.0) };
            m.Name = name;
            Apply(m, o, reg, source, origin);
            return m;
        }

        /// <summary>Apply the properties present in <paramref name="o"/> onto <paramref name="m"/> (used both for
        /// definitions and for material-targeted overrides).</summary>
        public static void Apply(Material m, JsonObject o, ModelRegistry reg, ModelSource source, string origin)
        {
            if (o.Has("index"))
            {
                var spec = ModelSpec.FromJson(o["index"], source, origin + ".index");
                var idx = reg.Create<IRefractiveIndex>("index", spec);
                if (idx is RefractiveIndexInfoIndex rii && rii.Page.HasK && !o.Has("attenuation")) m.Attenuation = rii;   // k data -> absorption
                else if (!(idx is RefractiveIndexInfoIndex) && idx is IAttenuation both && !o.Has("attenuation")) m.Attenuation = both;   // one model gives n and alpha (Kramers-Kronig consistent, e.g. gas lines)
                string reference = o.GetString("indexReference", spec.Kind == "table" ? "vacuum" : "vacuum");
                m.IndexReference = reference;
                if (string.Equals(reference, "air", StringComparison.OrdinalIgnoreCase))
                {
                    var airSpec = o.GetObject("referenceAir");
                    var air = airSpec != null ? reg.Create<IRefractiveIndex>("index", ModelSpec.FromJson(airSpec, source, origin + ".referenceAir")) : new CiddorAirIndex();
                    idx = new AirReferencedIndex(idx, air);
                }
                m.Index = idx;
                m.Provenance["index"] = (idx.Description, source, origin);
                m.Used.Remove("index");
            }
            if (o.Has("kerrAnisotropy")) { m.Used.Remove("kerrAnisotropy"); m.KerrAnisotropy = o.GetDouble("kerrAnisotropy", 0); m.Provenance["kerrAnisotropy"] = (m.KerrAnisotropy.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), source, origin); }
            if (o.Has("n2")) { m.Used.Remove("n2"); m.N2 = o.GetDouble("n2", 0); m.Provenance["n2"] = (m.N2.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W", source, origin); }
            if (o.TryGet("rotationalRaman", out var rr) && !rr.IsNull)
            {
                foreach (var k in m.Used.Keys.Where(k => k.StartsWith("rotation:") || k.StartsWith("dephasing:")).ToList()) m.Used.Remove(k);
                if (rr.Kind == JsonKind.Bool && !rr.AsBool()) m.Rotational = null;
                else if (rr.Kind == JsonKind.Object)
                {
                    var ro = rr.AsObject();
                    string sp = ro.GetString("species", m.GasSpecies ?? "N2");
                    var mix = string.Equals(sp, "air", StringComparison.OrdinalIgnoreCase) ? GasData.AirComposition : new[] { (sp, 1.0) };
                    m.Rotational = RotationalRamanResponse.For(mix, ro.GetDouble("pressureBar", double.IsNaN(m.GasPressureBar) ? 1.0 : m.GasPressureBar), ro.GetDouble("temperatureK", double.IsNaN(m.GasTemperatureK) ? 293.15 : m.GasTemperatureK), 1.0, ro.GetDouble("dephasingTimeS", double.NaN));
                }
                m.Provenance["rotationalRaman"] = (m.Rotational?.Description ?? "off", source, origin);
            }
            if (o.Has("ramanFraction")) { m.RamanFraction = o.GetDouble("ramanFraction", 0); m.Used.Remove("ramanFraction"); }
            if (o.Has("raman"))
            {
                m.Used.Remove("raman"); if (!o.Has("ramanFraction")) m.Used.Remove("ramanFraction");
                var rs = ModelSpec.FromJson(o["raman"], source, origin + ".raman");
                m.Raman = reg.Create<IDelayedResponse>("delayedResponse", rs);
                if (rs.Json.Has("fR")) m.RamanFraction = rs.Json.GetDouble("fR", m.RamanFraction);
                if (!o.Has("ramanFraction") && !rs.Json.Has("fR") && !double.IsNaN(DefaultRamanFraction(rs.Kind))) m.RamanFraction = DefaultRamanFraction(rs.Kind);
                if (m.Raman == null) m.RamanFraction = 0;
                m.Provenance["raman"] = ((m.Raman?.Description ?? "none") + ", fR = " + m.RamanFraction.ToString("G3", System.Globalization.CultureInfo.InvariantCulture), source, origin);
            }
            if (o.Has("attenuation"))
            {
                m.Attenuation = reg.Create<IAttenuation>("attenuation", ModelSpec.FromJson(o["attenuation"], source, origin + ".attenuation"));
                m.Provenance["attenuation"] = (m.Attenuation?.Description ?? "none", source, origin);
            }
            if (o.Has("lidt")) m.Lidt = LidtSpec.FromJson(o.GetObject("lidt"));
            if (o.Has("notes")) m.Notes = o.GetString("notes");
        }
    }
}
