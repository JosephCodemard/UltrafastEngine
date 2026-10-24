using System;
using System.Collections.Generic;

namespace UltrafastEngine.Models
{
    // =====================================================================================================
    //  The extension points. Every physical ingredient the engine uses is behind one of these interfaces,
    //  created from a ModelSpec by the ModelRegistry. To customise the physics you either
    //    (a) write JSON data / an expression in the system file or run spec (no code), or
    //    (b) register a callback (C#, Python, C/C++ via the C API) and reference it as {"kind":"callback"}, or
    //    (c) implement the interface in C# and register a factory for a new "kind".
    //  All evaluation is VECTORISED: one call per array (per solver stage), never per sample.
    // =====================================================================================================

    /// <summary>Real refractive index n(omega). Units: omega in rad/s (absolute). May return NaN outside its
    /// range of validity; the engine detects and reports that rather than propagating NaNs.</summary>
    public interface IRefractiveIndex
    {
        string Description { get; }
        void Evaluate(double[] omega, double[] nOut);
    }

    /// <summary>Power attenuation coefficient alpha(omega) in 1/m (intensity falls as exp(-alpha z)).</summary>
    public interface IAttenuation
    {
        string Description { get; }
        void Evaluate(double[] omega, double[] alphaPerMOut);
    }

    /// <summary>
    /// Everything a time-domain response model can see at one solver stage. Arrays are length N on the time
    /// grid of the current representation (envelope grid, or the finer carrier-resolved grid in field mode).
    /// </summary>
    public sealed class ResponseContext
    {
        /// <summary>Time in the co-moving frame (s): t = 0 is the carrier group delay of the pulse.</summary>
        public double[] T { get; set; }
        /// <summary>Lab-frame arrival time of the frame origin at this z (s): t_lab = T + TArrival.</summary>
        public double TArrival { get; set; }
        /// <summary>Lab time t_lab = T + TArrival (s). Use this for phenomena fixed in the lab (e.g. a
        /// time-modulated index, a pump pulse, a plasma that lives after the pulse).</summary>
        public double[] TLab { get; set; }
        /// <summary>Position inside the current element (m) and the element's total length.</summary>
        public double Z { get; set; }
        public double ElementLength { get; set; }
        /// <summary>Cycle-averaged intensity (W/m^2), total and per polarisation.</summary>
        public double[] Intensity { get; set; }
        public double[] IntensityX { get; set; }
        public double[] IntensityY { get; set; }
        /// <summary>Instantaneous intensity n0 c eps0 E(t)^2 / 2 -- oscillates at 2 omega0. Equals Intensity in
        /// envelope mode (no carrier); only meaningful in field mode.</summary>
        public double[] InstantIntensity { get; set; }
        /// <summary>Real optical field in sqrt(W) units (field mode only, otherwise null).</summary>
        public double[] Field { get; set; }
        public double Omega0 { get; set; }
        public bool CarrierResolved { get; set; }
        public double EffectiveAreaM2 { get; set; }
        public string ElementId { get; set; }
        /// <summary>Transverse position of this point (m) in spatial models (x, y from the beam axis; r, theta polar);
        /// 0 in 1-D models, where the response sees the beam's on-axis intensity.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public double R => Math.Sqrt(X * X + Y * Y);
        public double Theta => Math.Atan2(Y, X);
        /// <summary>True when evaluated at one point of a transverse (radial / cartesian) grid.</summary>
        public bool Spatial { get; set; }
        /// <summary>Refractive index of the medium at the carrier (carrier-resolved mode; NaN if unknown).</summary>
        public double N0 { get; set; } = double.NaN;
        public int N => T?.Length ?? 0;
    }

    /// <summary>
    /// A time-domain change of refractive index, Delta n(t) = dnRe + i dnIm, driven by the pulse (and/or by
    /// lab time). Positive dnIm is absorption. The built-in Kerr+Raman response is one of these; user models
    /// are ADDED to it (or replace it, with "replaceKerr": true). Contract: write into dnRe/dnIm (pre-zeroed,
    /// length ctx.N); do not keep references to the context arrays.
    /// </summary>
    public interface ITimeDomainResponse
    {
        string Description { get; }
        /// <summary>True if the model varies on the optical-cycle time scale (e.g. index modulated at ~fs
        /// period). The engine then switches that element to the carrier-resolved field representation.</summary>
        bool NeedsCarrierResolution { get; }
        void Evaluate(ResponseContext ctx, double[] dnRe, double[] dnIm);
    }

    /// <summary>A spectral filter: power transmission T(omega) in [0,1] and spectral phase phi(omega) (rad).
    /// Phase may contain a linear part -- the engine removes the carrier group delay and reports it.</summary>
    public interface ISpectralFilter
    {
        string Description { get; }
        void Evaluate(double[] omega, double[] powerTransmissionOut, double[] phaseOut);
    }

    /// <summary>Beam size along an element (z from the element start, m). Radii are 1/e^2 intensity radii.
    /// EffectiveArea is defined so peak intensity = power / EffectiveArea (pi wx wy / 2 for a Gaussian).</summary>
    public interface IBeamModel
    {
        string Description { get; }
        double RadiusX(double z);
        double RadiusY(double z);
        double EffectiveArea(double z);
    }

    /// <summary>A hard aperture acting on a beam: returns the fraction of power transmitted.</summary>
    public interface IAperture
    {
        string Description { get; }
        double PowerTransmission(double beamRadiusX, double beamRadiusY, double decenterX, double decenterY);
    }

    /// <summary>Wavefront aberrations at an element (Zernike, Noll ordering, in waves at ReferenceWavelengthM,
    /// over NormalizationRadiusM). Carried through Phase 0/1 (reported, used for Strehl estimates); applied to
    /// the field at model level L2.</summary>
    public interface IAberrationModel
    {
        string Description { get; }
        double ReferenceWavelengthM { get; }
        double NormalizationRadiusM { get; }
        IReadOnlyList<double> ZernikeNollWaves { get; }
    }

    /// <summary>Normalised delayed (Raman-like) response h(t), integral 1, h(t &lt; 0) = 0.</summary>
    public interface IDelayedResponse
    {
        string Description { get; }
        double[] Evaluate(double[] t);
    }

    /// <summary>
    /// A delayed response with a polarisation structure (Hellwarth; Lin &amp; Agrawal, Opt. Lett. 29, 1114 (2004)):
    /// R_ijkl(t) = h_a(t) d_ij d_kl + h_b(t) (d_ik d_jl + d_il d_jk) / 2, with h_a + h_b = h (<see cref="IDelayedResponse.Evaluate"/>).
    /// The isotropic part h_a only changes the index for both polarisations alike; the anisotropic part h_b couples them
    /// (it contributes to nonlinear ellipse rotation and makes circular light see less Raman than linear light).
    /// Responses without this interface are isotropic (h_b = 0).
    /// </summary>
    public interface IVectorDelayedResponse : IDelayedResponse
    {
        /// <summary>The anisotropic part h_b(t), on the same normalisation as Evaluate (h_a = h - h_b).</summary>
        double[] EvaluateAnisotropic(double[] t);
        /// <summary>Integral of h_b relative to that of h (0: isotropic, 1: purely anisotropic).</summary>
        double AnisotropicFraction { get; }
    }

    // ------------------------------------------------------------------------------- managed callbacks
    // These are what the C API and the Python bindings wrap user functions into. Register them on the
    // ModelRegistry (or the run) under a name and reference them as {"kind": "callback", "name": "..."}.

    /// <summary>n(omega) callback: fill nOut[i] for omega[i].</summary>
    public delegate void IndexCallback(double[] omega, double[] nOut);
    /// <summary>Delta n(t) callback (see <see cref="ITimeDomainResponse"/>).</summary>
    public delegate void TimeResponseCallback(ResponseContext ctx, double[] dnRe, double[] dnIm);
    /// <summary>Spectral filter callback: power transmission and phase at each omega.</summary>
    public delegate void FilterCallback(double[] omega, double[] powerTransmissionOut, double[] phaseOut);
    /// <summary>Beam callback: radii (m) at each z (m from element start).</summary>
    public delegate void BeamCallback(double[] z, double[] radiusXOut, double[] radiusYOut);
    /// <summary>Generic scalar-function callback y = f(x), vectorised (e.g. attenuation, Raman h(t)).</summary>
    public delegate void ArrayCallback(double[] x, double[] yOut);
    /// <summary>User element: transform the main beam's spectrum in place (see <see cref="Engine.UserElementContext"/>).</summary>
    public delegate void ElementCallback(Engine.UserElementContext ctx);
}
