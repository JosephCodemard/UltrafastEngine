using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Materials
{
    /// <summary>Laser-induced damage threshold: fluence at a reference pulse duration, scaled as
    /// (tau/tauRef)^exponent (0.5 is the usual rule of thumb for tau &gt; ~10 ps; use ~0.3 or a table below that).</summary>
    public sealed class LidtSpec
    {
        public double FluenceJPerM2 { get; set; }
        public double ReferencePulseDurationS { get; set; } = 10e-9;
        public double ScalingExponent { get; set; } = 0.5;
        public double ReferenceWavelengthM { get; set; } = double.NaN;
        public string Notes { get; set; }

        public double FluenceAt(double pulseDurationS) =>
            FluenceJPerM2 * Math.Pow(Math.Max(pulseDurationS, 1e-16) / ReferencePulseDurationS, ScalingExponent);

        public static LidtSpec FromJson(JsonObject o)
        {
            if (o == null) return null;
            var s = new LidtSpec
            {
                FluenceJPerM2 = o.Has("fluenceJPerCm2") ? o.GetDouble("fluenceJPerCm2", 0) * 1e4 : o.GetRequiredDouble("fluenceJPerM2", "lidt"),
                ReferencePulseDurationS = o.GetDouble("pulseDurationS", 10e-9),
                ScalingExponent = o.GetDouble("scalingExponent", 0.5),
                ReferenceWavelengthM = o.GetDouble("wavelengthM", double.NaN),
                Notes = o.GetString("notes")
            };
            return s;
        }

        public JsonObject ToJson() => new JsonObject().Set("fluenceJPerM2", FluenceJPerM2).Set("pulseDurationS", ReferencePulseDurationS)
            .Set("scalingExponent", ScalingExponent).Set("wavelengthM", ReferenceWavelengthM).Set("notes", Notes);
    }

    /// <summary>
    /// An optical material: linear index (absolute, i.e. vacuum-referenced, after any air-reference
    /// conversion), optional attenuation, and the nonlinear properties Zemax cannot supply (n2, delayed/Raman
    /// response). Every property is individually overridable (see docs/overrides.md).
    /// </summary>
    public sealed class Material
    {
        public string Name { get; set; }
        public IRefractiveIndex Index { get; set; }
        /// <summary>"vacuum" or "air": what the ORIGINAL index data was referenced to (informational --
        /// <see cref="Index"/> is always absolute).</summary>
        public string IndexReference { get; set; } = "vacuum";
        public IAttenuation Attenuation { get; set; }
        /// <summary>Nonlinear index n2 (m^2/W), Delta n = n2 I.</summary>
        public double N2 { get; set; }
        /// <summary>Delayed (Raman) fraction f_R of the Kerr response.</summary>
        public double RamanFraction { get; set; }
        public IDelayedResponse Raman { get; set; }
        public LidtSpec Lidt { get; set; }
        public bool IsGas { get; set; }
        /// <summary>Orientational (rotational Raman) response of a molecular gas (null otherwise).</summary>
        public Physics.IAnisotropicResponse Rotational { get; set; }
        /// <summary>Gas composition and state (for ionisation / plasma models): species (or "Air"), pressure, temperature.</summary>
        public string GasSpecies { get; set; }
        public double GasPressureBar { get; set; } = double.NaN;
        public double GasTemperatureK { get; set; } = double.NaN;
        /// <summary>Photo-ionisation data of a solid (band gap etc.), used by the plasma model; null for gases (taken from GasData).</summary>
        public SolidIonizationData Ionization { get; set; }
        public string Notes { get; set; }
        /// <summary>Provenance of each property (property name -&gt; description + source).</summary>
        public Dictionary<string, (string Value, ModelSource Source, string Origin)> Provenance { get; } =
            new Dictionary<string, (string, ModelSource, string)>(StringComparer.OrdinalIgnoreCase);

        public double IndexAt(double omega)
        {
            var n = new double[1];
            Index.Evaluate(new[] { omega }, n);
            return n[0];
        }

        /// <summary>Group index n_g = n + omega dn/domega at omega (5-point central difference).</summary>
        public double GroupIndexAt(double omega)
        {
            double h = omega * 1e-4;
            var w = new[] { omega - 2 * h, omega - h, omega, omega + h, omega + 2 * h };
            var n = new double[5];
            Index.Evaluate(w, n);
            double dn = (n[0] - 8 * n[1] + 8 * n[3] - n[4]) / (12 * h);
            return n[2] + omega * dn;
        }

        public Material Clone()
        {
            var copy = new Material
            {
                Name = Name, Index = Index, IndexReference = IndexReference, Attenuation = Attenuation, N2 = N2,
                RamanFraction = RamanFraction, Raman = Raman, Lidt = Lidt, IsGas = IsGas, Notes = Notes,
                Rotational = Rotational, GasSpecies = GasSpecies, GasPressureBar = GasPressureBar, GasTemperatureK = GasTemperatureK, Ionization = Ionization
            };
            foreach (var kv in Provenance) copy.Provenance[kv.Key] = kv.Value;
            return copy;
        }

        public override string ToString() => Name;
    }

    // ------------------------------------------------------------------------------------ Raman models

    /// <summary>Single damped oscillator h(t) = ((t1^2+t2^2)/(t1 t2^2)) exp(-t/t2) sin(t/t1) (Blow &amp; Wood 1989).
    /// Fused silica: fR = 0.18, t1 = 12.2 fs, t2 = 32 fs.</summary>
    public sealed class DampedOscillatorResponse : IDelayedResponse
    {
        public double Tau1 { get; } public double Tau2 { get; }
        public DampedOscillatorResponse(double tau1, double tau2) { Tau1 = tau1; Tau2 = tau2; }
        public string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture, "damped oscillator (tau1 {0:G3} fs, tau2 {1:G3} fs)", Tau1 * 1e15, Tau2 * 1e15);
        public double[] Evaluate(double[] t)
        {
            double norm = (Tau1 * Tau1 + Tau2 * Tau2) / (Tau1 * Tau2 * Tau2);
            var h = new double[t.Length];
            for (int i = 0; i < t.Length; i++) h[i] = t[i] >= 0 ? norm * Math.Exp(-t[i] / Tau2) * Math.Sin(t[i] / Tau1) : 0.0;
            return h;
        }
    }

    /// <summary>h(t) from an expression in t (s) or a callback; normalised numerically to unit integral on the
    /// grid it is sampled on, and forced causal.</summary>
    public sealed class FunctionDelayedResponse : IDelayedResponse
    {
        private readonly Func<double[], double[]> _f; private readonly string _d;
        public FunctionDelayedResponse(string description, Func<double[], double[]> f) { _d = description; _f = f; }
        public string Description => _d;
        public double[] Evaluate(double[] t)
        {
            var h = _f(t);
            double sum = 0, dt = t.Length > 1 ? t[1] - t[0] : 1;
            for (int i = 0; i < t.Length; i++) { if (t[i] < 0) h[i] = 0; sum += h[i] * dt; }
            if (sum != 0) for (int i = 0; i < h.Length; i++) h[i] /= sum;
            return h;
        }
    }

    /// <summary>Data for Keldysh photo-ionisation and plasma in a dielectric (docs/reference/ionization.md).</summary>
    public sealed class SolidIonizationData
    {
        public double BandGapEv;
        /// <summary>Reduced effective mass / electron mass.</summary>
        public double EffectiveMass = 0.635;
        /// <summary>Density of valence electrons that can be ionised (1/m^3).</summary>
        public double NeutralDensityM3 = 2.1e28;
        /// <summary>Electron collision time (s) and recombination / trapping time (s).</summary>
        public double CollisionTimeS = 1e-15;
        public double RecombinationTimeS = 150e-15;

        public static SolidIonizationData For(string material)
        {
            switch ((MaterialLibrary.Canonical(material) ?? "").ToLowerInvariant())
            {
                case "fusedsilica": case "quartz": case "quartz-e": return new SolidIonizationData { BandGapEv = 9.0, EffectiveMass = 0.635, NeutralDensityM3 = 2.1e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                case "sapphire": case "sapphire-e": return new SolidIonizationData { BandGapEv = 8.8, EffectiveMass = 0.5, NeutralDensityM3 = 2.3e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                case "caf2": return new SolidIonizationData { BandGapEv = 10.0, EffectiveMass = 0.5, NeutralDensityM3 = 2.4e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                case "yag": return new SolidIonizationData { BandGapEv = 6.5, EffectiveMass = 0.5, NeutralDensityM3 = 2.0e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                case "mgf2": case "mgf2-e": return new SolidIonizationData { BandGapEv = 10.8, EffectiveMass = 0.5, NeutralDensityM3 = 2.0e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                case "n-bk7": return new SolidIonizationData { BandGapEv = 4.2, EffectiveMass = 0.5, NeutralDensityM3 = 2.0e28, CollisionTimeS = 1e-15, RecombinationTimeS = 150e-15 };
                default: return null;
            }
        }
    }
}
