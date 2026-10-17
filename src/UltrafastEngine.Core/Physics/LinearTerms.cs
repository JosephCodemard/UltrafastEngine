using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Physics
{
    /// <summary>Linear dispersion at one frequency: absolute group delay, GDD, TOD.</summary>
    public struct DispersionPoint
    {
        public double GroupDelayS, GddS2, TodS3;
        public static DispersionPoint operator +(DispersionPoint a, DispersionPoint b) =>
            new DispersionPoint { GroupDelayS = a.GroupDelayS + b.GroupDelayS, GddS2 = a.GddS2 + b.GddS2, TodS3 = a.TodS3 + b.TodS3 };
    }

    /// <summary>
    /// One linear ingredient of an element (dispersion, a Fresnel interface, a coating, a clip loss, a filter,
    /// a waveplate...). Each term carries a <see cref="Mechanism"/> tag so the engine can build an exact loss
    /// budget: terms are applied one after the other and the energy removed by each is attributed to its
    /// mechanism.
    ///
    /// CO-MOVING FRAME: <see cref="Evaluate"/> returns the transfer function with the term's carrier phase and
    /// carrier group delay removed; the removed group delay is reported by <see cref="GroupDelayAt"/>(omega0) and
    /// accumulated by the engine as the pulse's lab-frame arrival time.
    ///
    /// To add a new kind of linear physics, derive from <see cref="PhaseTerm"/> (scalar phase + amplitude),
    /// <see cref="GroupDelayTerm"/> (when group delay is the natural quantity, e.g. from ray tracing) or
    /// <see cref="JonesTerm"/> (polarisation-dependent), and implement one method.
    /// </summary>
    public abstract class LinearTerm
    {
        /// <summary>"dispersion", "absorption", "fresnel", "coating", "clip", "scatter", "grating", "filter",
        /// "polarization", "custom"...</summary>
        public string Mechanism { get; set; } = "dispersion";
        public abstract string Description { get; }

        /// <summary>Absolute group delay (s) at omega (not frame-referenced).</summary>
        public abstract double GroupDelayAt(double omega);

        /// <summary>Frame-referenced transfer function on the axis.</summary>
        public abstract JonesSpectrum Evaluate(OmegaAxis axis);

        /// <summary>For scalar/diagonal terms: frame-referenced phase and amplitude per polarisation (Y may be
        /// null = same as X). Used to slice an element's linear operator inside nonlinear propagation.
        /// Returns false for terms that mix polarisations.</summary>
        public virtual bool TryPhaseAmplitude(OmegaAxis axis, out double[] phX, out double[] ampX, out double[] phY, out double[] ampY)
        {
            phX = ampX = phY = ampY = null;
            return false;
        }

        /// <summary>GD, GDD, TOD at omega by finite differences of the group delay.</summary>
        public virtual DispersionPoint DispersionAt(double omega)
        {
            double h = omega * 2e-4;
            double g0 = GroupDelayAt(omega), gp = GroupDelayAt(omega + h), gm = GroupDelayAt(omega - h);
            return new DispersionPoint { GroupDelayS = g0, GddS2 = (gp - gm) / (2 * h), TodS3 = (gp - 2 * g0 + gm) / (h * h) };
        }

        /// <summary>Power transmission (unpolarised average) at omega -- for reports.</summary>
        public virtual double PowerTransmissionAt(double omega)
        {
            var ax = new OmegaAxis(new[] { omega }, 0);
            return Evaluate(ax).MeanPowerTransmission(0);
        }

        /// <summary>True if this term only removes energy uniformly (no phase, no spectral shape).</summary>
        public virtual bool IsFlat => false;

        internal static double SafeAmp(double a) => double.IsNaN(a) || a < 0 ? 0.0 : a;
    }

    // =========================================================================================== PhaseTerm

    /// <summary>A scalar (or birefringent-diagonal) term defined by an absolute spectral phase phi(omega) and
    /// amplitude transmission a(omega). Frame removal: phi - phi(w0) - tau0 (w - w0), tau0 = dphi/dw(w0).</summary>
    public abstract class PhaseTerm : LinearTerm
    {
        /// <summary>Absolute phase (rad). Fill phiOut; may contain NaN where undefined (amplitude is zeroed there).</summary>
        protected abstract void Phase(double[] omega, double[] phiOut);
        /// <summary>Amplitude transmission (sqrt of power transmission). Default 1.</summary>
        protected virtual void Amplitude(double[] omega, double[] ampOut) { for (int i = 0; i < omega.Length; i++) ampOut[i] = 1.0; }
        /// <summary>Optional different phase for y (birefringence). Return false if isotropic.</summary>
        protected virtual bool PhaseY(double[] omega, double[] phiOut) => false;

        public override double GroupDelayAt(double omega)
        {
            double h = omega * 1e-4;
            var w = new[] { omega - 2 * h, omega - h, omega + h, omega + 2 * h };
            var p = new double[4];
            Phase(w, p);
            return (p[0] - 8 * p[1] + 8 * p[2] - p[3]) / (12 * h);
        }

        public override DispersionPoint DispersionAt(double omega)
        {
            double h = omega * 2e-3; // large enough that round-off of the (huge) absolute phase does not swamp TOD
            var w = new double[7]; for (int k = 0; k < 7; k++) w[k] = omega + (k - 3) * h;
            var p = new double[7];
            Phase(w, p);
            // 7-point stencils for 1st..3rd derivative
            double d1 = (-p[0] + 9 * p[1] - 45 * p[2] + 45 * p[4] - 9 * p[5] + p[6]) / (60 * h);
            double d2 = (2 * p[0] - 27 * p[1] + 270 * p[2] - 490 * p[3] + 270 * p[4] - 27 * p[5] + 2 * p[6]) / (180 * h * h);
            double d3 = (p[0] - 8 * p[1] + 13 * p[2] - 13 * p[4] + 8 * p[5] - p[6]) / (8 * h * h * h);
            return new DispersionPoint { GroupDelayS = d1, GddS2 = d2, TodS3 = d3 };
        }

        private double[] FramePhase(OmegaAxis axis, Func<double[], double[], bool> phaseFn, double tau0, double phi0)
        {
            var phi = new double[axis.N];
            if (!phaseFn(axis.Omega, phi)) return null;
            for (int i = 0; i < axis.N; i++) phi[i] = phi[i] - phi0 - tau0 * (axis.Omega[i] - axis.Omega0);
            return phi;
        }

        public override bool TryPhaseAmplitude(OmegaAxis axis, out double[] phX, out double[] ampX, out double[] phY, out double[] ampY)
        {
            double w0 = axis.Omega0;
            double tau0 = GroupDelayAt(w0);
            var p0 = new double[1]; Phase(new[] { w0 }, p0);
            phX = FramePhase(axis, (w, o) => { Phase(w, o); return true; }, tau0, p0[0]);
            phY = FramePhase(axis, PhaseY, tau0, p0[0]); // same reference -> relative x/y phase kept
            ampX = new double[axis.N];
            Amplitude(axis.Omega, ampX);
            ampY = null;
            Sanitize(phX, ampX);
            if (phY != null) { ampY = (double[])ampX.Clone(); Sanitize(phY, ampY); }
            return true;
        }

        /// <summary>Non-finite phase: zero amplitude there and continue the phase linearly (keeps the operator finite).</summary>
        internal static void Sanitize(double[] ph, double[] amp)
        {
            int n = ph.Length;
            for (int i = 0; i < n; i++)
            {
                if (!double.IsNaN(amp[i]) && !double.IsInfinity(amp[i]) && amp[i] >= 0) continue;
                amp[i] = 0;
            }
            int firstGood = -1;
            for (int i = 0; i < n; i++) if (!double.IsNaN(ph[i]) && !double.IsInfinity(ph[i])) { firstGood = i; break; }
            if (firstGood < 0) { for (int i = 0; i < n; i++) { ph[i] = 0; amp[i] = 0; } return; }
            for (int i = 0; i < firstGood; i++) { ph[i] = ph[firstGood]; amp[i] = 0; }
            for (int i = firstGood + 1; i < n; i++)
                if (double.IsNaN(ph[i]) || double.IsInfinity(ph[i])) { ph[i] = ph[i - 1]; amp[i] = 0; }
        }

        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            TryPhaseAmplitude(axis, out var px, out var ax, out var py, out var ay);
            var hx = new Complex[axis.N];
            for (int i = 0; i < axis.N; i++) hx[i] = Complex.FromPolarCoordinates(ax[i], px[i]);
            if (py == null) return JonesSpectrum.Scalar(hx);
            var hy = new Complex[axis.N];
            for (int i = 0; i < axis.N; i++) hy[i] = Complex.FromPolarCoordinates(ay[i], py[i]);
            return JonesSpectrum.Diagonal(hx, hy);
        }
    }

    // ===================================================================================== GroupDelayTerm

    /// <summary>A term defined by its absolute group delay tau(omega) (and optional power transmission). The
    /// frame-referenced phase is the integral of tau - tau(w0) from the carrier outwards (trapezoid rule on the
    /// uniform axis) -- exact for any length, and robust for metre-long systems whose absolute phase is huge.</summary>
    public abstract class GroupDelayTerm : LinearTerm
    {
        protected abstract void GroupDelay(double[] omega, double[] tauOut);
        protected virtual bool GroupDelayY(double[] omega, double[] tauOut) => false;
        /// <summary>Power transmission (default 1).</summary>
        protected virtual void PowerTransmission(double[] omega, double[] tOut) { for (int i = 0; i < omega.Length; i++) tOut[i] = 1.0; }

        public override double GroupDelayAt(double omega) { var t = new double[1]; GroupDelay(new[] { omega }, t); return t[0]; }

        public override DispersionPoint DispersionAt(double omega)
        {
            double h = omega * 2e-4;
            var w = new[] { omega - 2 * h, omega - h, omega, omega + h, omega + 2 * h };
            var t = new double[5]; GroupDelay(w, t);
            return new DispersionPoint
            {
                GroupDelayS = t[2],
                GddS2 = (t[0] - 8 * t[1] + 8 * t[3] - t[4]) / (12 * h),
                TodS3 = (-t[0] + 16 * t[1] - 30 * t[2] + 16 * t[3] - t[4]) / (12 * h * h)
            };
        }

        internal static double[] IntegrateFromCarrier(OmegaAxis axis, double[] tau, double tau0)
        {
            int n = axis.N, c = axis.CarrierIndex;
            var phi = new double[n];
            for (int i = c + 1; i < n; i++) phi[i] = phi[i - 1] + 0.5 * ((tau[i - 1] - tau0) + (tau[i] - tau0)) * (axis.Omega[i] - axis.Omega[i - 1]);
            for (int i = c - 1; i >= 0; i--) phi[i] = phi[i + 1] - 0.5 * ((tau[i + 1] - tau0) + (tau[i] - tau0)) * (axis.Omega[i + 1] - axis.Omega[i]);
            return phi;
        }

        public override bool TryPhaseAmplitude(OmegaAxis axis, out double[] phX, out double[] ampX, out double[] phY, out double[] ampY)
        {
            var tau = new double[axis.N];
            GroupDelay(axis.Omega, tau);
            double tau0 = GroupDelayAt(axis.Omega0);
            var amp = new double[axis.N];
            PowerTransmission(axis.Omega, amp);
            for (int i = 0; i < amp.Length; i++) amp[i] = Math.Sqrt(SafeAmp(amp[i]));
            for (int i = 0; i < tau.Length; i++) if (double.IsNaN(tau[i]) || double.IsInfinity(tau[i])) { tau[i] = double.NaN; amp[i] = 0; }
            FillGaps(tau, tau0);
            phX = IntegrateFromCarrier(axis, tau, tau0);
            ampX = amp;
            var tauY = new double[axis.N];
            if (GroupDelayY(axis.Omega, tauY)) { FillGaps(tauY, tau0); phY = IntegrateFromCarrier(axis, tauY, tau0); ampY = (double[])amp.Clone(); }
            else { phY = null; ampY = null; }
            return true;
        }

        private static void FillGaps(double[] tau, double fallback)
        {
            int n = tau.Length;
            int first = Array.FindIndex(tau, v => !double.IsNaN(v));
            if (first < 0) { for (int i = 0; i < n; i++) tau[i] = fallback; return; }
            for (int i = 0; i < first; i++) tau[i] = tau[first];
            for (int i = first + 1; i < n; i++) if (double.IsNaN(tau[i])) tau[i] = tau[i - 1];
        }

        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            TryPhaseAmplitude(axis, out var px, out var ax, out var py, out var ay);
            var hx = new Complex[axis.N];
            for (int i = 0; i < axis.N; i++) hx[i] = Complex.FromPolarCoordinates(ax[i], px[i]);
            if (py == null) return JonesSpectrum.Scalar(hx);
            var hy = new Complex[axis.N];
            for (int i = 0; i < axis.N; i++) hy[i] = Complex.FromPolarCoordinates(ay[i], py[i]);
            return JonesSpectrum.Diagonal(hx, hy);
        }
    }

    // =========================================================================================== JonesTerm

    /// <summary>A polarisation-dependent term given as a full 2x2 Jones matrix per frequency (absolute phase).
    /// The common phase (from the trace, or det^(1/2) when the trace vanishes) is frame-referenced.</summary>
    public abstract class JonesTerm : LinearTerm
    {
        protected abstract void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy);

        private Complex RefValue(double w)
        {
            var xx = new Complex[1]; var xy = new Complex[1]; var yx = new Complex[1]; var yy = new Complex[1];
            Jones(new[] { w }, xx, xy, yx, yy);
            return Reference(xx[0], xy[0], yx[0], yy[0]);
        }

        private static Complex Reference(Complex xx, Complex xy, Complex yx, Complex yy)
        {
            var tr = xx + yy;
            double scale = xx.Magnitude + yy.Magnitude + xy.Magnitude + yx.Magnitude;
            if (scale == 0) return Complex.One;
            if (tr.Magnitude >= 0.2 * scale) return tr;
            var det = xx * yy - xy * yx;
            if (det.Magnitude > 1e-6 * scale * scale) return Complex.Sqrt(det);
            return xx.Magnitude >= yy.Magnitude ? xx : yy;
        }

        public override double GroupDelayAt(double omega)
        {
            double h = omega * 1e-5;
            var a = RefValue(omega - h); var b = RefValue(omega + h);
            double d = (b / a).Phase; // small-step unwrapped difference
            return d / (2 * h);
        }

        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            int n = axis.N;
            var xx = new Complex[n]; var xy = new Complex[n]; var yx = new Complex[n]; var yy = new Complex[n];
            Jones(axis.Omega, xx, xy, yx, yy);
            double tau0 = GroupDelayAt(axis.Omega0);
            var r0 = RefValue(axis.Omega0);
            double phi0 = r0.Phase;
            var s = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                s[i] = Complex.FromPolarCoordinates(1.0, -(phi0 + tau0 * (axis.Omega[i] - axis.Omega0)));
                if (double.IsNaN(xx[i].Real) || double.IsNaN(yy[i].Real)) { xx[i] = xy[i] = yx[i] = yy[i] = Complex.Zero; }
            }
            bool diag = xy.All(c => c == Complex.Zero) && yx.All(c => c == Complex.Zero);
            var j = diag ? JonesSpectrum.Diagonal(xx, yy) : JonesSpectrum.Full(xx, xy, yx, yy);
            j.ScaleBy(s);
            return j;
        }
    }

    // =========================================================================================== concrete

    /// <summary>Propagation through a homogeneous material: phi = n(w) w L / c (lossless; see AttenuationTerm).</summary>
    public sealed class BulkTerm : PhaseTerm
    {
        public Material Material { get; }
        public double LengthM { get; }
        public BulkTerm(Material m, double lengthM) { Material = m; LengthM = lengthM; Mechanism = "dispersion"; }
        public override string Description => Material.Name + ", " + (LengthM * 1e3).ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + " mm";
        protected override void Phase(double[] omega, double[] phi)
        {
            Material.Index.Evaluate(omega, phi);
            double k = LengthM / PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < omega.Length; i++) phi[i] = phi[i] * omega[i] * k;
        }
        // Attenuation is deliberately NOT applied here: the compiler adds a separate AttenuationTerm
        // (mechanism "absorption") so the loss budget can tell dispersion and absorption apart.
    }

    /// <summary>Material attenuation as its own term (mechanism "absorption"), so the loss budget separates it
    /// from dispersion when a bulk element is split into dispersion + absorption.</summary>
    public sealed class AttenuationTerm : PhaseTerm
    {
        private readonly IAttenuation _a; private readonly double _l; private readonly string _name;
        public AttenuationTerm(IAttenuation a, double lengthM, string name) { _a = a; _l = lengthM; _name = name; Mechanism = "absorption"; }
        public override string Description => "absorption in " + _name;
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = 0; }
        protected override void Amplitude(double[] omega, double[] amp)
        {
            _a.Evaluate(omega, amp);
            for (int i = 0; i < omega.Length; i++) amp[i] = Math.Exp(-0.5 * amp[i] * _l);
        }
    }

    /// <summary>Sampled absolute group delay (e.g. Zemax group path / c along the real ray) with optional
    /// sampled power transmission. Cubic spline in omega, continued with constant GDD outside the data.</summary>
    public sealed class SampledGroupDelayTerm : GroupDelayTerm
    {
        private readonly double[] _w, _tx, _ty, _wt, _t;
        private readonly NaturalCubicSpline _sx, _sy;
        public string Label { get; }
        public double MinOmega => _w[0];
        public double MaxOmega => _w[_w.Length - 1];

        public SampledGroupDelayTerm(string label, double[] omega, double[] tauX, double[] tauY = null, double[] omegaT = null, double[] powerT = null)
        {
            Label = label;
            var ord = Enumerable.Range(0, omega.Length).Where(i => !double.IsNaN(tauX[i])).OrderBy(i => omega[i]).ToArray();
            if (ord.Length == 0) throw new ArgumentException(label + ": no finite group-delay samples.");
            _w = ord.Select(i => omega[i]).ToArray();
            _tx = ord.Select(i => tauX[i]).ToArray();
            if (tauY != null) _ty = ord.Select(i => tauY[i]).ToArray();
            if (_w.Length >= 4) { _sx = new NaturalCubicSpline(_w, _tx); if (_ty != null) _sy = new NaturalCubicSpline(_w, _ty); }
            if (powerT != null)
            {
                var o2 = Enumerable.Range(0, (omegaT ?? omega).Length).OrderBy(i => (omegaT ?? omega)[i]).ToArray();
                _wt = o2.Select(i => (omegaT ?? omega)[i]).ToArray();
                _t = o2.Select(i => powerT[i]).ToArray();
            }
            Mechanism = "dispersion";
        }

        public override string Description => Label + ": sampled group delay (" + _w.Length + " wavelengths)";

        private double Eval(double[] y, NaturalCubicSpline s, double w)
        {
            if (s != null) return s.EvaluateLinearExtrapolate(w);
            if (y.Length == 1) return y[0];
            double slope = (y[y.Length - 1] - y[0]) / (_w[_w.Length - 1] - _w[0]);
            if (w <= _w[0]) return y[0] + slope * (w - _w[0]);
            if (w >= _w[_w.Length - 1]) return y[y.Length - 1] + slope * (w - _w[_w.Length - 1]);
            return new LinearInterpolator(_w, y).Evaluate(w);
        }

        protected override void GroupDelay(double[] omega, double[] tau) { for (int i = 0; i < omega.Length; i++) tau[i] = Eval(_tx, _sx, omega[i]); }
        protected override bool GroupDelayY(double[] omega, double[] tau)
        {
            if (_ty == null) return false;
            for (int i = 0; i < omega.Length; i++) tau[i] = Eval(_ty, _sy, omega[i]);
            return true;
        }
        protected override void PowerTransmission(double[] omega, double[] t)
        {
            if (_t == null) { base.PowerTransmission(omega, t); return; }
            if (_t.Length == 1) { for (int i = 0; i < omega.Length; i++) t[i] = _t[0]; return; }
            var li = new LinearInterpolator(_wt, _t);
            for (int i = 0; i < omega.Length; i++)
                t[i] = Math.Max(0, Math.Min(1, omega[i] <= _wt[0] ? _t[0] : omega[i] >= _wt[_wt.Length - 1] ? _t[_t.Length - 1] : li.Evaluate(omega[i])));
        }
    }

    /// <summary>
    /// Treacy grating pair (parallel gratings, perpendicular separation G, groove density 1/d, diffraction order
    /// m): sin(theta_i) + sin(theta_d) = |m| lambda / d, group delay tau = (G/c)(1 + cos(theta_i - theta_d))/cos(theta_d).
    /// Gives the classic GDD = -m^2 lambda^3 G / (2 pi c^2 d^2 cos^3 theta_d) per pass. "passes" multiplies
    /// (2 = double-passed compressor, spatial chirp removed). Negative separation models the stretcher
    /// (Martinez) equivalent with an effective negative distance.
    /// </summary>
    public sealed class GratingPairTerm : GroupDelayTerm
    {
        public double LinesPerM { get; } public double AoiRad { get; } public double SeparationM { get; } public int Order { get; } public int Passes { get; }
        public double EfficiencyPerGrating { get; }
        public GratingPairTerm(double linesPerM, double aoiRad, double separationM, int order, int passes, double efficiencyPerGrating)
        {
            LinesPerM = linesPerM; AoiRad = aoiRad; SeparationM = separationM; Order = order == 0 ? -1 : order; Passes = Math.Max(1, passes);
            EfficiencyPerGrating = efficiencyPerGrating;
            Mechanism = "dispersion";
        }
        public override string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "grating pair {0:G5} l/mm, AOI {1:G4} deg, G = {2:G5} mm, order {3}, {4} pass(es)", LinesPerM * 1e-3, AoiRad * 180 / Math.PI, SeparationM * 1e3, Order, Passes);
        protected override void GroupDelay(double[] omega, double[] tau)
        {
            double d = 1.0 / LinesPerM, c = PhysicalConstants.SpeedOfLightMPerS;
            for (int i = 0; i < omega.Length; i++)
            {
                double lam = 2 * Math.PI * c / omega[i];
                double s = Math.Abs(Order) * lam / d - Math.Sin(AoiRad);
                if (Math.Abs(s) >= 1 || omega[i] <= 0) { tau[i] = double.NaN; continue; }
                double td = Math.Asin(s);
                tau[i] = Passes * SeparationM / c * (1 + Math.Cos(AoiRad - td)) / Math.Cos(td);
            }
        }
        protected override void PowerTransmission(double[] omega, double[] t)
        {
            double eff = Math.Pow(EfficiencyPerGrating, 2 * Passes);
            for (int i = 0; i < omega.Length; i++) t[i] = eff;
        }
        public static double TreacyGdd(double lambda, double linesPerM, double aoiRad, double separationM, int order)
        {
            double d = 1.0 / linesPerM, c = PhysicalConstants.SpeedOfLightMPerS;
            double td = Math.Asin(Math.Abs(order) * lambda / d - Math.Sin(aoiRad));
            return -order * order * Math.Pow(lambda, 3) * separationM / (2 * Math.PI * c * c * d * d * Math.Pow(Math.Cos(td), 3));
        }
    }

    /// <summary>Taylor-series phase about a reference frequency: phi = GD dw + GDD dw^2/2 + TOD dw^3/6 + FOD dw^4/24,
    /// optional flat power transmission. Used by "dispersion" elements given as numbers, chirped mirrors, and
    /// ideal "gdd" coatings.</summary>
    public sealed class PolynomialPhaseTerm : PhaseTerm
    {
        public double OmegaRef { get; } public double Gd { get; } public double Gdd { get; } public double Tod { get; } public double Fod { get; }
        public double PowerTransmissionValue { get; }
        private readonly string _label;
        public PolynomialPhaseTerm(string label, double omegaRef, double gd, double gdd, double tod, double fod, double powerTransmission = 1.0)
        {
            _label = label; OmegaRef = omegaRef; Gd = gd; Gdd = gdd; Tod = tod; Fod = fod; PowerTransmissionValue = powerTransmission;
        }
        public override string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}: GDD {1:G4} fs^2, TOD {2:G4} fs^3{3}", _label, Gdd * 1e30, Tod * 1e45, PowerTransmissionValue < 1 ? ", T = " + PowerTransmissionValue.ToString("G5", System.Globalization.CultureInfo.InvariantCulture) : "");
        protected override void Phase(double[] omega, double[] phi)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double d = omega[i] - OmegaRef;
                phi[i] = Gd * d + Gdd * d * d / 2 + Tod * d * d * d / 6 + Fod * d * d * d * d / 24;
            }
        }
        protected override void Amplitude(double[] omega, double[] amp) { double a = Math.Sqrt(PowerTransmissionValue); for (int i = 0; i < omega.Length; i++) amp[i] = a; }
        public override bool IsFlat => Gd == 0 && Gdd == 0 && Tod == 0 && Fod == 0;
    }

    /// <summary>A spectral filter (any <see cref="ISpectralFilter"/>).</summary>
    public sealed class FilterTerm : PhaseTerm
    {
        public ISpectralFilter Filter { get; }
        public FilterTerm(ISpectralFilter f, string mechanism = "filter") { Filter = f; Mechanism = mechanism; }
        public override string Description => Filter.Description;
        protected override void Phase(double[] omega, double[] phi) { var t = new double[omega.Length]; Filter.Evaluate(omega, t, phi); }
        protected override void Amplitude(double[] omega, double[] amp)
        {
            var ph = new double[omega.Length];
            Filter.Evaluate(omega, amp, ph);
            for (int i = 0; i < omega.Length; i++) amp[i] = Math.Sqrt(SafeAmp(amp[i]));
        }
    }

    /// <summary>A flat (wavelength-independent) power loss: clipping, scatter, a "transmission" number.</summary>
    public sealed class FlatLossTerm : PhaseTerm
    {
        public double PowerTransmissionValue { get; }
        private readonly string _d;
        public FlatLossTerm(double powerTransmission, string mechanism, string description) { PowerTransmissionValue = Math.Max(0, Math.Min(1, powerTransmission)); Mechanism = mechanism; _d = description; }
        public override string Description => _d + " (T = " + PowerTransmissionValue.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + ")";
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = 0; }
        protected override void Amplitude(double[] omega, double[] amp) { double a = Math.Sqrt(PowerTransmissionValue); for (int i = 0; i < omega.Length; i++) amp[i] = a; }
        public override bool IsFlat => true;
        public override double GroupDelayAt(double omega) => 0;
    }

    /// <summary>Wavelength-dependent power loss given per sample (e.g. clip with lambda-dependent beam size, scatter TIS).</summary>
    public sealed class SampledLossTerm : PhaseTerm
    {
        private readonly double[] _w, _t; private readonly string _d;
        public SampledLossTerm(double[] omega, double[] powerT, string mechanism, string description)
        {
            var o = Enumerable.Range(0, omega.Length).OrderBy(i => omega[i]).ToArray();
            _w = o.Select(i => omega[i]).ToArray(); _t = o.Select(i => powerT[i]).ToArray(); Mechanism = mechanism; _d = description;
        }
        public override string Description => _d;
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = 0; }
        protected override void Amplitude(double[] omega, double[] amp)
        {
            if (_w.Length == 1) { for (int i = 0; i < omega.Length; i++) amp[i] = Math.Sqrt(_t[0]); return; }
            var li = new LinearInterpolator(_w, _t);
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i];
                double t = w <= _w[0] ? _t[0] : w >= _w[_w.Length - 1] ? _t[_t.Length - 1] : li.Evaluate(w);
                amp[i] = Math.Sqrt(Math.Max(0, Math.Min(1, t)));
            }
        }
        public override double GroupDelayAt(double omega) => 0;
    }

    /// <summary>Waveplate: retardance Gamma(w) = 2 pi |delta n| d / lambda; either from a birefringent material pair
    /// or from a design retardance (in waves) at a design wavelength with Gamma proportional to omega
    /// (dispersionless birefringence). Fast axis at angle theta from x. The plate's common (mean-index) phase is
    /// not included -- add a bulk element for the plate material if its dispersion matters.</summary>
    public sealed class WaveplateTerm : JonesTerm
    {
        public double RetardanceWaves { get; } public double DesignOmega { get; } public double AxisAngleRad { get; }
        public WaveplateTerm(double retardanceWaves, double designOmega, double axisAngleRad) { RetardanceWaves = retardanceWaves; DesignOmega = designOmega; AxisAngleRad = axisAngleRad; Mechanism = "polarization"; }
        public override string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture, "waveplate {0:G4} waves, axis {1:G4} deg", RetardanceWaves, AxisAngleRad * 180 / Math.PI);
        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double g = 2 * Math.PI * RetardanceWaves * omega[i] / DesignOmega;
                JonesSpectrum.RotatedDiagonal(AxisAngleRad, Complex.FromPolarCoordinates(1, -g / 2), Complex.FromPolarCoordinates(1, g / 2), out xx[i], out xy[i], out yx[i], out yy[i]);
            }
        }
        public override double GroupDelayAt(double omega) => 0; // symmetric +-Gamma/2: no common delay
    }

    /// <summary>Linear polariser at angle theta with power extinction ratio ER (transmitted/blocked).</summary>
    public sealed class PolarizerTerm : JonesTerm
    {
        public double AxisAngleRad { get; } public double ExtinctionRatio { get; } public double Transmission { get; }
        public PolarizerTerm(double axisAngleRad, double extinctionRatio, double transmission) { AxisAngleRad = axisAngleRad; ExtinctionRatio = extinctionRatio; Transmission = transmission; Mechanism = "polarization"; }
        public override string Description => string.Format(System.Globalization.CultureInfo.InvariantCulture, "polarizer at {0:G4} deg, ER {1:G3}", AxisAngleRad * 180 / Math.PI, ExtinctionRatio);
        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            double a = Math.Sqrt(Transmission), b = Math.Sqrt(Transmission / Math.Max(1.0, ExtinctionRatio));
            for (int i = 0; i < omega.Length; i++) JonesSpectrum.RotatedDiagonal(AxisAngleRad, a, b, out xx[i], out xy[i], out yx[i], out yy[i]);
        }
        public override double GroupDelayAt(double omega) => 0;
    }

    /// <summary>Rotation of the polarisation basis (or of the field) by an angle -- e.g. an image-rotating periscope
    /// or a Faraday rotator: [[c, -s],[s, c]].</summary>
    public sealed class RotatorTerm : JonesTerm
    {
        public double AngleRad { get; }
        public RotatorTerm(double angleRad) { AngleRad = angleRad; Mechanism = "polarization"; }
        public override string Description => "polarization rotation " + (AngleRad * 180 / Math.PI).ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " deg";
        protected override void Jones(double[] omega, Complex[] xx, Complex[] xy, Complex[] yx, Complex[] yy)
        {
            double c = Math.Cos(AngleRad), s = Math.Sin(AngleRad);
            for (int i = 0; i < omega.Length; i++) { xx[i] = c; xy[i] = -s; yx[i] = s; yy[i] = c; }
        }
        public override double GroupDelayAt(double omega) => 0;
    }
}
