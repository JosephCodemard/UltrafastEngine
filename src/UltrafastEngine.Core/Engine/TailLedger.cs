using System;
using System.Collections.Generic;
using System.Numerics;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// The delayed-light ledger: a second, long time grid that carries weak delayed light (ghosts, substrate echoes,
    /// free-induction decay of gas lines, delayed scatter) next to the main nonlinear grid, so the nonlinear solver
    /// never has to run on the long window.
    /// <para>
    /// Both grids have the same time step and carrier, so the main frequency samples are every K-th sample of the long
    /// grid and the main time axis is the central part of the long one. The field is always the sum
    /// <c>U = embed(A_main) + tail</c>. A linear element is applied to <c>U</c> exactly on the long grid; the result is
    /// split by a smooth window (1 inside 85 % of the main half-window, cos² taper to 0 at its edge) into the new main
    /// field and the new tail. The split only moves light between the two parts, it never changes <c>U</c>, so
    /// energy is conserved and light that leaves the main window and comes back (a stretcher followed by a compressor)
    /// is not lost. Nonlinear and active elements act on the main field only; the tail passes through their linear
    /// terms. What is neglected is documented in docs/reference/delayed-light.md: the tail's own nonlinear
    /// propagation (including cross-phase modulation by the main pulse) and main x tail interference in nonlinear
    /// instruments.
    /// </para>
    /// </summary>
    public sealed class TailLedger
    {
        public FrequencyGrid Main { get; }
        public FrequencyGrid Long { get; }
        /// <summary>Long grid size / main grid size (a power of two, at least 2).</summary>
        public int Factor { get; }
        private readonly OmegaAxis _axis;
        private readonly double[] _window;       // split window on the main time axis
        private readonly int _offset;            // index of main sample 0 on the long time axis
        private Complex[] _tx, _ty;              // tail spectrum on the long grid (centred order)

        public TailLedger(FrequencyGrid main, double tailWindowS, int maxN)
        {
            Main = main;
            double ratio = tailWindowS / (main.N * main.DTime);
            int k = 2;
            while (k < ratio && (long)k * main.N < maxN) k *= 2;
            if ((long)k * main.N > maxN) k = Math.Max(2, maxN / main.N);
            Factor = k;
            Long = FrequencyGrid.Create(main.N * k, main.DTime, main.OmegaCenter);
            _axis = OmegaAxis.FromGrid(Long);
            _offset = Long.N / 2 - main.N / 2;
            _window = new double[main.N];
            for (int i = 0; i < main.N; i++)
            {
                double u = Math.Abs(i - main.N / 2) / (0.5 * main.N);
                _window[i] = u <= 0.85 ? 1.0 : u >= 1.0 ? 0.0 : Math.Pow(Math.Cos(0.5 * Math.PI * (u - 0.85) / 0.15), 2);
            }
            _tx = new Complex[Long.N]; _ty = new Complex[Long.N];
        }

        public double WindowS => Long.N * Long.DTime;
        /// <summary>Linear steps taken exactly on the long grid, and steps of smooth elements applied to main and tail separately.</summary>
        public int ExactSteps { get; internal set; }
        public int SeparateSteps { get; internal set; }

        private static bool IsZero(Complex[] a) { for (int i = 0; i < a.Length; i++) if (a[i] != Complex.Zero) return false; return true; }

        /// <summary>Energy (J) of a spectrum pair on the long grid.</summary>
        public double Energy(Complex[] x, Complex[] y)
            => EnergyNormalization.ComputeEnergyJFromFreqDomain(x, y, Long);

        public double TailEnergyJ => Energy(_tx, _ty);

        /// <summary>The tail alone as a field on the long grid (a snapshot; later steps do not change it).</summary>
        public PulseField TailField(double repRateHz = 0) => new PulseField(Long, _tx, _ty, repRateHz);

        /// <summary>Spectrum of the main-grid spectrum <paramref name="a"/> placed in the long time window.</summary>
        private Complex[] Embed(Complex[] a)
        {
            var t = SpectralTransform.FreqToTime(a);
            var big = new Complex[Long.N];
            Array.Copy(t, 0, big, _offset, Main.N);
            return SpectralTransform.TimeToFreq(big);
        }

        /// <summary>The main field as a field on the long grid, without the tail.</summary>
        public PulseField EmbedMain(PulseField main)
        {
            var ey = IsZero(main.Ay) ? new Complex[Long.N] : Embed(main.Ay);
            return new PulseField(Long, Embed(main.Ax), ey, main.RepRateHz);
        }

        /// <summary>Main + tail on the long grid.</summary>
        public PulseField Composite(PulseField main, PulseField tail = null)
        {
            var tx = tail?.Ax ?? _tx; var ty = tail?.Ay ?? _ty;
            var ux = Embed(main.Ax);
            var uy = IsZero(main.Ay) ? new Complex[Long.N] : Embed(main.Ay);
            for (int i = 0; i < ux.Length; i++) { ux[i] += tx[i]; uy[i] += ty[i]; }
            return new PulseField(Long, ux, uy, main.RepRateHz);
        }

        /// <summary>
        /// Apply linear terms to main + tail exactly and return the new main field. <paramref name="loss"/> receives the
        /// energy removed by each term (mechanism, J); light moved between main and tail is not a loss.
        /// </summary>
        public PulseField Apply(PulseField main, IReadOnlyList<LinearTerm> terms, Action<string, double> loss)
        {
            if (terms.Count == 0) return main;
            var ux = Embed(main.Ax);
            bool yOn = !IsZero(main.Ay) || !IsZero(_ty);
            var uy = IsZero(main.Ay) ? new Complex[Long.N] : Embed(main.Ay);
            for (int i = 0; i < ux.Length; i++) { ux[i] += _tx[i]; uy[i] += _ty[i]; }
            double e = Energy(ux, uy);
            foreach (var t in terms)
            {
                var h = t.Evaluate(_axis);
                h.Apply(ux, uy, out var bx, out var by);
                ux = bx; uy = by;
                double e2 = Energy(ux, uy);
                loss?.Invoke(t.Mechanism, e - e2);
                e = e2;
            }
            return Split(main, ux, uy, yOn);
        }

        private PulseField Split(PulseField main, Complex[] ux, Complex[] uy, bool yOn)
        {
            var mx = SplitOne(ux, out var tx);
            Complex[] my;
            Complex[] tyNew;
            if (yOn || !IsZero(uy)) my = SplitOne(uy, out tyNew); else { my = new Complex[Main.N]; tyNew = new Complex[Long.N]; }
            _tx = tx; _ty = tyNew;
            return main.WithSpectrum(mx, my);
        }

        /// <summary>u (long spectrum) = embed(main part) + tail part; returns the main spectrum, the tail spectrum in <paramref name="tail"/>.</summary>
        private Complex[] SplitOne(Complex[] u, out Complex[] tail)
        {
            var t = SpectralTransform.FreqToTime(u);
            var mainT = new Complex[Main.N];
            for (int i = 0; i < Main.N; i++)
            {
                mainT[i] = t[_offset + i] * _window[i];
                t[_offset + i] *= 1.0 - _window[i];
            }
            tail = SpectralTransform.TimeToFreq(t);
            return SpectralTransform.TimeToFreq(mainT);
        }

        /// <summary>Pass the tail through linear terms (elements whose main field is propagated by another solver).
        /// <paramref name="loss"/> receives the tail energy each term removes.</summary>
        public void PropagateTail(IEnumerable<LinearTerm> terms, Action<string, double> loss)
        {
            double e = Energy(_tx, _ty);
            foreach (var t in terms)
            {
                var h = t.Evaluate(_axis);
                h.Apply(_tx, _ty, out var bx, out var by);
                _tx = bx; _ty = by;
                double e2 = Energy(_tx, _ty);
                loss?.Invoke(t.Mechanism, e - e2);
                e = e2;
            }
        }

        /// <summary>
        /// First-order cross-phase of the tail by the main pulse in a Kerr element of nonlinear coefficient <paramref name="gammaPerW"/>
        /// (k0 n2 integral dz / A_eff, per W): the Kerr term i gamma |A|^2 A with A = A_m + A_t gives, to first order in the tail, i gamma
        /// (2 |A_m|^2 A_t + A_m^2 A_t*). |A_m|^2 is the mean of the powers entering and leaving the element, A_m the field entering.
        /// Only the part of the tail that overlaps the main pulse (inside the main window) is changed; accurate to first order in the
        /// B-integral. Scalar (x) only, no Raman.
        /// </summary>
        public void CrossPhase(PulseField mainIn, PulseField mainOut, double gammaPerW)
        {
            if (!(gammaPerW > 0) || IsZero(_tx)) return;
            var ain = SpectralTransform.FreqToTime(mainIn.Ax); var aout = SpectralTransform.FreqToTime(mainOut.Ax);
            var t = SpectralTransform.FreqToTime(_tx);
            bool changed = false;
            for (int i = 0; i < Main.N; i++)
            {
                int j = _offset + i;
                double p = 0.5 * (ain[i].Real * ain[i].Real + ain[i].Imaginary * ain[i].Imaginary + aout[i].Real * aout[i].Real + aout[i].Imaginary * aout[i].Imaginary);
                if (p == 0) continue;
                var a = t[j];
                t[j] = a + Complex.ImaginaryOne * gammaPerW * (2 * p * a + ain[i] * ain[i] * Complex.Conjugate(a));
                changed = true;
            }
            if (changed) _tx = SpectralTransform.TimeToFreq(t);
        }

        /// <summary>For tests and tools: add a field (on the long grid) to the tail.</summary>
        /// <summary>An independent copy, for a new branch.</summary>
        public TailLedger Clone()
        {
            var c = (TailLedger)MemberwiseClone();
            c._tx = (Complex[])_tx.Clone(); c._ty = (Complex[])_ty.Clone();
            return c;
        }

        /// <summary>Multiply the tail by a factor of absolute frequency, on both polarisations.</summary>
        public void Multiply(Func<double, Complex> factor) => MultiplyPolarized(w => { var f = factor(w); return (f, f); });

        public void MultiplyPolarized(Func<double, (Complex X, Complex Y)> factor)
        {
            for (int i = 0; i < _tx.Length; i++)
            {
                var (x, y) = factor(Long.OmegaAbsolute[i]);
                _tx[i] *= x; _ty[i] *= y;
            }
        }

        /// <summary>Add another path's tail, on the same grid.</summary>
        public void Add(TailLedger other)
        {
            for (int i = 0; i < _tx.Length; i++) { _tx[i] += other._tx[i]; _ty[i] += other._ty[i]; }
        }

        public void AddToTail(PulseField f) { for (int i = 0; i < _tx.Length; i++) { _tx[i] += f.Ax[i]; _ty[i] += f.Ay[i]; } }

        /// <summary>Fraction of the tail energy in the outer 5 % of each side of the long window (a tail that wraps).</summary>
        public double TailEdgeEnergyFraction()
        {
            var tx = SpectralTransform.FreqToTime(_tx); var ty = SpectralTransform.FreqToTime(_ty);
            double tot = 0, edge = 0; int n = Long.N, w = n / 20;
            for (int i = 0; i < n; i++)
            {
                double p = tx[i].Real * tx[i].Real + tx[i].Imaginary * tx[i].Imaginary + ty[i].Real * ty[i].Real + ty[i].Imaginary * ty[i].Imaginary;
                tot += p; if (i < w || i >= n - w) edge += p;
            }
            return tot > 0 ? edge / tot : 0;
        }
    }
}
