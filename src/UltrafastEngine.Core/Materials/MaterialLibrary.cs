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
    /// sensible defaults, not reference data: override n2 / Raman for quantitative work (docs/materials.md
    /// lists the sources and their spread).
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "F_SILICA", "FusedSilica" }, { "SILICA", "FusedSilica" }, { "FUSED_SILICA", "FusedSilica" }, { "FS", "FusedSilica" }, { "SIO2", "FusedSilica" }, { "UVFS", "FusedSilica" },
            { "BK7", "N-BK7" }, { "NBK7", "N-BK7" },
            { "CAF2", "CaF2" }, { "SAPPHIRE", "Sapphire" }, { "AL2O3", "Sapphire" },
            { "YAG", "YAG" }, { "SF11", "N-SF11" }, { "NSF11", "N-SF11" }, { "MGF2", "MgF2" },
            { "QUARTZ", "Quartz" }, { "CRYSTALQUARTZ", "Quartz" }, { "SIO2_CRYSTAL", "Quartz" }, { "QUARTZ-O", "Quartz" }, { "MGF2-O", "MgF2" }, { "SAPPHIRE-O", "Sapphire" },
            { "CALCITE-O", "Calcite" }, { "CACO3", "Calcite" }, { "YVO4-O", "YVO4" }, { "ABBO", "aBBO" }, { "ALPHA-BBO", "aBBO" }, { "ABBO-O", "aBBO" }, { "A-BBO", "aBBO" },
            { "AIR", "Air" }, { "VACUUM", "Vacuum" }, { "", "Air" }, { "OXYGEN", "O2" }, { "HYDROGEN", "H2" }, { "CARBONDIOXIDE", "CO2" },
            { "TA2O5", "Ta2O5" }, { "TIO2", "TiO2" }, { "HFO2", "HfO2" }, { "NB2O5", "Nb2O5" }, { "AL2O3_FILM", "Al2O3_film" }, { "SIO2_FILM", "SiO2_film" },
            { "HELIUM", "He" }, { "NEON", "Ne" }, { "ARGON", "Ar" }, { "KRYPTON", "Kr" }, { "XENON", "Xe" }, { "NITROGEN", "N2" }
        };

        public static IReadOnlyList<string> BuiltInNames => new[] { "Vacuum", "Air", "FusedSilica", "N-BK7", "CaF2", "Sapphire", "YAG", "N-SF11", "MgF2", "He", "Ne", "Ar", "Kr", "Xe", "N2", "O2", "CO2", "H2",
            "Quartz", "Quartz-e", "MgF2-e", "Sapphire-e", "Calcite", "Calcite-e", "YVO4", "YVO4-e", "aBBO", "aBBO-e", "TGG",
            "Ta2O5", "TiO2", "HfO2", "Nb2O5", "Al2O3_film", "SiO2_film" };

        public static string Canonical(string name) => name != null && Aliases.TryGetValue(name.Trim(), out var c) ? c : name?.Trim();

        public static bool IsBuiltIn(string name) => BuiltInNames.Contains(Canonical(name), StringComparer.OrdinalIgnoreCase);

        /// <summary>Approximate nonlinear indices of gases at 1 bar, 273 K, ~800 nm (m^2/W); scaled with density.
        /// Values follow the commonly used compilation (Lehmeier 1985 / Borzsonyi 2010 / Wahlstrand 2012) and
        /// vary by ~20 % between sources.</summary>
        public static readonly Dictionary<string, double> GasN2PerBar = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // electronic (instantaneous) part; molecular gases add their rotational Raman response (GasData, docs/reference/gases.md)
            { "He", 3.5e-25 }, { "Ne", 7.5e-25 }, { "Ar", 1.0e-23 }, { "Kr", 2.8e-23 }, { "Xe", 8.0e-23 }, { "N2", 8.0e-24 }, { "O2", 1.03e-23 }, { "CO2", 1.4e-23 }, { "H2", 6.9e-24 }, { "Air", 8.5e-24 }
        };

        /// <summary>A built-in material by name (or alias, e.g. Zemax's "F_SILICA"). Gases at 1 bar / 293 K.</summary>
        public static Material BuiltIn(string name, double gasPressureBar = 1.0, double gasTemperatureK = 293.15)
        {
            string c = Canonical(name);
            var air = new CiddorAirIndex();
            Material m;
            switch (c?.ToLowerInvariant())
            {
                case "vacuum":
                    m = new Material { Name = "Vacuum", Index = new ConstantIndex(1.0) };
                    break;
                case "air":
                    m = new Material { Name = "Air", Index = air, IsGas = true, N2 = GasN2PerBar["Air"] * 1.01325 * 273.0 / 293.15, Notes = "Ciddor 1996 at 20 C, 101325 Pa, 0 % RH, 450 ppm CO2; electronic n2 + rotational Raman of N2 and O2",
                        Rotational = RotationalRamanResponse.For(GasData.AirComposition, 1.01325, 293.15), GasSpecies = "Air", GasPressureBar = 1.01325, GasTemperatureK = 293.15 };
                    break;
                case "fusedsilica":
                    m = Glass("FusedSilica", new SellmeierIndex(new[] { 0.6961663, 0.4079426, 0.8974794 }, new[] { 0.0684043 * 0.0684043, 0.1162414 * 0.1162414, 9.896161 * 9.896161 }, 1.0, "Malitson 1965"),
                        air, 2.6e-20, 0.18, new DampedOscillatorResponse(12.2e-15, 32e-15));
                    break;
                case "n-bk7":
                    m = Glass("N-BK7", new SellmeierIndex(new[] { 1.03961212, 0.231792344, 1.01046945 }, new[] { 0.00600069867, 0.0200179144, 103.560653 }, 1.0, "Schott"), air, 3.4e-20, 0.0, null);
                    break;
                case "caf2":
                    m = Glass("CaF2", new SellmeierIndex(new[] { 0.5675888, 0.4710914, 3.8484723 }, new[] { 0.050263605 * 0.050263605, 0.1003909 * 0.1003909, 34.649040 * 34.649040 }, 1.0, "Malitson 1963"), air, 1.9e-20, 0.0, null);
                    break;
                case "sapphire":
                    m = Glass("Sapphire", new SellmeierIndex(new[] { 1.4313493, 0.65054713, 5.3414021 }, new[] { 0.0726631 * 0.0726631, 0.1193242 * 0.1193242, 18.028251 * 18.028251 }, 1.0, "Malitson & Dodge 1972, ordinary"), air, 3.0e-20, 0.0, null);
                    break;
                case "yag":
                    m = Glass("YAG", new SellmeierIndex(new[] { 2.282, 3.27644 }, new[] { 0.01185, 282.734 }, 1.0, "Zelmon 1998"), air, 6.2e-20, 0.0, null);
                    break;
                case "n-sf11":
                    m = Glass("N-SF11", new SellmeierIndex(new[] { 1.73759695, 0.313747346, 1.89878101 }, new[] { 0.013188707, 0.0623068142, 155.23629 }, 1.0, "Schott"), air, 1.0e-19, 0.0, null);
                    break;
                case "mgf2":
                    m = Glass("MgF2", new SellmeierIndex(new[] { 0.48755108, 0.39875031, 2.3120353 }, new[] { 0.04338408 * 0.04338408, 0.09461442 * 0.09461442, 23.793604 * 23.793604 }, 1.0, "Dodge 1984, ordinary"), air, 0.9e-20, 0.0, null);
                    break;
                // birefringent crystals (ordinary = the plain name, extraordinary = "-e"); BirefringentMaterials pairs them
                case "quartz":
                    m = Glass("Quartz", new SellmeierIndex(new[] { 1.07044083, 1.10202242 }, new[] { 1.00585997e-2, 100.0 }, 1.28604141, "Ghosh 1999, crystalline quartz, ordinary"), air, 3.0e-20, 0.0, null);
                    break;
                case "quartz-e":
                    m = Glass("Quartz-e", new SellmeierIndex(new[] { 1.09509924, 1.15662475 }, new[] { 1.02101864e-2, 100.0 }, 1.28851804, "Ghosh 1999, crystalline quartz, extraordinary"), air, 3.0e-20, 0.0, null);
                    break;
                case "mgf2-e":
                    m = Glass("MgF2-e", new SellmeierIndex(new[] { 0.41344023, 0.50497499, 2.4904862 }, new[] { 0.03684262 * 0.03684262, 0.09076162 * 0.09076162, 23.771995 * 23.771995 }, 1.0, "Dodge 1984, extraordinary"), air, 0.9e-20, 0.0, null);
                    break;
                case "sapphire-e":
                    m = Glass("Sapphire-e", new SellmeierIndex(new[] { 1.5039759, 0.55069141, 6.5927379 }, new[] { 0.0740288 * 0.0740288, 0.1216529 * 0.1216529, 20.072248 * 20.072248 }, 1.0, "Malitson & Dodge 1972, extraordinary"), air, 3.0e-20, 0.0, null);
                    break;
                case "calcite":
                    m = Glass("Calcite", new SellmeierIndex(new[] { 0.96464345, 1.82831454 }, new[] { 1.94325203e-2, 120.0 }, 1.73358749, "Ghosh 1999, ordinary"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "calcite-e":
                    m = Glass("Calcite-e", new SellmeierIndex(new[] { 0.82427830, 0.14429128 }, new[] { 1.06689543e-2, 120.0 }, 1.35859695, "Ghosh 1999, extraordinary"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "yvo4":
                    m = Glass("YVO4", new SellmeierIrIndex(3.77834 - 0.069736 / 0.04724, new[] { 0.069736 / 0.04724 }, new[] { 0.04724 }, 0.0108133, "YVO4 ordinary (common vendor Sellmeier)"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "yvo4-e":
                    m = Glass("YVO4-e", new SellmeierIrIndex(4.59905 - 0.110534 / 0.04813, new[] { 0.110534 / 0.04813 }, new[] { 0.04813 }, 0.0122676, "YVO4 extraordinary (common vendor Sellmeier)"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "abbo":
                    m = Glass("aBBO", new SellmeierIrIndex(2.67579 - 0.02099 / 0.00470, new[] { 0.02099 / 0.00470 }, new[] { 0.00470 }, 0.00528, "alpha-BBO ordinary (vendor Sellmeier)"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "abbo-e":
                    m = Glass("aBBO-e", new SellmeierIrIndex(2.31197 - 0.01184 / 0.01607, new[] { 0.01184 / 0.01607 }, new[] { 0.01607 }, 0.00400, "alpha-BBO extraordinary (vendor Sellmeier)"), air, 0.0, 0.0, null);
                    m.Notes = "n2 not set (give one if it matters)";
                    break;
                case "tgg":
                    // two-point single-pole fit (n = 1.954 at 1064 nm, 1.974 at 633 nm): approximate; override for precision work
                    m = Glass("TGG", new SellmeierIndex(new[] { 2.7769 }, new[] { 0.01656 }, 1.0, "TGG, approximate single-pole fit"), air, 0.0, 0.0, null);
                    m.Notes = "approximate dispersion (two-point fit); n2 not set (give one: the B-integral of an isolator matters)";
                    break;
                // thin-film materials (ion-beam-sputtered, approximate Cauchy fits; override for real designs)
                case "ta2o5": m = Film("Ta2O5", 2.0500, 0.0200, 0.0012); break;
                case "tio2": m = Film("TiO2", 2.2000, 0.0550, 0.0030); break;
                case "hfo2": m = Film("HfO2", 1.9500, 0.0160, 0.0); break;
                case "nb2o5": m = Film("Nb2O5", 2.1800, 0.0400, 0.0); break;
                case "al2o3_film": m = Film("Al2O3_film", 1.6200, 0.0080, 0.0); break;
                case "sio2_film": m = Film("SiO2_film", 1.4500, 0.0036, 0.0); break;
                case "he": case "ne": case "ar": case "kr": case "xe": case "n2": case "o2": case "co2": case "h2":
                    m = Gas(c, gasPressureBar, gasTemperatureK);
                    break;
                default:
                    throw new ModelException("Unknown material \"" + name + "\". Built-in materials: " + string.Join(", ", BuiltInNames) + ". Define it in the system file's \"materials\" section (e.g. as an index table).");
            }
            foreach (var p in new[] { "index", "n2", "raman" })
                if (!m.Provenance.ContainsKey(p)) m.Provenance[p] = (p == "index" ? m.Index.Description : p == "n2" ? m.N2.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W" : (m.Raman?.Description ?? "none"), ModelSource.MaterialDefault, "built-in library");
            return m;
        }

        private static Material Glass(string name, IRefractiveIndex relIndex, IRefractiveIndex air, double n2, double fR, IDelayedResponse raman) =>
            new Material { Name = name, Index = new AirReferencedIndex(relIndex, air), IndexReference = "air", N2 = n2, RamanFraction = fR, Raman = raman };

        private static Material Film(string name, double a, double b, double c) =>
            new Material { Name = name, Index = new CauchyIndex(new[] { a, b, c }), N2 = 0, Notes = "thin-film Cauchy approximation" };

        /// <summary>A gas at pressure and temperature: index (Borzsonyi fits, or dispersion formulas for O2, CO2, H2), electronic
        /// n2 scaled with density, and for molecules the rotational Raman response (docs/reference/gases.md).</summary>
        public static Material Gas(string species, double pressureBar, double temperatureK)
        {
            var g = GasData.Get(species);
            string sp = g?.Species ?? species;
            if (string.Equals(sp, "air", StringComparison.OrdinalIgnoreCase))
            {
                var am = BuiltIn("Air");
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
            return new Material
            {
                Name = sp + (pressureBar != 1.0 ? "@" + pressureBar.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + "bar" : ""),
                Index = idx, IsGas = true, N2 = n2, Rotational = rot, GasSpecies = sp, GasPressureBar = pressureBar, GasTemperatureK = temperatureK,
                Notes = "electronic n2 scaled with density from " + (GasN2PerBar.TryGetValue(sp, out var b) ? b.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) : "?") + " m^2/W at 1 bar, 273 K" + (rot != null ? "; plus rotational Raman (adiabatic n2 " + rot.AdiabaticN2.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W)" : "")
            };
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
            }
            if (o.Has("n2")) { m.N2 = o.GetDouble("n2", 0); m.Provenance["n2"] = (m.N2.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " m^2/W", source, origin); }
            if (o.TryGet("rotationalRaman", out var rr) && !rr.IsNull)
            {
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
            if (o.Has("ramanFraction")) m.RamanFraction = o.GetDouble("ramanFraction", 0);
            if (o.Has("raman"))
            {
                var rs = ModelSpec.FromJson(o["raman"], source, origin + ".raman");
                m.Raman = reg.Create<IDelayedResponse>("delayedResponse", rs);
                if (rs.Json.Has("fR")) m.RamanFraction = rs.Json.GetDouble("fR", m.RamanFraction);
                if (rs.Kind == "silica" && !o.Has("ramanFraction") && !rs.Json.Has("fR")) m.RamanFraction = 0.18;
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
