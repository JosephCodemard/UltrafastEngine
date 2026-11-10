// TemporalShapers.cs -- programmable spectral phase / amplitude devices of the ao plugin (temporal shaping).
//
//   SpectralShaperTerm  : PhaseTerm   a pixelated mask in the Fourier plane of a 4f line (element "spectralShaper")
//     frequency -> position x(omega) = D (2 pi c / omega - lambda_c) (exact in lambda; the array of centre `offset` is what moves) (D = focal length x grating dispersion, or given);
//     each frequency is a Gaussian spot of 1/e^2 field radius w0, so the transfer function is the overlap of the spot with the
//     pixel mask (erf differences): finite spectral resolution, fill factor, gaps and phase quantisation fall out of it.
//     Weiner: a stepwise phase on pixels of spectral width d_omega makes replica pulses at t = 2 pi m / d_omega.
//   AopdfTerm           : PhaseTerm   an acousto-optic programmable dispersive filter (element "aopdf"): a programmed spectral phase
//     (delay, GDD, TOD, FOD at the carrier, or a table), a finite acoustic window (components whose programmed group delay lies outside
//     +-window/2 are not diffracted), a diffraction efficiency, an amplitude table and an optional spurious replica (ripple).
//   Both keep the programmed group delay IN the transfer function (a programmed linear phase moves the pulse inside the time window) and
//   only remove the carrier phase from the frame; GroupDelayAt is 0, so the arrival-time bookkeeping is unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Schema;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins.Ao
{
    /// <summary>Base: a complex transfer H(omega) given as magnitude and phase, programmed delay kept in the transfer.</summary>
    public abstract class ShaperTerm : PhaseTerm
    {
        private sealed class Memo { public double[] W; public Complex[] H; }
        private Memo _memo;                                          // one reference: a concurrent call can never pair one axis with another's transfer function
        protected abstract void Transfer(double[] omega, Complex[] h);
        private Complex[] Get(double[] omega)
        {
            var m = _memo;
            if (m != null && ReferenceEquals(omega, m.W)) return m.H;
            var h = new Complex[omega.Length]; Transfer(omega, h); _memo = new Memo { W = omega, H = h }; return h;
        }
        protected override void Phase(double[] omega, double[] phi) { var h = Get(omega); for (int i = 0; i < h.Length; i++) phi[i] = h[i] == Complex.Zero ? 0 : h[i].Phase; }
        protected override void Amplitude(double[] omega, double[] amp) { var h = Get(omega); for (int i = 0; i < h.Length; i++) amp[i] = h[i].Magnitude; }
        public override double GroupDelayAt(double omega) => 0;
        protected override void FrameReference(double w0, out double phi0, out double tau0)
        {
            tau0 = 0; var h = new Complex[1]; Transfer(new[] { w0 }, h); phi0 = h[0] == Complex.Zero ? 0 : h[0].Phase;
        }
        public override DispersionPoint DispersionAt(double omega) => new DispersionPoint { GroupDelayS = 0, GddS2 = 0, TodS3 = 0, FrameDelayS = 0, PhaseUnknown = false };
        /// <summary>The transfer function on an angular-frequency axis (public for tests and the Python twin).</summary>
        public Complex[] TransferOn(double[] omega) { var h = new Complex[omega.Length]; Transfer(omega, h); return h; }
    }

    public sealed class SpectralShaperTerm : ShaperTerm
    {
        public int Pixels; public double PitchM, FillFactor = 1, GapAmplitude, OutsideAmplitude, SpotRadiusM, CentreOmega, DispersionMPerRadS, OffsetM, DxDLambda, CentreLambdaM;
        public Complex[] PixelValue;      // complex amplitude transmission of each pixel
        public string Text = "spectral shaper";
        public override string Description => Text;

        protected override void Transfer(double[] omega, Complex[] h)
        {
            double w0 = SpotRadiusM, p = PitchM, half = Pixels * p / 2;
            double xl = OffsetM - half, xr = OffsetM + half;
            for (int i = 0; i < omega.Length; i++)
            {
                if (!(omega[i] > 0)) { h[i] = Complex.Zero; continue; }
                double xc = DxDLambda * (2 * Math.PI * UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS / omega[i] - CentreLambdaM);   // exact in lambda; the array (centre OffsetM) is what moves
                // outside the array
                Complex acc = OutsideAmplitude * (0.5 * (AoMath.Erf((xl - xc) / w0) + 1) + 0.5 * (1 - AoMath.Erf((xr - xc) / w0)));
                int j0 = Math.Max(0, (int)Math.Floor((xc - 6 * w0 - xl) / p)), j1 = Math.Min(Pixels - 1, (int)Math.Ceiling((xc + 6 * w0 - xl) / p));
                for (int j = j0; j <= j1; j++)
                {
                    double a = xl + j * p, b = a + p, da = a + p * (1 - FillFactor) / 2, db = b - p * (1 - FillFactor) / 2;
                    double wCell = W(a, b, xc, w0), wAct = W(da, db, xc, w0);
                    acc += PixelValue[j] * wAct + GapAmplitude * (wCell - wAct);
                }
                h[i] = acc;
            }
        }
        private static double W(double a, double b, double xc, double w0) => 0.5 * (AoMath.Erf((b - xc) / w0) - AoMath.Erf((a - xc) / w0));
        /// <summary>Spectral width of one pixel (rad/s).</summary>
        public double PixelBandwidthRadS => PitchM / Math.Abs(DispersionMPerRadS);
    }

    public sealed class AopdfTerm : ShaperTerm
    {
        public double CentreOmega, DelayS, GddS2, TodS3, FodS4, WindowS, Efficiency = 1, ReplicaAmplitude, ReplicaDelayS;
        public double[] TableOmega, TablePhase, AmpOmega, AmpValue;
        public string Text = "aopdf";
        public override string Description => Text;

        private double Phi(double w)
        {
            double d = w - CentreOmega;
            double phi = DelayS * d + GddS2 * d * d / 2 + TodS3 * d * d * d / 6 + FodS4 * d * d * d * d / 24;
            if (TableOmega != null) phi += Interp(TableOmega, TablePhase, w);
            return phi;
        }
        private static double Interp(double[] x, double[] y, double v)
        {
            if (v <= x[0]) return y[0]; if (v >= x[x.Length - 1]) return y[y.Length - 1];
            int j = 1; while (x[j] < v) j++;
            double t = (v - x[j - 1]) / (x[j] - x[j - 1]); return y[j - 1] + t * (y[j] - y[j - 1]);
        }
        protected override void Transfer(double[] omega, Complex[] h)
        {
            for (int i = 0; i < omega.Length; i++)
            {
                double w = omega[i], dw = Math.Max(1e-6 * Math.Abs(CentreOmega), 1e8);
                double amp = Math.Sqrt(Efficiency);
                if (AmpOmega != null) amp *= Math.Max(0, Interp(AmpOmega, AmpValue, w));
                if (WindowS > 0)
                {
                    double tau = (Phi(w + dw) - Phi(w - dw)) / (2 * dw);            // programmed group delay at this frequency
                    if (Math.Abs(tau) > WindowS / 2) amp = 0;
                }
                var t = Complex.FromPolarCoordinates(amp, Phi(w));
                if (ReplicaAmplitude != 0) t *= (1 + ReplicaAmplitude * Complex.FromPolarCoordinates(1, w * ReplicaDelayS)) / Math.Sqrt(1 + ReplicaAmplitude * ReplicaAmplitude);   // the replica is split off the pulse: total energy = efficiency
                h[i] = t;
            }
        }
    }

    internal static class TemporalShapers
    {
        public static void CompileSpectralShaper(CompilerServices s, SystemElement e, CompiledElement ce)
        {
            string origin = "spectralShaper '" + e.Id + "'";
            int n = (int)s.Num(e, "pixels", 0);
            double pitch = s.Num(e, "pixelPitchM", double.NaN);
            if (n < 1 || !(pitch > 0)) throw new JsonException(origin + ": pixels and pixelPitchM are needed.");
            double c = UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS;
            double lamC = e.Json.Has("centerWavelengthM") ? s.Num(e, "centerWavelengthM", 0) : 2 * Math.PI * c / s.Omega0;
            double wC = 2 * Math.PI * c / lamC;
            double f = s.Num(e, "focalLengthM", double.NaN);
            double dxdl = s.Num(e, "dispersionMPerM", double.NaN);          // focal-plane position per wavelength
            if (double.IsNaN(dxdl))
            {
                double lines = s.Num(e, "gratingLinesPerMm", double.NaN) * 1e3, th = s.Num(e, "incidenceAngleRad", 0), ord = s.Num(e, "order", 1);
                if (double.IsNaN(lines) || double.IsNaN(f)) throw new JsonException(origin + ": give dispersionMPerM, or gratingLinesPerMm and focalLengthM (and incidenceAngleRad).");
                double sd = ord * lamC * lines - Math.Sin(th);
                if (Math.Abs(sd) >= 1) throw new JsonException(origin + ": the grating does not diffract this wavelength at that angle.");
                dxdl = f * ord * lines / Math.Sqrt(1 - sd * sd);
            }
            double w0 = s.Num(e, "spotRadiusM", double.NaN);
            if (double.IsNaN(w0))
            {
                double win = s.Num(e, "inputBeamRadiusM", double.NaN);
                if (double.IsNaN(win) || double.IsNaN(f)) throw new JsonException(origin + ": give spotRadiusM, or inputBeamRadiusM and focalLengthM (w0 = lambda f / (pi w_in)).");
                w0 = lamC * f / (Math.PI * win);
            }
            // x(omega) = dx/dlambda * (lambda - lambda_c); d lambda / d omega = -lambda^2 / (2 pi c)
            double dxdw = -dxdl * lamC * lamC / (2 * Math.PI * c);
            double wrap = s.Num(e, "phaseWrapRad", 2 * Math.PI); int levels = (int)s.Num(e, "greyLevels", 0);
            int nn = n, one = 1;
            var phase = AoMath.ReadMap(s, e, "phaseRad", null, ref nn, ref one, origin) ?? new double[n];
            int nn2 = n, one2 = 1;
            var amp = AoMath.ReadMap(s, e, "amplitude", null, ref nn2, ref one2, origin);
            var px = new Complex[n];
            for (int j = 0; j < n; j++)
            {
                double ph = phase[j];
                if (wrap > 0) ph -= wrap * Math.Floor(ph / wrap);
                if (levels > 0) { double step = 2 * Math.PI / levels; ph = step * (((long)Math.Round(ph / step)) % levels); }
                px[j] = Complex.FromPolarCoordinates(amp != null ? amp[j] : 1, ph);
            }
            var t = new SpectralShaperTerm
            {
                Pixels = n, PitchM = pitch, FillFactor = s.Num(e, "fillFactor", 1), GapAmplitude = s.Num(e, "gapTransmission", 0), OutsideAmplitude = s.Num(e, "outsideTransmission", 0),
                SpotRadiusM = w0, DxDLambda = dxdl, CentreLambdaM = lamC, CentreOmega = wC, DispersionMPerRadS = dxdw, OffsetM = s.Num(e, "offsetM", 0), PixelValue = px, Mechanism = "spectral shaper"
            };
            t.Text = string.Format(CultureInfo.InvariantCulture, "spectral shaper {0}: {1} pixels, pixel bandwidth {2:G4} rad/s (replica pulses at {3:G4} s), spot radius {4:G3} m", e.Id, n, t.PixelBandwidthRadS, 2 * Math.PI / t.PixelBandwidthRadS, w0);
            ce.Post.Add(t);
            ce.Notes.Add(t.Text);
        }

        public static void CompileAopdf(CompilerServices s, SystemElement e, CompiledElement ce)
        {
            string origin = "aopdf '" + e.Id + "'";
            double c = UltrafastEngine.Numerics.PhysicalConstants.SpeedOfLightMPerS;
            double lamC = e.Json.Has("centerWavelengthM") ? s.Num(e, "centerWavelengthM", 0) : 2 * Math.PI * c / s.Omega0;
            var t = new AopdfTerm
            {
                CentreOmega = 2 * Math.PI * c / lamC, DelayS = s.Num(e, "delayFs", 0) * 1e-15, GddS2 = s.Num(e, "gddFs2", 0) * 1e-30, TodS3 = s.Num(e, "todFs3", 0) * 1e-45,
                FodS4 = s.Num(e, "fodFs4", 0) * 1e-60, WindowS = s.Num(e, "windowS", 0), Efficiency = s.Num(e, "efficiency", 1), ReplicaAmplitude = s.Num(e, "replicaAmplitude", 0),
                ReplicaDelayS = s.Num(e, "replicaDelayS", 0), Mechanism = "aopdf"
            };
            if (!(t.Efficiency >= 0 && t.Efficiency <= 1)) throw new JsonException(origin + ": efficiency is a power fraction in [0, 1].");
            if (e.Json.Has("spectralPhaseRad"))
            {
                var ph = e.Json.GetDoubleArray("spectralPhaseRad"); var lam = e.Json.GetDoubleArray("tableWavelengthsM");
                if (lam == null || lam.Length != ph.Length || lam.Length < 2) throw new JsonException(origin + ": spectralPhaseRad needs tableWavelengthsM of the same length (at least 2).");
                var ord = Enumerable.Range(0, lam.Length).OrderBy(i => 2 * Math.PI * c / lam[i]).ToArray();
                t.TableOmega = ord.Select(i => 2 * Math.PI * c / lam[i]).ToArray(); t.TablePhase = ord.Select(i => ph[i]).ToArray();
            }
            if (e.Json.Has("amplitudeTable"))
            {
                var a = e.Json.GetDoubleArray("amplitudeTable"); var lam = e.Json.GetDoubleArray("tableWavelengthsM");
                if (lam == null || lam.Length != a.Length || lam.Length < 2) throw new JsonException(origin + ": amplitudeTable needs tableWavelengthsM of the same length (at least 2).");
                var ord = Enumerable.Range(0, lam.Length).OrderBy(i => 2 * Math.PI * c / lam[i]).ToArray();
                t.AmpOmega = ord.Select(i => 2 * Math.PI * c / lam[i]).ToArray(); t.AmpValue = ord.Select(i => a[i]).ToArray();
            }
            t.Text = string.Format(CultureInfo.InvariantCulture, "aopdf {0}: delay {1:G4} s, GDD {2:G4} s^2, TOD {3:G4} s^3, efficiency {4:G3}{5}", e.Id, t.DelayS, t.GddS2, t.TodS3, t.Efficiency,
                t.WindowS > 0 ? ", acoustic window " + t.WindowS.ToString("G4", CultureInfo.InvariantCulture) + " s" : "");
            ce.Post.Add(t);
            ce.Notes.Add(t.Text);
        }
    }
}
