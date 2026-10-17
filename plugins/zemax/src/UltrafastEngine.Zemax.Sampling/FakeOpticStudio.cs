using System;
using System.Collections.Generic;
using System.Linq;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// A tiny analytic stand-in for OpticStudio (tests and demos without Zemax): exact straight-line chief-ray
    /// geometry for a few hand-built systems, with Zemax conventions (index of the medium after each surface,
    /// negative after a mirror; OPL without grating phase; 1-based wavelengths; a wavelength-count limit).
    ///   "treacy": grating pair (1200 l/mm, 40 deg) followed by a 10 mm fused-silica slab;
    ///   "mpc":    an argon-filled Herriott-type cell, 8 mirrors, 500 mm passes, focus mid-pass;
    ///   "slab":   a single 5 mm fused-silica plate at 10 deg incidence with an AR-coated front.
    /// </summary>
    public sealed class FakeOpticStudio : IOpticStudio, ISurfaceEditor
    {
        public static int MaxWaves = 24;
        public string Kind { get; }
        public readonly List<SurfaceRow> Rows = new List<SurfaceRow>();
        private readonly List<(double Um, double Weight)> _waves = new List<(double, double)> { (0.78, 0.3), (0.888, 0.3), (0.99, 0.3) };
        private int _primary = 2;
        private Func<int, double, double[]> _pos, _dir, _normal;
        private Func<int, double, double> _idx;
        private Func<int, double> _gbRadius, _gbWaist;
        private Func<int, double, int> _vignette = (s, l) => 0;
        public double GratingSeparationMm { get; }
        public int OperandCalls { get; private set; }
        /// <summary>Gradient 1 surfaces written through <see cref="ISurfaceEditor"/>: surface -> (n0, Nr2, deltaT).</summary>
        public readonly Dictionary<int, (double N0, double Nr2, double DeltaT)> Gradients = new Dictionary<int, (double, double, double)>();
        public void SetGradient1(int surface, double n0, double nr2, double deltaT) { Gradients[surface] = (n0, nr2, deltaT); if (surface < Rows.Count) Rows[surface].TypeName = "Gradient1"; }

        public FakeOpticStudio(string kind = "treacy", double gratingSeparationMm = 100)
        {
            Kind = kind.ToLowerInvariant();
            GratingSeparationMm = gratingSeparationMm;
            switch (Kind)
            {
                case "mpc": BuildMpc(); break;
                case "slab": BuildSlab(); break;
                default: BuildTreacy(); break;
            }
        }

        public static double SilicaIndex(double um)
        {
            double l2 = um * um;
            return Math.Sqrt(1 + 0.6961663 * l2 / (l2 - 0.0684043 * 0.0684043) + 0.4079426 * l2 / (l2 - 0.1162414 * 0.1162414) + 0.8974794 * l2 / (l2 - 9.896161 * 9.896161));
        }

        private void Add(string material = "", string comment = "", string type = "Standard", double thickness = 0, double semiDiameter = double.NaN, string coating = "")
            => Rows.Add(new SurfaceRow { Index = Rows.Count, Material = material, Comment = comment, TypeName = type, Thickness = thickness, SemiDiameter = semiDiameter, Coating = coating });

        private void BuildTreacy()
        {
            double d = 1e-3 / 1200 * 1e3, G = GratingSeparationMm, gam = 40 * Math.PI / 180;
            Add(thickness: 1e10);
            Add(comment: "start");
            Add(type: "DiffractionGrating", comment: "G1", material: "MIRROR");
            Add(type: "DiffractionGrating", comment: "G2", material: "MIRROR", thickness: 150);
            Add(comment: "out plane", thickness: 20);
            Add(material: "F_SILICA", thickness: 10, semiDiameter: 12.5);
            Add(thickness: 20);
            Add(comment: "image");
            var kin = new[] { Math.Sin(gam), 0, Math.Cos(gam) };
            Func<double, double> thd = um => Math.Asin(um * 1e-3 / d - Math.Sin(gam));
            Func<double, double[]> p2 = um => { double t = G / Math.Cos(thd(um)); return new[] { -t * Math.Sin(thd(um)), 0, t * Math.Cos(thd(um)) }; };
            var r0 = new[] { 500 * kin[0], 0, 2 * G + 500 * kin[2] };
            _pos = (s, um) =>
            {
                if (s <= 2) return new[] { 0.0, 0, 0 };
                if (s == 3) return p2(um);
                var p = p2(um); double L2 = (r0[0] - p[0]) * kin[0] + (r0[2] - p[2]) * kin[2];
                double extra = s == 4 ? 0 : s == 5 ? 20 : s == 6 ? 30 : 50;
                return new[] { p[0] + (L2 + extra) * kin[0], 0, p[2] + (L2 + extra) * kin[2] };
            };
            _dir = (s, um) => s == 0 || s == 1 ? new[] { -Math.Sin(gam), 0, -Math.Cos(gam) } : s == 2 ? new[] { -Math.Sin(thd(um)), 0, Math.Cos(thd(um)) } : kin;
            _normal = (s, um) => s == 2 || s == 3 ? new[] { 0.0, 0, 1 } : kin;
            _idx = (s, um) => s == 5 ? SilicaIndex(um) : (s == 2 || s == 3 ? -1.0 : 1.0);
            _gbRadius = s => 2.0; _gbWaist = s => 2.0;
            _vignette = (s, um) => um > 1.15 ? 3 : 0;
        }

        private void BuildMpc()
        {
            const int P = 8; double L = 500, w0 = 0.2, lam = 1.03e-3, zr = Math.PI * w0 * w0 / lam, wm = w0 * Math.Sqrt(1 + Math.Pow(0.5 * L / zr, 2));
            Add(thickness: 1e10);
            Add(material: "ARGON", comment: "entrance", thickness: L);
            for (int k = 0; k < P; k++) Add(material: "MIRROR", comment: "MPC_MIRROR", thickness: (k % 2 == 0) ? -L : L, semiDiameter: 25.4);
            Add(comment: "exit", thickness: 10);
            Add(comment: "image");
            int last = Rows.Count - 1;
            _pos = (s, um) =>
            {
                if (s <= 1) return new[] { 0.0, 0, 0 };
                if (s <= P + 1) { int k = s - 1; return new[] { 0.5 * k, 0, (k % 2 == 1) ? L : 0 }; }
                var pm = new[] { 0.5 * P, 0, (P % 2 == 1) ? L : 0.0 }; double dz = (P % 2 == 1) ? -1 : 1;
                return new[] { pm[0], 0, pm[2] + dz * (s == P + 2 ? L : L + 10) };
            };
            _dir = (s, um) =>
            {
                var a = _pos(Math.Min(s + 1, last), um); var b = _pos(s, um);
                var v = new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; double n = Math.Sqrt(v.Sum(x => x * x));
                return n > 0 ? v.Select(x => x / n).ToArray() : new[] { 0.0, 0, 1 };
            };
            _normal = (s, um) => new[] { 0.0, 0, (s % 2 == 0) ? -1.0 : 1.0 };
            _idx = (s, um) => (s >= 2 && s <= P + 1) ? -1.0 : 1.0;
            _gbRadius = s => wm; _gbWaist = s => w0;
        }

        private void BuildSlab()
        {
            double th = 10 * Math.PI / 180, t = 5;
            Add(thickness: 1e10);
            Add(comment: "start", thickness: 50);
            Add(material: "F_SILICA", comment: "plate front", thickness: t, semiDiameter: 12.7, coating: "AR1030");
            Add(comment: "plate back", thickness: 50, semiDiameter: 12.7);
            Add(comment: "image");
            var k0 = new[] { 0.0, 0, 1 };
            var nrm = new[] { Math.Sin(th), 0, Math.Cos(th) }; // tilted plate
            _pos = (s, um) =>
            {
                double n = SilicaIndex(um), tt = Math.Asin(Math.Sin(th) / n);
                double inside = t / Math.Cos(tt); // geometric path inside the tilted plate
                if (s <= 1) return new[] { 0.0, 0, 0 };
                if (s == 2) return new[] { 0.0, 0, 50 };
                var din = new[] { Math.Sin(th - tt) * -1, 0, Math.Cos(th - tt) }; // refracted direction (approx. in x-z plane)
                var p3 = new[] { inside * din[0], 0, 50 + inside * din[2] };
                if (s == 3) return p3;
                return new[] { p3[0], 0, p3[2] + 50 };
            };
            _dir = (s, um) =>
            {
                double n = SilicaIndex(um), tt = Math.Asin(Math.Sin(th) / n);
                return s == 2 ? new[] { -Math.Sin(th - tt), 0, Math.Cos(th - tt) } : k0;
            };
            _normal = (s, um) => nrm;
            _idx = (s, um) => s == 2 ? SilicaIndex(um) : 1.0;
            _gbRadius = s => 1.0; _gbWaist = s => 1.0;
        }

        // ---------------------------------------------------------------- IOpticStudio
        public string SystemFile => "FAKE_" + Kind + ".zos";
        public double LensUnitToMeters => 1e-3;
        public double TemperatureC => 20;
        public double PressureAtm => 1;
        public int NumberOfSurfaces => Rows.Count;
        public SurfaceRow GetSurface(int index) => Rows[index];

        public IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary) { primary = _primary; return _waves.ToList(); }

        public int SetWavelengths(IReadOnlyList<double> um)
        {
            int n = Math.Min(um.Count, MaxWaves);
            _waves.Clear();
            for (int i = 0; i < n; i++) _waves.Add((um[i], 1.0));
            _primary = 1;
            return n;
        }

        public void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary)
        {
            _waves.Clear(); _waves.AddRange(waves); _primary = primary;
        }

        private double Opl(int s, double um)
        {
            double opl = 0;
            for (int k = 2; k <= s; k++)
            {
                var a = _pos(k - 1, um); var b = _pos(k, um); var dv = _dir(k - 1, um);
                opl += Math.Abs(_idx(k - 1, um)) * ((b[0] - a[0]) * dv[0] + (b[1] - a[1]) * dv[1] + (b[2] - a[2]) * dv[2]);
            }
            return opl;
        }

        public ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves)
        {
            var r = new ChiefRayHit[numberOfWaves];
            for (int w = 0; w < numberOfWaves; w++)
            {
                double um = _waves[w].Um;
                var p = _pos(toSurface, um); var d = _dir(toSurface, um);
                r[w] = new ChiefRayHit { X = p[0], Y = p[1], Z = p[2], L = d[0], M = d[1], N = d[2], Opl = Opl(toSurface, um) };
                int v = _vignette(toSurface, um); if (v != 0 && v <= toSurface) r[w].Vignette = v;
                for (int k = 1; k <= toSurface; k++) if (_pos(k, um).Any(double.IsNaN)) { r[w].Error = 2; break; }
            }
            return r;
        }

        public double[] GlobalMatrix(int surface) => new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };

        public void GetIndex(int surface, int numberOfWaves, double[] dest)
        {
            for (int i = 0; i < numberOfWaves; i++) dest[i] = _idx(surface, _waves[i].Um);
        }

        public double Operand(string type, int surface, int wave, double hx = 0, double hy = 0, double px = 0, double py = 0, double ex = 0, double ey = 0)
        {
            OperandCalls++;
            double um = _waves[wave - 1].Um;
            var p = _pos(surface, um); var d = _dir(surface, um);
            switch (type)
            {
                case "RAGX": return p[0];
                case "RAGY": return p[1];
                case "RAGZ": return p[2];
                case "RAGA": return d[0];
                case "RAGB": return d[1];
                case "RAGC": return d[2];
                case "RENA": return _normal(surface, um)[0];
                case "RENB": return _normal(surface, um)[1];
                case "RENC": return _normal(surface, um)[2];
                case "RAID":
                {
                    var kin = _dir(surface - 1, um); var n = _normal(surface, um);
                    double c = Math.Abs(kin[0] * n[0] + kin[1] * n[1] + kin[2] * n[2]);
                    return Math.Acos(Math.Min(1, c)) * 180 / Math.PI;
                }
            }
            throw new NotSupportedException("fake OpticStudio does not implement operand " + type);
        }

        public double[] EvaluateRows(IReadOnlyList<OperandRow> rows) =>
            rows.Select(r => r.Type == "GBPS" ? _gbRadius((int)r.Params[0]) : r.Type == "GBPW" ? _gbWaist((int)r.Params[0]) : throw new NotSupportedException(r.Type)).ToArray();
    }
}
