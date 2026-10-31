using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// An element that is more than "linear terms + one nonlinear segment": a spatial bulk element, a chi(2)
    /// crystal, a user callback, a lens acting on the transverse profile, (2.2) a gain medium... It receives the
    /// main beam and returns the main beam -- possibly on a new grid with a new carrier -- and may record
    /// by-products and energy-budget entries through the <see cref="ActiveContext"/>.
    /// </summary>
    public interface IActiveElement
    {
        string Kind { get; }
        string Description { get; }
        /// <summary>Nominal carrier of the beam leaving the element (rad/s), NaN if unchanged. Used at compile time
        /// so that later elements (waveplates, chirped mirrors, Gaussian beams) default to the right wavelength.</summary>
        double OutputOmega0 { get; }
        /// <summary>True if the element keeps the carried transverse profile physically valid (it propagates or
        /// transforms it itself, or does not change it).</summary>
        bool TransverseAware { get; }
        PulseField Execute(ActiveContext ctx, PulseField input);
    }

    /// <summary>What an active element can see and report during execution.</summary>
    public sealed class ActiveContext
    {
        public CompiledRun Run { get; set; }
        public CompiledElement Element { get; set; }
        public ElementRecord Record { get; set; }
        public RunResult Result { get; set; }
        public int Index { get; set; }
        /// <summary>The input port the pulse is on (null: the default port).</summary>
        public string InputPort { get; set; }
        /// <summary>The path this pulse is on; null outside the graph scheduler is not used, the executor always sets it.</summary>
        public PathLedger Path { get; set; }
        /// <summary>The carrier phase of the output is this multiple of the input's (2 for a second harmonic).</summary>
        public double PathPhaseFactor { get; set; } = 1;
        /// <summary>The output's carrier phase cannot be stated on this path.</summary>
        public bool PhaseLost { get; set; }
        /// <summary>True in a Branched system, where the scheduler routes by-products and books unused ports.</summary>
        public bool InGraph { get; set; }
        /// <summary>Lab arrival time of the co-moving frame at the element entrance (s).</summary>
        public double ArrivalS { get; set; }
        public Action<double> Progress { get; set; }
        public CancellationToken Cancel { get; set; }
        /// <summary>Linear dispersion of the element for the main beam (adds to arrival time and system dispersion).</summary>
        public DispersionPoint Own { get; set; }
        /// <summary>Returns a grid sized for the elements after this one, related exactly to the given pulse's grid.</summary>
        public Func<PulseField, FrequencyGrid> DownstreamGrid { get; set; }
        public Dictionary<string, PulseField> Byproducts { get; } = new Dictionary<string, PulseField>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Energy leaving (positive) or entering (negative) the main beam, by mechanism.</summary>
        public void Energy(string mechanism, double joules)
        {
            if (Math.Abs(joules) < 1e-30 || Math.Abs(joules) < 1e-12 * Math.Abs(Record.EnergyInJ)) return;
            Record.LossJ.TryGetValue(mechanism, out double v);
            Record.LossJ[mechanism] = v + joules;
        }

        public void Byproduct(string field, PulseField p) => Byproducts[field] = p;
        public void Note(string s) => Record.Notes.Add(s);
    }

    // ============================================================================================ user element

    /// <summary>
    /// What a user element callback sees: the main beam's spectrum (absolute omega, ascending) with writable
    /// real/imaginary parts of Ax and Ay (sqrt(W) per bin, the engine's spectral convention), the co-moving time
    /// axis, and the element's JSON parameters. Write the new spectrum into the same arrays.
    /// </summary>
    public sealed class UserElementContext
    {
        public double[] Omega { get; set; }
        public double[] T { get; set; }
        public double Omega0 { get; set; }
        public double DtS { get; set; }
        public double[] AxRe { get; set; }
        public double[] AxIm { get; set; }
        public double[] AyRe { get; set; }
        public double[] AyIm { get; set; }
        public double ArrivalS { get; set; }
        public string ElementId { get; set; }
        public string ParamsJson { get; set; }
        public int N => Omega?.Length ?? 0;
    }

    public sealed class UserElement : IActiveElement
    {
        private readonly ElementCallback _cb; private readonly string _name, _params;
        public UserElement(string name, ElementCallback cb, string paramsJson) { _name = name; _cb = cb; _params = paramsJson ?? "{}"; }
        public string Kind => "user";
        public string Description => "user element @" + _name;
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => false;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            int n = p.Grid.N;
            var c = new UserElementContext
            {
                Omega = (double[])p.Grid.OmegaAbsolute.Clone(), T = (double[])p.Grid.Time.Clone(), Omega0 = p.Grid.OmegaCenter, DtS = p.Grid.DTime,
                AxRe = p.Ax.Select(z => z.Real).ToArray(), AxIm = p.Ax.Select(z => z.Imaginary).ToArray(),
                AyRe = p.Ay.Select(z => z.Real).ToArray(), AyIm = p.Ay.Select(z => z.Imaginary).ToArray(),
                ArrivalS = ctx.ArrivalS, ElementId = ctx.Element.Id, ParamsJson = _params
            };
            double e0 = p.TotalEnergyJ();
            _cb(c);
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++) { ax[i] = new Complex(c.AxRe[i], c.AxIm[i]); ay[i] = new Complex(c.AyRe[i], c.AyIm[i]); }
            var q = p.WithSpectrum(ax, ay);
            if (ax.Concat(ay).Any(z => double.IsNaN(z.Real) || double.IsNaN(z.Imaginary)))
                throw new InvalidOperationException("user element '" + ctx.Element.Id + "' returned NaN values");
            ctx.Energy("user", e0 - q.TotalEnergyJ());
            return q;
        }
    }

    // ============================================================================================ lens

    /// <summary>
    /// Thin lens (or curved mirror, f = R/2) acting on the carried transverse profile: phase -k r^2 / 2f. With a
    /// lens material the focal length follows (n(omega0) - 1) / (n(omega) - 1) (chromatic focal shift). Without
    /// a carried profile it only records a note (1-D elements take their beam size from their own beam models).
    /// </summary>
    public sealed class LensElement : IActiveElement, IReportsCarrierPhase
    {
        private readonly double _f, _w0; private readonly Material _lensMat, _medium;
        public LensElement(double focalLengthM, Material lensMaterial, Material medium, double referenceOmega)
        { _f = focalLengthM; _lensMat = lensMaterial; _medium = medium; _w0 = referenceOmega; }
        public string Kind => "lens";
        public string Description => string.Format(CultureInfo.InvariantCulture, "thin lens f = {0:G5} m{1}", _f, _lensMat != null ? " (" + _lensMat.Name + ", chromatic)" : "");
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public double FocalLengthAt(double w)
        {
            if (_lensMat == null) return _f;
            double n0 = _lensMat.IndexAt(_w0), n = _lensMat.IndexAt(w), m0 = _medium?.IndexAt(_w0) ?? 1, m = _medium?.IndexAt(w) ?? 1;
            return _f * (n0 - m0) / (n - m);
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null) { ctx.Note("lens: no transverse profile carried (1-D beam) -- the lens only matters to spatial elements downstream"); return p; }
            var q = p.WithSpectrum(p.Ax, p.Ay);
            q.Profile = TransverseOps.ApplyLens(p.Profile, FocalLengthAt, w => _medium?.IndexAt(w) ?? 1.0);
            return q;
        }
    }
}
