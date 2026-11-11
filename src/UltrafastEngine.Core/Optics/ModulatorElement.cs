// ModulatorElement.cs -- a pixelated thin phase / amplitude modulator on the carried transverse profile (SLM, DM surface, tile mask).
//
//   class ModulatorElement : IActiveElement
//     Nx, Ny, PitchXM, PitchYM, OffsetXM, OffsetYM, RotationRad     the device's cell grid (centred on the offset, rotated about it)
//     double[] PhaseRad | OpdM, Amplitude   [ix * Ny + iy], as commanded; DesignOmega the frequency at which PhaseRad holds
//     FillFactor, GapTransmission, OutsideTransmission, ZeroOrderLeak, CrosstalkSigmaCells, QuantisationLevels, PhaseWrapRad
//     Chromatic: Opd (phase = phi_design * omega / omega_design: a path, so a wrapped map is a real grating) | FixedPhase |
//                Index (phase = phi_design * omega dn(omega) / (omega_design dn(omega_design)): a liquid-crystal cell of dispersive birefringence dn)
//     double PhaseScale(omega)             the factor the design phase is multiplied by at omega (the chromatic rule)
//     void Prepare()                       wrap, quantise, blur the command once
//     Complex[] CellsAt(basis, omega)      transmission per simulation cell: the average over the cell of the device (6 x 6 sub-samples)
//                                          (cell centre, fill factor and gaps, array edge), not a point sample
//     PulseField Execute(ctx, p)           multiplies the profile at every stored frequency (ApplyProfileMask books the lost energy as "clip")
//
// Conventions: the device cell (ix, iy) is centred at ((ix - (Nx-1)/2) pitchX, (iy - (Ny-1)/2) pitchY) before the offset and rotation
// are applied; x is the first index. See docs/reference/modulator.md.
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Engine;
using UltrafastEngine.Lasers;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Optics
{
    public sealed class ModulatorElement : IActiveElement, IReportsCarrierPhase
    {
        public int Nx, Ny;
        public double PitchXM, PitchYM, OffsetXM, OffsetYM, RotationRad;
        /// <summary>Commanded phase at <see cref="DesignOmega"/> (rad) or optical path (m), [ix * Ny + iy]; at most one is given.</summary>
        public double[] PhaseRad, OpdM, Amplitude;
        public double DesignOmega;
        public bool FixedPhase;
        /// <summary>Chromatic rule "index": the device's index modulation dn(omega) (only its ratio to dn(omega_design) matters); null = not used.</summary>
        public Func<double, double> DeltaIndex;
        /// <summary>Explicit cross-talk kernel (odd length, applied separably along x and y, normalised to unit sum); overrides CrosstalkSigmaCells.</summary>
        public double[] CrosstalkKernel;
        public double FillFactor = 1, GapTransmission, OutsideTransmission, ZeroOrderLeak, CrosstalkSigmaCells, PhaseWrapRad;
        public int QuantisationLevels;

        public string Kind => "modulator";
        public string Description => string.Format(CultureInfo.InvariantCulture, "modulator: {0} x {1} cells", Nx, Ny);
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        private const int Sub = 6;
        private double[] _phase, _amp;        // as written to the device, after wrap, quantisation and cross-talk

        /// <summary>Check the arrays and turn the command into the phase and amplitude the device really imposes (design wavelength).</summary>
        public void Prepare()
        {
            int n = Nx * Ny;
            if (Nx < 1 || Ny < 1) throw new InvalidOperationException("modulator: cellsX and cellsY must be at least 1.");
            if (!(PitchXM > 0) || !(PitchYM > 0)) throw new InvalidOperationException("modulator: pitchXM must be positive.");
            if (PhaseRad != null && OpdM != null) throw new InvalidOperationException("modulator: give phaseRad or opdM, not both.");
            if ((PhaseRad ?? OpdM) != null && (PhaseRad ?? OpdM).Length != n) throw new InvalidOperationException("modulator: the phase map has " + (PhaseRad ?? OpdM).Length + " values, cellsX x cellsY = " + n + ".");
            if (Amplitude != null && Amplitude.Length != n) throw new InvalidOperationException("modulator: the amplitude map has " + Amplitude.Length + " values, cellsX x cellsY = " + n + ".");
            if (!(FillFactor > 0 && FillFactor <= 1)) throw new InvalidOperationException("modulator: fillFactor must be in (0, 1].");
            _phase = new double[n]; _amp = new double[n];
            double k = DesignOmega / PhysicalConstants.SpeedOfLightMPerS;
            double wrap = PhaseWrapRad > 0 ? PhaseWrapRad : QuantisationLevels > 0 ? 2 * Math.PI : 0;
            for (int i = 0; i < n; i++)
            {
                double ph = PhaseRad != null ? PhaseRad[i] : OpdM != null ? k * OpdM[i] : 0;
                if (wrap > 0)
                {
                    ph -= wrap * Math.Floor(ph / wrap);
                    if (QuantisationLevels > 0) { double step = wrap / QuantisationLevels; ph = step * (((long)Math.Round(ph / step)) % QuantisationLevels); }   // the top half-level rounds to level 0, not to a full wrap
                }
                _phase[i] = ph; _amp[i] = Amplitude != null ? Amplitude[i] : 1;
            }
            if (CrosstalkKernel != null)
            {
                if (CrosstalkKernel.Length % 2 == 0 || CrosstalkKernel.Sum() <= 0 || CrosstalkKernel.Any(v => v < 0)) throw new InvalidOperationException("modulator: crosstalkKernel needs an odd number of non-negative weights with a positive sum (a negative weight could raise the amplitude above 1: gain).");
                double ks = CrosstalkKernel.Sum(); var w = CrosstalkKernel.Select(v => v / ks).ToArray();
                Blur(_phase, w); Blur(_amp, w);
            }
            else if (CrosstalkSigmaCells > 0)
            {
                int r = (int)Math.Ceiling(3 * CrosstalkSigmaCells); var w = new double[2 * r + 1]; double s = 0;
                for (int i = -r; i <= r; i++) s += w[i + r] = Math.Exp(-0.5 * i * i / (CrosstalkSigmaCells * CrosstalkSigmaCells));
                for (int i = 0; i < w.Length; i++) w[i] /= s;
                Blur(_phase, w); Blur(_amp, w);
            }
            if (DeltaIndex != null && !(Math.Abs(DeltaIndex(DesignOmega)) > 0)) throw new InvalidOperationException("modulator: the index modulation dn is zero at the design wavelength.");
        }

        /// <summary>The factor the design phase is multiplied by at <paramref name="omega"/> (the chromatic rule).</summary>
        public double PhaseScale(double omega) => FixedPhase ? 1 : DeltaIndex != null ? omega * DeltaIndex(omega) / (DesignOmega * DeltaIndex(DesignOmega)) : omega / DesignOmega;

        /// <summary>Fringing-field cross-talk: a separable blur of the written map with normalised weights w (edge values repeated).</summary>
        private void Blur(double[] a, double[] w)
        {
            int r = w.Length / 2;
            var t = new double[a.Length];
            for (int ix = 0; ix < Nx; ix++) for (int iy = 0; iy < Ny; iy++)
            { double v = 0; for (int d = -r; d <= r; d++) v += w[d + r] * a[ix * Ny + Math.Min(Ny - 1, Math.Max(0, iy + d))]; t[ix * Ny + iy] = v; }
            for (int ix = 0; ix < Nx; ix++) for (int iy = 0; iy < Ny; iy++)
            { double v = 0; for (int d = -r; d <= r; d++) v += w[d + r] * t[Math.Min(Nx - 1, Math.Max(0, ix + d)) * Ny + iy]; a[ix * Ny + iy] = v; }
        }

        private Complex Cell(int i, double omega)
        {
            double ph = _phase[i] * PhaseScale(omega);
            var t = Complex.FromPolarCoordinates(_amp[i], ph);
            if (ZeroOrderLeak > 0) t = _amp[i] * ((1 - ZeroOrderLeak) * Complex.FromPolarCoordinates(1, ph) + ZeroOrderLeak);   // amplitude mix: a flat map stays lossless
            return t;
        }

        /// <summary>Device cell index at (x, y) (simulation frame) and whether the point is on the active area (1), in a gap (0) or off the array (-1).</summary>
        private int Locate(double x, double y, out int state)
        {
            double dx = x - OffsetXM, dy = y - OffsetYM, c = Math.Cos(RotationRad), s = Math.Sin(RotationRad);
            double u = (c * dx + s * dy) / PitchXM + Nx / 2.0, v = (-s * dx + c * dy) / PitchYM + Ny / 2.0;
            int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
            if (ix < 0 || ix >= Nx || iy < 0 || iy >= Ny) { state = -1; return -1; }
            state = Math.Abs(u - ix - 0.5) <= FillFactor / 2 && Math.Abs(v - iy - 0.5) <= FillFactor / 2 ? 1 : 0;
            return ix * Ny + iy;
        }

        /// <summary>The device's transmission averaged over each simulation cell, at one frequency.</summary>
        public Complex[] CellsAt(CartesianBasis b, double omega)
        {
            var t = new Complex[b.M]; int nys = b.Ny > 1 ? Sub : 1;
            Parallel.For(0, b.M, m =>
            {
                double x = b.X[m], y = b.Y[m], hx = b.Dx / 2, hy = b.Ny > 1 ? b.Dy / 2 : 0;
                // fast path: all four corners on the active area of one device cell => the whole simulation cell is (the area is convex)
                int i0 = Locate(x - hx, y - hy, out int s0);
                if (s0 == 1 && Locate(x + hx, y - hy, out int s1) == i0 && s1 == 1 && Locate(x - hx, y + hy, out int s2) == i0 && s2 == 1 && Locate(x + hx, y + hy, out int s3) == i0 && s3 == 1)
                { t[m] = Cell(i0, omega); return; }
                Complex sum = 0;
                for (int i = 0; i < Sub; i++)
                    for (int j = 0; j < nys; j++)
                    {
                        int idx = Locate(x + ((i + 0.5) / Sub - 0.5) * b.Dx, y + (b.Ny > 1 ? ((j + 0.5) / Sub - 0.5) * b.Dy : 0), out int st);
                        sum += st == 1 ? Cell(idx, omega) : st == 0 ? GapTransmission : OutsideTransmission;
                    }
                t[m] = sum / (Sub * nys);
            });
            return t;
        }

        private static double Frac(double v) => v - Math.Floor(v);

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null) { ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': a modulator acts on a carried transverse profile; there is none here, so it was ignored (give the element before it a \"transverse\" model)."); return p; }
            if (!(p.Profile.Basis is CartesianBasis B)) throw new InvalidOperationException("element '" + ctx.Element.Id + "': a modulator needs a cartesian transverse grid (\"model\": \"cartesian\").");
            double cellW = Math.Min(PitchXM, PitchYM), dxs = Math.Min(B.Dx, B.Ny > 1 ? B.Dy : double.MaxValue);
            if (cellW < 2 * dxs) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the device cell ({1:G3} m) is under two grid steps ({2:G3} m): the cell structure (replica orders, gaps) is under-resolved; use at least 2 grid cells per device cell, or 4 to show the fill factor.", ctx.Element.Id, cellW, dxs));
            // device cell edges on the simulation grid points: the grid has a sample at x = 0, the cell centres of an even device are half a pitch from the axis, so with
            // pitch = grid step every simulation cell averages two device cells (a blaze of period P keeps cos^2(pi / P) of its light); an offset of half a pitch aligns them
            bool straddleX = Math.Abs(PitchXM / B.Dx - 1) < 1e-3 && Math.Abs(Frac(Nx / 2.0 - OffsetXM / PitchXM) - 0.5) > 0.05 && RotationRad == 0;
            bool straddleY = B.Ny > 1 && Math.Abs(PitchYM / B.Dy - 1) < 1e-3 && Math.Abs(Frac(Ny / 2.0 - OffsetYM / PitchYM) - 0.5) > 0.05 && RotationRad == 0;
            if (straddleX || straddleY) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the device pitch equals the grid step but the device cell edges fall on the grid points, so each simulation cell averages two device cells along {1} (a blazed grating of P cells per period keeps only cos^2(pi/P) of its light). Shift the device by half a pitch (offsetXM / offsetYM = pitch / 2) to put the cell centres on the grid points, or use a grid step of pitch / 2 or less.", ctx.Element.Id, straddleX && straddleY ? "x and y" : straddleX ? "x" : "y"));
            int nf = p.Profile.Omega.Length, cached = -1; Complex[] cur = null;
            // aliasing guard (the interpolation error below cannot see a phase step near a multiple of 2 pi per stored frequency)
            double maxPhase = _phase.Max() - _phase.Min(), dS = 0;      // the phase range: a uniform offset is a piston, not a chromatic structure
            for (int f = 1; f < nf; f++) dS = Math.Max(dS, Math.Abs(PhaseScale(p.Profile.Omega[f]) - PhaseScale(p.Profile.Omega[f - 1])));
            if (!FixedPhase && maxPhase * dS > Math.PI / 2)
                ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the phase changes by up to {1:F2} rad between stored frequencies (limit pi/2): raise transverse.spectralSamples.", ctx.Element.Id, maxPhase * dS));
            Complex At(int m, int f)
            {
                int key = FixedPhase ? 0 : f;
                if (key != cached) { cur = CellsAt(B, FixedPhase ? DesignOmega : p.Profile.Omega[f]); cached = key; }
                return cur[m];
            }
            ctx.Record.Values["cellsX"] = Nx; ctx.Record.Values["cellsY"] = Ny;
            ctx.Record.Values["quantisationLevels"] = QuantisationLevels;
            var q = ApertureElement.ApplyProfileMask(ctx, p, At);
            // how well the stored frequencies resolve the chromatic phase: a large value says "increase transverse.spectralSamples" (compare K and 2K)
            double err = TransverseOps.InterpolationError(q.Profile);
            ctx.Record.Values["spectralSamplingError"] = err;
            if (err > 0.05) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the {1} stored frequencies do not resolve the modulator's chromatic phase (interpolation error {2:G2} of the field norm): raise transverse.spectralSamples and compare.", ctx.Element.Id, nf, err));
            return q;
        }
    }
}
