using System;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;
using ZOSAPI_NetHelper;

namespace UltrafastEngine.Zemax
{
    /// <summary>How to reach OpticStudio: attach to a running instance in Interactive Extension mode
    /// (Programming tab &gt; Interactive Extension), or start a private standalone instance.</summary>
    public enum ConnectionMode { Extension, Standalone }

    /// <summary>Connection boilerplate (as in the official ZOS-API C# samples).</summary>
    public sealed class ZosConnection : IDisposable
    {
        private readonly bool _standalone;
        public IZOSAPI_Application Application { get; private set; }
        public IOpticalSystem System { get; private set; }

        private ZosConnection(IZOSAPI_Application app, IOpticalSystem sys, bool standalone) { Application = app; System = sys; _standalone = standalone; }

        public static ZosConnection Connect(ConnectionMode mode, string opticStudioDirectory = null)
        {
            bool ok = string.IsNullOrEmpty(opticStudioDirectory) ? ZOSAPI_Initializer.Initialize() : ZOSAPI_Initializer.Initialize(opticStudioDirectory);
            if (!ok) throw new InvalidOperationException("ZOSAPI_NetHelper could not locate OpticStudio (pass --opticstudio <install dir>).");
            var conn = new ZOSAPI_Connection();
            var app = mode == ConnectionMode.Standalone ? conn.CreateNewApplication() : conn.ConnectAsExtension(0);
            if (app == null)
                throw new InvalidOperationException(mode == ConnectionMode.Extension
                    ? "Could not attach to OpticStudio. Open your system and click Programming > Interactive Extension first."
                    : "Could not start a standalone OpticStudio instance.");
            if (!app.IsValidLicenseForAPI) throw new InvalidOperationException("This OpticStudio licence does not allow ZOS-API (OpticStudio Professional or Premium is required).");
            var sys = app.PrimarySystem ?? throw new InvalidOperationException("No primary optical system.");
            return new ZosConnection(app, sys, mode == ConnectionMode.Standalone);
        }

        public void Load(string path)
        {
            if (!System.LoadFile(path, false)) throw new InvalidOperationException("OpticStudio could not load " + path);
        }

        public void Dispose()
        {
            // Extension mode: never close the user's OpticStudio.
            try { if (_standalone) Application?.CloseApplication(); } catch { }
            Application = null; System = null;
        }
    }

    /// <summary>
    /// <see cref="IOpticStudio"/> over ZOS-API. Operands are addressed by NAME (parsed into MeritOperandType at run
    /// time), so an operand missing from an older OpticStudio fails with a clear message instead of breaking the
    /// build. Checked against the ZOS-API assemblies in lib/ (ILDERow.Coating / SemiDiameter,
    /// ISDEnvironmentData.Temperature / Pressure, and every operand the sampler uses).
    /// </summary>
    public sealed class ZosOpticStudio : IOpticStudio, ISurfaceEditor
    {
        private readonly IOpticalSystem _s;
        public ZosOpticStudio(IOpticalSystem system) { _s = system; }

        public string SystemFile => _s.SystemFile;

        public void SetGradient1(int surface, double n0, double nr2, double deltaT)
        {
            var row = _s.LDE.GetSurfaceAt(surface);
            var settings = row.GetSurfaceTypeSettings(ZOSAPI.Editors.LDE.SurfaceType.Gradient1);
            if (!row.ChangeType(settings)) throw new InvalidOperationException("OpticStudio refused to change surface " + surface + " to Gradient 1");
            var gr = row.SurfaceData as ZOSAPI.Editors.LDE.ISurfaceGradient1 ?? throw new InvalidOperationException("surface " + surface + " has no Gradient 1 data after the change");
            gr.DeltaT = deltaT; gr.n0 = n0; gr.Nr1 = 0; gr.Nr2 = nr2;
        }

        public double LensUnitToMeters
        {
            get
            {
                try
                {
                    switch (_s.SystemData.Units.LensUnits.ToString())
                    {
                        case "Millimeters": return 1e-3;
                        case "Centimeters": return 1e-2;
                        case "Meters": return 1.0;
                        case "Inches": return 0.0254;
                        default: return 1e-3;
                    }
                }
                catch { return 1e-3; }
            }
        }

        public double TemperatureC
        {
            get { try { return _s.SystemData.Environment.Temperature; } catch { return 20.0; } }
        }

        public double PressureAtm
        {
            get { try { return _s.SystemData.Environment.Pressure; } catch { return 1.0; } }
        }

        public int NumberOfSurfaces => _s.LDE.NumberOfSurfaces;

        public SurfaceRow GetSurface(int index)
        {
            var row = _s.LDE.GetSurfaceAt(index);
            var r = new SurfaceRow { Index = index };
            try { r.Material = row.Material ?? ""; } catch { }
            try { r.Comment = row.Comment ?? ""; } catch { }
            try { r.TypeName = row.Type.ToString(); } catch { r.TypeName = "(unknown)"; }
            try { r.Thickness = row.Thickness; } catch { }
            try { r.IsIgnored = row.TypeData.IgnoreSurface; } catch { }
            try { r.SemiDiameter = row.SemiDiameter; } catch { }
            try { r.Coating = row.Coating ?? ""; } catch { }
            return r;
        }

        public IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary)
        {
            var wl = _s.SystemData.Wavelengths;
            var list = new List<(double, double)>();
            primary = 1;
            for (int i = 1; i <= wl.NumberOfWavelengths; i++)
            {
                var w = wl.GetWavelength(i);
                list.Add((w.Wavelength, w.Weight));
                try { if (w.IsPrimary) primary = i; } catch { }
            }
            return list;
        }

        public int SetWavelengths(IReadOnlyList<double> um)
        {
            var wl = _s.SystemData.Wavelengths;
            int target = um.Count;
            while (wl.NumberOfWavelengths < target)
            {
                int before = wl.NumberOfWavelengths;
                try { wl.AddWavelength(um[Math.Min(before, target - 1)], 1.0); } catch { break; }
                if (wl.NumberOfWavelengths == before) break; // OpticStudio's wavelength limit
            }
            while (wl.NumberOfWavelengths > target) wl.RemoveWavelength(wl.NumberOfWavelengths);
            int n = Math.Min(target, wl.NumberOfWavelengths);
            for (int i = 0; i < n; i++) { var w = wl.GetWavelength(i + 1); w.Wavelength = um[i]; w.Weight = 1.0; }
            try { wl.GetWavelength(1).MakePrimary(); } catch { }
            return n;
        }

        public void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary)
        {
            SetWavelengths(waves.Select(w => w.Um).ToList());
            var wl = _s.SystemData.Wavelengths;
            for (int i = 0; i < waves.Count && i < wl.NumberOfWavelengths; i++) wl.GetWavelength(i + 1).Weight = waves[i].Weight;
            try { if (primary <= wl.NumberOfWavelengths) wl.GetWavelength(primary).MakePrimary(); } catch { }
        }

        public ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves)
        {
            var hits = new ChiefRayHit[numberOfWaves];
            var tracer = _s.Tools.OpenBatchRayTrace();
            try
            {
                var data = tracer.CreateNormUnpol(numberOfWaves, RaysType.Real, toSurface);
                for (int w = 0; w < numberOfWaves; w++) data.AddRay(w + 1, 0.0, 0.0, 0.0, 0.0, OPDMode.None);
                tracer.RunAndWaitForCompletion();
                data.StartReadingResults();
                while (data.ReadNextResult(out int ray, out int err, out int vig, out double x, out double y, out double z, out double l, out double m, out double n,
                    out double l2, out double m2, out double n2, out double opl, out double intensity))
                {
                    int i = ray - 1;
                    if (i < 0 || i >= numberOfWaves) continue;
                    hits[i] = new ChiefRayHit { Error = err, Vignette = vig, X = x, Y = y, Z = z, L = l, M = m, N = n, Opl = opl };
                }
            }
            finally { tracer.Close(); }
            return hits;
        }

        public double[] GlobalMatrix(int surface)
        {
            _s.LDE.GetGlobalMatrix(surface, out double r11, out double r12, out double r13, out double r21, out double r22, out double r23,
                out double r31, out double r32, out double r33, out double x0, out double y0, out double z0);
            return new[] { r11, r12, r13, r21, r22, r23, r31, r32, r33, x0, y0, z0 };
        }

        public void GetIndex(int surface, int numberOfWaves, double[] dest) => _s.LDE.GetIndex(surface, numberOfWaves, dest);

        private static readonly Dictionary<string, MeritOperandType> _types = new Dictionary<string, MeritOperandType>(StringComparer.OrdinalIgnoreCase);

        private static MeritOperandType Type(string name)
        {
            lock (_types)
            {
                if (_types.TryGetValue(name, out var t)) return t;
                try { t = (MeritOperandType)Enum.Parse(typeof(MeritOperandType), name, true); }
                catch (ArgumentException) { throw new NotSupportedException("This OpticStudio's ZOS-API has no merit operand '" + name + "'."); }
                _types[name] = t;
                return t;
            }
        }

        public double Operand(string type, int surface, int wave, double hx = 0, double hy = 0, double px = 0, double py = 0, double ex = 0, double ey = 0)
            => _s.MFE.GetOperandValue(Type(type), surface, wave, hx, hy, px, py, ex, ey);

        public double[] EvaluateRows(IReadOnlyList<OperandRow> rows)
        {
            var mfe = _s.MFE;
            int first = mfe.NumberOfOperands + 1, added = 0;
            var cols = new[] { MeritColumn.Param1, MeritColumn.Param2, MeritColumn.Param3, MeritColumn.Param4, MeritColumn.Param5, MeritColumn.Param6, MeritColumn.Param7, MeritColumn.Param8 };
            try
            {
                foreach (var r in rows)
                {
                    var row = mfe.InsertNewOperandAt(first + added);
                    added++;
                    row.ChangeType(Type(r.Type));
                    for (int k = 0; k < r.Params.Length && k < cols.Length; k++)
                    {
                        var cell = row.GetOperandCell(cols[k]);
                        bool integer = r.IntegerParams != null && k < r.IntegerParams.Length && r.IntegerParams[k];
                        if (integer) { try { cell.IntegerValue = (int)Math.Round(r.Params[k]); continue; } catch { } cell.DoubleValue = r.Params[k]; }
                        else { try { cell.DoubleValue = r.Params[k]; continue; } catch { } cell.IntegerValue = (int)Math.Round(r.Params[k]); }
                    }
                }
                mfe.CalculateMeritFunction();
                var v = new double[rows.Count];
                for (int i = 0; i < rows.Count; i++) v[i] = mfe.GetOperandAt(first + i).Value;
                return v;
            }
            finally
            {
                if (added > 0) mfe.RemoveOperandsAt(first, added); // (start, COUNT)
            }
        }
    }
}
