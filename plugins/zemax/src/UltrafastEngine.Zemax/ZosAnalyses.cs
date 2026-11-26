// ZosAnalyses.cs -- ZosOpticStudio, part 3 of 3 (see ZosOpticStudio.cs): computations OpticStudio runs for the sampler --
// the batch ray trace of chief rays and merit-function operands (single values and temporary rows).
//   ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves)
//   double Operand(string type, int surface, int wave, ...); double[] EvaluateRows(IReadOnlyList<OperandRow> rows)
using System;
using System.Collections.Generic;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.RayTrace;

namespace UltrafastEngine.Zemax
{
    public sealed partial class ZosOpticStudio
    {
        public ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves)
        {
            // TODO(zemax-hw): AddRay(wave, Hx, Hy, Px, Py, OPDMode) and the ReadNextResult column order are taken from the ZOS-API documentation, not run.
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
