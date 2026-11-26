// ZosAnalyses.cs -- ZosOpticStudio, part 3 of 3 (see ZosOpticStudio.cs): computations OpticStudio runs for the sampler --
// the batch ray trace of chief rays, merit-function operands (single values and temporary rows) and the Zernike Standard
// Coefficients (ZERN rows, ENPD / EXPD).
//   ChiefRayHit[] TraceChief(int toSurface, int numberOfWaves)
//   double Operand(string type, int surface, int wave, ...); double[] EvaluateRows(IReadOnlyList<OperandRow> rows)
//   ZernikeStandardData ReadZernikeStandard(int field, int wave, int maxTerm, int sampling)
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

        /// <summary>
        /// Zernike Standard Coefficients through temporary ZERN merit-function rows (one per term, appended and removed in one go,
        /// like GBPS / GBPW) rather than the Zernike Standard Coefficients analysis window, whose results come only as a text file to
        /// parse (that is how ZOSPy reads them; ZOSPy, MIT, was read as a guide only, decision E19). ZERN gives the same fit as the
        /// analysis (OpticStudio's operand help) with typed numbers.
        /// TODO(zemax-hw): the ZERN parameter layout [Term, Wave, Samp (1 = 32x32, 2 = 64x64, ...), Field, Type (1 = Standard),
        /// Epsilon, Vertex (0 = reference to the chief ray)] and ENPD / EXPD as diameters in lens units (HARDWARE-CHECKLIST.md step 4b).
        /// </summary>
        public ZernikeStandardData ReadZernikeStandard(int field, int wave, int maxTerm, int sampling)
        {
            int samp = 1;
            for (int n = 32; n < sampling; n *= 2) samp++;
            var ints = new[] { true, true, true, true, true, false, true };
            var rows = new List<OperandRow>();
            for (int j = 1; j <= maxTerm; j++)
                rows.Add(new OperandRow { Type = "ZERN", Params = new double[] { j, wave, samp, field, 1, 0, 0 }, IntegerParams = ints });
            var c = EvaluateRows(rows);
            return new ZernikeStandardData
            {
                NollWaves = c, Field = field, Sampling = sampling,
                EntrancePupilRadius = 0.5 * Operand("ENPD", 0, 0), ExitPupilRadius = 0.5 * Operand("EXPD", 0, 0)
            };
        }

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
