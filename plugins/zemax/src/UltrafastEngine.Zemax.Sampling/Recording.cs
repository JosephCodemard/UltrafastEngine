using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// Wraps a live <see cref="IOpticStudio"/> and records every call and its result, so a sampling session on
    /// the OpticStudio PC can be replayed bit-for-bit anywhere (<see cref="ReplayOpticStudio"/>) -- e.g. to debug
    /// the sampler or the system builder on a Mac. Save with <see cref="Save"/>.
    /// </summary>
    public sealed class RecordingOpticStudio : IOpticStudio
    {
        private readonly IOpticStudio _inner;
        private readonly JsonArray _log = new JsonArray();
        private readonly JsonObject _header;

        public RecordingOpticStudio(IOpticStudio inner)
        {
            _inner = inner;
            _header = new JsonObject().Set("systemFile", inner.SystemFile).Set("lensUnitToMeters", inner.LensUnitToMeters)
                .Set("temperatureC", inner.TemperatureC).Set("pressureAtm", inner.PressureAtm).Set("numberOfSurfaces", inner.NumberOfSurfaces);
        }

        private T Rec<T>(string method, JsonValue args, Func<T> call, Func<T, JsonValue> enc)
        {
            try
            {
                var r = call();
                _log.Add(new JsonObject().Set("m", method).Set("a", args).Set("r", enc(r)));
                return r;
            }
            catch (Exception ex)
            {
                _log.Add(new JsonObject().Set("m", method).Set("a", args).Set("error", ex.Message));
                throw;
            }
        }

        public string SystemFile => _inner.SystemFile;
        public double LensUnitToMeters => _inner.LensUnitToMeters;
        public double TemperatureC => _inner.TemperatureC;
        public double PressureAtm => _inner.PressureAtm;
        public int NumberOfSurfaces => _inner.NumberOfSurfaces;

        public SurfaceRow GetSurface(int index) => Rec("GetSurface", index, () => _inner.GetSurface(index), r => new JsonObject()
            .Set("material", r.Material).Set("comment", r.Comment).Set("type", r.TypeName).Set("coating", r.Coating).Set("thickness", r.Thickness).Set("semiDiameter", r.SemiDiameter).Set("ignored", r.IsIgnored));

        public IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary)
        {
            var w = _inner.GetWavelengths(out primary);
            _log.Add(new JsonObject().Set("m", "GetWavelengths").Set("r", new JsonObject().Set("um", JsonValue.FromArray(w.Select(x => x.Um))).Set("weight", JsonValue.FromArray(w.Select(x => x.Weight))).Set("primary", primary)));
            return w;
        }

        public int SetWavelengths(IReadOnlyList<double> um) => Rec("SetWavelengths", JsonValue.FromArray(um), () => _inner.SetWavelengths(um), r => r);
        public void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary)
        {
            _inner.RestoreWavelengths(waves, primary);
            _log.Add(new JsonObject().Set("m", "RestoreWavelengths"));
        }

        public ChiefRayHit[] TraceChief(int toSurface, int n) => Rec("TraceChief", JsonValue.FromArray(new double[] { toSurface, n }), () => _inner.TraceChief(toSurface, n),
            r => new JsonArray(r.Select(h => (JsonValue)JsonValue.FromArray(new double[] { h.Error, h.Vignette, h.X, h.Y, h.Z, h.L, h.M, h.N, h.Opl }))));

        public double[] GlobalMatrix(int surface) => Rec("GlobalMatrix", surface, () => _inner.GlobalMatrix(surface), r => JsonValue.FromArray(r));

        public void GetIndex(int surface, int n, double[] dest)
        {
            Rec("GetIndex", JsonValue.FromArray(new double[] { surface, n }), () => { _inner.GetIndex(surface, n, dest); return dest.Take(n).ToArray(); }, r => JsonValue.FromArray(r));
        }

        public double Operand(string type, int surface, int wave, double hx = 0, double hy = 0, double px = 0, double py = 0, double ex = 0, double ey = 0) =>
            Rec("Operand", new JsonArray().Add(type).Add(surface).Add(wave).Add(hx).Add(hy).Add(px).Add(py).Add(ex).Add(ey), () => _inner.Operand(type, surface, wave, hx, hy, px, py, ex, ey), r => r);

        public double[] EvaluateRows(IReadOnlyList<OperandRow> rows) =>
            Rec("EvaluateRows", new JsonArray(rows.Select(r => (JsonValue)new JsonObject().Set("type", r.Type).Set("params", JsonValue.FromArray(r.Params)))), () => _inner.EvaluateRows(rows), r => JsonValue.FromArray(r));

        public void Save(string path) => File.WriteAllText(path, new JsonObject().Set("format", "ultrafast-zemax-session").Set("header", _header).Set("calls", _log).ToJson(true));
    }

    /// <summary>Replays a session recorded by <see cref="RecordingOpticStudio"/>. Calls must arrive in the recorded
    /// order (the sampler is deterministic); a mismatch throws with the call index.</summary>
    public sealed class ReplayOpticStudio : IOpticStudio
    {
        private readonly JsonArray _calls; private readonly JsonObject _h; private int _next;

        public ReplayOpticStudio(string path)
        {
            var o = JsonValue.ParseFile(path).AsObject();
            if (o.GetString("format") != "ultrafast-zemax-session") throw new JsonException(path + " is not a recorded Zemax session.");
            _h = o.GetObject("header"); _calls = o.GetArray("calls");
        }

        private JsonObject Next(string method)
        {
            if (_next >= _calls.Count) throw new InvalidOperationException("replay: no more recorded calls (wanted " + method + ")");
            var c = _calls[_next++].AsObject();
            if (c.GetString("m") != method) throw new InvalidOperationException("replay: call " + (_next - 1) + " was " + c.GetString("m") + " but " + method + " was requested (the sampler options differ from the recording)");
            if (c.Has("error")) throw new InvalidOperationException("recorded failure: " + c.GetString("error"));
            return c;
        }

        public string SystemFile => _h.GetString("systemFile");
        public double LensUnitToMeters => _h.GetDouble("lensUnitToMeters", 1e-3);
        public double TemperatureC => _h.GetDouble("temperatureC", 20);
        public double PressureAtm => _h.GetDouble("pressureAtm", 1);
        public int NumberOfSurfaces => _h.GetInt("numberOfSurfaces", 0);

        public SurfaceRow GetSurface(int index)
        {
            var r = Next("GetSurface").GetObject("r");
            return new SurfaceRow { Index = index, Material = r.GetString("material", ""), Comment = r.GetString("comment", ""), TypeName = r.GetString("type", ""), Coating = r.GetString("coating", ""), Thickness = r.GetDouble("thickness", 0), SemiDiameter = r.GetDouble("semiDiameter", double.NaN), IsIgnored = r.GetBool("ignored", false) };
        }

        public IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary)
        {
            var r = Next("GetWavelengths").GetObject("r");
            primary = r.GetInt("primary", 1);
            var um = r.GetDoubleArray("um"); var wt = r.GetDoubleArray("weight");
            return um.Select((u, i) => (u, wt[i])).ToList();
        }

        public int SetWavelengths(IReadOnlyList<double> um) => Next("SetWavelengths")["r"].AsInt();
        public void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary) => Next("RestoreWavelengths");

        public ChiefRayHit[] TraceChief(int toSurface, int n) => Next("TraceChief")["r"].AsArray().Select(v =>
        {
            var a = v.AsDoubleArray();
            return new ChiefRayHit { Error = (int)a[0], Vignette = (int)a[1], X = a[2], Y = a[3], Z = a[4], L = a[5], M = a[6], N = a[7], Opl = a[8] };
        }).ToArray();

        public double[] GlobalMatrix(int surface) => Next("GlobalMatrix")["r"].AsDoubleArray();
        public void GetIndex(int surface, int n, double[] dest) { var a = Next("GetIndex")["r"].AsDoubleArray(); Array.Copy(a, dest, Math.Min(n, a.Length)); }
        public double Operand(string type, int surface, int wave, double hx = 0, double hy = 0, double px = 0, double py = 0, double ex = 0, double ey = 0) => Next("Operand")["r"].AsDouble();
        public double[] EvaluateRows(IReadOnlyList<OperandRow> rows) => Next("EvaluateRows")["r"].AsDoubleArray();
    }
}
