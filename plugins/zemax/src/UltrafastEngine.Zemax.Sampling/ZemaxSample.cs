using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Zemax
{
    /// <summary>Coating coefficients read from OpticStudio for one surface and wavelength (power R/T, phases in rad).</summary>
    public struct CoatingSample
    {
        public double Rs, Rp, Ts, Tp, PhaseRs, PhaseRp, PhaseTs, PhaseTp;
        public bool Valid;
    }

    /// <summary>
    /// The raw data sampled from OpticStudio -- everything the system builder needs, and nothing tied to
    /// ZOS-API. Saved as JSON (<c>*.ufsample.json</c>) so a system file can be rebuilt with different export
    /// settings on any machine, without OpticStudio.
    ///
    /// Indexing: sample j (ascending omega), surface s (0..LastSurface). "Gap s" runs from surface s-1 to s
    /// in the medium following surface s-1 (Zemax convention). Geometry is global, in lens units.
    /// </summary>
    public sealed class ZemaxSample
    {
        public const string FormatName = "ultrafast-zemax-sample";
        public string SystemFile { get; set; }
        public string Created { get; set; }
        public double LensUnitToMeters { get; set; } = 1e-3;
        public double TemperatureC { get; set; } = 20;
        public double PressureAtm { get; set; } = 1;
        public int LastSurface { get; set; }
        public List<SurfaceRow> Surfaces { get; set; } = new List<SurfaceRow>();
        public double[] WavelengthsUm { get; set; }
        public double CarrierUm { get; set; }

        // [j][s]
        public double[][] Gx, Gy, Gz, Gl, Gm, Gn, Index, Opl, AoiRad, Nx, Ny, Nz;
        public int[] VignetteSurface { get; set; }
        public CoatingSample[][] Coating { get; set; }

        // Gaussian beam at the carrier, per surface (lens units)
        public double[] BeamRadius { get; set; }
        public double[] BeamWaist { get; set; }
        public double InputWaist { get; set; }
        public double InputWaistToSurface1 { get; set; }
        public double InputM2 { get; set; } = 1;

        public List<string> Warnings { get; } = new List<string>();

        public int SampleCount => WavelengthsUm?.Length ?? 0;
        public double[] Omega => WavelengthsUm.Select(u => 2 * Math.PI * PhysicalConstants.SpeedOfLightMPerS / (u * 1e-6)).ToArray();

        /// <summary>Signed geometric length of gap s for sample j, lens units: (r_s - r_{s-1}) . k_{s-1}.</summary>
        public double GapLength(int j, int s)
        {
            if (s < 2) return 0;
            double dx = Gx[j][s] - Gx[j][s - 1], dy = Gy[j][s] - Gy[j][s - 1], dz = Gz[j][s] - Gz[j][s - 1];
            return dx * Gl[j][s - 1] + dy * Gm[j][s - 1] + dz * Gn[j][s - 1];
        }

        public int NearestSample(double um)
        {
            int best = 0;
            for (int j = 1; j < SampleCount; j++) if (Math.Abs(WavelengthsUm[j] - um) < Math.Abs(WavelengthsUm[best] - um)) best = j;
            return best;
        }

        // ------------------------------------------------------------------ JSON
        private static JsonArray M(double[][] a) => a == null ? null : new JsonArray(a.Select(r => (JsonValue)JsonValue.FromArray(r)));
        private static double[][] M(JsonObject o, string k) => o.Has(k) ? o[k].AsDoubleMatrix() : null;

        public JsonObject ToJson()
        {
            var o = new JsonObject().Set("format", FormatName).Set("version", 1)
                .Set("systemFile", SystemFile).Set("created", Created).Set("lensUnitToMeters", LensUnitToMeters)
                .Set("temperatureC", TemperatureC).Set("pressureAtm", PressureAtm).Set("lastSurface", LastSurface)
                .Set("carrierUm", CarrierUm).Set("wavelengthsUm", JsonValue.FromArray(WavelengthsUm));
            var rows = new JsonArray();
            foreach (var r in Surfaces)
                rows.Add(new JsonObject().Set("index", r.Index).Set("material", r.Material).Set("comment", r.Comment).Set("type", r.TypeName)
                    .Set("coating", r.Coating).Set("thickness", r.Thickness).Set("semiDiameter", r.SemiDiameter).Set("ignored", r.IsIgnored));
            o.Set("surfaces", rows);
            o.Set("gx", M(Gx)).Set("gy", M(Gy)).Set("gz", M(Gz)).Set("gl", M(Gl)).Set("gm", M(Gm)).Set("gn", M(Gn));
            o.Set("index", M(Index)).Set("opl", M(Opl)).Set("aoiRad", M(AoiRad)).Set("nx", M(Nx)).Set("ny", M(Ny)).Set("nz", M(Nz));
            if (VignetteSurface != null) o.Set("vignetteSurface", JsonValue.FromArray(VignetteSurface.Select(v => (double)v)));
            if (Coating != null)
            {
                var c = new JsonArray();
                foreach (var row in Coating)
                    c.Add(new JsonArray(row.Select(x => (JsonValue)(x.Valid ? JsonValue.FromArray(new[] { x.Rs, x.Rp, x.Ts, x.Tp, x.PhaseRs, x.PhaseRp, x.PhaseTs, x.PhaseTp }) : JsonValue.Null))));
                o.Set("coating", c);
            }
            o.Set("beamRadius", BeamRadius == null ? null : JsonValue.FromArray(BeamRadius)).Set("beamWaist", BeamWaist == null ? null : JsonValue.FromArray(BeamWaist));
            o.Set("inputBeam", new JsonObject().Set("waist", InputWaist).Set("waistToSurface1", InputWaistToSurface1).Set("m2", InputM2));
            o.Set("warnings", JsonValue.FromArray(Warnings));
            return o;
        }

        public static ZemaxSample FromJson(JsonObject o)
        {
            if (o.GetString("format") != FormatName) throw new JsonException("Not a Zemax sample file (format \"" + o.GetString("format") + "\").");
            var s = new ZemaxSample
            {
                SystemFile = o.GetString("systemFile"), Created = o.GetString("created"),
                LensUnitToMeters = o.GetDouble("lensUnitToMeters", 1e-3), TemperatureC = o.GetDouble("temperatureC", 20), PressureAtm = o.GetDouble("pressureAtm", 1),
                LastSurface = o.GetInt("lastSurface", 0), CarrierUm = o.GetDouble("carrierUm", double.NaN), WavelengthsUm = o.GetDoubleArray("wavelengthsUm"),
                Gx = M(o, "gx"), Gy = M(o, "gy"), Gz = M(o, "gz"), Gl = M(o, "gl"), Gm = M(o, "gm"), Gn = M(o, "gn"),
                Index = M(o, "index"), Opl = M(o, "opl"), AoiRad = M(o, "aoiRad"), Nx = M(o, "nx"), Ny = M(o, "ny"), Nz = M(o, "nz"),
                BeamRadius = o.GetDoubleArray("beamRadius"), BeamWaist = o.GetDoubleArray("beamWaist")
            };
            foreach (var rv in o.GetArray("surfaces") ?? new JsonArray())
            {
                var r = rv.AsObject();
                s.Surfaces.Add(new SurfaceRow
                {
                    Index = r.GetInt("index", 0), Material = r.GetString("material", ""), Comment = r.GetString("comment", ""), TypeName = r.GetString("type", ""),
                    Coating = r.GetString("coating", ""), Thickness = r.GetDouble("thickness", 0), SemiDiameter = r.GetDouble("semiDiameter", double.NaN), IsIgnored = r.GetBool("ignored", false)
                });
            }
            if (o.Has("vignetteSurface")) s.VignetteSurface = o.GetDoubleArray("vignetteSurface").Select(v => (int)v).ToArray();
            if (o.Has("coating"))
                s.Coating = o.GetArray("coating").Select(row => row.AsArray().Select(x =>
                {
                    if (x.IsNull) return new CoatingSample();
                    var a = x.AsDoubleArray();
                    return new CoatingSample { Rs = a[0], Rp = a[1], Ts = a[2], Tp = a[3], PhaseRs = a[4], PhaseRp = a[5], PhaseTs = a[6], PhaseTp = a[7], Valid = true };
                }).ToArray()).ToArray();
            var ib = o.GetObject("inputBeam");
            if (ib != null) { s.InputWaist = ib.GetDouble("waist", 0); s.InputWaistToSurface1 = ib.GetDouble("waistToSurface1", 0); s.InputM2 = ib.GetDouble("m2", 1); }
            foreach (var w in o.GetArray("warnings") ?? new JsonArray()) s.Warnings.Add(w.AsString());
            return s;
        }

        public void Save(string path) => File.WriteAllText(path, ToJson().ToJson(true));
        public static ZemaxSample Load(string path) => FromJson(JsonValue.ParseFile(path).AsObject());
    }
}
