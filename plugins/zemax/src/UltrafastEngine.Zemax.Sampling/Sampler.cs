using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace UltrafastEngine.Zemax
{
    /// <summary>Which merit-function data codes give the coating coefficients (see docs/plugins/zemax.md: verify against
    /// your OpticStudio's operand help before enabling coatingSource "zemaxOperand").</summary>
    public sealed class CoatingOperandSpec
    {
        public string Type { get; set; } = "CODA";
        /// <summary>Data code (passed as the Hx argument slot by default, see <see cref="DataArgument"/>) per quantity.</summary>
        public Dictionary<string, int> Codes { get; set; } = new Dictionary<string, int>
        {
            { "Rs", 0 }, { "Rp", 1 }, { "Ts", 2 }, { "Tp", 3 }, { "PhaseRs", 6 }, { "PhaseRp", 7 }, { "PhaseTs", 8 }, { "PhaseTp", 9 }
        };
        /// <summary>Which GetOperandValue argument carries the data code: "hx" (default) | "hy" | "px" | "py" | "ex" | "ey".</summary>
        public string DataArgument { get; set; } = "hx";
        /// <summary>Phases reported in degrees (true) or radians.</summary>
        public bool PhaseInDegrees { get; set; } = true;
    }

    public sealed class SamplerOptions
    {
        public IReadOnlyList<double> WavelengthsUm { get; set; }
        /// <summary>Trace exactly the wavelengths already in the Wavelength Data Editor.</summary>
        public bool KeepCurrentWavelengths { get; set; }
        public int BatchSize { get; set; } = 24;
        public bool GeometryFromOperands { get; set; } = true;
        public double CarrierUm { get; set; }
        /// <summary>Input Gaussian beam for GBPS/GBPW (lens units). Null: no beam data.</summary>
        public double? BeamWaist { get; set; }
        public double BeamWaistToSurface1 { get; set; }
        public double BeamM2 { get; set; } = 1.0;
        public bool SampleAoi { get; set; } = true;
        public bool SampleNormals { get; set; } = true;
        public CoatingOperandSpec CoatingOperand { get; set; }
        public Action<string> Log { get; set; } = Console.WriteLine;
    }

    /// <summary>
    /// Samples an optical system through <see cref="IOpticStudio"/> at any number of wavelengths, by rewriting
    /// the Wavelength Data Editor in batches (restored afterwards, also on error):
    ///   per batch and surface: chief-ray batch trace (error/vignette codes, OPL), global geometry from the
    ///   RAGX/Y/Z + RAGA/B/C operands (cross-checked against GetGlobalMatrix), angle of incidence (RAID),
    ///   surface normal (RENA/B/C -> global), index of the following medium (GetIndex), optionally coating
    ///   coefficients; once at the carrier: GBPS / GBPW Gaussian beam radius and waist at every surface.
    /// Ported from v2 (ZemaxDispersionSampler) onto the IOpticStudio abstraction.
    /// </summary>
    public static class Sampler
    {
        public static ZemaxSample Sample(IOpticStudio os, SamplerOptions opt)
        {
            var log = opt.Log ?? (_ => { });
            int last = os.NumberOfSurfaces - 1;
            var original = os.GetWavelengths(out int primary).ToList();
            log("  Wavelength Data Editor (restored afterwards): " + string.Join(", ", original.Select((w, i) => (i + 1 == primary ? "*" : "") + w.Um.ToString("F6", CultureInfo.InvariantCulture))));
            var res = new ZemaxSample
            {
                SystemFile = os.SystemFile, Created = DateTime.UtcNow.ToString("o"), LensUnitToMeters = os.LensUnitToMeters,
                TemperatureC = os.TemperatureC, PressureAtm = os.PressureAtm, LastSurface = last, CarrierUm = opt.CarrierUm,
                InputWaist = opt.BeamWaist ?? 0, InputWaistToSurface1 = opt.BeamWaistToSurface1, InputM2 = opt.BeamM2
            };
            for (int s = 0; s <= last; s++)
            {
                try { res.Surfaces.Add(os.GetSurface(s)); }
                catch (Exception ex)
                {
                    res.Warnings.Add("surface " + s + " metadata unreadable (" + ex.Message + "); placeholder used");
                    res.Surfaces.Add(new SurfaceRow { Index = s, TypeName = "(unreadable)" });
                }
            }
            var recs = new List<Rec>();
            double maxMismatch = double.NaN;
            var sw = Stopwatch.StartNew();
            try
            {
                if (opt.BeamWaist.HasValue && opt.BeamWaist.Value > 0)
                {
                    os.SetWavelengths(new[] { opt.CarrierUm });
                    ReadBeam(os, last, opt, res);
                }
                var queue = new Queue<double>(opt.KeepCurrentWavelengths ? original.Select(w => w.Um) : opt.WavelengthsUm ?? throw new ArgumentException("no wavelengths to sample"));
                int batchSize = Math.Max(1, opt.BatchSize), batchNo = 0;
                while (queue.Count > 0)
                {
                    var batch = new List<double>();
                    while (batch.Count < batchSize && queue.Count > 0) batch.Add(queue.Dequeue());
                    int set = os.SetWavelengths(batch);
                    if (set < batch.Count)
                    {
                        var rest = batch.Skip(set).Concat(queue).ToList();
                        queue = new Queue<double>(rest);
                        batch = batch.Take(set).ToList();
                        batchSize = Math.Max(1, set);
                        log("  NOTE: OpticStudio accepted only " + set + " wavelengths at once -- batch size reduced.");
                    }
                    double t0 = sw.Elapsed.TotalSeconds;
                    recs.AddRange(TraceBatch(os, batch, last, opt, res.Warnings, ref maxMismatch));
                    batchNo++;
                    log(string.Format(CultureInfo.InvariantCulture, "  batch {0}: {1} wavelengths {2:F1}-{3:F1} nm traced to {4} surfaces in {5:F1} s",
                        batchNo, batch.Count, batch.Min() * 1e3, batch.Max() * 1e3, last, sw.Elapsed.TotalSeconds - t0));
                }
            }
            finally
            {
                try { os.RestoreWavelengths(original, primary); log("  Wavelength Data Editor restored."); }
                catch (Exception ex) { log("  *** could not restore the Wavelength Data Editor (" + ex.Message + ")"); }
            }
            if (!double.IsNaN(maxMismatch))
                log(string.Format(CultureInfo.InvariantCulture, "  Geometry cross-check (operands vs trace + global matrix): max difference {0:E2} lens units{1}", maxMismatch, maxMismatch > 1e-6 ? "  <-- INVESTIGATE" : " (agree)"));

            foreach (var r in recs.Where(r => r.Error == 0))
                for (int s = 1; s <= last && r.Error == 0; s++)
                    if (!(Fin(r.Gx[s]) && Fin(r.Gy[s]) && Fin(r.Gz[s]) && Fin(r.Gl[s]) && Fin(r.Gm[s]) && Fin(r.Gn[s]) && Fin(r.Index[s]))) { r.Error = -1; r.ErrorSurface = s; }
            foreach (var bad in recs.Where(r => r.Error != 0))
                res.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Wavelength {0:F2} nm dropped: ray error {1} at surface {2} (TIR / missed surface / evanescent order; -1 = non-finite).", bad.Um * 1e3, bad.Error, bad.ErrorSurface));
            var valid = recs.Where(r => r.Error == 0).OrderByDescending(r => r.Um).ToList(); // ascending omega
            if (valid.Count < 4)
                throw new InvalidOperationException("Only " + valid.Count + " wavelengths traced without ray errors (need >= 4):\n  " + string.Join("\n  ", res.Warnings));
            res.WavelengthsUm = valid.Select(r => r.Um).ToArray();
            double[][] Take(Func<Rec, double[]> f) => valid.Select(f).ToArray();
            res.Gx = Take(r => r.Gx); res.Gy = Take(r => r.Gy); res.Gz = Take(r => r.Gz);
            res.Gl = Take(r => r.Gl); res.Gm = Take(r => r.Gm); res.Gn = Take(r => r.Gn);
            res.Index = Take(r => r.Index); res.Opl = Take(r => r.Opl); res.AoiRad = Take(r => r.Aoi);
            res.Nx = Take(r => r.Nx); res.Ny = Take(r => r.Ny); res.Nz = Take(r => r.Nz);
            res.VignetteSurface = valid.Select(r => r.Vignette).ToArray();
            if (opt.CoatingOperand != null) res.Coating = valid.Select(r => r.Coating).ToArray();
            return res;
        }

        private static bool Fin(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private sealed class Rec
        {
            public double Um; public int Error, ErrorSurface, Vignette;
            public double[] Gx, Gy, Gz, Gl, Gm, Gn, Opl, Index, Aoi, Nx, Ny, Nz;
            public CoatingSample[] Coating;
        }

        private static List<Rec> TraceBatch(IOpticStudio os, List<double> um, int last, SamplerOptions opt, List<string> warnings, ref double maxMismatch)
        {
            int nw = um.Count;
            var recs = um.Select(u => new Rec
            {
                Um = u, Gx = new double[last + 1], Gy = new double[last + 1], Gz = new double[last + 1], Gl = new double[last + 1], Gm = new double[last + 1], Gn = new double[last + 1],
                Opl = new double[last + 1], Index = new double[last + 1], Aoi = new double[last + 1], Nx = new double[last + 1], Ny = new double[last + 1], Nz = new double[last + 1],
                Coating = new CoatingSample[last + 1]
            }).ToList();
            // local hits
            var local = new ChiefRayHit[nw, last + 1];
            for (int s = 1; s <= last; s++)
            {
                var hits = os.TraceChief(s, nw);
                for (int w = 0; w < nw && w < hits.Length; w++)
                {
                    var h = hits[w]; var r = recs[w];
                    if (h.Error != 0 && r.Error == 0) { r.Error = h.Error; r.ErrorSurface = s; }
                    if (h.Vignette != 0 && r.Vignette == 0) r.Vignette = h.Vignette;
                    r.Opl[s] = h.Opl;
                    local[w, s] = h;
                }
            }
            // global matrices
            double[][] gm = new double[last + 1][];
            bool haveMatrix = true;
            try { for (int s = 1; s <= last; s++) gm[s] = os.GlobalMatrix(s); }
            catch (Exception ex)
            {
                haveMatrix = false;
                if (!opt.GeometryFromOperands) throw new InvalidOperationException("GetGlobalMatrix failed (" + ex.Message + "); use geometry \"operands\".", ex);
                warnings.Add("GetGlobalMatrix unavailable (" + ex.Message + "): geometry cross-check skipped, normals left in local coordinates.");
            }
            (double, double, double) Rot(double[] g, double x, double y, double z) => (g[0] * x + g[1] * y + g[2] * z, g[3] * x + g[4] * y + g[5] * z, g[6] * x + g[7] * y + g[8] * z);
            for (int w = 0; w < nw; w++)
            {
                var r = recs[w];
                for (int s = 1; s <= last; s++)
                {
                    double mx = 0, my = 0, mz = 0, ml = 0, mm = 0, mn = 0;
                    if (haveMatrix)
                    {
                        var g = gm[s]; var p = local[w, s];
                        (mx, my, mz) = Rot(g, p.X, p.Y, p.Z); mx += g[9]; my += g[10]; mz += g[11];
                        (ml, mm, mn) = Rot(g, p.L, p.M, p.N);
                    }
                    if (opt.GeometryFromOperands && r.Error == 0)
                    {
                        r.Gx[s] = os.Operand("RAGX", s, w + 1); r.Gy[s] = os.Operand("RAGY", s, w + 1); r.Gz[s] = os.Operand("RAGZ", s, w + 1);
                        r.Gl[s] = os.Operand("RAGA", s, w + 1); r.Gm[s] = os.Operand("RAGB", s, w + 1); r.Gn[s] = os.Operand("RAGC", s, w + 1);
                        if (haveMatrix)
                        {
                            double d = Math.Sqrt((mx - r.Gx[s]) * (mx - r.Gx[s]) + (my - r.Gy[s]) * (my - r.Gy[s]) + (mz - r.Gz[s]) * (mz - r.Gz[s]));
                            maxMismatch = double.IsNaN(maxMismatch) ? d : Math.Max(maxMismatch, d);
                        }
                    }
                    else { r.Gx[s] = mx; r.Gy[s] = my; r.Gz[s] = mz; r.Gl[s] = ml; r.Gm[s] = mm; r.Gn[s] = mn; }
                    if (r.Error != 0) continue;
                    if (opt.SampleAoi)
                    {
                        try { r.Aoi[s] = os.Operand("RAID", s, w + 1) * Math.PI / 180; }
                        catch (Exception ex) { if (w == 0 && s == 1) warnings.Add("RAID unavailable (" + ex.Message + "): angles of incidence not exported"); opt.SampleAoi = false; }
                    }
                    if (opt.SampleNormals)
                    {
                        try
                        {
                            double a = os.Operand("RENA", s, w + 1), b = os.Operand("RENB", s, w + 1), c = os.Operand("RENC", s, w + 1);
                            if (haveMatrix) (a, b, c) = Rot(gm[s], a, b, c);
                            r.Nx[s] = a; r.Ny[s] = b; r.Nz[s] = c;
                        }
                        catch (Exception ex) { if (w == 0 && s == 1) warnings.Add("RENA/B/C unavailable (" + ex.Message + "): surface normals not exported"); opt.SampleNormals = false; }
                    }
                    if (opt.CoatingOperand != null)
                    {
                        try { r.Coating[s] = ReadCoating(os, s, w + 1, opt.CoatingOperand); }
                        catch (Exception ex) { warnings.Add("coating operand failed at surface " + s + " (" + ex.Message + "); coating data disabled"); opt.CoatingOperand = null; }
                    }
                }
            }
            for (int s = 0; s <= last; s++)
            {
                var idx = new double[nw];
                try { os.GetIndex(s, nw, idx); } catch { for (int w = 0; w < nw; w++) idx[w] = 1.0; }
                for (int w = 0; w < nw; w++) recs[w].Index[s] = idx[w];
            }
            return recs;
        }

        private static CoatingSample ReadCoating(IOpticStudio os, int s, int wave, CoatingOperandSpec spec)
        {
            double Q(string key)
            {
                int code = spec.Codes[key];
                double hx = 0, hy = 0, px = 0, py = 0, ex = 0, ey = 0;
                switch (spec.DataArgument.ToLowerInvariant())
                {
                    case "hy": hy = code; break; case "px": px = code; break; case "py": py = code; break;
                    case "ex": ex = code; break; case "ey": ey = code; break; default: hx = code; break;
                }
                return os.Operand(spec.Type, s, wave, hx, hy, px, py, ex, ey);
            }
            double ph = spec.PhaseInDegrees ? Math.PI / 180 : 1.0;
            var c = new CoatingSample
            {
                Rs = Q("Rs"), Rp = Q("Rp"), Ts = Q("Ts"), Tp = Q("Tp"),
                PhaseRs = Q("PhaseRs") * ph, PhaseRp = Q("PhaseRp") * ph, PhaseTs = Q("PhaseTs") * ph, PhaseTp = Q("PhaseTp") * ph
            };
            // sanity: R + T must not exceed 1 noticeably for either polarisation
            c.Valid = c.Rs >= 0 && c.Rp >= 0 && c.Rs + c.Ts <= 1.001 && c.Rp + c.Tp <= 1.001;
            return c;
        }

        /// <summary>GBPS + GBPW at every surface (wave 1 = carrier) via temporary merit-function rows.
        /// Parameter layout [Surf, Wave, UseX, Waist, S1toW, M2] (confirmed for GBPS; GBPW assumed identical).</summary>
        private static void ReadBeam(IOpticStudio os, int last, SamplerOptions opt, ZemaxSample res)
        {
            res.BeamRadius = Enumerable.Repeat(double.NaN, last + 1).ToArray();
            res.BeamWaist = Enumerable.Repeat(double.NaN, last + 1).ToArray();
            var rows = new List<OperandRow>();
            var ints = new[] { true, true, true, false, false, false };
            for (int s = 1; s <= last; s++)
                foreach (var t in new[] { "GBPS", "GBPW" })
                    rows.Add(new OperandRow { Type = t, Params = new[] { s, 1, 1, opt.BeamWaist.Value, opt.BeamWaistToSurface1, opt.BeamM2 }, IntegerParams = ints });
            try
            {
                var v = os.EvaluateRows(rows);
                for (int s = 1; s <= last; s++) { res.BeamRadius[s] = v[2 * (s - 1)]; res.BeamWaist[s] = v[2 * (s - 1) + 1]; }
            }
            catch (Exception ex) { res.Warnings.Add("Gaussian beam operands failed (" + ex.Message + "): give nonlinear elements a beam in the export config."); }
        }

        /// <summary>Sample wavelengths (um), evenly spaced in frequency between the limits (nm).</summary>
        public static double[] EvenlySpacedInFrequency(double minNm, double maxNm, int count)
        {
            count = Math.Max(4, count);
            double fHi = 1.0 / minNm, fLo = 1.0 / maxNm;
            return Enumerable.Range(0, count).Select(i => 1e-3 / (fLo + (fHi - fLo) * i / (count - 1))).ToArray();
        }
    }
}
