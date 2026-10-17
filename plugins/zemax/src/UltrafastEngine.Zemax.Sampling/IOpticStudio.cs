using System;
using System.Collections.Generic;

namespace UltrafastEngine.Zemax
{
    /// <summary>One Lens Data Editor row (what the system builder needs to classify it).</summary>
    public sealed class SurfaceRow
    {
        public int Index { get; set; }
        public string Material { get; set; } = "";
        public string Comment { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Coating { get; set; } = "";
        public double Thickness { get; set; }
        /// <summary>Semi-diameter (clear aperture radius), lens units; NaN if unknown.</summary>
        public double SemiDiameter { get; set; } = double.NaN;
        public bool IsIgnored { get; set; }

        public bool IsMirror => string.Equals((Material ?? "").Trim(), "MIRROR", StringComparison.OrdinalIgnoreCase);
        public bool IsCoordinateBreak => TypeName != null && (TypeName.IndexOf("CoordinateBreak", StringComparison.OrdinalIgnoreCase) >= 0 || TypeName.IndexOf("CoordBrk", StringComparison.OrdinalIgnoreCase) >= 0 || TypeName.IndexOf("Coordinate Break", StringComparison.OrdinalIgnoreCase) >= 0);
        public bool IsGrating => TypeName != null && TypeName.IndexOf("Grating", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Result of tracing the chief ray (Hx = Hy = Px = Py = 0) of one wavelength to one surface,
    /// in that surface's LOCAL coordinates (batch ray trace, real rays, OPD mode None).</summary>
    public struct ChiefRayHit
    {
        public int Error, Vignette;
        public double X, Y, Z, L, M, N, Opl;
    }

    /// <summary>A temporary merit-function row: operand type and its parameter cells (Param1..ParamN).</summary>
    public sealed class OperandRow
    {
        public string Type { get; set; }
        public double[] Params { get; set; }
        /// <summary>Which parameters are integer cells (surface/wave numbers...).</summary>
        public bool[] IntegerParams { get; set; }
    }

    /// <summary>
    /// The small slice of OpticStudio the sampler uses. Implemented over ZOS-API by the Windows binding
    /// (ZosOpticStudio), by <see cref="FakeOpticStudio"/> for tests, and by <see cref="RecordingOpticStudio"/> /
    /// <see cref="ReplayOpticStudio"/> for capturing a session on the Zemax PC and replaying it anywhere.
    /// Surfaces are 0-based LDE rows (0 = object, NumberOfSurfaces-1 = image); wavelengths 1-based.
    /// </summary>
    public interface IOpticStudio
    {
        string SystemFile { get; }
        double LensUnitToMeters { get; }
        /// <summary>System environment (System Explorer > Environment): temperature (C) and pressure (atm).</summary>
        double TemperatureC { get; }
        double PressureAtm { get; }
        int NumberOfSurfaces { get; }
        SurfaceRow GetSurface(int index);

        IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary);
        /// <summary>Set the Wavelength Data Editor to exactly these wavelengths (weight 1, first primary).
        /// Returns how many were set (OpticStudio caps the number of wavelengths).</summary>
        int SetWavelengths(IReadOnlyList<double> um);
        void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary);

        /// <summary>Chief ray of wavelengths 1..numberOfWaves traced to <paramref name="toSurface"/>.</summary>
        ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves);
        /// <summary>Local-to-global matrix of a surface: r11 r12 r13 r21 r22 r23 r31 r32 r33 x0 y0 z0.</summary>
        double[] GlobalMatrix(int surface);
        /// <summary>Index of the medium FOLLOWING the surface for wavelengths 1..n (negative after a mirror).</summary>
        void GetIndex(int surface, int numberOfWaves, double[] dest);
        /// <summary>IMeritFunctionEditor.GetOperandValue (no rows added).</summary>
        double Operand(string type, int surface, int wave, double hx = 0, double hy = 0, double px = 0, double py = 0, double ex = 0, double ey = 0);
        /// <summary>Append temporary rows to the merit function, calculate it, read their values, remove them.</summary>
        double[] EvaluateRows(IReadOnlyList<OperandRow> rows);
    }

    /// <summary>Writing back to OpticStudio (optional capability of an <see cref="IOpticStudio"/>).</summary>
    public interface ISurfaceEditor
    {
        /// <summary>Make a surface a Gradient 1 surface: n = n0 + Nr2 r^2 (Nr2 in 1/lens-unit^2), ray-trace step deltaT (lens units).</summary>
        void SetGradient1(int surface, double n0, double nr2PerLensUnit2, double deltaTLensUnits);
    }

    /// <summary>
    /// The thermal lens of a gain element (summary.json values thermalGrinNr2PerM2, thermalDnOnAxis, hostIndex) as a
    /// Gradient 1 surface: n(r) = n_host + Delta n(0) + Nr2 r^2, z-averaged over the crystal.
    /// </summary>
    public static class ThermalLensExport
    {
        public static (double N0, double Nr2PerLensUnit2, double DeltaT, double FocalLengthM) Plan(Serialization.JsonObject summary, string elementId, double lensUnitM, double lengthLensUnits)
        {
            Serialization.JsonObject el = null;
            foreach (var e in summary.GetArray("elements") ?? new Serialization.JsonArray())
                if (string.Equals(e.AsObject().GetString("id"), elementId, StringComparison.OrdinalIgnoreCase)) el = e.AsObject();
            if (el == null) throw new ArgumentException("no element '" + elementId + "' in the results");
            var v = el.GetObject("values") ?? throw new ArgumentException("element '" + elementId + "' has no values");
            double nr2 = v.GetDouble("thermalGrinNr2PerM2", double.NaN);
            if (double.IsNaN(nr2)) throw new ArgumentException("element '" + elementId + "' has no thermal lens (gain or amplifier element with a pump and thermalLens on)");
            double n0 = v.GetDouble("hostIndex", double.NaN) + v.GetDouble("thermalDnOnAxis", 0);
            double f = v.GetDouble("thermalLensFocalLengthM", double.NaN);
            double dt = Math.Max(lengthLensUnits / 50, 1e-6);
            return (n0, nr2 * lensUnitM * lensUnitM, dt, f);
        }
    }
}
