using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// A smooth random function of the angular frequency whose structure lives at delays 0 .. maxDelay: N(w) = sqrt(2/K) sum_k cos(w tau_k + phi_k),
    /// tau_k uniform in (0, maxDelay), phi_k uniform in (0, 2 pi), K components; unit variance. It depends on the absolute w only, so
    /// the main grid and the tail grid see the same function, and it is reproducible from the seed.
    /// </summary>
    public sealed class DelayBandNoise
    {
        private readonly double[] _tau, _phi; private readonly double _norm;
        public double MaxDelayS { get; }
        public DelayBandNoise(double maxDelayS, int components, int seed)
        {
            MaxDelayS = maxDelayS;
            var rnd = new Random(seed);
            _tau = new double[components]; _phi = new double[components];
            for (int k = 0; k < components; k++) { _tau[k] = maxDelayS * rnd.NextDouble(); _phi[k] = 2 * Math.PI * rnd.NextDouble(); }
            _norm = Math.Sqrt(2.0 / components);
        }
        public double At(double omega, double omega0)
        {
            double s = 0;
            for (int k = 0; k < _tau.Length; k++) s += Math.Cos((omega - omega0) * _tau[k] + _phi[k]);
            return _norm * s;
        }
    }

    /// <summary>
    /// Phase and amplitude ripple of a mirror coating (the stochastic version; measured ripple is a "table" coating): the reflection is
    /// multiplied by A(w) exp(i phi(w)), phi = phaseRmsRad N1(w), A = min(1, 1 + amplitudeRms N2(w)) (a ripple only takes light away).
    /// N1, N2 are independent random functions whose structure lies at delays 0 .. maxDelayS, so the first-order pedestal
    /// (a fraction phaseRms^2 of the energy) sits within +-maxDelayS of the pulse -- 5 ps by default -- and weaker multiples of it
    /// within +-n maxDelayS.
    /// </summary>
    public sealed class CoatingRippleTerm : PhaseTerm
    {
        private readonly DelayBandNoise _ph, _am; private readonly double _phaseRms, _ampRms, _w0;
        public double PhaseRmsRad => _phaseRms; public double AmplitudeRms => _ampRms; public double MaxDelayS => _ph.MaxDelayS;
        public CoatingRippleTerm(double phaseRmsRad, double amplitudeRms, double maxDelayS, int seed, int components, double referenceOmega)
        {
            _phaseRms = phaseRmsRad; _ampRms = amplitudeRms; _w0 = referenceOmega;
            _ph = new DelayBandNoise(maxDelayS, components, seed); _am = new DelayBandNoise(maxDelayS, components, seed + 7919);
            Mechanism = "coating";
        }
        public override string Description => string.Format(CultureInfo.InvariantCulture, "coating ripple (phase {0:G3} rad rms, amplitude {1:G3} rms, structure within {2:G3} ps)", _phaseRms, _ampRms, _ph.MaxDelayS * 1e12);
        protected override void Phase(double[] omega, double[] phi) { for (int i = 0; i < omega.Length; i++) phi[i] = _phaseRms * _ph.At(omega[i], _w0); }
        protected override void Amplitude(double[] omega, double[] amp)
        {
            for (int i = 0; i < omega.Length; i++) amp[i] = _ampRms > 0 ? Math.Min(1.0, Math.Max(0.0, 1.0 + _ampRms * _am.At(omega[i], _w0))) : 1.0;
        }
    }

    /// <summary>The delays of delayed scatter: uniform in [min, max], exponential with a time constant, or a table of delays and weights.</summary>
    public sealed class DelayKernel
    {
        public double[] DelaysS; public double[] Weights;     // weights sum to 1
        public double MaxDelayS => DelaysS.Length == 0 ? 0 : DelaysS.Max();
        public double MeanDelayS => DelaysS.Zip(Weights, (d, w) => d * w).Sum();

        public static DelayKernel Uniform(double minS, double maxS, int count)
        {
            var d = new double[count]; for (int k = 0; k < count; k++) d[k] = minS + (maxS - minS) * (k + 0.5) / count;
            return new DelayKernel { DelaysS = d, Weights = Enumerable.Repeat(1.0 / count, count).ToArray() };
        }
        public static DelayKernel Exponential(double tauS, double maxS, int count)
        {
            var d = new double[count]; var w = new double[count];
            for (int k = 0; k < count; k++) { d[k] = maxS * (k + 0.5) / count; w[k] = Math.Exp(-d[k] / tauS); }
            double s = w.Sum(); for (int k = 0; k < count; k++) w[k] /= s;
            return new DelayKernel { DelaysS = d, Weights = w };
        }
        public static DelayKernel Table(double[] delays, double[] weights)
        {
            if (delays.Length != weights.Length || delays.Length == 0) throw new Serialization.JsonException("a delay table needs delaysS and weights of the same length.");
            double s = weights.Sum(); if (!(s > 0)) throw new Serialization.JsonException("delay weights must sum to a positive number.");
            return new DelayKernel { DelaysS = (double[])delays.Clone(), Weights = weights.Select(w => w / s).ToArray() };
        }
    }

    /// <summary>
    /// Surface scatter from the roughness, as delayed light (the analytic model of <c>delayedScatter.model = "surface"</c>). Per surface:
    /// the total integrated scatter TIS(w) of a surface of rms roughness sigma (Davies / Bennett, see ScatterTerm), of which the part inside
    /// the angular acceptance of the Gaussian mode (radius w) returns into the beam. With a Gaussian roughness correlation of length l, the
    /// angular power spectrum of the scatter is S(f) = pi sigma^2 l^2 exp(-pi^2 l^2 f^2) (f = sin(theta) / lambda); the mode of radius w accepts
    /// theta &lt; lambda / (pi w), i.e. f &lt; 1 / (pi w) -- independent of lambda -- so the share that stays in the mode is
    /// <c>g = 1 - exp(-(l / w)^2)</c>. The transfer is H = sqrt(1 - TIS) + sqrt(g TIS) xi(w): the part (1 - g) TIS leaves the beam (a loss booked as
    /// "scatter"), g TIS returns after the delays of the kernel with random phases (xi: unit power, see <see cref="DelayedScatterTerm"/>).
    /// The wavelength law is that of the TIS (lambda^-2). Without a delay
    /// the returned part is indistinguishable from the beam and the transfer is the real number sqrt(1 - (1 - g) TIS).
    /// </summary>
    public sealed class SurfaceScatterTerm : LinearTerm
    {
        private readonly Func<double, double> _tis; private readonly double _g; private readonly double[] _tau; private readonly Complex[] _c;
        public double InModeShare => _g;
        public SurfaceScatterTerm(Func<double, double> totalIntegratedScatter, double correlationLengthM, double beamRadiusM, DelayKernel kernel, int seed)
        {
            _tis = totalIntegratedScatter;
            _g = 1 - Math.Exp(-Math.Pow(correlationLengthM / beamRadiusM, 2));
            _tau = kernel.DelaysS; _c = DelayedScatterTerm.Amplitudes(kernel, seed);
            Mechanism = "scatter";
        }
        public override string Description => string.Format(CultureInfo.InvariantCulture, "surface scatter (analytic): {0:G3} of the TIS stays in the mode and returns after {1:G3} .. {2:G3} ps", _g, _tau.Min() * 1e12, _tau.Max() * 1e12);
        public override double GroupDelayAt(double omega) => 0;
        public override DispersionPoint DispersionAt(double omega) => new DispersionPoint();
        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            int n = axis.N; var h = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                double w = axis.Omega[i];
                double tis = w > 0 ? Math.Min(1.0, Math.Max(0.0, _tis(w))) : 0;
                if (_tau.Max() <= 0) { h[i] = Math.Sqrt(1 - (1 - _g) * tis); continue; }   // no delay: the in-mode part is the beam itself, only the rest is lost
                var sum = Complex.Zero;
                if (tis > 0) for (int k = 0; k < _tau.Length; k++) sum += _c[k] * Complex.Exp(Complex.ImaginaryOne * (w - axis.Omega0) * _tau[k]);
                h[i] = Math.Sqrt(1 - tis) + Math.Sqrt(_g * tis) * sum;
            }
            return JonesSpectrum.Scalar(h);
        }
    }

    /// <summary>
    /// Scatter as delayed light: a fraction f of the power is not lost but coupled into the mode again, incoherently and later. The transfer is
    /// <code>H(w) = sqrt(1 - f) + sqrt(f) s(w) sum_k c_k exp(i w tau_k),   s(w) = (w / w0)^(n/2),   c_k ~ CN(0, p_k)</code>
    /// with delays tau_k and weights p_k from the <see cref="DelayKernel"/>, random complex amplitudes c_k from a seed (random phases:
    /// incoherent with the main pulse), and a spectral weight whose intensity is proportional to lambda^-n (n = spectralExponent; 4 for
    /// Rayleigh-like scatter, 0 = flat; s(w) = (w/w0)^(n/2)). On average the power is conserved: (1 - f) in the main pulse, f in the scattered light.
    /// </summary>
    public sealed class DelayedScatterTerm : LinearTerm
    {
        private readonly double _f, _n, _w0; private readonly double[] _tau; private readonly Complex[] _c;
        public double Fraction => _f; public DelayKernel Kernel { get; }
        public DelayedScatterTerm(double fraction, DelayKernel kernel, double spectralExponent, double referenceOmega, int seed)
        {
            _f = fraction; Kernel = kernel; _n = spectralExponent; _w0 = referenceOmega;
            _tau = kernel.DelaysS;
            _c = Amplitudes(kernel, seed);
            Mechanism = "scatter";
        }

        /// <summary>Random complex amplitudes c_k with E|c_k|^2 = p_k (complex Gaussian, from the seed).</summary>
        public static Complex[] Amplitudes(DelayKernel kernel, int seed)
        {
            var rnd = new Random(seed); var c = new Complex[kernel.DelaysS.Length];
            for (int k = 0; k < c.Length; k++)
            {
                double a = Math.Sqrt(kernel.Weights[k]);
                double u1 = 1 - rnd.NextDouble(), u2 = rnd.NextDouble(), r = Math.Sqrt(-Math.Log(u1));
                c[k] = a * Complex.FromPolarCoordinates(r, 2 * Math.PI * u2);
            }
            return c;
        }
        public override string Description => string.Format(CultureInfo.InvariantCulture, "delayed scatter ({0:G3} of the power returns after {1:G3} .. {2:G3} ps, lambda^-{3:G2})", _f, _tau.Min() * 1e12, _tau.Max() * 1e12, _n);
        public override double GroupDelayAt(double omega) => 0;
        public override DispersionPoint DispersionAt(double omega) => new DispersionPoint();
        public override JonesSpectrum Evaluate(OmegaAxis axis)
        {
            int n = axis.N; var h = new Complex[n];
            double a = Math.Sqrt(1 - _f), b = Math.Sqrt(_f);
            for (int i = 0; i < n; i++)
            {
                double w = axis.Omega[i];
                if (w <= 0) { h[i] = a; continue; }
                var sum = Complex.Zero;
                for (int k = 0; k < _tau.Length; k++) sum += _c[k] * Complex.Exp(Complex.ImaginaryOne * (w - axis.Omega0) * _tau[k]);
                h[i] = a + b * Math.Pow(w / _w0, 0.5 * _n) * sum;      // intensity ~ lambda^-n, amplitude ~ w^(n/2)
            }
            return JonesSpectrum.Scalar(h);
        }
    }
}
