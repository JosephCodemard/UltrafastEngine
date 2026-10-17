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

        public static readonly string[] Names = { "Yb:YAG", "Ti:sapphire", "Nd:YAG" };

        /// <summary>Sum of Gaussian bands in wavelength: (lambda0 m, peak m^2, FWHM m).</summary>
        internal static Func<double, double> Bands(params (double L0, double S, double Fwhm)[] b)
            => w => { double l = 2 * Math.PI * C / w, s = 0; foreach (var x in b) { double u = (l - x.L0) / x.Fwhm; s += x.S * Math.Exp(-4 * Math.Log(2) * u * u); } return s; };

        /// <summary>Asymmetric Gaussian in wavelength (different half widths on each side of the peak).</summary>
        internal static Func<double, double> Asym(double l0, double s, double hwhmShort, double hwhmLong)
            => w => { double l = 2 * Math.PI * C / w, h = l < l0 ? hwhmShort : hwhmLong, u = (l - l0) / h; return s * Math.Exp(-Math.Log(2) * u * u); };

        public static GainMedium Get(string name, double temperatureK = double.NaN)
        {
            string k = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            const double cm2 = 1e-4; // cm^2 -> m^2
            GainMedium g;
            switch (k)
            {
                case "yb:yag": case "ybyag":
                    g = new GainMedium
                    {
                        Name = "Yb:YAG", Host = "YAG", LifetimeS = 0.95e-3, DopingDensityM3 = 1.38e26 * 2, ZeroLineM = 968.8e-9,
                        ThermalConductivity = 9.5, DnDT = 7.8e-6,
                        // emission in the lasing band (room temperature): main line 1030 nm, shoulder ~1048 nm, broad base
                        EmissionBand = Bands((1030.0e-9, 2.1e-20 * cm2, 8.5e-9), (1048.5e-9, 0.32e-20 * cm2, 16e-9), (1015e-9, 0.35e-20 * cm2, 30e-9)),
                        // absorption in the pump band: 940 nm (broad) and the 969 nm zero-phonon line
                        AbsorptionBand = Bands((940.5e-9, 0.77e-20 * cm2, 18e-9), (968.8e-9, 0.85e-20 * cm2, 2.8e-9), (915e-9, 0.25e-20 * cm2, 15e-9)),
                        EmissionDescription = "Gaussian-band fit: 2.1e-20 cm^2 at 1030 nm (8.5 nm FWHM), 1048 nm shoulder, broad base",
                        AbsorptionDescription = "Gaussian-band fit: 0.77e-20 cm^2 at 940 nm (18 nm FWHM), 0.85e-20 cm^2 at 969 nm (2.8 nm)",
                        References = "room-temperature values after Koerner et al., Appl. Phys. B 75, 2002 (cross sections) and Sumida & Fan, OSA TOPS 1995 (lifetime 0.95 ms); lasing-band absorption and pump-band emission from the McCumber relation (zero line 968.8 nm); approximate analytic fit (~10-20 %) -- override with measured tables"
                    };
                    g.Notes.Add("Yb:YAG cross sections are an analytic room-temperature fit; the McCumber relation gives reabsorption at the lasing wavelength (~0.1e-20 cm^2 at 1030 nm). Cryogenic linewidths are not modelled (only the Boltzmann factor follows temperatureK)");
                    break;
                case "ti:sapphire": case "ti:sa": case "tisa": case "tisapphire": case "ti:al2o3":
                    g = new GainMedium
                    {
                        Name = "Ti:sapphire", Host = "Sapphire", LifetimeS = 3.2e-6, DopingDensityM3 = 3.3e25,
                        ThermalConductivity = 33, DnDT = 13e-6,
                        EmissionBand = Asym(795e-9, 3.0e-19 * cm2, 75e-9, 140e-9),
                        AbsorptionBand = Bands((490e-9, 6.4e-20 * cm2, 115e-9)),
                        EmissionDescription = "asymmetric-Gaussian fit, pi polarisation: 3.0e-19 cm^2 at 795 nm, half widths 75 / 140 nm",
                        AbsorptionDescription = "Gaussian fit, pi polarisation: 6.4e-20 cm^2 at 490 nm, 115 nm FWHM (~4.4e-20 cm^2 at 532 nm)",
                        References = "after Moulton, JOSA B 3, 125 (1986) (pi-polarised cross sections, lifetime 3.2 us); four-level; approximate analytic fit (~10-20 %) -- override with measured tables"
                    };
                    g.Notes.Add("Ti:sapphire is treated as a four-level system (no lasing-band absorption); the residual infrared absorption (figure of merit) is not included -- add it with absorptionProfile or a material attenuation");
                    break;
                case "nd:yag": case "ndyag":
                    g = new GainMedium
                    {
                        Name = "Nd:YAG", Host = "YAG", LifetimeS = 230e-6, DopingDensityM3 = 1.38e26,
                        ThermalConductivity = 10.5, DnDT = 7.8e-6,
                        EmissionBand = Bands((1064.2e-9, 2.8e-19 * cm2, 0.6e-9)),
                        AbsorptionBand = Bands((808.5e-9, 7.0e-20 * cm2, 2.5e-9)),
                        EmissionDescription = "Gaussian fit: 2.8e-19 cm^2 at 1064.2 nm, 0.6 nm FWHM",
                        AbsorptionDescription = "Gaussian fit: 7e-20 cm^2 at 808.5 nm, 2.5 nm FWHM",
                        References = "Koechner, Solid-State Laser Engineering (1 at.% Nd, tau = 230 us); four-level; approximate analytic fit"
                    };
                    break;
                default:
                    throw new JsonException("unknown gain medium \"" + name + "\" (built-in: " + string.Join(", ", Names) + "); define your own with an object (docs/gain.md)");
            }
            if (!double.IsNaN(temperatureK)) g.TemperatureK = temperatureK;
            return g;
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
            if (o.Has("lifetimeS")) g.LifetimeS = o.GetDouble("lifetimeS", g.LifetimeS);
            if (o.Has("dopingDensityM3")) g.DopingDensityM3 = o.GetDouble("dopingDensityM3", g.DopingDensityM3);
            if (o.Has("dopingAtPercent")) g.DopingDensityM3 = o.GetDouble("dopingAtPercent", 1) / 100 * o.GetDouble("sitesPerM3", 1.38e28);
            if (o.Has("temperatureK")) g.TemperatureK = o.GetDouble("temperatureK", g.TemperatureK);
            if (o.Has("zeroLineWavelengthM")) g.ZeroLineM = o.GetDouble("zeroLineWavelengthM", double.NaN);
            if (o.Has("thermalConductivity")) g.ThermalConductivity = o.GetDouble("thermalConductivity", g.ThermalConductivity);
            if (o.Has("dnDT")) g.DnDT = o.GetDouble("dnDT", g.DnDT);
            if (o.Has("references")) g.References = o.GetString("references");
            if (o.Has("emission")) { g.EmissionBand = CrossSection(o["emission"], reg, origin + ".emission", out var d); g.EmissionDescription = d; }
            if (o.Has("absorption")) { g.AbsorptionBand = CrossSection(o["absorption"], reg, origin + ".absorption", out var d); g.AbsorptionDescription = d; }
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
