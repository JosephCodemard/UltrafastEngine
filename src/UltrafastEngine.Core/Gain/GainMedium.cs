using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Gain
{
    /// <summary>
    /// A laser gain medium: emission and absorption cross sections sigma_e(omega), sigma_a(omega) (m^2), upper-state
    /// lifetime, dopant density, host material (dispersion, n2), thermal data (conductivity, dn/dT).
    /// Quasi-three-level media (Yb) have reabsorption at the lasing wavelength: the cross sections are linked by the
    /// McCumber relation sigma_e / sigma_a = exp((E_zl - h nu) / kT) around the zero-phonon line, so one measured
    /// spectrum (emission in the lasing band, absorption in the pump band) gives both everywhere.
    /// Gain coefficient (intensity, 1/m): g = sigma_e N2 - sigma_a (N_t - N2).
    /// </summary>
    public sealed class GainMedium
    {
        public const double H = 6.62607015e-34, Kb = 1.380649e-23, C = 299792458.0;
        public string Name { get; set; }
        public string Host { get; set; } = "YAG";
        public double LifetimeS { get; set; }
        public double DopingDensityM3 { get; set; }
        public double TemperatureK { get; set; } = 293.15;
        /// <summary>Zero-phonon line (m) for the McCumber relation; NaN = four-level (no linking).</summary>
        public double ZeroLineM { get; set; } = double.NaN;
        public double ThermalConductivity { get; set; } = 10;   // W / (m K)
        public double DnDT { get; set; } = 7.8e-6;              // 1/K
        /// <summary>Stress birefringence of a cubic host (Koechner): thermal expansion (1/K) and photo-elastic coefficients C_r, C_phi; NaN = not available.</summary>
        public double ThermalExpansionPerK { get; set; } = double.NaN;
        public double StressCr { get; set; } = double.NaN;
        public double StressCphi { get; set; } = double.NaN;
        public string References { get; set; } = "";
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Explicit band shapes: emission (lasing band) and absorption (pump band) in m^2 vs omega.</summary>
        public Func<double, double> EmissionBand { get; set; } = _ => 0;
        public Func<double, double> AbsorptionBand { get; set; } = _ => 0;
        public string EmissionDescription { get; set; } = "";
        public string AbsorptionDescription { get; set; } = "";

        private double McCumber(double omega)
        {
            if (double.IsNaN(ZeroLineM)) return double.NaN;
            double ezl = H * C / ZeroLineM, hv = H * omega / (2 * Math.PI);
            return Math.Exp((hv - ezl) / (Kb * TemperatureK));
        }

        /// <summary>Emission cross section (m^2) at angular frequency omega.</summary>
        public double SigmaE(double omega)
        {
            if (!(omega > 0)) return 0;
            double e = EmissionBand(omega), a = AbsorptionBand(omega), m = McCumber(omega);
            return double.IsNaN(m) ? e : e + (a > 0 ? a / m : 0);
        }

        /// <summary>Absorption cross section (m^2) at angular frequency omega.</summary>
        public double SigmaA(double omega)
        {
            if (!(omega > 0)) return 0;
            double e = EmissionBand(omega), a = AbsorptionBand(omega), m = McCumber(omega);
            return double.IsNaN(m) ? a : a + (e > 0 ? e * m : 0);
        }

        public static double Omega(double lambda) => 2 * Math.PI * C / lambda;

        /// <summary>Saturation fluence h nu / (sigma_e + sigma_a) (J/m^2).</summary>
        public double SaturationFluence(double omega) => H * omega / (2 * Math.PI) / Math.Max(1e-40, SigmaE(omega) + SigmaA(omega));

        /// <summary>Wavelength of the largest emission cross section in a range (m).</summary>
        public double EmissionPeakM(double loM, double hiM)
        {
            double best = loM, bv = -1;
            for (int i = 0; i <= 2000; i++) { double l = loM + (hiM - loM) * i / 2000; double v = SigmaE(Omega(l)); if (v > bv) { bv = v; best = l; } }
            return best;
        }

        public string Describe() => string.Format(CultureInfo.InvariantCulture, "{0} (host {1}, tau = {2:G4} us, N_t = {3:G4} m^-3, T = {4:G4} K{5})",
            Name, Host, LifetimeS * 1e6, DopingDensityM3, TemperatureK, double.IsNaN(ZeroLineM) ? ", four-level" : string.Format(CultureInfo.InvariantCulture, ", quasi-three-level, zero line {0:G5} nm", ZeroLineM * 1e9));

        // ------------------------------------------------------------------------------------------ library

        public static string[] Names => Catalogue.Entries("gain").Select(e => e.Id).ToArray();

        /// <summary>Sum of Gaussian bands in wavelength: (lambda0 m, peak m^2, FWHM m).</summary>
        internal static Func<double, double> Bands(params (double L0, double S, double Fwhm)[] b)
            => w => { double l = 2 * Math.PI * C / w, s = 0; foreach (var x in b) { double u = (l - x.L0) / x.Fwhm; s += x.S * Math.Exp(-4 * Math.Log(2) * u * u); } return s; };

        /// <summary>Asymmetric Gaussian in wavelength (different half widths on each side of the peak).</summary>
        internal static Func<double, double> Asym(double l0, double s, double hwhmShort, double hwhmLong)
            => w => { double l = 2 * Math.PI * C / w, h = l < l0 ? hwhmShort : hwhmLong, u = (l - l0) / h; return s * Math.Exp(-Math.Log(2) * u * u); };

        /// <summary>The catalogue quantities this medium takes its numbers from ("doping", "lifetime", "thermal", "emission", ...); a user override removes the quantity.</summary>
        public Dictionary<string, CatalogueQuantity> Used { get; } = new Dictionary<string, CatalogueQuantity>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A built-in gain medium from the catalogue (Data/materials/gain): lifetime, doping, thermal and stress data, cross-section bands, each with its source and status.</summary>
        public static GainMedium Get(string name, double temperatureK = double.NaN)
        {
            string k = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            var e = Catalogue.Find(k, "gain") ?? throw new JsonException("unknown gain medium \"" + name + "\" (built-in: " + string.Join(", ", Names) + "); define your own with an object (docs/reference/gain.md)");
            var dop = e.Q("doping"); var th = e.Q("thermal"); var em = e.Q("emission"); var ab = e.Q("absorption");
            var g = new GainMedium
            {
                Name = e.Id, Host = dop.Text("host"), LifetimeS = e.Q("lifetime").Number("lifetimeS"), DopingDensityM3 = dop.Number("dopingDensityM3"),
                ThermalConductivity = th.Number("thermalConductivityWPerMK"), DnDT = th.Number("dnDTPerK"),
                EmissionBand = Band(em), AbsorptionBand = Band(ab), EmissionDescription = em.Text("description", ""), AbsorptionDescription = ab.Text("description", ""),
                References = em.Source
            };
            foreach (var q in e.Quantities.Values) g.Used[q.Name] = q;
            var zl = e.TryQ("zeroLine"); if (zl != null) g.ZeroLineM = zl.Number("zeroLineWavelengthM");
            var st = e.TryQ("stress"); if (st != null) { g.ThermalExpansionPerK = st.Number("thermalExpansionPerK"); g.StressCr = st.Number("stressCr"); g.StressCphi = st.Number("stressCphi"); }
            if (!string.IsNullOrEmpty(e.Notes)) g.Notes.Add(e.Notes);
            if (!double.IsNaN(temperatureK)) g.TemperatureK = temperatureK;
            return g;
        }

        /// <summary>A cross-section band set of a catalogue quantity: "gaussians" (bands in wavelength) or "asymmetricGaussian"; cross sections are stored in cm^2.</summary>
        private static Func<double, double> Band(CatalogueQuantity q)
        {
            const double cm2 = 1e-4;   // cm^2 -> m^2
            switch (q.Text("shape", ""))
            {
                case "gaussians":
                    return Bands(q.Json.GetArray("bands").Select(x => x.AsObject()).Select(x => (x.GetDouble("wavelengthM", 0), x.GetDouble("sigmaCm2", 0) * cm2, x.GetDouble("fwhmM", 1e-9))).ToArray());
                case "asymmetricGaussian":
                    return Asym(q.Number("peakWavelengthM"), q.Number("sigmaCm2") * cm2, q.Number("hwhmShortM"), q.Number("hwhmLongM"));
                default: throw new JsonException("catalogue " + q.Path + ": unknown cross-section shape \"" + q.Text("shape", "") + "\"");
            }
        }

        /// <summary>
        /// A gain medium from JSON: {"base": "Yb:YAG", ...overrides} or a full definition with "emission" / "absorption"
        /// cross-section models ({"kind": "gaussians", "bands": [{"wavelengthM", "sigmaM2", "fwhmM"}]}, {"kind": "table",
        /// "wavelengthsM": [...], "values": [...]}, an expression in omega / lambda, or "@callback"), "lifetimeS",
        /// "dopingDensityM3" (or "dopingAtPercent" with "sitesPerM3"), "host", "zeroLineWavelengthM" (quasi-three-level),
        /// "temperatureK", "thermalConductivity", "dnDT".
        /// </summary>
        public static GainMedium FromJson(JsonValue v, ModelRegistry reg, string origin)
        {
            if (v.Kind == JsonKind.String) return Get(v.AsString());
            var o = v.AsObject();
            var g = o.Has("base") ? Get(o.GetString("base")) : new GainMedium { Name = o.GetString("name", "user gain medium"), References = "user definition" };
            if (o.Has("name")) g.Name = o.GetString("name");
            if (o.Has("host")) g.Host = o.GetString("host");
            if (o.Has("lifetimeS")) { g.LifetimeS = o.GetDouble("lifetimeS", g.LifetimeS); g.Used.Remove("lifetime"); }
            if (o.Has("dopingDensityM3")) { g.DopingDensityM3 = o.GetDouble("dopingDensityM3", g.DopingDensityM3); g.Used.Remove("doping"); }
            if (o.Has("dopingAtPercent")) { g.DopingDensityM3 = o.GetDouble("dopingAtPercent", 1) / 100 * o.GetDouble("sitesPerM3", 1.38e28); g.Used.Remove("doping"); }
            if (o.Has("temperatureK")) g.TemperatureK = o.GetDouble("temperatureK", g.TemperatureK);
            if (o.Has("zeroLineWavelengthM")) { g.ZeroLineM = o.GetDouble("zeroLineWavelengthM", double.NaN); g.Used.Remove("zeroLine"); }
            if (o.Has("thermalConductivity")) { g.ThermalConductivity = o.GetDouble("thermalConductivity", g.ThermalConductivity); g.Used.Remove("thermal"); }
            if (o.Has("dnDT")) { g.DnDT = o.GetDouble("dnDT", g.DnDT); g.Used.Remove("thermal"); }
            if (o.Has("thermalExpansionPerK") || o.Has("stressCr") || o.Has("stressCphi")) g.Used.Remove("stress");
            g.ThermalExpansionPerK = o.GetDouble("thermalExpansionPerK", g.ThermalExpansionPerK);
            g.StressCr = o.GetDouble("stressCr", g.StressCr); g.StressCphi = o.GetDouble("stressCphi", g.StressCphi);
            if (o.Has("references")) g.References = o.GetString("references");
            if (o.Has("emission")) { g.Used.Remove("emission"); g.EmissionBand = CrossSection(o["emission"], reg, origin + ".emission", out var d); g.EmissionDescription = d; }
            if (o.Has("absorption")) { g.Used.Remove("absorption"); g.AbsorptionBand = CrossSection(o["absorption"], reg, origin + ".absorption", out var d); g.AbsorptionDescription = d; }
            if (!(g.LifetimeS > 0)) throw new JsonException(origin + ": gain medium needs lifetimeS > 0");
            if (!(g.DopingDensityM3 > 0)) throw new JsonException(origin + ": gain medium needs dopingDensityM3 > 0");
            return g;
        }

        private static Func<double, double> CrossSection(JsonValue v, ModelRegistry reg, string origin, out string description)
        {
            if (v.Kind == JsonKind.Object)
            {
                var o = v.AsObject(); string kind = o.GetString("kind", "");
                if (kind == "gaussians")
                {
                    var bands = o.GetArray("bands") ?? throw new JsonException(origin + ": \"gaussians\" needs \"bands\"");
                    var b = bands.Select(x => x.AsObject()).Select(x => (x.GetDouble("wavelengthM", 0), x.GetDouble("sigmaM2", 0), x.GetDouble("fwhmM", 1e-9))).ToArray();
                    description = "Gaussian bands: " + string.Join(", ", b.Select(x => string.Format(CultureInfo.InvariantCulture, "{0:G4} m^2 at {1:G5} nm ({2:G3} nm)", x.Item2, x.Item1 * 1e9, x.Item3 * 1e9)));
                    return Bands(b);
                }
                if (kind == "table")
                {
                    var wl = o.GetDoubleArray("wavelengthsM") ?? throw new JsonException(origin + ": table needs wavelengthsM");
                    var val = o.GetDoubleArray("values") ?? throw new JsonException(origin + ": table needs values (m^2)");
                    var ord = Enumerable.Range(0, wl.Length).OrderBy(i => Omega(wl[i])).ToArray();
                    var w = ord.Select(i => Omega(wl[i])).ToArray(); var s = ord.Select(i => val[i]).ToArray();
                    description = "table, " + wl.Length + " points " + (wl.Min() * 1e9).ToString("F0", CultureInfo.InvariantCulture) + "-" + (wl.Max() * 1e9).ToString("F0", CultureInfo.InvariantCulture) + " nm";
                    return x =>
                    {
                        if (x <= w[0] || x >= w[w.Length - 1]) return 0;
                        int j = Array.BinarySearch(w, x); if (j >= 0) return s[j]; j = ~j;
                        double t = (x - w[j - 1]) / (w[j] - w[j - 1]); return Math.Max(0, s[j - 1] * (1 - t) + s[j] * t);
                    };
                }
            }
            // expression / callback / constant through the index-model machinery (a function of omega)
            var spec = ModelSpec.FromJson(v, ModelSource.SystemData, origin);
            var f = reg.Create<IRefractiveIndex>("index", spec);
            description = f.Description;
            return x => { var o1 = new double[1]; f.Evaluate(new[] { x }, o1); return Math.Max(0, o1[0]); };
        }
    }
}
