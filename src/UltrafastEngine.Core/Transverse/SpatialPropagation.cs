using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UltrafastEngine.Engine;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;

namespace UltrafastEngine.Transverse
{
    /// <summary>
    /// Scalar spatio-temporal envelope propagation, state E(k_perp, omega) (layout [m * n + i], sqrt(W/m^2)):
    /// dE/dz = [L_1D(omega) + i (k_z(omega, k_perp) - k(omega))] E + N(E), with k_z = sqrt(k^2 - k_perp^2)
    /// (non-paraxial, evanescent modes decay) or the paraxial k - k_perp^2 / 2k. L_1D is the element's
    /// frame-referenced 1-D linear operator (dispersion, losses), so diffraction, space-time focusing and
    /// dispersion are all exact in the interaction picture. N is the Kerr + Raman (+ user Delta n) response
    /// evaluated pointwise in (x, t), with self-steepening.
    /// </summary>
    public sealed class SpatialEnvelopeProblem : IPropagationProblem, IStaticRate
    {
        /// <summary>Phase / attenuation rate (1/m) of the position-dependent terms where the beam is (step control; not part of the B-integral).</summary>
        public double StaticPhaseRate { get; private set; }
        private readonly FrequencyGrid _g;
        private readonly ITransverseBasis _b;
        private readonly NonlinearMedium _m;
        private readonly Complex[] _ramanKernelFreq, _ramanAnisoFreq;
        private readonly double _k0, _tArrival0, _tauElement;
        private readonly string _id;
        private readonly SpatialProperties _props;
        private readonly double[] _dnStatic, _alphaStatic, _n2Static; // per point, when independent of z
        private readonly double[] _omegaOverC;
        public int Length { get; }
        public double LengthM { get; }
        public Complex[] LinearRate { get; }
        public bool Active { get; }

        /// <summary>Vector (Jones) propagation: state = [Ex; Ey], each M x n, with the isotropic chi(3) vector coupling
        /// evaluated pointwise -- nonlinear ellipse rotation and XPM are resolved across the beam.</summary>
        public bool Vector { get; }
        private readonly int _block;
        private readonly double[] _uppe;   // UPPE nonlinear coupling factor n(w) w / (c k_z) per (mode, frequency), or null

        public SpatialEnvelopeProblem(FrequencyGrid grid, ITransverseBasis basis, NonlinearMedium medium, double lengthM, double[] ph1D, double[] amp1D,
            Func<double, double> indexAt, bool paraxial, double tArrivalAtStart, double elementCarrierGroupDelay, string elementId, SpatialProperties props = null,
            bool vector = false, double[] ph1DY = null, double[] amp1DY = null, bool uppe = false)
        {
            Vector = vector;
            _props = props != null && props.Any ? props : null;
            _g = grid; _b = basis; _m = medium; LengthM = lengthM; _id = elementId;
            _tArrival0 = tArrivalAtStart; _tauElement = elementCarrierGroupDelay;
            _k0 = grid.OmegaCenter / PhysicalConstants.SpeedOfLightMPerS;
            int n = grid.N, M = basis.M;
            _block = M * n;
            Length = vector ? 2 * M * n : M * n;
            var lx = BuildLinearRate(grid, basis, lengthM, ph1D, amp1D, indexAt, paraxial, null);
            if (vector)
            {
                var ly = ph1DY != null ? BuildLinearRate(grid, basis, lengthM, ph1DY, amp1DY ?? amp1D, indexAt, paraxial, null) : lx;
                LinearRate = new Complex[2 * _block];
                Array.Copy(lx, 0, LinearRate, 0, _block); Array.Copy(ly, 0, LinearRate, _block, _block);
            }
            else LinearRate = lx;
            if (uppe) _uppe = UppeFactor(grid, basis, indexAt);
            Active = (medium != null && medium.IsActive) || _props != null;
            _omegaOverC = grid.OmegaAbsolute.Select(w => w / PhysicalConstants.SpeedOfLightMPerS).ToArray();
            if (_props != null)
            {
                if (_props.IndexChange != null && !_props.IndexChange.DependsOnZ) _dnStatic = _props.IndexChange.AtPoints(basis.X, basis.Y, 0);
                if (_props.Absorption != null && !_props.Absorption.DependsOnZ) _alphaStatic = _props.Absorption.AtPoints(basis.X, basis.Y, 0);
                if (_props.N2Scale != null && !_props.N2Scale.DependsOnZ) _n2Static = _props.N2Scale.AtPoints(basis.X, basis.Y, 0);
            }
            if (medium != null && medium.IsActive && medium.EffectiveRamanFraction > 0)
                (_ramanKernelFreq, _ramanAnisoFreq) = Physics.EnvelopeProblem.RamanKernels(medium.Raman, grid, Vector);
        }

        /// <summary>Linear rate per (k mode, frequency): L_1D + i (k_z - k) [+ i kxShift * k_x for walk-off / lateral drift].</summary>
        internal static Complex[] BuildLinearRate(FrequencyGrid grid, ITransverseBasis basis, double lengthM, double[] ph1D, double[] amp1D,
            Func<double, double> indexAt, bool paraxial, double[] kxTilt)
        {
            int n = grid.N, M = basis.M;
            var r = new Complex[M * n];
            var k = new double[n];
            var nIdx = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = grid.OmegaAbsolute[i];
                nIdx[i] = w > 0 ? indexAt(w) : double.NaN;
                k[i] = w > 0 && !double.IsNaN(nIdx[i]) ? nIdx[i] * w / PhysicalConstants.SpeedOfLightMPerS : 0;
            }
            Parallel.For(0, M, m =>
            {
                double k2 = basis.K2[m];
                for (int i = 0; i < n; i++)
                {
                    var l1 = EnvelopeProblem.Rate(ph1D[i], amp1D[i], lengthM);
                    double ki = k[i];
                    Complex d;
                    if (ki <= 0) d = new Complex(-1e3 / Math.Max(lengthM, 1e-9), 0);
                    else if (paraxial) d = new Complex(0, -k2 / (2 * ki));
                    else if (k2 < ki * ki) d = new Complex(0, Math.Sqrt(ki * ki - k2) - ki);
                    else d = new Complex(-Math.Sqrt(k2 - ki * ki), -ki);
                    if (kxTilt != null) d += new Complex(0, kxTilt[i] * basis.Kx[m]);
                    r[m * n + i] = l1 + d;
                }
            });
            return r;
        }

        /// <summary>
        /// Unidirectional pulse propagation equation (UPPE) coupling: the nonlinear polarisation drives each plane-wave
        /// component with ω² / (2 c² ε0 k_z) instead of the paraxial ω / (2 c ε0 n); relative to the envelope model this is
        /// the factor n(ω) ω / (c k_z(ω, k⊥)) (1 on axis, larger for steep components; evanescent components are not driven).
        /// </summary>
        internal static double[] UppeFactor(FrequencyGrid grid, ITransverseBasis basis, Func<double, double> indexAt)
        {
            int n = grid.N, M = basis.M;
            var f = new double[M * n];
            var k = new double[n];
            for (int i = 0; i < n; i++) { double w = grid.OmegaAbsolute[i]; double ni = w > 0 ? indexAt(w) : double.NaN; k[i] = w > 0 && !double.IsNaN(ni) ? ni * w / PhysicalConstants.SpeedOfLightMPerS : 0; }
            for (int m = 0; m < M; m++)
                for (int i = 0; i < n; i++)
                {
                    double kz2 = k[i] * k[i] - basis.K2[m];
                    f[m * n + i] = k[i] > 0 && kz2 > 0 ? Math.Min(k[i] / Math.Sqrt(kz2), 10.0) : 0.0;
                }
            return f;
        }

        public double AreaAt(double z) => 1.0; // diffraction is in the linear operator: no area-based step limit

        /// <summary>Position-dependent linear terms at point m: i (omega / c) Delta n E - alpha / 2 E.</summary>
        private Complex StaticRate(Complex e, int i, int m, double[] dn, double[] al)
        {
            double re = al != null ? -0.5 * al[m] : 0, im = dn != null ? _omegaOverC[i] * dn[m] : 0;
            return new Complex(re * e.Real - im * e.Imaginary, re * e.Imaginary + im * e.Real);
        }

        public double Nonlinear(Complex[] state, double z, Complex[] rate)
        {
            int n = _g.N, M = _b.M;
            if (!Active) { Array.Clear(rate, 0, rate.Length); return 0; }
            var data = new Complex[_block]; Array.Copy(state, 0, data, 0, _block);
            _b.ToSpace(data, n);
            Complex[] dataY = null;
            if (Vector) { dataY = new Complex[_block]; Array.Copy(state, _block, dataY, 0, _block); _b.ToSpace(dataY, n); }
            double[] peaks = new double[M];
            bool nl = _m != null && _m.IsActive;
            double fR = nl ? _m.EffectiveRamanFraction : 0;
            bool kerr = nl && !_m.ReplaceKerr && _m.N2 != 0;
            var rot = nl ? _m.Rotational : null;
            double[] dnS = _dnStatic, alS = _alphaStatic, n2S = _n2Static;
            if (_props != null)
            {
                if (_props.IndexChange != null && dnS == null) dnS = _props.IndexChange.AtPoints(_b.X, _b.Y, z);
                if (_props.Absorption != null && alS == null) alS = _props.Absorption.AtPoints(_b.X, _b.Y, z);
                if (_props.N2Scale != null && n2S == null) n2S = _props.N2Scale.AtPoints(_b.X, _b.Y, z);
            }
            double tArr = _tArrival0 + _tauElement * (z / LengthM);
            double[] pointE = _props != null && (dnS != null || alS != null) ? new double[M] : null;
            Parallel.For(0, M, m =>
            {
                var ef = new Complex[n];
                Array.Copy(data, m * n, ef, 0, n);
                Complex[] efy = null;
                if (Vector) { efy = new Complex[n]; Array.Copy(dataY, m * n, efy, 0, n); }
                if (pointE != null) { double s = 0; for (int i = 0; i < n; i++) s += ef[i].Real * ef[i].Real + ef[i].Imaginary * ef[i].Imaginary + (efy != null ? efy[i].Real * efy[i].Real + efy[i].Imaginary * efy[i].Imaginary : 0); pointE[m] = s * _b.Weights[m]; }
                if (!nl)
                {
                    for (int i = 0; i < n; i++) data[m * n + i] = StaticRate(ef[i], i, m, dnS, alS);
                    if (Vector) for (int i = 0; i < n; i++) dataY[m * n + i] = StaticRate(efy[i], i, m, dnS, alS);
                    return;
                }
                var e = (Complex[])ef.Clone();
                SpectralTransform.FreqToTimeInPlace(e);
                Complex[] ey = null;
                if (Vector) { ey = (Complex[])efy.Clone(); SpectralTransform.FreqToTimeInPlace(ey); }
                var Ix = new double[n]; var Iy = new double[n]; var I = new double[n];
                for (int i = 0; i < n; i++)
                {
                    Ix[i] = e[i].Real * e[i].Real + e[i].Imaginary * e[i].Imaginary;
                    if (Vector) Iy[i] = ey[i].Real * ey[i].Real + ey[i].Imaginary * ey[i].Imaginary;
                    I[i] = Ix[i] + Iy[i];
                }
                var dnX = new double[n]; var dnY = Vector ? new double[n] : null; var dnI = new double[n];
                double[] dnXY = null, ramXY = null;
                double sc = n2S != null ? n2S[m] : 1.0;
                if (kerr)
                {
                    double[] ram = null, ramX = null, ramY = null;
                    if (fR > 0)
                    {
                        if (Vector && _ramanAnisoFreq != null)
                        {
                            var rxy = new double[n];
                            for (int i = 0; i < n; i++) rxy[i] = (e[i] * Complex.Conjugate(ey[i])).Real;
                            Physics.EnvelopeProblem.VectorRaman(Ix, Iy, I, rxy, _ramanKernelFreq, _ramanAnisoFreq, _g.DTime, out ramX, out ramY, out ramXY);
                        }
                        else ram = Convolution.CircularConvolveWithKernelFreq(I, _ramanKernelFreq, _g.DTime);
                    }
                    for (int i = 0; i < n; i++)
                    {
                        double rx = fR > 0 ? fR * (ramX != null ? ramX[i] : ram[i]) : 0;
                        double ry = fR > 0 ? fR * (ramY != null ? ramY[i] : ram[i]) : 0;
                        dnX[i] = sc * _m.N2 * ((1 - fR) * (Ix[i] + (Vector ? 2.0 / 3.0 * Iy[i] : 0)) + rx);
                        if (Vector) dnY[i] = sc * _m.N2 * ((1 - fR) * (Iy[i] + 2.0 / 3.0 * Ix[i]) + ry);
                    }
                }
                if (rot != null)
                {
                    // orientational (rotational Raman) response: anisotropic index tensor (docs/reference/gases.md)
                    if (!Vector) { var c = rot.Convolve(Ix, _g.DTime); for (int i = 0; i < n; i++) dnX[i] += sc * c[i]; }
                    else
                    {
                        var qx = new double[n]; var qy = new double[n]; var qxy = new double[n];
                        for (int i = 0; i < n; i++) { qx[i] = 1.5 * Ix[i] - 0.5 * I[i]; qy[i] = 1.5 * Iy[i] - 0.5 * I[i]; qxy[i] = 1.5 * (e[i] * Complex.Conjugate(ey[i])).Real; }
                        var cx = rot.Convolve(qx, _g.DTime); var cy = rot.Convolve(qy, _g.DTime); var cxy = rot.Convolve(qxy, _g.DTime);
                        dnXY = new double[n];
                        for (int i = 0; i < n; i++) { dnX[i] += sc * cx[i]; dnY[i] += sc * cy[i]; dnXY[i] = sc * cxy[i]; }
                    }
                }
                if (_m.ExtraResponses.Count > 0)
                {
                    var tl = new double[n]; for (int i = 0; i < n; i++) tl[i] = _g.Time[i] + tArr;
                    var ctx = new ResponseContext
                    {
                        T = _g.Time, TArrival = tArr, TLab = tl, Z = z, ElementLength = LengthM, Intensity = I, IntensityX = Ix, IntensityY = Iy,
                        InstantIntensity = I, Field = null, Omega0 = _g.OmegaCenter, CarrierResolved = false, EffectiveAreaM2 = _b.Weights[m], ElementId = _id,
                        X = _b.X[m], Y = _b.Y[m], Spatial = true
                    };
                    var re = new double[n]; var im = new double[n];
                    foreach (var resp in _m.ExtraResponses)
                    {
                        Array.Clear(re, 0, n); Array.Clear(im, 0, n);
                        lock (resp) resp.Evaluate(ctx, re, im);
                        for (int i = 0; i < n; i++) { dnX[i] += re[i]; if (Vector) dnY[i] += re[i]; dnI[i] += im[i]; }
                    }
                }
                var nt = new Complex[n]; var nty = Vector ? new Complex[n] : null;
                double cc = kerr && Vector ? sc * _m.N2 * (1 - fR) / 3.0 : 0;
                for (int i = 0; i < n; i++)
                {
                    nt[i] = Complex.ImaginaryOne * _k0 * new Complex(dnX[i], dnI[i]) * e[i];
                    if (Vector)
                    {
                        nty[i] = Complex.ImaginaryOne * _k0 * new Complex(dnY[i], dnI[i]) * ey[i];
                        if (cc != 0)
                        {
                            nt[i] += Complex.ImaginaryOne * _k0 * cc * ey[i] * ey[i] * Complex.Conjugate(e[i]);
                            nty[i] += Complex.ImaginaryOne * _k0 * cc * e[i] * e[i] * Complex.Conjugate(ey[i]);
                        }
                        if (dnXY != null) { nt[i] += Complex.ImaginaryOne * _k0 * dnXY[i] * ey[i]; nty[i] += Complex.ImaginaryOne * _k0 * dnXY[i] * e[i]; }
                        if (ramXY != null) { double d = sc * _m.N2 * fR * ramXY[i]; nt[i] += Complex.ImaginaryOne * _k0 * d * ey[i]; nty[i] += Complex.ImaginaryOne * _k0 * d * e[i]; }
                    }
                }
                double pk = Physics.EnvelopeProblem.PeakIndexChange(e, ey, nt, nty, _k0);
                var nf = nt; SpectralTransform.TimeToFreqInPlace(nf);
                Complex[] nfy = null; if (Vector) { nfy = nty; SpectralTransform.TimeToFreqInPlace(nfy); }
                if (_m.EnableSelfSteepening)
                    for (int i = 0; i < n; i++) { double f = 1.0 + _g.OmegaOffset[i] / _g.OmegaCenter; nf[i] *= f; if (Vector) nfy[i] *= f; }
                if (_props != null)
                {
                    for (int i = 0; i < n; i++) nf[i] += StaticRate(ef[i], i, m, dnS, alS);
                    if (Vector) for (int i = 0; i < n; i++) nfy[i] += StaticRate(efy[i], i, m, dnS, alS);
                }
                Array.Copy(nf, 0, data, m * n, n);
                if (Vector) Array.Copy(nfy, 0, dataY, m * n, n);
                peaks[m] = pk;
            });
            _b.ToK(data, n);
            if (_uppe != null) for (int k = 0; k < _block; k++) data[k] *= _uppe[k];
            Array.Copy(data, 0, rate, 0, _block);
            if (Vector)
            {
                _b.ToK(dataY, n);
                if (_uppe != null) for (int k = 0; k < _block; k++) dataY[k] *= _uppe[k];
                Array.Copy(dataY, 0, rate, _block, _block);
            }
            double phaseRate = 0;
            if (pointE != null)
            {
                double emax = pointE.Max();
                for (int m = 0; m < M; m++)
                    if (pointE[m] > 1e-4 * emax)
                        phaseRate = Math.Max(phaseRate, (dnS != null ? _k0 * Math.Abs(dnS[m]) : 0) + (alS != null ? 0.5 * Math.Abs(alS[m]) : 0));
            }
            StaticPhaseRate = phaseRate;
            return _k0 * peaks.Max();
        }
    }

    /// <summary>Building the transverse field an element starts from, following the transport rules
    /// (docs/reference/transverse.md): exact carried profile, rescaled carried profile, or the element's beam model.</summary>
    public static class TransverseStart
    {
        public static TransverseProfile Initial(TransverseSpec spec, ITransverseBasis basis, PulseField p, IBeamModel beam, Func<double, double> indexAt, string elementId, List<string> notes)
        {
            var spectrum = p.SpectralIntensity();
            var bins = TransverseOps.SampleBins(spectrum);
            var omegas = bins.Select(b => p.Grid.OmegaAbsolute[b]).ToArray();
            double wx = double.NaN, wy = double.NaN, R = double.PositiveInfinity;
            if (beam != null) (wx, wy, R) = TransverseOps.BeamAt(beam, 0);
            string mode = spec.Transport;
            if (p.Profile != null && mode != "beam")
            {
                bool exact = mode == "exact" || (mode == "auto" && p.Profile.Exact);
                if (exact)
                {
                    notes.Add("transverse field: profile carried exactly from '" + p.Profile.Origin + "'");
                    return ReferenceEquals(p.Profile.Basis, basis) ? p.Profile : TransverseOps.Rescale(p.Profile, basis, double.NaN, double.NaN, double.NaN, indexAt, p.Profile.Origin).WithExact(true);
                }
                if (double.IsNaN(wx))
                {
                    notes.Add("transverse field: profile from '" + p.Profile.Origin + "' resampled (no beam model on this element to rescale it to)");
                    return TransverseOps.Rescale(p.Profile, basis, double.NaN, double.NaN, double.NaN, indexAt, p.Profile.Origin);
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "transverse field: profile from '{0}' rescaled to this element's beam (w = {1:G4} mm, R = {2:G4} m)", p.Profile.Origin, wx * 1e3, R));
                return TransverseOps.Rescale(p.Profile, basis, wx, wy, R, indexAt, p.Profile.Origin);
            }
            if (double.IsNaN(wx)) throw new InvalidOperationException("element '" + elementId + "' is spatial but has neither a beam model (\"beam\" or inputBeam) nor a carried transverse profile.");
            notes.Add(string.Format(CultureInfo.InvariantCulture, "transverse field: from the beam model ({0}, w = {1:G4} mm, R = {2:G4} m)", spec.InitialProfile?.GetString("kind", "gaussian") ?? "gaussian", wx * 1e3, R));
            return TransverseOps.FromShape(basis, spec.InitialProfile, wx, wy, R, indexAt, omegas, elementId);
        }

        private static TransverseProfile WithExact(this TransverseProfile p, bool exact) { p.Exact = exact; return p; }

        /// <summary>Largest beam radius expected in an element (grid sizing).</summary>
        public static double ExpectedRadius(IBeamModel beam, double lengthM, PulseField p)
        {
            double w = 0;
            if (beam != null) for (int k = 0; k <= 20; k++) { double z = lengthM * k / 20; w = Math.Max(w, Math.Max(beam.RadiusX(z), beam.RadiusY(z))); }
            if (p.Profile != null) { var (a, b) = p.Profile.SecondMomentRadius(); if (!double.IsNaN(a)) w = Math.Max(w, Math.Max(a, b)); }
            return w;
        }
    }

    /// <summary>
    /// A bulk element (or gap) propagated with a spatial model: diffraction + dispersion + Kerr/Raman/user
    /// responses on a radial or cartesian grid; vector (Jones) fields when the beam carries a y component
    /// (transverse.polarization), optionally with the UPPE nonlinear coupling (transverse.equation).
    /// </summary>
    public sealed class SpatialBulkElement : IActiveElement
    {
        public TransverseSpec Spec { get; set; }
        public NonlinearMedium Medium { get; set; }
        public IBeamModel Beam { get; set; }
        public Material Material { get; set; }
        public double LengthM { get; set; }
        public SpatialProperties Properties { get; set; }
        public List<LinearTerm> Background { get; } = new List<LinearTerm>();
        public string Kind => "spatial";
        public string Description => "spatial " + Spec.Model + " propagation in " + Material?.Name + " (" + LengthM.ToString("G5", CultureInfo.InvariantCulture) + " m" + (Medium != null && Medium.IsActive ? ", nonlinear" : ", linear") + ")";
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            var g = p.Grid; int n = g.N;
            Func<double, double> idx = w => Material.IndexAt(w);
            double wexp = TransverseStart.ExpectedRadius(Beam, LengthM, p);
            if (!(wexp > 0)) throw new InvalidOperationException("element '" + ctx.Element.Id + "': cannot size the transverse grid (no beam model and no carried profile).");
            var basis = Spec.CreateBasis(wexp);
            var prof = TransverseStart.Initial(Spec, basis, p, Beam, idx, ctx.Element.Id, ctx.Record.Notes);
            basis = prof.Basis;
            var axis = OmegaAxis.FromGrid(g);
            Executor.Combine(Background, axis, out var phX, out var ampX, out var phY, out var ampY);
            double tauEl = Background.Sum(b => b.GroupDelayAt(g.OmegaCenter));
            var own = new DispersionPoint(); foreach (var b in Background) own += b.DispersionAt(g.OmegaCenter);
            ctx.Own = own;
            double e0 = p.TotalEnergyJ();
            double eyFrac = p.Ay.Sum(c => c.Real * c.Real + c.Imaginary * c.Imaginary) / Math.Max(1e-300, p.Ax.Concat(p.Ay).Sum(c => c.Real * c.Real + c.Imaginary * c.Imaginary));
            bool vector = Spec.Polarization == "vector" || (Spec.Polarization == "auto" && eyFrac > 1e-6) || (prof.DataY != null && Spec.Polarization != "scalar");
            var E = TransverseOps.Expand(p.Ax, g.OmegaAbsolute, prof);
            basis.ToK(E, n);
            Complex[] state = E;
            int block = E.Length;
            if (vector)
            {
                var Ey = TransverseOps.ExpandY(p.Ay, g.OmegaAbsolute, prof);
                basis.ToK(Ey, n);
                state = new Complex[2 * block];
                Array.Copy(E, 0, state, 0, block); Array.Copy(Ey, 0, state, block, block);
            }
            bool uppe = Spec.Equation == "uppe";
            var prob = new SpatialEnvelopeProblem(g, basis, Medium, LengthM, phX, ampX, idx, Spec.Paraxial, ctx.ArrivalS, tauEl, ctx.Element.Id, Properties, vector, phY, ampY, uppe);
            if (Properties != null && Properties.Any) ctx.Note("position-dependent properties: " + Properties.Describe());
            var stats = new StepStatistics();
            var outS = LengthM > 0 ? Steppers.Propagate(prob, state, ctx.Run.Run.Stepper, stats, ctx.Progress) : state;
            Complex[] outK = outS, outKy = null;
            if (vector) { outK = new Complex[block]; outKy = new Complex[block]; Array.Copy(outS, 0, outK, 0, block); Array.Copy(outS, block, outKy, 0, block); basis.ToSpace(outKy, n); }
            basis.ToSpace(outK, n);
            Complex[] ax, ay; TransverseProfile profOut;
            if (vector) { (ax, ay) = TransverseOps.CollapseVector(outK, outKy, basis, g.OmegaAbsolute, ctx.Element.Id, out profOut); ctx.Note("vector (Jones) propagation on the transverse grid" + (uppe ? ", UPPE coupling" : "")); }
            else
            {
                ax = TransverseOps.Collapse(outK, basis, g.OmegaAbsolute, ctx.Element.Id, out profOut);
                ay = p.Ay;
                if (p.Ay.Any(c => c != Complex.Zero))
                {
                    ay = new Complex[n];
                    for (int i = 0; i < n; i++) ay[i] = p.Ay[i] * Complex.FromPolarCoordinates((ampY ?? ampX)[i], (phY ?? phX)[i]);
                    ctx.Note("transverse.polarization = scalar: the y component was propagated with the 1-D linear operator only");
                }
                if (uppe) ctx.Note("UPPE coupling");
            }
            var q = new PulseField(g, ax, ay, p.RepRateHz) { Profile = profOut };
            // diagnostics
            var flu = TransverseOps.Fluence(outK, n, g, basis);
            if (outKy != null) { var fy = TransverseOps.Fluence(outKy, n, g, basis); for (int m = 0; m < flu.Length; m++) flu[m] += fy[m]; }
            ctx.Record.Values["peakFluenceOutJPerM2"] = flu.Max();
            ctx.Record.Values["onAxisFluenceOutJPerM2"] = flu[Array.IndexOf(basis.Radius, basis.Radius.Min())];
            double edge = 0, tot = 0; double rmax = basis.Extent;
            for (int m = 0; m < basis.M; m++) { double e = flu[m] * basis.Weights[m]; tot += e; if (basis.Radius[m] > 0.85 * rmax) edge += e; }
            if (tot > 0 && edge / tot > 1e-4)
                ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': {1:P2} of the energy reached the edge of the transverse grid (reflection/aliasing) -- enlarge transverse.radiusM / halfWidthM or extentFactor.", ctx.Element.Id, edge / tot));
            ctx.Record.Steps = stats; ctx.Record.BIntegralRad = stats.BIntegralRad;
            if (Medium != null) foreach (var rr in Medium.ExtraResponses.OfType<IReportingResponse>()) rr.Report(ctx.Record.Values);
            ctx.Record.Representation = basis.Describe() + " x " + n + " samples";
            stats.Representation = ctx.Record.Representation;
            foreach (var s in stats.Notes) ctx.Note(s);
            double e1 = q.TotalEnergyJ();
            bool absorbs = Background.Any(b => b.Mechanism == "absorption") || (Medium?.ExtraResponses.Count ?? 0) > 0 || Properties?.Absorption != null;
            ctx.Energy(absorbs ? "absorption" : (Medium != null && Medium.EffectiveRamanFraction > 0 && Medium.IsActive ? "raman+numerical" : "numerical"), e0 - e1);
            return q;
        }
    }
}
