using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// A run of consecutive linear transform operators as one engine linear term, so the grid planner, arrival times,
    /// the loss budget and backward propagation treat it like any other optic.
    /// </summary>
    public sealed class TransformTerm : LinearTerm
    {
        public IReadOnlyList<TransformOperator> Operators { get; }
        private readonly string _id;
        public TransformTerm(IReadOnlyList<TransformOperator> ops, string elementId) { Operators = ops; _id = elementId; Mechanism = "transform"; }
        public override string Description => "transform " + _id + ": " + string.Join("; ", Operators.Select(o => o.Description));
        public override double GroupDelayAt(double omega) => Operators.Sum(o => o.GroupDelayAt(omega));
        public override bool IsFlat => Operators.All(o => o is EnergyOperator);

        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            var h = JonesSpectrum.Identity(axis.N);
            foreach (var o in Operators) h = h.Then(o.Transfer(axis));
            return FrameReferenced(h, axis, GroupDelayAt);
        }

        /// <summary>Remove the carrier phase and the carrier group delay (the engine's co-moving frame), as every
        /// linear term does: multiply by exp(-i (phi(w0) + tau0 (w - w0))). phi(w0) is taken from the transfer itself.</summary>
        public static JonesSpectrum FrameReferenced(JonesSpectrum h, OmegaAxis axis, Func<double, double> groupDelay)
        {
            int c = axis.CarrierIndex, n = axis.N;
            h.Get(c, out var xx, out _, out _, out var yy);
            var ref0 = xx.Magnitude >= yy.Magnitude ? xx : yy;
            double phi0 = ref0.Magnitude > 0 ? ref0.Phase : 0;
            double tau0 = groupDelay(axis.Omega0);
            if (double.IsNaN(tau0)) tau0 = 0;
            var s = new Complex[n];
            for (int i = 0; i < n; i++) s[i] = Complex.FromPolarCoordinates(1, -(phi0 + tau0 * (axis.Omega[i] - axis.Omega0)));
            h.ScaleBy(s);
            return h;
        }
    }

    /// <summary>
    /// The operators of a transform element from the first field operator (incoherent pedestal, wavefront) to the
    /// last, executed in order on the whole field. Linear operators before / after that range are ordinary linear terms.
    /// </summary>
    public sealed class TransformActive : IActiveElement
    {
        public IReadOnlyList<TransformOperator> Operators { get; }
        private readonly TransformContext _ctx;
        public TransformActive(IReadOnlyList<TransformOperator> ops, TransformContext ctx) { Operators = ops; _ctx = ctx; }
        public string Kind => "transform";
        public string Description => "transform: " + string.Join("; ", Operators.Select(o => o.Description));
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            _ctx.Notes.Clear(); _ctx.Values.Clear();
            foreach (var o in Operators)
            {
                double e0 = p.TotalEnergyJ();
                p = o.Apply(p);
                ctx.Energy("transform", e0 - p.TotalEnergyJ());
            }
            foreach (var n in _ctx.Notes) ctx.Note(n);
            foreach (var kv in _ctx.Values) ctx.Record.Values[kv.Key] = kv.Value;
            return p;
        }

        /// <summary>Undo the operators in reverse order (backward propagation).</summary>
        public PulseField Invert(PulseField p, double regularization, List<string> notes)
        {
            _ctx.Notes.Clear();
            for (int i = Operators.Count - 1; i >= 0; i--) p = Operators[i].Invert(p, regularization);
            notes.AddRange(_ctx.Notes);
            return p;
        }
    }

    /// <summary>Compiles <c>"type": "transform"</c> elements (docs/plugins/fitting.md#transform-elements).</summary>
    public static class TransformElement
    {
        public static void Compile(CompilerServices s, SystemElement e, CompiledElement ce)
        {
            var ctx = new TransformContext { Omega0 = s.Omega0, ElementId = e.Id, Registry = s.Registry };
            var arr = e.Json.GetArray("operators") ?? throw new JsonException("transform '" + e.Id + "': needs \"operators\" (a list of {\"kind\": ...})");
            var ops = new List<TransformOperator>();
            int k = 0;
            foreach (var v in arr)
            {
                var o = v.AsObject();
                if (!o.GetBool("enabled", true)) { k++; continue; }
                ops.Add(TransformOperators.Create(o, k++, ctx));
            }
            int first = ops.FindIndex(o => !o.IsLinear), last = ops.FindLastIndex(o => !o.IsLinear);
            if (first < 0)
            {
                if (ops.Count > 0) ce.Pre.Add(new TransformTerm(ops, e.Id));
            }
            else
            {
                if (first > 0) ce.Pre.Add(new TransformTerm(ops.Take(first).ToList(), e.Id));
                ce.Active = new TransformActive(ops.Skip(first).Take(last - first + 1).ToList(), ctx);
                if (last < ops.Count - 1) ce.Post.Add(new TransformTerm(ops.Skip(last + 1).ToList(), e.Id));
                if (ops.Skip(first).Take(last - first + 1).Any(o => o.IsLinear && Math.Abs(o.GroupDelayAt(s.Omega0)) > 0))
                    ce.Notes.Add("linear operators between field operators are applied inside the element: the grid planner does not see their dispersion (put large GDD before or after the pedestal / wavefront operators)");
            }
            ce.SatelliteDelayS = ops.Count > 0 ? ops.Max(o => o.SatelliteDelayS) : 0;
            foreach (var o in ops) if (o.SatelliteDelayS != 0) ce.Satellites.Add((o.SatelliteDelayS, o.SatelliteLevel));
            ce.Notes.Add("transform: " + (ops.Count == 0 ? "no operators" : string.Join("; ", ops.Select(o => o.Description))));
            int nFit = FitCollector.CountFlags(e.Json);
            if (nFit > 0) ce.Notes.Add(nFit + " transform parameter(s) marked for fitting (used by \"fit\"; a plain run uses their current values)");
        }

        /// <summary>The element and operator specs for the schema, validation and reference docs.</summary>
        public static IEnumerable<ComponentSpec> Specs()
        {
            const string D = "plugins/fitting.md#";
            yield return new ComponentSpec("element", "transform", "Fittable physical changes to the beam: spectral phase / amplitude, energy, polarisation, contrast (replicas, phase noise, pedestals), wavefront.").DocAt(D + "transform-elements")
                .ListOf("operators", "fitting:operator", "The operators, applied in order", required: true)
                .Num("regularization", "", "Backward propagation: relative Tikhonov level for inverting this element", "the run's backward.regularization");
            var op = new ComponentSpec("fitting:operator", "", "One transform operator (kind + parameters; numbers may be fit objects).").DocAt(D + "operators")
                .Enum("kind", new[] { "spectralPhase", "spectralAmplitude", "energy", "delay", "jones", "replica", "phaseNoise", "pedestal", "wavefront" }, "Operator kind (plugins may add more)", required: true)
                .Bool("enabled", "false skips the operator", "true")
                .Free("fit", ParamType.Array, "Names of parameters to fit (dotted for nested: \"knots.phaseRad\" fits every knot), alternative to fit objects")
                .Free("bounds", ParamType.Object, "Bounds for the names in \"fit\": {name: [min, max]}")
                .Free("priors", ParamType.Object, "Gaussian priors for the names in \"fit\": {name: sigma | {mean, sigma}}")
                .Num("smoothness", "", "Regularisation weight on the second differences of fitted knots", "0")
                // spectralPhase
                .Num("gddFs2", "fs^2", "spectralPhase: GDD").Fittable().Num("todFs3", "fs^3", "spectralPhase: TOD").Fittable()
                .Num("fodFs4", "fs^4", "spectralPhase: FOD").Fittable().Num("fifthFs5", "fs^5", "spectralPhase: fifth order").Fittable()
                .Num("gddS2", "s^2", "spectralPhase: GDD in s^2").Fittable().Num("todS3", "s^3", "spectralPhase: TOD in s^3").Fittable().Num("fodS4", "s^4", "spectralPhase: FOD in s^4").Fittable()
                .Num("centerWavelengthM", "m", "Reference / centre wavelength", "the element's carrier").Fittable().Num("centerWavelengthNm", "nm", "Reference / centre wavelength in nm").Fittable()
                .Obj("knots", "Spline knots: wavelengthsM | wavelengthsNm with phaseRad (spectralPhase) or transmission | transmissionDb (spectralAmplitude)", "fitting:knots")
                // spectralAmplitude
                .Enum("shape", new[] { "gaussian", "superGaussian", "tilt", "spline", "model" }, "spectralAmplitude: shape", "from the keys given")
                .Num("fwhmM", "m", "spectralAmplitude: band FWHM").Fittable().Num("fwhmNm", "nm", "spectralAmplitude: band FWHM in nm").Fittable()
                .Num("order", "", "superGaussian order", "2").Fittable().Num("peak", "", "Peak power transmission", "1").Fittable()
                .Num("tiltDbPerNm", "dB/nm", "spectralAmplitude: transmission slope").Fittable().Num("curvatureDbPerNm2", "dB/nm^2", "spectralAmplitude: curvature").Fittable()
                .Model("model", "filter", "spectralAmplitude: any filter model (shape: model)")
                .Bool("passive", "spectralAmplitude: clip the transmission to <= 1", "true").Bool("causal", "spectralAmplitude: add the minimum phase (Kramers-Kronig)", "false")
                .Num("floor", "", "causal: smallest relative transmission used in the logarithm", "1e-8")
                // energy, delay
                .Num("transmission", "", "energy: power transmission", "1").Fittable().Num("gainDb", "dB", "energy: gain (alternative)").Fittable()
                .Num("delayS", "s", "delay / replica: delay").Fittable().Num("delayFs", "fs", "delay / replica: delay in fs").Fittable().Num("delayPs", "ps", "replica: delay in ps").Fittable()
                // jones
                .Num("retardanceRad", "rad", "jones: retardance").Fittable().Num("axisDeg", "deg", "jones: retarder fast axis").Fittable()
                .Num("diattenuation", "", "jones: power loss of the weak axis (0..1)").Fittable().Num("diattenuationAxisDeg", "deg", "jones: weak axis", "axisDeg").Fittable()
                .Num("rotationDeg", "deg", "jones: rotation of the polarisation").Fittable()
                // replica, phaseNoise, pedestal
                .Num("relativeIntensity", "", "replica / pedestal: intensity relative to the main peak").Fittable()
                .Num("relativeIntensityDb", "dB", "replica / pedestal: the same in dB (better for fitting levels spanning decades)").Fittable().Num("phaseRad", "rad", "replica: relative phase").Fittable()
                .Num("rmsRad", "rad", "phaseNoise: phase noise rms").Fittable().Num("amplitudeRms", "", "phaseNoise: amplitude noise rms").Fittable()
                .Num("correlationNm", "nm", "phaseNoise: correlation width").Fittable().Num("correlationRadPerS", "rad/s", "phaseNoise: correlation width").Fittable()
                .Int("seed", "phaseNoise / pedestal: random seed", "1")
                .Enum("type", new[] { "ase", "exponential" }, "pedestal: incoherent background type", "ase")
                .Num("durationS", "s", "pedestal (ase): duration", "10e-9").Fittable().Num("energyFraction", "", "pedestal (ase): energy / main energy").Fittable()
                .Num("level", "", "pedestal (exponential): level at the peak").Fittable().Num("levelDb", "dB", "pedestal (exponential): level in dB").Fittable().Num("riseS", "s", "pedestal (exponential): rise").Fittable().Num("fallS", "s", "pedestal (exponential): fall").Fittable()
                .Num("bandwidthFwhmNm", "nm", "pedestal: spectral FWHM (default: the pulse's spectrum)").Fittable()
                // wavefront
                .Free("zernike", ParamType.Object, "wavefront: Noll index -> waves rms, e.g. {\"4\": 0.1, \"11\": 0.02}; values may be fit objects")
                .Num("radiusM", "m", "wavefront: normalisation radius", "the beam's 1/e^2 radius").Fittable()
                .Num("wavelengthM", "m", "wavefront: wavelength of the waves", "the carrier").Fittable()
                .Num("tiltXRad", "rad", "wavefront: pointing x").Fittable().Num("tiltYRad", "rad", "wavefront: pointing y").Fittable();
            op.Free("params", ParamType.Object, "Parameters of operator kinds added by other plugins (free-form; numbers may be fit objects)");
            yield return op;
            yield return new ComponentSpec("fitting:knots", "", "Spline knots of a transform operator.")
                .Arr("wavelengthsM", "m", "Knot wavelengths").Arr("wavelengthsNm", "nm", "Knot wavelengths in nm")
                .Free("phaseRad", ParamType.Array, "spectralPhase: phase at the knots (numbers or fit objects)")
                .Free("transmission", ParamType.Array, "spectralAmplitude: power transmission at the knots")
                .Free("transmissionDb", ParamType.Array, "spectralAmplitude: transmission in dB at the knots");
            yield return new ComponentSpec("fitting:value", "", "A fit parameter in place of a number: its value, and how to fit it.").DocAt(D + "marking-parameters-for-fitting")
                .Num("value", "", "The value (used by plain runs; the start value of a fit)", required: true)
                .Bool("fit", "Fit this number", "false")
                .Num("min", "", "Lower bound").Num("max", "", "Upper bound")
                .Num("scale", "", "Typical size of a change", "(max - min) / 4, else a per-quantity default")
                .Num("step", "", "Finite-difference step", "1e-3 x scale")
                .P("prior", ParamType.Any, "", "Gaussian prior: sigma (mean = value) or {mean, sigma}")
                .Str("name", "Label in reports", "element.operator.key")
                .Str("unit", "Unit label", "from the key");
        }
    }
}
