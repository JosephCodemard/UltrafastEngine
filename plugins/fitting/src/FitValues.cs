using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Measured;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Fitting
{
    /// <summary>
    /// Numbers in transform operators may be plain numbers or fit objects
    /// <c>{"value": 0, "fit": true, "min": -1e4, "max": 1e4, "prior": {"mean": 0, "sigma": 500}, "scale": 1000, "unit": "fs^2"}</c>.
    /// These helpers read the value either way, and address numbers in the system / run files by path.
    /// </summary>
    public static class FitValues
    {
        /// <summary>True for a fit object (an object with "value").</summary>
        public static bool IsFitObject(JsonValue v) => v != null && v.Kind == JsonKind.Object && v.AsObject().Has("value");

        /// <summary>The value of a number or a fit object.</summary>
        public static double Value(JsonValue v, double fallback)
        {
            if (v == null || v.IsNull) return fallback;
            if (v.Kind == JsonKind.Number) return v.AsDouble();
            if (IsFitObject(v)) return v.AsObject().GetDouble("value", fallback);
            if (v.Kind == JsonKind.Bool) return v.AsBool() ? 1 : 0;
            throw new JsonException("expected a number or a fit object {\"value\": ...}, got " + v.Kind);
        }

        /// <summary>The value of key in o (a number or a fit object), or fallback when absent.</summary>
        public static double Num(JsonObject o, string key, double fallback) => o != null && o.TryGet(key, out var v) ? Value(v, fallback) : fallback;

        /// <summary>An array of numbers or fit objects (or a single number), or null when absent.</summary>
        public static double[] Arr(JsonObject o, string key)
        {
            if (o == null || !o.TryGet(key, out var v) || v.IsNull) return null;
            if (v.Kind == JsonKind.Array) return v.AsArray().Select(x => Value(x, double.NaN)).ToArray();
            return new[] { Value(v, double.NaN) };
        }

        // ------------------------------------------------------------------ paths

        /// <summary>
        /// Where a path points: the container (object or array) and the key / index. Paths:
        /// <c>pulse/...</c>, <c>run/...</c>, <c>system/...</c>, <c>element:ID/...</c>, <c>instrument:NAME/...</c>
        /// (an instrument of the run by name, else by kind); segments step into objects (by key) and arrays (by index).
        /// <c>material:NAME/prop</c> is handled by <see cref="Set"/> as an override.
        /// </summary>
        private static (JsonValue C, string K, int I) Resolve(JsonObject sys, JsonObject run, string path, bool create)
        {
            string p = path.Trim();
            JsonValue root; string rest;
            int sl = p.IndexOf('/');
            if (p.StartsWith("pulse/", StringComparison.OrdinalIgnoreCase)) { root = run.GetObject("pulse"); rest = p.Substring(6); }
            else if (p.StartsWith("run/", StringComparison.OrdinalIgnoreCase)) { root = run; rest = p.Substring(4); }
            else if (p.StartsWith("system/", StringComparison.OrdinalIgnoreCase)) { root = sys; rest = p.Substring(7); }
            else if (p.StartsWith("element:", StringComparison.OrdinalIgnoreCase))
            {
                if (sl < 0) throw new JsonException("path " + path + ": expected element:ID/property");
                string id = p.Substring(8, sl - 8); rest = p.Substring(sl + 1);
                root = (sys.GetArray("elements") ?? new JsonArray()).Select(e => e.AsObject()).FirstOrDefault(e => string.Equals(e.GetString("id"), id, StringComparison.OrdinalIgnoreCase))
                       ?? throw new JsonException("path " + path + ": no element '" + id + "'");
            }
            else if (p.StartsWith("instrument:", StringComparison.OrdinalIgnoreCase))
            {
                if (sl < 0) throw new JsonException("path " + path + ": expected instrument:NAME/property");
                string name = p.Substring(11, sl - 11); rest = p.Substring(sl + 1);
                var list = (run.GetArray("instruments") ?? new JsonArray()).Select(e => e.AsObject()).ToList();
                root = list.FirstOrDefault(e => string.Equals(e.GetString("name"), name, StringComparison.OrdinalIgnoreCase))
                       ?? list.FirstOrDefault(e => !e.Has("name") && string.Equals(e.GetString("kind"), name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new JsonException("path " + path + ": no instrument named '" + name + "'");
            }
            else throw new JsonException("path " + path + ": must start with pulse/, element:ID/, instrument:NAME/, material:NAME/, run/ or system/");
            if (root == null) throw new JsonException("path " + path + ": the root object does not exist");
            var parts = rest.Split('/');
            JsonValue cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                JsonValue next;
                if (cur.Kind == JsonKind.Array)
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int ix) || ix >= cur.AsArray().Count)
                        throw new JsonException("path " + path + ": '" + part + "' is not a valid index of a list with " + cur.AsArray().Count + " entries");
                    next = cur.AsArray()[ix];
                }
                else
                {
                    var o = cur.AsObject();
                    if (!o.TryGet(part, out next) || next.IsNull)
                    {
                        if (!create) return (null, null, -1);
                        next = new JsonObject(); o.Set(part, next);
                    }
                }
                // step through a fit object at an intermediate level (never happens for well-formed paths)
                cur = next;
            }
            string last = parts[parts.Length - 1];
            if (cur.Kind == JsonKind.Array)
            {
                if (!int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out int ix) || ix >= cur.AsArray().Count)
                    throw new JsonException("path " + path + ": '" + last + "' is not a valid index");
                return (cur, null, ix);
            }
            if (cur.Kind != JsonKind.Object) throw new JsonException("path " + path + ": '" + parts[Math.Max(0, parts.Length - 2)] + "' is not an object");
            return (cur, last, -1);
        }

        private static JsonValue Read((JsonValue C, string K, int I) t)
        {
            if (t.C == null) return null;
            if (t.I >= 0) return t.C.AsArray()[t.I];
            return t.C.AsObject().TryGet(t.K, out var v) ? v : null;
        }

        /// <summary>The number at a path (the value of a fit object there), or NaN.</summary>
        public static double Get(JsonObject sys, JsonObject run, string path)
        {
            if (path.StartsWith("material:", StringComparison.OrdinalIgnoreCase)) return double.NaN;
            var v = Read(Resolve(sys, run, path, false));
            if (v == null) return double.NaN;
            if (v.Kind == JsonKind.Number) return v.AsDouble();
            if (IsFitObject(v)) return v.AsObject().GetDouble("value", double.NaN);
            return double.NaN;
        }

        /// <summary>Set the number at a path (the "value" of a fit object there; created when missing).</summary>
        public static void Set(JsonObject sys, JsonObject run, string path, double value)
        {
            if (path.StartsWith("material:", StringComparison.OrdinalIgnoreCase))
            {
                int sl = path.IndexOf('/'); if (sl < 0) throw new JsonException("path " + path + ": expected material:NAME/property");
                var ov = run.GetArray("overrides"); if (ov == null) { ov = new JsonArray(); run.Set("overrides", ov); }
                ov.Add(new JsonObject().Set("target", path.Substring(0, sl)).Set("property", path.Substring(sl + 1)).Set("value", value));
                return;
            }
            var t = Resolve(sys, run, path, true);
            var cur = Read(t);
            if (IsFitObject(cur)) { cur.AsObject().Set("value", value); return; }
            if (t.I >= 0) t.C.AsArray()[t.I] = value;
            else t.C.AsObject().Set(t.K, value);
        }

        /// <summary>Replace the fit object at a path by a plain number (used to write the fitted system).</summary>
        public static void Freeze(JsonObject sys, JsonObject run, string path, double value)
        {
            if (path.StartsWith("material:", StringComparison.OrdinalIgnoreCase)) { Set(sys, run, path, value); return; }
            var t = Resolve(sys, run, path, true);
            if (t.I >= 0) t.C.AsArray()[t.I] = value;
            else t.C.AsObject().Set(t.K, value);
        }

        /// <summary>Unit label from a key's suffix (gddFs2 → fs^2, delayS → s, energyJ → J, fwhmNm → nm).</summary>
        public static string UnitOf(string key)
        {
            string[] suf = { "Fs2", "fs^2", "Fs3", "fs^3", "Fs4", "fs^4", "Fs5", "fs^5", "S2", "s^2", "S3", "s^3", "S4", "s^4", "Fs", "fs", "Ps", "ps", "Nm", "nm", "Rad", "rad", "Deg", "deg", "Db", "dB", "M", "m", "S", "s", "J", "J", "W", "W" };
            for (int i = 0; i < suf.Length; i += 2) if (key.EndsWith(suf[i], StringComparison.Ordinal)) return suf[i + 1];
            return "";
        }

        /// <summary>A typical size of change of a parameter named <paramref name="key"/> (when no bounds or scale are given).</summary>
        public static double TypicalScale(string key, double value)
        {
            var k = key;
            if (k.EndsWith("Fs2")) return 500; if (k.EndsWith("Fs3")) return 5e3; if (k.EndsWith("Fs4")) return 5e4; if (k.EndsWith("Fs5")) return 5e5;
            if (k.EndsWith("S2")) return 5e-28; if (k.EndsWith("S3")) return 5e-42; if (k.EndsWith("S4")) return 5e-56;
            if (k.EndsWith("Fs")) return 20; if (k.EndsWith("Ps")) return 0.1;
            if (k.EndsWith("Nm")) return 1; if (k.EndsWith("Deg")) return 2; if (k.EndsWith("Rad")) return 0.3; if (k.EndsWith("Db")) return 0.5;
            if (k.EndsWith("delayS") || k.EndsWith("DelayS")) return 20e-15;
            if (k.EndsWith("M") && Math.Abs(value) < 1e-5) return 1e-9;      // wavelengths, widths
            if (k.EndsWith("M")) return Math.Max(Math.Abs(value) * 0.1, 1e-6);
            return Math.Max(Math.Abs(value) * 0.2, 0.05);
        }
    }
}
