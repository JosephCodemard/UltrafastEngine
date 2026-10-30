using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Engine;
using UltrafastEngine.Lasers;
using UltrafastEngine.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;
using UltrafastEngine.Transverse;

namespace UltrafastEngine.Optics
{
    /// <summary>
    /// A thin mask on the carried transverse profile (docs/reference/transverse.md#masks): axicon, Fresnel zone plate, vortex phase
    /// plate, slits, or an amplitude / phase map from a file. The field is multiplied at every stored frequency; a phase in
    /// optical path (axicon, map) scales with the frequency. Each grid cell gets the average of the mask over the cell.
    /// </summary>
    public sealed class MaskElement : IActiveElement
    {
        public string MaskKind;
        public double ConeAngleRad = double.NaN, FocalLengthM = double.NaN, DesignOmega = double.NaN;
        public double SlitWidthM = double.NaN, SlitSeparationM = double.NaN, SlitHeightM = double.PositiveInfinity;
        public int Charge = 1, SlitCount = 2;
        public bool BinaryPhase;
        public double CenterX, CenterY;
        public TransverseMap Map;

        public string Kind => "mask";
        public string Description => "mask: " + MaskKind;
        public double OutputOmega0 => double.NaN;
        public bool TransverseAware => true;

        private const int Sub = 6;

        /// <summary>Mask at (x, y) and frequency omega (relative to the mask's centre).</summary>
        private Complex Value(double x, double y, double omega)
        {
            double k = omega / PhysicalConstants.SpeedOfLightMPerS;
            switch (MaskKind)
            {
                case "axicon":
                    return Complex.FromPolarCoordinates(1, -k * Math.Sin(ConeAngleRad) * Math.Sqrt(x * x + y * y));
                case "zoneplate":
                {
                    double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / DesignOmega;
                    bool open = ((long)Math.Floor((x * x + y * y) / (lam * FocalLengthM))) % 2 == 0;
                    return open ? Complex.One : BinaryPhase ? -Complex.One : Complex.Zero;
                }
                case "vortex":
                    return Complex.FromPolarCoordinates(1, Charge * Math.Atan2(y, x));
                case "slits":
                {
                    if (Math.Abs(y) > SlitHeightM / 2) return Complex.Zero;
                    for (int i = 0; i < SlitCount; i++)
                        if (Math.Abs(x - (i - (SlitCount - 1) / 2.0) * SlitSeparationM) <= SlitWidthM / 2) return Complex.One;
                    return Complex.Zero;
                }
                case "file":
                    return Complex.FromPolarCoordinates(Map.Sample(Map.Amplitude, x, y), Map.Phase == null ? 0 : Map.Sample(Map.Phase, x, y) * omega / DesignOmega);
            }
            throw new InvalidOperationException("unknown mask kind " + MaskKind);
        }

        /// <summary>The mask averaged over each grid cell (cartesian: Sub x Sub points; radial: Sub equal-area rings of each annulus).</summary>
        private Complex[] Cells(ITransverseBasis b, double omega)
        {
            var t = new Complex[b.M];
            if (b is RadialBasis)
            {
                double lo2 = 0;
                for (int m = 0; m < b.M; m++)
                {
                    double hi2 = lo2 + b.Weights[m] / Math.PI;
                    Complex s = 0;
                    for (int i = 0; i < Sub; i++) s += Value(Math.Sqrt(lo2 + (i + 0.5) / Sub * (hi2 - lo2)), 0, omega);
                    t[m] = s / Sub; lo2 = hi2;
                }
                return PurePhase(t);
            }
            var c = (CartesianBasis)b;
            System.Threading.Tasks.Parallel.For(0, b.M, m =>
            {
                Complex s = 0; int ny = c.Ny > 1 ? Sub : 1;
                for (int i = 0; i < Sub; i++)
                    for (int j = 0; j < ny; j++)
                        s += Value(b.X[m] + ((i + 0.5) / Sub - 0.5) * c.Dx - CenterX, b.Y[m] + (c.Ny > 1 ? ((j + 0.5) / Sub - 0.5) * c.Dy : 0) - CenterY, omega);
                t[m] = s / (Sub * ny);
            });
            return PurePhase(t);
        }

        /// <summary>A phase mask transmits everything: the average over a cell keeps its phase, not its reduced magnitude
        /// (except where the average nearly vanishes: the singular cell on a vortex's axis stays dark).</summary>
        private Complex[] PurePhase(Complex[] t)
        {
            if (MaskKind == "axicon" || MaskKind == "vortex")
                for (int m = 0; m < t.Length; m++) { double a = t[m].Magnitude; if (a > 0.5) t[m] /= a; }
            return t;
        }

        public PulseField Execute(ActiveContext ctx, PulseField p)
        {
            if (p.Profile == null) { ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': a mask acts on a carried transverse profile; there is none here, so it was ignored (give the element before it a \"transverse\" model)."); return p; }
            var B = p.Profile.Basis;
            if (B is RadialBasis)
            {
                if (MaskKind == "vortex" || MaskKind == "slits") throw new InvalidOperationException("element '" + ctx.Element.Id + "': a " + MaskKind + " mask is not round: it needs a cartesian transverse grid (\"model\": \"cartesian\").");
                if (MaskKind == "file" && !Map.Radial) throw new InvalidOperationException("element '" + ctx.Element.Id + "': an x-y map needs a cartesian transverse grid (or give r_m for a radial one).");
                if (CenterX != 0 || CenterY != 0) ctx.Result.Warnings.Add("Element '" + ctx.Element.Id + "': a decentred mask on a radial grid is applied as if centred -- use \"model\": \"cartesian\".");
            }
            bool chromatic = MaskKind == "axicon" || (MaskKind == "file" && Map.Phase != null);
            var masks = new Complex[chromatic ? p.Profile.Omega.Length : 1][];
            for (int f = 0; f < masks.Length; f++) masks[f] = Cells(B, p.Profile.Omega[f]);
            if (MaskKind == "zoneplate")
            {
                // the zones must be resolved where the beam is (2 x the second-moment radius holds it)
                double lam = 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / DesignOmega, a = 2 * p.Profile.SecondMomentRadius().Wx;
                double outer = lam * FocalLengthM / (2 * a), dx = B is CartesianBasis c ? c.Dx : B.Extent / B.M;
                ctx.Record.Values["outerZoneWidthM"] = outer;
                if (outer < 3 * dx) ctx.Result.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Element '{0}': the Fresnel zones across the beam are down to {1:G3} m wide and the grid step is {2:G3} m: they are under-resolved (use at least 3 samples per zone).", ctx.Element.Id, outer, dx));
            }
            return ApertureElement.ApplyProfileMask(ctx, p, (m, f) => masks[chromatic ? f : 0][m]);
        }
    }
}
