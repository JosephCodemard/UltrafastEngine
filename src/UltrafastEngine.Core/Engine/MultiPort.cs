// MultiPort.cs -- beams at ports: packets, how packets merge, and the elements with more than one input or output.
//
//   interface IReportsCarrierPhase                                 an active element whose carrier phase is known
//   interface IMultiPortElement : IActiveElement
//     string ContinuingPort                                        the output a Sequential system keeps (`output`)
//   class BeamPacket           PulseField Pulse; PathLedger Path; double EnergyJ
//   class MergeContext         Cw, Warnings, MismatchJ             what a merge needs from its run
//   static class Packets
//     List<BeamPacket> Merge(IEnumerable<BeamPacket> packets, MergeContext ctx)
//     BeamPacket Strongest(IEnumerable<BeamPacket> packets)
//     Complex Overlap(PathLedger a, PathLedger b)                  mode overlap of two tracked Gaussian beams
//   class BeamSplitterElement : IMultiPortElement                  the splitter family: c = t a - r b, d = r a + t b
//     (Complex X, Complex Y) Coefficient(inPort, outPort, omega)
//   class BeamTiltElement : IActiveElement                         misalignment of an arm
//   class DelayTerm : PhaseTerm                                    a pure delay, phase omega tau
//
// An element runs once per packet per input port. That is exact for a linear element, by superposition: interference
// appears when the packets on one output merge. Merging puts them in a common frame, from their ledgers, and takes the
// overlap of their modes: tracked Gaussians analytically, carried profiles by adding the fields point by point.
// Design: docs/design/branching.md, sections 5 and 6.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;

namespace UltrafastEngine.Engine
{
    /// <summary>An active element that adds its carrier phase to <see cref="ActiveContext.Own"/>, or changes none.</summary>
    public interface IReportsCarrierPhase { }

    /// <summary>An active element with several ports. Outputs other than <see cref="ContinuingPort"/> are given as by-products
    /// named after their port; <see cref="ActiveContext.InputPort"/> says which input the pulse is on.</summary>
    public interface IMultiPortElement : IActiveElement
    {
        string ContinuingPort { get; }
    }

    /// <summary>A pulse on an edge, with its history.</summary>
    public sealed class BeamPacket
    {
        public PulseField Pulse;
        public PathLedger Path;
        public double EnergyJ => Pulse.TotalEnergyJ();
    }

    /// <summary>What a merge needs from its run: the source's kind of beam, where warnings go, and where mode mismatch is booked.</summary>
    public sealed class MergeContext
    {
        public bool Cw;
        public ICollection<string> Warnings = new List<string>();
        public double MismatchJ;
    }

    public static class Packets
    {
        /// <summary>The packet with the most energy; null if there are none.</summary>
        public static BeamPacket Strongest(IEnumerable<BeamPacket> packets) => packets.OrderByDescending(p => p.EnergyJ).FirstOrDefault();

        /// <summary>Add up the packets that are mutually coherent and fit one time window. Others stay separate.</summary>
        public static List<BeamPacket> Merge(IEnumerable<BeamPacket> packets, MergeContext ctx)
        {
            var result = new List<BeamPacket>();
            foreach (var p in packets)
            {
                var into = result.FindIndex(q => CanMerge(q, p, ctx.Cw));
                if (into < 0) result.Add(p);
                else result[into] = Sum(result[into], p, ctx);
            }
            return result;
        }

        private static bool CanMerge(BeamPacket a, BeamPacket b, bool cw)
        {
            if (a.Path.SourceId != b.Path.SourceId) return false;
            if (a.Pulse.Profile != null != (b.Pulse.Profile != null)) throw new Models.ModelException("A beam with a carried transverse profile meets one without: give both arms a profile, or neither.");
            var g = a.Pulse.Grid; var h = b.Pulse.Grid;
            if (cw) return g.N == h.N && g.OmegaCenter == h.OmegaCenter;
            double dt = Math.Abs(a.Path.FrameDelayS - b.Path.FrameDelayS);
            return dt / 2 + Math.Max(HalfExtentS(a), HalfExtentS(b)) < 0.5 * Math.Max(g.N * g.DTime, h.N * h.DTime);
        }

        /// <summary>Half-width of the interval around t = 0 that holds all but 1e-6 of the energy.</summary>
        private static double HalfExtentS(BeamPacket p)
        {
            var power = PulseMetrics.TemporalPower(p.Pulse);
            var time = p.Pulse.Grid.Time;
            double total = power.Sum(), outside = 0;
            foreach (int i in Enumerable.Range(0, power.Length).OrderByDescending(k => Math.Abs(time[k])))
            {
                outside += power[i];
                if (outside > 1e-6 * total) return Math.Abs(time[i]);
            }
            return 0;
        }

        private static BeamPacket Sum(BeamPacket a, BeamPacket b, MergeContext ctx)
        {
            if (!a.Path.PhaseKnown || !b.Path.PhaseKnown)
            {
                string w = "Beams meet after an element that does not report its carrier phase: the fringe position is arbitrary, the visibility is right.";
                if (!ctx.Warnings.Contains(w)) ctx.Warnings.Add(w);
            }
            // a different carrier: both go on the grid that spans more (a fundamental and its harmonic in an f-2f interferometer)
            var target = a.Pulse.Grid.N * a.Pulse.Grid.DOmega >= b.Pulse.Grid.N * b.Pulse.Grid.DOmega ? a.Pulse.Grid : b.Pulse.Grid;
            double tauRef = 0.5 * (a.Path.FrameDelayS + b.Path.FrameDelayS), phiRef = a.Path.PhaseRad;
            var pa = Rebased(a, target, tauRef, phiRef, target.OmegaCenter, ctx);
            var pb = Rebased(b, target, tauRef, phiRef, target.OmegaCenter, ctx);
            var path = a.Path.Clone();
            path.ArrivalS += tauRef - a.Path.FrameDelayS;
            path.FrameDelayS = tauRef; path.PhaseRad = phiRef;
            path.PhaseKnown = a.Path.PhaseKnown && b.Path.PhaseKnown;
            path.Tail = MergeTails(a, b, tauRef, phiRef);
            int n = target.N;
            var ax = new Complex[n]; var ay = new Complex[n];
            if (pa.Profile != null)
            {
                if (pa.Profile.Basis.M != pb.Profile.Basis.M) throw new Models.ModelException("Beams with carried profiles on different grids meet: use the same transverse grid in both arms.");
                var w = target.OmegaAbsolute;
                var ex = Transverse.TransverseOps.Expand(pa.Ax, w, pa.Profile); var ey = Transverse.TransverseOps.ExpandY(pa.Ay, w, pa.Profile);
                var fx = Transverse.TransverseOps.Expand(pb.Ax, w, pb.Profile); var fy = Transverse.TransverseOps.ExpandY(pb.Ay, w, pb.Profile);
                for (int i = 0; i < ex.Length; i++) { ex[i] += fx[i]; ey[i] += fy[i]; }
                var (cx, cy) = Transverse.TransverseOps.CollapseVector(ex, ey, pa.Profile.Basis, w, pa.Profile.Origin, out var prof);
                var q = new PulseField(target, cx, cy, a.Pulse.RepRateHz) { Profile = prof };
                return new BeamPacket { Pulse = q, Path = path };
            }
            var overlap = Overlap(a.Path, b.Path);
            for (int i = 0; i < n; i++) { ax[i] = pa.Ax[i] + overlap * pb.Ax[i]; ay[i] = pa.Ay[i] + overlap * pb.Ay[i]; }
            ctx.MismatchJ += (1 - overlap.Magnitude * overlap.Magnitude) * pb.TotalEnergyJ();
            return new BeamPacket { Pulse = a.Pulse.Grid == target ? a.Pulse.WithSpectrum(ax, ay) : new PulseField(target, ax, ay, a.Pulse.RepRateHz), Path = path };
        }

        private static TailLedger MergeTails(BeamPacket a, BeamPacket b, double tauRef, double phiRef)
        {
            if (a.Path.Tail == null || b.Path.Tail == null) return a.Path.Tail ?? b.Path.Tail;
            double dA = a.Path.FrameDelayS - tauRef, pA = a.Path.PhaseRad - phiRef, dB = b.Path.FrameDelayS - tauRef, pB = b.Path.PhaseRad - phiRef;
            var t = a.Path.Tail.Clone();
            t.Multiply(w => Complex.FromPolarCoordinates(1, pA + (w - a.Pulse.Grid.OmegaCenter) * dA));
            var u = b.Path.Tail.Clone();
            u.Multiply(w => Complex.FromPolarCoordinates(1, pB + (w - b.Pulse.Grid.OmegaCenter) * dB));
            t.Add(u);
            return t;
        }

        /// <summary>The spectrum on <paramref name="target"/>, in the frame of delay tauRef and phase phiRef at the target's carrier.</summary>
        private static PulseField Rebased(BeamPacket p, FrequencyGrid target, double tauRef, double phiRef, double omegaRef, MergeContext ctx)
        {
            var g = p.Pulse.Grid; int n = g.N;
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double w = g.OmegaAbsolute[i];
                var f = Complex.FromPolarCoordinates(1, p.Path.PhaseRad + (w - g.OmegaCenter) * p.Path.FrameDelayS - phiRef - (w - omegaRef) * tauRef);
                ax[i] = p.Pulse.Ax[i] * f; ay[i] = p.Pulse.Ay[i] * f;
            }
            var q = p.Pulse.WithSpectrum(ax, ay);
            q.Profile = p.Pulse.Profile;
            if (ReferenceEquals(g, target)) return q;
            var r = GridResampler.Resample(q, target, out double lost);
            r.Profile = p.Pulse.Profile;
            if (lost > 1e-3)
            {
                string w = string.Format(CultureInfo.InvariantCulture, "{0:P2} of a beam's energy is outside the grid two beams meet on.", lost);
                if (!ctx.Warnings.Contains(w)) ctx.Warnings.Add(w);
            }
            return r;
        }

        /// <summary>Amplitude overlap of two tracked Gaussian beams at one plane, with their tilts and offsets: the integral of u1* u2
        /// over x and y for unit-normalised u. 1 for identical beams; no tracked beams means perfect alignment is assumed.</summary>
        public static Complex Overlap(PathLedger a, PathLedger b)
        {
            if (a.Beam == null || b.Beam == null)
            {
                if (a.TiltXRad != b.TiltXRad || a.TiltYRad != b.TiltYRad || a.OffsetXM != b.OffsetXM || a.OffsetYM != b.OffsetYM)
                    throw new Models.ModelException("Beams with a tilt or offset meet, but there is no tracked beam to work out their overlap: give the run an inputBeam.");
                return Complex.One;
            }
            double k = a.Beam.K0;
            return Axis(a.Beam.Qx, b.Beam.Qx, k, b.TiltXRad - a.TiltXRad, b.OffsetXM - a.OffsetXM) * Axis(a.Beam.Qy, b.Beam.Qy, k, b.TiltYRad - a.TiltYRad, b.OffsetYM - a.OffsetYM);
        }

        /// <summary>One plane. With E = Re{A exp(-i w t)}, u = exp(-a x^2), a = -(k / 2)(Im + i Re)(1 / q).</summary>
        private static Complex Axis(Complex q1, Complex q2, double k, double tilt, double offset)
        {
            Complex Width(Complex q) { var iq = 1 / q; return -(k / 2) * new Complex(iq.Imaginary, iq.Real); }
            var a1 = Width(q1); var a2 = Width(q2);
            var A = Complex.Conjugate(a1) + a2;
            var B = 2 * a2 * offset + Complex.ImaginaryOne * k * tilt;
            return Complex.Sqrt(2 * Math.Sqrt(a1.Real * a2.Real) / A) * Complex.Exp(B * B / (4 * A) - a2 * offset * offset);
        }
    }

    /// <summary>The splitter family: a lossless 2x2 element, c = t a - r b, d = r a + t b (Stokes relations, r' = -r), with
    /// r and t by frequency and polarisation. Types differ only in their coefficients and port names (Compiler.CompileSplitter).
    /// <paramref name="side"/> = -1 reverses the sign of r: the same plate met from its other face.</summary>
    public sealed class BeamSplitterElement : IMultiPortElement, IReportsCarrierPhase
    {
        private readonly Func<double, (double Rx, double Ry)> _reflect;
        private readonly Func<double, double> _phaseR, _phaseT;
        private readonly double _side;
        private readonly string _a, _b, _c, _d;
        private readonly VirtualInput _virtual;

        public BeamSplitterElement(string kind, Func<double, (double Rx, double Ry)> reflect, Func<double, double> phaseR, Func<double, double> phaseT, double side,
            string a, string b, string c, string d, string continuingPort, VirtualInput virtualOnB)
        {
            Kind = kind; _reflect = reflect; _phaseR = phaseR; _phaseT = phaseT; _side = side;
            _a = a; _b = b; _c = c; _d = d; ContinuingPort = continuingPort; _virtual = virtualOnB;
        }

        public string Kind { get; }
        public string Description => Kind;
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;
        public string ContinuingPort { get; }

        /// <summary>Amplitude from an input to an output at one frequency, for x and y.</summary>
        public (Complex X, Complex Y) Coefficient(string inPort, string outPort, double omega)
        {
            bool onB = string.Equals(inPort, _b, StringComparison.OrdinalIgnoreCase), toC = string.Equals(outPort, _c, StringComparison.OrdinalIgnoreCase);
            var (rx, ry) = _reflect(omega);
            bool reflected = onB == toC;   // a -> d and b -> c are the reflections
            double sign = !reflected ? 1 : (onB ? -_side : _side);
            double phase = (reflected ? _phaseR : _phaseT)?.Invoke(omega) ?? 0;
            Complex X = (reflected ? Math.Sqrt(rx) : Math.Sqrt(1 - rx)) * sign, Y = (reflected ? Math.Sqrt(ry) : Math.Sqrt(1 - ry)) * sign;
            if (phase == 0) return (X, Y);
            var f = Complex.FromPolarCoordinates(1, phase);
            return (X * f, Y * f);
        }

        private PulseField Through(PulseField p, string inPort, string outPort)
        {
            int n = p.Grid.N;
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                if (p.Ax[i] == Complex.Zero && p.Ay[i] == Complex.Zero) continue;
                var (x, y) = Coefficient(inPort, outPort, p.Grid.OmegaAbsolute[i]);
                ax[i] = p.Ax[i] * x; ay[i] = p.Ay[i] * y;
            }
            var q = p.WithSpectrum(ax, ay);
            q.Profile = p.Profile;
            return q;
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            string port = ctx.InputPort ?? _a;
            var c = Through(p, port, _c); var d = Through(p, port, _d);
            if (_virtual != null && !ctx.InGraph)
            {
                var v = _virtual.Spec.Build(p.Grid, 0, ctx.Record.Notes);
                var f = Complex.FromPolarCoordinates(1, _virtual.PhaseRad);
                v = v.WithSpectrum(v.Ax.Select(z => z * f).ToArray(), v.Ay.Select(z => z * f).ToArray());
                ctx.Record.EnergyInJ += v.TotalEnergyJ();
                c = Added(c, Through(v, _b, _c)); d = Added(d, Through(v, _b, _d));
            }
            bool keepC = string.Equals(ContinuingPort, _c, StringComparison.OrdinalIgnoreCase);
            var other = keepC ? d : c;
            string otherPort = keepC ? _d : _c;
            ctx.Byproduct(otherPort, other);
            if (!ctx.InGraph) Executor.AddLoss(ctx.Record, "port " + otherPort + " (not tracked)", other.TotalEnergyJ());
            return keepC ? c : d;
        }

        private static PulseField Added(PulseField p, PulseField v)
        {
            int n = p.Grid.N;
            var ax = new Complex[n]; var ay = new Complex[n];
            for (int i = 0; i < n; i++) { ax[i] = p.Ax[i] + v.Ax[i]; ay[i] = p.Ay[i] + v.Ay[i]; }
            var q = p.WithSpectrum(ax, ay);
            q.Profile = p.Profile;
            return q;
        }
    }

    /// <summary>A beam tilted or offset against the axis. A carried profile takes the tilt as a phase (offsets need a grid move, so
    /// they go in the ledger only, for mode overlap); the path ledger keeps both.</summary>
    public sealed class BeamTiltElement : IActiveElement, IReportsCarrierPhase
    {
        private readonly double _tx, _ty, _ox, _oy;
        public BeamTiltElement(double tiltXRad, double tiltYRad, double offsetXM, double offsetYM) { _tx = tiltXRad; _ty = tiltYRad; _ox = offsetXM; _oy = offsetYM; }
        public string Kind => "beamTilt";
        public string Description => "tilt and offset of the beam";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (ctx.Path != null) { ctx.Path.TiltXRad += _tx; ctx.Path.TiltYRad += _ty; ctx.Path.OffsetXM += _ox; ctx.Path.OffsetYM += _oy; }
            if (p.Profile == null) return p;
            if (!(p.Profile.Basis is Transverse.CartesianBasis b)) { ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': a tilt of a carried profile needs a cartesian grid."); return p; }
            if (_ox != 0 || _oy != 0) ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': an offset is not applied to a carried profile.");
            var prof = p.Profile.Clone();
            int nf = prof.Omega.Length;
            for (int f = 0; f < nf; f++)
            {
                double k = prof.Omega[f] / Numerics.PhysicalConstants.SpeedOfLightMPerS;
                for (int m = 0; m < b.M; m++) prof.Data[m * nf + f] *= Complex.FromPolarCoordinates(1, k * (_tx * b.X[m] + _ty * b.Y[m]));
            }
            var q = p.WithSpectrum(p.Ax, p.Ay); q.Profile = prof;
            return q;
        }
    }

    /// <summary>A pure delay: phase omega tau, with no dispersion. In the co-moving frame it changes only the ledger.</summary>
    public sealed class DelayTerm : PhaseTerm
    {
        public double DelayS { get; }
        public DelayTerm(double delayS) { DelayS = delayS; }
        public override string Description => string.Format(CultureInfo.InvariantCulture, "delay {0:G6} fs", DelayS * 1e15);
        public override double GroupDelayAt(double omega) => DelayS;
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = omega[i] * DelayS; }
    }
}
