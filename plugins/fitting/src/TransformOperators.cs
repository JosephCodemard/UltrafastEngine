using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Optics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>What an operator needs to know about where it sits.</summary>
    public sealed class TransformContext
    {
        /// <summary>Reference carrier of the element (rad/s): Taylor expansions and default centre wavelengths refer to it.</summary>
        public double Omega0 { get; set; }
        public string ElementId { get; set; }
        public ModelRegistry Registry { get; set; }
        /// <summary>Notes for the element record (filled during execution).</summary>
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Scalar readings for the element record ("wavefrontRmsWaves", "strehl", ...).</summary>
        public Dictionary<string, double> Values { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One physical change to the beam inside a <c>transform</c> element (docs/plugins/fitting.md#transform-elements).
    /// Linear operators (<see cref="IsLinear"/>) are a 2x2 Jones transfer per frequency and are compiled into the
    /// engine's linear terms; the others act on the whole field (additive pedestals, the transverse profile).
    /// Every operator can be undone (<see cref="Invert"/>) for backward propagation. To add your own, derive from this
    /// class and register a factory with <see cref="TransformOperators.Register"/>.
    /// </summary>
    public abstract class TransformOperator
    {
        /// <summary>The operator's JSON (numbers may be fit objects: read them with <see cref="FitValues.Num"/>).</summary>
        public JsonObject Spec { get; internal set; }
        public string Kind { get; internal set; }
        public int Index { get; internal set; }
        public TransformContext Context { get; internal set; }

        public abstract string Description { get; }
        /// <summary>True when the operator is a Jones transfer H(omega) (then implement <see cref="Transfer"/>).</summary>
        public virtual bool IsLinear => true;
        /// <summary>Absolute transfer on the axis (phase about the operator's reference, not frame-referenced).</summary>
        public virtual JonesSpectrum Transfer(OmegaAxis axis) => JonesSpectrum.Identity(axis.N);
        /// <summary>Group delay (s) the operator adds at omega (for the grid planner and arrival times).</summary>
        public virtual double GroupDelayAt(double omega) => 0;
        /// <summary>Largest |delay| of satellite pulses the operator creates (s): the grid planner makes room.</summary>
        public virtual double SatelliteDelayS => 0;

        /// <summary>Apply to a pulse. Linear operators: the transfer with the frame (carrier phase and group delay) removed.</summary>
        public virtual PulseField Apply(PulseField p)
        {
            var ax = OmegaAxis.FromGrid(p.Grid);
            var h = TransformTerm.FrameReferenced(Transfer(ax), ax, GroupDelayAt);
            h.Apply(p.Ax, p.Ay, out var bx, out var by);
            return p.WithSpectrum(bx, by);
        }

        /// <summary>Undo the operator. Linear operators: the regularised inverse of the transfer (Tikhonov, relative
        /// level <paramref name="regularization"/>). Non-invertible parts return the pulse unchanged with a note.</summary>
        public virtual PulseField Invert(PulseField p, double regularization)
        {
            var ax = OmegaAxis.FromGrid(p.Grid);
            var h = TransformTerm.FrameReferenced(Transfer(ax), ax, GroupDelayAt);
            return LinearInverse.Apply(p, h, regularization, out _);
        }

        // ------------------------------------------------------------------ helpers for implementations
        protected double Num(string key, double fallback) => FitValues.Num(Spec, key, fallback);
        protected bool Has(string key) => Spec.Has(key);
        protected static double C => PhysicalConstants.SpeedOfLightMPerS;
        /// <summary>Wavelength (m) from key "xM" or "xNm".</summary>
        protected double Wavelength(string stem, double fallback)
        {
            if (Has(stem + "M")) return Num(stem + "M", fallback);
            if (Has(stem + "Nm")) return Num(stem + "Nm", fallback * 1e9) * 1e-9;
            return fallback;
        }
        protected double ReferenceOmega => 2 * Math.PI * C / Wavelength("centerWavelength", 2 * Math.PI * C / Context.Omega0);
    }

    /// <summary>Registry of operator kinds ("spectralPhase", ... and your own).</summary>
    public static class TransformOperators
    {
        private static readonly Dictionary<string, Func<JsonObject, TransformOperator>> Factories = new Dictionary<string, Func<JsonObject, TransformOperator>>(StringComparer.OrdinalIgnoreCase)
        {
            { "spectralPhase", o => new SpectralPhaseOperator() },
            { "spectralAmplitude", o => new SpectralAmplitudeOperator() },
            { "energy", o => new EnergyOperator() },
            { "delay", o => new DelayOperator() },
            { "jones", o => new JonesOperator() },
            { "replica", o => new ReplicaOperator() },
            { "phaseNoise", o => new PhaseNoiseOperator() },
            { "pedestal", o => new PedestalOperator() },
            { "wavefront", o => new WavefrontOperator() },
        };

        /// <summary>Add an operator kind (e.g. from another plugin: a learned operator, a measured transfer matrix).</summary>
        public static void Register(string kind, Func<JsonObject, TransformOperator> factory) { lock (Factories) Factories[kind] = factory; }
        public static IReadOnlyList<string> Kinds { get { lock (Factories) return Factories.Keys.OrderBy(k => k).ToList(); } }

        public static TransformOperator Create(JsonObject spec, int index, TransformContext ctx)
        {
            string kind = spec.GetString("kind", null) ?? throw new JsonException("transform operator #" + index + ": missing \"kind\" (" + string.Join(", ", Kinds) + ")");
            Func<JsonObject, TransformOperator> f;
            lock (Factories) if (!Factories.TryGetValue(kind, out f)) throw new JsonException("transform operator #" + index + ": unknown kind \"" + kind + "\" (" + string.Join(", ", Kinds) + ")");
            var op = f(spec);
            op.Spec = spec; op.Kind = kind; op.Index = index; op.Context = ctx;
            return op;
        }
    }

    // =========================================================================================== linear operators

    /// <summary>Spectral phase: Taylor coefficients about the reference wavelength (GDD, TOD, FOD, fifth order) plus
    /// optional spline knots phi(lambda) (natural cubic spline in omega, constant outside the knots).</summary>
    public sealed class SpectralPhaseOperator : TransformOperator
    {
        public override string Description
        {
            get
            {
                var ci = CultureInfo.InvariantCulture; var parts = new List<string>();
                foreach (var (k, u) in new[] { ("gddFs2", "fs^2"), ("todFs3", "fs^3"), ("fodFs4", "fs^4"), ("fifthFs5", "fs^5") })
                    if (Has(k) && Num(k, 0) != 0) parts.Add(k.Substring(0, k.Length - 3).ToUpperInvariant() + " " + Num(k, 0).ToString("G4", ci) + " " + u);
                if (Spec.GetObject("knots") != null) parts.Add("spline phase (" + (FitValues.Arr(Spec.GetObject("knots"), "phaseRad")?.Length ?? 0) + " knots)");
                return "spectral phase: " + (parts.Count > 0 ? string.Join(", ", parts) : "none");
            }
        }

        private double[] Coefficients() => new[]
        {
            Num("gddFs2", 0) * 1e-30 + Num("gddS2", 0), Num("todFs3", 0) * 1e-45 + Num("todS3", 0),
            Num("fodFs4", 0) * 1e-60 + Num("fodS4", 0), Num("fifthFs5", 0) * 1e-75
        };

        private Spline _spline; private bool _splineBuilt;
        private Spline Knots()
        {
            if (_splineBuilt) return _spline;
            _splineBuilt = true;
            var k = Spec.GetObject("knots");
            if (k == null) return null;
            var lam = FitValues.Arr(k, "wavelengthsM") ?? FitValues.Arr(k, "wavelengthsNm")?.Select(x => x * 1e-9).ToArray();
            var ph = FitValues.Arr(k, "phaseRad");
            if (lam == null || ph == null || lam.Length != ph.Length || lam.Length < 2) throw new JsonException("spectralPhase.knots needs wavelengthsM (or wavelengthsNm) and phaseRad of the same length (>= 2)");
            _spline = new Spline(lam.Select(l => 2 * Math.PI * C / l).ToArray(), ph);
            return _spline;
        }

        public double PhaseAt(double w)
        {
            var c = Coefficients(); double d = w - ReferenceOmega;
            double phi = c[0] * d * d / 2 + c[1] * d * d * d / 6 + c[2] * d * d * d * d / 24 + c[3] * Math.Pow(d, 5) / 120;
            var s = Knots(); if (s != null) phi += s.Eval(w);
            return phi;
        }

        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            var h = new Complex[axis.N];
            for (int i = 0; i < h.Length; i++) h[i] = Complex.FromPolarCoordinates(1, PhaseAt(axis.Omega[i]));
            return JonesSpectrum.Scalar(h);
        }

        public override double GroupDelayAt(double w) { double e = w * 1e-5; return (PhaseAt(w + e) - PhaseAt(w - e)) / (2 * e); }
    }

    /// <summary>
    /// Spectral amplitude (power transmission T(lambda)): a Gaussian / super-Gaussian band, a tilt and curvature in dB
    /// per nm, spline knots of T, or any filter model; optionally passive (T &lt;= 1) and causal (the minimum phase
    /// that a physical filter with this amplitude must have, from the Kramers-Kronig relation).
    /// </summary>
    public sealed class SpectralAmplitudeOperator : TransformOperator
    {
        private string Shape => Spec.GetString("shape", Spec.GetObject("knots") != null ? "spline" : Spec.Has("model") ? "model" : Has("tiltDbPerNm") || Has("curvatureDbPerNm2") ? "tilt" : "gaussian").ToLowerInvariant();
        private bool Passive => Spec.GetBool("passive", true);
        private bool Causal => Spec.GetBool("causal", false);

        public override string Description
        {
            get
            {
                var ci = CultureInfo.InvariantCulture; string s = Shape;
                string d = s == "gaussian" || s == "supergaussian"
                    ? string.Format(ci, "{0} band {1:F2} nm FWHM at {2:F2} nm", s, Wavelength("fwhm", 1e-7) * 1e9, Wavelength("centerWavelength", 2 * Math.PI * C / Context.Omega0) * 1e9)
                    : s == "tilt" ? string.Format(ci, "tilt {0:G3} dB/nm, curvature {1:G3} dB/nm^2", Num("tiltDbPerNm", 0), Num("curvatureDbPerNm2", 0))
                    : s == "spline" ? "spline transmission (" + (FitValues.Arr(Spec.GetObject("knots"), "transmission") ?? FitValues.Arr(Spec.GetObject("knots"), "transmissionDb"))?.Length + " knots)"
                    : "filter model";
                return "spectral amplitude: " + d + (Causal ? ", causal (minimum phase)" : "") + (Passive ? "" : ", may amplify");
            }
        }

        private ISpectralFilter _model; private Spline _spline; private bool _splineDb;

        /// <summary>Power transmission at the axis frequencies.</summary>
        public double[] Transmission(double[] omega)
        {
            int n = omega.Length; var T = new double[n];
            double lc = Wavelength("centerWavelength", 2 * Math.PI * C / Context.Omega0), peak = Num("peak", 1);
            switch (Shape)
            {
                case "gaussian": case "supergaussian":
                {
                    double fw = Wavelength("fwhm", double.NaN);
                    if (double.IsNaN(fw) || fw <= 0) throw new JsonException("spectralAmplitude (" + Shape + "): give fwhmM or fwhmNm > 0");
                    double order = Shape == "gaussian" ? 1 : Num("order", 2);
                    for (int i = 0; i < n; i++) { double l = 2 * Math.PI * C / omega[i]; T[i] = peak * Math.Exp(-Math.Log(2) * Math.Pow(Math.Abs(2 * (l - lc) / fw), 2 * order)); }
                    break;
                }
                case "tilt":
                {
                    double a = Num("tiltDbPerNm", 0), b = Num("curvatureDbPerNm2", 0);
                    for (int i = 0; i < n; i++) { double x = (2 * Math.PI * C / omega[i] - lc) * 1e9; T[i] = peak * Math.Pow(10, (a * x + b * x * x) / 10); }
                    break;
                }
                case "spline":
                {
                    if (_spline == null)
                    {
                        var k = Spec.GetObject("knots");
                        var lam = FitValues.Arr(k, "wavelengthsM") ?? FitValues.Arr(k, "wavelengthsNm")?.Select(x => x * 1e-9).ToArray();
                        var tv = FitValues.Arr(k, "transmission"); _splineDb = tv == null;
                        if (tv == null) tv = FitValues.Arr(k, "transmissionDb");
                        if (lam == null || tv == null || lam.Length != tv.Length || lam.Length < 2) throw new JsonException("spectralAmplitude.knots needs wavelengthsM (or wavelengthsNm) and transmission (or transmissionDb) of the same length (>= 2)");
                        _spline = new Spline(lam.Select(l => 2 * Math.PI * C / l).ToArray(), tv);
                    }
                    for (int i = 0; i < n; i++) { double v = _spline.Eval(omega[i]); T[i] = peak * (_splineDb ? Math.Pow(10, v / 10) : Math.Max(0, v)); }
                    break;
                }
                case "model":
                {
                    if (_model == null)
                    {
                        var spec = ModelSpec.FromJson(Spec["model"], ModelSource.SystemData, "transform " + Context.ElementId + " operator #" + Index + ".model");
                        _model = (Context.Registry ?? ModelRegistry.CreateDefault()).Create<ISpectralFilter>("filter", spec);
                        if (_model is ExpressionFilter ef) ef.Omega0 = Context.Omega0;
                    }
                    var ph = new double[n]; _model.Evaluate(omega, T, ph);
                    for (int i = 0; i < n; i++) T[i] = peak * (double.IsNaN(T[i]) ? 0 : T[i]);
                    break;
                }
                default: throw new JsonException("spectralAmplitude: unknown shape \"" + Shape + "\" (gaussian, superGaussian, tilt, spline, model)");
            }
            if (Passive) for (int i = 0; i < n; i++) T[i] = Math.Min(1, Math.Max(0, T[i]));
            else for (int i = 0; i < n; i++) T[i] = Math.Max(0, T[i]);
            return T;
        }

        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            var T = Transmission(axis.Omega);
            var h = new Complex[axis.N];
            if (Causal) h = MinimumPhase.FromTransmission(T, Spec.GetDouble("floor", 1e-8));
            else for (int i = 0; i < h.Length; i++) h[i] = Math.Sqrt(T[i]);
            return JonesSpectrum.Scalar(h);
        }
    }

    /// <summary>Energy: a flat power transmission (or gain in dB).</summary>
    public sealed class EnergyOperator : TransformOperator
    {
        private double T => Has("gainDb") ? Math.Pow(10, Num("gainDb", 0) / 10) : Num("transmission", 1);
        public override string Description => "energy x " + T.ToString("G5", CultureInfo.InvariantCulture);
        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            if (T < 0) throw new JsonException("transform energy: transmission must be >= 0");
            var h = new Complex[axis.N]; double a = Math.Sqrt(T);
            for (int i = 0; i < h.Length; i++) h[i] = a;
            return JonesSpectrum.Scalar(h);
        }
    }

    /// <summary>A pure group delay (only matters relative to other beams: the engine's frame follows the pulse).</summary>
    public sealed class DelayOperator : TransformOperator
    {
        private double Tau => Num("delayS", 0) + Num("delayFs", 0) * 1e-15;
        public override string Description => "delay " + (Tau * 1e15).ToString("G5", CultureInfo.InvariantCulture) + " fs";
        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            var h = new Complex[axis.N];
            for (int i = 0; i < h.Length; i++) h[i] = Complex.FromPolarCoordinates(1, (axis.Omega[i] - ReferenceOmega) * Tau);
            return JonesSpectrum.Scalar(h);
        }
        public override double GroupDelayAt(double omega) => Tau;
    }

    /// <summary>Polarisation errors: a retarder (retardance, axis), a diattenuator (weak-axis power loss), then a rotation.</summary>
    public sealed class JonesOperator : TransformOperator
    {
        public override string Description => string.Format(CultureInfo.InvariantCulture, "Jones: retardance {0:G3} rad at {1:G3} deg, diattenuation {2:G3}, rotation {3:G3} deg",
            Num("retardanceRad", 0), Num("axisDeg", 0), Num("diattenuation", 0), Num("rotationDeg", 0));

        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            double d = Num("retardanceRad", 0), th = Num("axisDeg", 0) * Math.PI / 180, dia = Math.Max(0, Math.Min(1, Num("diattenuation", 0)));
            double thD = Num("diattenuationAxisDeg", Num("axisDeg", 0)) * Math.PI / 180, rot = Num("rotationDeg", 0) * Math.PI / 180;
            JonesSpectrum.RotatedDiagonal(th, Complex.FromPolarCoordinates(1, d / 2), Complex.FromPolarCoordinates(1, -d / 2), out var r11, out var r12, out var r21, out var r22);
            JonesSpectrum.RotatedDiagonal(thD, 1, Math.Sqrt(1 - dia), out var d11, out var d12, out var d21, out var d22);
            // M = Rot * D * R
            Complex m11 = d11 * r11 + d12 * r21, m12 = d11 * r12 + d12 * r22, m21 = d21 * r11 + d22 * r21, m22 = d21 * r12 + d22 * r22;
            double c = Math.Cos(rot), s = Math.Sin(rot);
            Complex a11 = c * m11 - s * m21, a12 = c * m12 - s * m22, a21 = s * m11 + c * m21, a22 = s * m12 + c * m22;
            int n = axis.N;
            var xx = Enumerable.Repeat(a11, n).ToArray(); var xy = Enumerable.Repeat(a12, n).ToArray();
            var yx = Enumerable.Repeat(a21, n).ToArray(); var yy = Enumerable.Repeat(a22, n).ToArray();
            return JonesSpectrum.Full(xx, xy, yx, yy);
        }
    }

    /// <summary>A coherent satellite (pre- or post-pulse) from a double reflection or an etalon: H = 1 + sqrt(r) exp(i (W tau + phi + GDD W^2 / 2)).</summary>
    public sealed class ReplicaOperator : TransformOperator
    {
        private double Tau => Num("delayS", 0) + Num("delayFs", 0) * 1e-15 + Num("delayPs", 0) * 1e-12;
        private double RelI => Has("relativeIntensityDb") ? Math.Pow(10, Num("relativeIntensityDb", -40) / 10) : Num("relativeIntensity", 1e-4);
        public override string Description => string.Format(CultureInfo.InvariantCulture, "replica at {0:G4} ps, {1:G3} relative intensity", Tau * 1e12, RelI);
        public override double SatelliteDelayS => Math.Abs(Tau);
        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            double a = Math.Sqrt(Math.Max(0, RelI)), phi = Num("phaseRad", 0), gdd = Num("gddFs2", 0) * 1e-30, w0 = ReferenceOmega;
            var h = new Complex[axis.N];
            for (int i = 0; i < h.Length; i++) { double d = axis.Omega[i] - w0; h[i] = 1 + a * Complex.FromPolarCoordinates(1, d * Tau + phi + gdd * d * d / 2); }
            return JonesSpectrum.Scalar(h);
        }
    }

    /// <summary>Spectral phase and amplitude noise (correlated over a width): the coherent pedestal of stretchers,
    /// pulse shapers and gratings. Deterministic for a given seed and grid.</summary>
    public sealed class PhaseNoiseOperator : TransformOperator
    {
        public override string Description => string.Format(CultureInfo.InvariantCulture, "spectral phase noise {0:G3} rad rms, amplitude {1:G3} rms (seed {2})", Num("rmsRad", 0), Num("amplitudeRms", 0), Spec.GetInt("seed", 1));
        public override JonesSpectrum Transfer(OmegaAxis axis)
        {
            int n = axis.N; double corr = Num("correlationRadPerS", double.NaN);
            if (double.IsNaN(corr) && (Has("correlationNm") || Has("correlationM")))
            {
                double lam = 2 * Math.PI * C / axis.Omega0, dl = Has("correlationM") ? Num("correlationM", 0) : Num("correlationNm", 0) * 1e-9;
                corr = 2 * Math.PI * C * dl / (lam * lam);
            }
            if (double.IsNaN(corr)) corr = 10 * axis.DOmega;
            var rnd = new Random(Spec.GetInt("seed", 1));
            var ph = Pedestals.CorrelatedNoise(n, axis.DOmega, corr, rnd, Num("rmsRad", 0));
            var am = Pedestals.CorrelatedNoise(n, axis.DOmega, corr, rnd, Num("amplitudeRms", 0));
            var h = new Complex[n];
            for (int i = 0; i < n; i++) h[i] = Math.Max(0, 1 + am[i]) * Complex.FromPolarCoordinates(1, ph[i]);
            return JonesSpectrum.Scalar(h);
        }
    }

    // =========================================================================================== field operators

    /// <summary>An incoherent background added to the pulse (ASE plateau or exponential wings): contrast degradation
    /// that no linear filter produces. Same parameters as the input pulse's pedestal components. Not invertible
    /// (backward propagation passes the pulse through unchanged, with a note).</summary>
    public sealed class PedestalOperator : TransformOperator
    {
        public override bool IsLinear => false;
        private string Type => Spec.GetString("type", "ase");
        public override string Description => string.Format(CultureInfo.InvariantCulture, "incoherent {0} pedestal, level {1:G3}", Type, Type == "ase" ? (Has("relativeIntensityDb") ? Math.Pow(10, Num("relativeIntensityDb", -80) / 10) : Num("relativeIntensity", Num("energyFraction", 1e-8))) : (Has("levelDb") ? Math.Pow(10, Num("levelDb", -60) / 10) : Num("level", 1e-6)));
        public override PulseField Apply(PulseField p)
        {
            var comp = new JsonObject().Set("kind", Type);
            foreach (var kv in Spec) if (kv.Key != "kind" && kv.Key != "type" && kv.Key != "fit" && kv.Key != "bounds" && kv.Key != "priors") comp.Set(kv.Key, kv.Value.Kind == JsonKind.Object && FitValues.IsFitObject(kv.Value) ? (JsonValue)FitValues.Value(kv.Value, 0) : kv.Value);
            if (Has("relativeIntensityDb")) { comp.Remove("relativeIntensityDb"); comp.Set("relativeIntensity", Math.Pow(10, Num("relativeIntensityDb", -80) / 10)); }
            if (Has("levelDb")) { comp.Remove("levelDb"); comp.Set("level", Math.Pow(10, Num("levelDb", -60) / 10)); }
            // the pedestal follows the pulse's dominant polarisation
            double ex = p.Ax.Sum(z => z.Magnitude * z.Magnitude), ey = p.Ay.Sum(z => z.Magnitude * z.Magnitude);
            var pol = ex >= ey ? JonesVector.LinearX : JonesVector.LinearY;
            return Pedestals.Apply(p, new[] { comp }, pol);
        }
        public override PulseField Invert(PulseField p, double regularization)
        {
            Context.Notes.Add("operator #" + Index + " (incoherent pedestal) cannot be undone: passed through unchanged in backward propagation");
            return p;
        }
    }

    /// <summary>
    /// Wavefront error: Zernike terms (Noll, waves rms at a reference wavelength over a radius) and pointing (tilt in
    /// rad), applied as an optical path difference to the carried transverse profile (spatial models). On a 1-D beam
    /// it is reported (rms, Strehl) and does not change the pulse.
    /// </summary>
    public sealed class WavefrontOperator : TransformOperator
    {
        public override bool IsLinear => false;
        public override string Description => "wavefront: " + string.Join(", ", Terms().Where(t => t.Waves != 0).Select(t => "Z" + t.J + " " + t.Waves.ToString("G3", CultureInfo.InvariantCulture) + " waves"))
            + (Num("tiltXRad", 0) != 0 || Num("tiltYRad", 0) != 0 ? string.Format(CultureInfo.InvariantCulture, ", tilt ({0:G3}, {1:G3}) urad", Num("tiltXRad", 0) * 1e6, Num("tiltYRad", 0) * 1e6) : "");

        public List<(int J, double Waves)> Terms()
        {
            var r = new List<(int, double)>();
            var z = Spec.GetObject("zernike");
            if (z != null) foreach (var kv in z) if (int.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int j) && j >= 1) r.Add((j, FitValues.Value(kv.Value, 0)));
            return r.OrderBy(t => t.Item1).ToList();
        }

        private PulseField Run(PulseField p, double sign)
        {
            var terms = Terms();
            double lamRef = Wavelength("wavelength", 2 * Math.PI * C / p.Grid.OmegaCenter);
            double rmsWaves = Math.Sqrt(terms.Where(t => t.J > 3).Sum(t => t.Waves * t.Waves));   // piston and tilt do not reduce the Strehl ratio
            Context.Values["wavefrontRmsWaves"] = rmsWaves;
            Context.Values["strehlEstimate"] = Math.Exp(-Math.Pow(2 * Math.PI * rmsWaves, 2));
            if (p.Profile == null)
            {
                Context.Notes.Add(string.Format(CultureInfo.InvariantCulture, "wavefront {0:G3} waves rms (Strehl ~ {1:G3}): no transverse profile is carried (1-D beam), so it is reported only -- give a downstream element a \"transverse\" model to see its effect", rmsWaves, Context.Values["strehlEstimate"]));
                return p;
            }
            var prof = p.Profile.Clone(); var B = prof.Basis; int nf = prof.Omega.Length;
            double radius = Wavelength("radius", double.NaN);
            if (double.IsNaN(radius)) { var (wx, _) = prof.SecondMomentRadius(); radius = wx; }
            var ab = new Aberration { RadiusM = radius, ReferenceWavelengthM = lamRef };
            ab.Terms.AddRange(terms);
            bool radial = B.Kind == "radial";
            if (radial && terms.Any(t => Zernike.Noll(t.J).M != 0)) Context.Notes.Add("radial spatial model: only rotationally symmetric Zernike terms (defocus, spherical) are applied");
            double tx = Num("tiltXRad", 0), ty = Num("tiltYRad", 0);
            if (radial && (tx != 0 || ty != 0)) Context.Notes.Add("radial spatial model: pointing (tilt) is not applied");
            var opd = new double[B.M];
            for (int m = 0; m < B.M; m++) opd[m] = sign * (ab.Opd(B.X[m], B.Y[m], radial) + (radial ? 0 : tx * B.X[m] + ty * B.Y[m]));
            for (int f = 0; f < nf; f++)
            {
                double k = prof.Omega[f] / C;
                for (int m = 0; m < B.M; m++)
                {
                    var ph = Complex.FromPolarCoordinates(1, k * opd[m]);
                    prof.Data[m * nf + f] *= ph;
                    if (prof.DataY != null) prof.DataY[m * nf + f] *= ph;
                }
            }
            var q = p.WithSpectrum(p.Ax, p.Ay); q.Profile = prof;
            return q;
        }

        public override PulseField Apply(PulseField p) => Run(p, +1);
        public override PulseField Invert(PulseField p, double regularization) => Run(p, -1);
    }

    // =========================================================================================== numerics

    /// <summary>Cubic spline through (x, y) (x need not be sorted) with not-a-knot ends, so quadratic and cubic data
    /// (e.g. a pure GDD / TOD phase) are reproduced exactly; constant outside the knots. Two knots: linear; three: the parabola.</summary>
    public sealed class Spline
    {
        private readonly double[] _x, _y, _m;
        public Spline(double[] x, double[] y)
        {
            var o = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
            _x = o.Select(i => x[i]).ToArray(); _y = o.Select(i => y[i]).ToArray();
            int n = _x.Length; _m = new double[n];
            if (n < 3) return;
            // second derivatives m: continuity of the first derivative inside, not-a-knot (continuous third derivative) at the ends
            var A = new double[n, n]; var d = new double[n];
            for (int i = 1; i < n - 1; i++)
            {
                double h0 = _x[i] - _x[i - 1], h1 = _x[i + 1] - _x[i];
                A[i, i - 1] = h0; A[i, i] = 2 * (h0 + h1); A[i, i + 1] = h1;
                d[i] = 6 * ((_y[i + 1] - _y[i]) / h1 - (_y[i] - _y[i - 1]) / h0);
            }
            if (n == 3) { A[0, 0] = 1; A[0, 1] = -1; A[2, 1] = 1; A[2, 2] = -1; }   // constant second derivative: the parabola
            else
            {
                double a0 = _x[1] - _x[0], a1 = _x[2] - _x[1], b0 = _x[n - 2] - _x[n - 3], b1 = _x[n - 1] - _x[n - 2];
                A[0, 0] = a1; A[0, 1] = -(a0 + a1); A[0, 2] = a0;
                A[n - 1, n - 3] = b1; A[n - 1, n - 2] = -(b0 + b1); A[n - 1, n - 1] = b0;
            }
            _m = FitMath.Solve(A, d) ?? new double[n];
        }

        public double Eval(double v)
        {
            int n = _x.Length;
            if (v <= _x[0]) return _y[0];
            if (v >= _x[n - 1]) return _y[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_x[mid] <= v) lo = mid; else hi = mid; }
            double h = _x[hi] - _x[lo], t = (v - _x[lo]) / h, u = 1 - t;
            return u * _y[lo] + t * _y[hi] + ((u * u * u - u) * _m[lo] + (t * t * t - t) * _m[hi]) * h * h / 6;
        }
    }

    /// <summary>
    /// The minimum-phase transfer with a given power transmission (Kramers-Kronig / Bode gain-phase relation) by the
    /// folded cepstrum: c = F^-1 ln|H|, keep c(0), double c(t &gt; 0), drop c(t &lt; 0), H = exp(F c). Its impulse response
    /// is causal. Computed on the grid band (a band-limited approximation of the relation).
    /// </summary>
    public static class MinimumPhase
    {
        public static Complex[] FromTransmission(double[] T, double floor)
        {
            int n = T.Length;
            double tmax = Math.Max(T.Max(), 1e-300);
            var l = new Complex[n];
            for (int i = 0; i < n; i++) l[i] = 0.5 * Math.Log(Math.Max(T[i], floor * tmax));
            var c = SpectralTransform.FreqToTime(l);
            int z = n / 2; // t = 0 (centred arrays)
            for (int i = 0; i < n; i++) c[i] = i == z ? c[i] : i > z ? 2 * c[i] : Complex.Zero;
            var lh = SpectralTransform.TimeToFreq(c);
            var h = new Complex[n];
            for (int i = 0; i < n; i++) h[i] = Complex.Exp(lh[i]);
            // keep the requested magnitude exactly (the fold is exact only for an infinitely sampled spectrum)
            for (int i = 0; i < n; i++) { double a = Math.Sqrt(Math.Max(0, T[i])); h[i] = h[i].Magnitude > 0 ? h[i] / h[i].Magnitude * a : a; }
            return h;
        }
    }

    /// <summary>Regularised inverse of a 2x2 Jones transfer per frequency: H^-1 ~ (H^H H + eps^2 I)^-1 H^H, eps relative
    /// to the largest singular value over the band, so strongly attenuated bins are not amplified without bound.</summary>
    public static class LinearInverse
    {
        /// <summary>Apply the regularised inverse. <paramref name="clippedFraction"/>: the fraction of the pulse's spectral
        /// energy in bins where the transfer is below the regularisation level (the field there is not recoverable).</summary>
        public static PulseField Apply(PulseField p, JonesSpectrum h, double regularization, out double clippedFraction)
        {
            int n = h.N; double smax = 0;
            for (int i = 0; i < n; i++) { h.Get(i, out var a, out var b, out var c, out var d); smax = Math.Max(smax, Math.Sqrt(a.Magnitude * a.Magnitude + b.Magnitude * b.Magnitude + c.Magnitude * c.Magnitude + d.Magnitude * d.Magnitude)); }
            double eps2 = Math.Pow(regularization * smax, 2);
            var bx = new Complex[n]; var by = new Complex[n];
            double eAll = 0, eClip = 0;
            for (int i = 0; i < n; i++)
            {
                h.Get(i, out var a, out var b, out var c, out var d);
                // G = H^H H + eps^2 I ; x = G^-1 H^H y
                Complex ac = Complex.Conjugate(a), bc = Complex.Conjugate(b), cc = Complex.Conjugate(c), dc = Complex.Conjugate(d);
                Complex g11 = ac * a + cc * c + eps2, g12 = ac * b + cc * d, g21 = bc * a + dc * c, g22 = bc * b + dc * d + eps2;
                Complex u1 = ac * p.Ax[i] + cc * p.Ay[i], u2 = bc * p.Ax[i] + dc * p.Ay[i];
                Complex det = g11 * g22 - g12 * g21;
                double e = p.Ax[i].Magnitude * p.Ax[i].Magnitude + p.Ay[i].Magnitude * p.Ay[i].Magnitude;
                eAll += e;
                if (det.Magnitude < 1e-300) { eClip += e; continue; }
                bx[i] = (g22 * u1 - g12 * u2) / det; by[i] = (-g21 * u1 + g11 * u2) / det;
                double s2 = a.Magnitude * a.Magnitude + b.Magnitude * b.Magnitude + c.Magnitude * c.Magnitude + d.Magnitude * d.Magnitude;
                if (s2 < 10 * eps2) eClip += e;
            }
            clippedFraction = eAll > 0 ? eClip / eAll : 0;
            return p.WithSpectrum(bx, by);
        }

        /// <summary>Undo an engine linear term (frame-referenced transfer, as applied going forward).</summary>
        public static PulseField Apply(PulseField p, LinearTerm t, double regularization, out double clippedFraction)
            => Apply(p, t.Evaluate(OmegaAxis.FromGrid(p.Grid)), regularization, out clippedFraction);
    }
}
