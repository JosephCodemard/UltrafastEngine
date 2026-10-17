using System;
using System.Numerics;
using UltrafastEngine.Numerics;

namespace UltrafastEngine.Pulses
{
    /// <summary>
    /// A pulse, represented as a two-component (lab x/y) complex spectral envelope
    /// A(omega) in the co-moving frame: E(z,t) = Re[A(z,t) * exp(i(beta0*z - omega0*t))].
    /// This is the standard "envelope" representation used throughout ultrafast/nonlinear
    /// optics (it's what the NLSE and its vector generalisation are equations FOR), as
    /// opposed to a raw carrier-resolved traveling wave -- storing the carrier explicitly
    /// would need ~10-100x more grid points for no physical benefit at these bandwidths.
    ///
    /// UNITS: Ax, Ay are stored in units of sqrt(Watts), i.e. |Ax(t)|^2 + |Ay(t)|^2 is the
    /// instantaneous optical POWER in watts (not intensity -- divide by an effective area
    /// to get intensity, which NonlinearOpticalSegment does internally). This is the
    /// standard nonlinear-fibre-optics convention and is what makes the RK4IP nonlinear
    /// coefficient gamma = n2*omega0/(c*Aeff) come out dimensionally consistent with no
    /// extra area factors floating around in the pulse class itself.
    ///
    /// Everything except Ax/Ay/Grid is a DERIVED quantity, computed on demand rather than
    /// cached -- this is deliberate: storing GD/GDD/TOD or energy alongside the spectrum
    /// invites the classic bug where a propagation step updates the spectrum but a stale
    /// derived value is left behind. If profiling ever shows this matters, add caching at
    /// the call site, not in this class.
    /// </summary>
    public sealed class PulseField
    {
        public FrequencyGrid Grid { get; }
        public Complex[] Ax { get; }
        public Complex[] Ay { get; }

        /// <summary>Optional metadata -- NOT used inside the physics engine itself, only
        /// useful downstream if you want to convert single-pulse energy to average power.</summary>
        public double RepRateHz { get; }

        /// <summary>Optional transverse profile the beam carries between spatial elements (null = 1-D beam).
        /// Ax/Ay are then the collapsed envelope: |Ax|^2 + |Ay|^2 is the beam-integrated spectrum.</summary>
        public Transverse.TransverseProfile Profile { get; set; }

        public PulseField(FrequencyGrid grid, Complex[] ax, Complex[] ay, double repRateHz = 0.0)
        {
            if (ax.Length != grid.N || ay.Length != grid.N)
                throw new ArgumentException("Ax/Ay length must match FrequencyGrid.N.");
            Grid = grid;
            Ax = ax;
            Ay = ay;
            RepRateHz = repRateHz;
        }

        /// <summary>Returns a new PulseField with a new spectrum on the same grid.
        /// PulseField itself is otherwise immutable.</summary>
        public PulseField WithSpectrum(Complex[] newAx, Complex[] newAy) => new PulseField(Grid, newAx, newAy, RepRateHz) { Profile = Profile };

        public Complex[] TemporalAx() => ToTimeUnlessZero(Ax);
        public Complex[] TemporalAy() => ToTimeUnlessZero(Ay);

        // A linearly polarized pulse has one identically-zero component; skipping its
        // FFT halves the cost of every diagnostic snapshot on multi-million-point grids.
        private static Complex[] ToTimeUnlessZero(Complex[] a)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != Complex.Zero) return SpectralTransform.FreqToTime(a);
            return new Complex[a.Length];
        }

        /// <summary>Total spectral intensity |Ax(omega)|^2 + |Ay(omega)|^2, same index
        /// order as Grid.OmegaAbsolute. This is a spectral POWER DENSITY-like quantity
        /// in the discrete sense used for plotting a spectrum, not independently
        /// SI-calibrated -- use TotalEnergyJ() for the absolute energy check.</summary>
        public double[] SpectralIntensity()
        {
            var ix = ArrayOps.Abs2(Ax);
            var iy = ArrayOps.Abs2(Ay);
            var result = new double[Grid.N];
            for (int i = 0; i < Grid.N; i++) result[i] = ix[i] + iy[i];
            return result;
        }

        /// <summary>
        /// Unwrapped spectral phase of the x-polarization component, in radians, over
        /// Grid.OmegaAbsolute. If your pulse is not predominantly x-polarized, unwrap
        /// Ay yourself the same way -- this class does not attempt to construct a single
        /// "total power weighted" phase because that quantity is not well-defined once
        /// Ax and Ay have independently evolving phases (e.g. after a birefringent element).
        /// </summary>
        public double[] SpectralPhaseUnwrappedX()
        {
            var raw = new double[Grid.N];
            for (int i = 0; i < Grid.N; i++) raw[i] = Math.Atan2(Ax[i].Imaginary, Ax[i].Real);
            return PhaseUnwrap.Unwrap(raw);
        }

        public double[] SpectralPhaseUnwrappedY()
        {
            var raw = new double[Grid.N];
            for (int i = 0; i < Grid.N; i++) raw[i] = Math.Atan2(Ay[i].Imaginary, Ay[i].Real);
            return PhaseUnwrap.Unwrap(raw);
        }

        /// <summary>GD/GDD/TOD extracted from the x-polarization spectral phase.</summary>
        public DispersionCurve GdGddTodX() =>
            DispersionMath.FromUnwrappedPhase(Grid.OmegaAbsolute, SpectralPhaseUnwrappedX(), Grid.CarrierIndex);

        /// <summary>Carrier-only GD/GDD/TOD (arrays left null): same finite-difference
        /// stencils and smoothing as GdGddTodX, evaluated only around the carrier --
        /// O(N) for the unwrap instead of allocating four more N-sized arrays, which
        /// matters once N is in the millions.</summary>
        public DispersionCurve CarrierDispersionX()
        {
            var phase = SpectralPhaseUnwrappedX();
            return DispersionMath.AtIndex(Grid.OmegaAbsolute, phase, Grid.CarrierIndex);
        }

        public DispersionCurve GdGddTodY() =>
            DispersionMath.FromUnwrappedPhase(Grid.OmegaAbsolute, SpectralPhaseUnwrappedY(), Grid.CarrierIndex);

        /// <summary>Total pulse energy in Joules (both polarizations), via the discrete
        /// Parseval relation consistent with ComplexFft's normalization convention.</summary>
        public double TotalEnergyJ() => EnergyNormalization.ComputeEnergyJFromFreqDomain(Ax, Ay, Grid);

        public double PeakPowerW()
        {
            var axT = TemporalAx();
            var ayT = TemporalAy();
            double maxP = 0.0;
            for (int i = 0; i < Grid.N; i++)
            {
                double p = axT[i].Real * axT[i].Real + axT[i].Imaginary * axT[i].Imaginary
                         + ayT[i].Real * ayT[i].Real + ayT[i].Imaginary * ayT[i].Imaginary;
                if (p > maxP) maxP = p;
            }
            return maxP;
        }

        public double PeakIntensityWPerM2(double effectiveAreaM2) => PeakPowerW() / effectiveAreaM2;
    }

    /// <summary>GD/GDD/TOD as a function of frequency, plus the values at the carrier.</summary>
    public sealed class DispersionCurve
    {
        public double[] Omega { get; set; }
        public double[] GroupDelayS { get; set; }
        public double[] GddS2 { get; set; }
        public double[] TodS3 { get; set; }
        public double GdAtCarrierS { get; set; }
        public double GddAtCarrierS2 { get; set; }
        public double TodAtCarrierS3 { get; set; }
    }

    /// <summary>
    /// Single source of truth for the discrete energy-to-spectrum relationship, so the
    /// pulse builder (which sets the absolute scale from a user-specified energy) and
    /// PulseField.TotalEnergyJ() (which reports it back) can never silently disagree.
    /// </summary>
    public static class EnergyNormalization
    {
        public static double ComputeEnergyJFromFreqDomain(Complex[] axFreq, Complex[] ayFreq, FrequencyGrid grid)
        {
            // Discrete Parseval for ComplexFft's convention (unnormalized forward,
            // 1/N-normalized inverse): sum_n |a[n]|^2 = (1/N) * sum_k |A[k]|^2
            double sumSq = 0.0;
            for (int i = 0; i < grid.N; i++)
            {
                sumSq += axFreq[i].Real * axFreq[i].Real + axFreq[i].Imaginary * axFreq[i].Imaginary;
                sumSq += ayFreq[i].Real * ayFreq[i].Real + ayFreq[i].Imaginary * ayFreq[i].Imaginary;
            }
            double timeDomainSumSq = sumSq / grid.N;
            return timeDomainSumSq * grid.DTime; // Watts * seconds = Joules
        }
    }
}
