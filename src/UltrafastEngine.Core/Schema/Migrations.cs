using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Schema
{
    /// <summary>
    /// Upgrades system files and run specs written for older format versions. Applied automatically when a file is
    /// loaded (the notes appear in the run's notes); <c>ultrafast migrate</c> rewrites the file. Each step is a pure
    /// JSON transformation, so old files keep working and the change is visible (docs/reference/schema.md).
    /// </summary>
    public static class Migrations
    {
        /// <summary>Current format version of system files.</summary>
        public const int SystemVersion = 3;
        /// <summary>Current format version of run specs.</summary>
        public const int RunVersion = 2;

        /// <summary>Keys that got their unit in the name (1.0.0): scope, old key, new key. A scope is an element <c>type</c>, a model <c>kind</c>
        /// (several, separated by |) or "*" for any object. The old names keep working: <see cref="RenameKeys"/> rewrites them when a file is loaded
        /// and says so in the run's notes. (An override addressed to the old name of a fibre's <c>betas</c> is read by the fibre compiler.)</summary>
        private static readonly string[][] Renames =
        {
            new[] { "type:fiber|fibre|waveguide", "betas", "betasSkPerM" },
            new[] { "type:faradayRotator|faraday", "fieldT", "fieldTesla" },
            new[] { "kind:linAgrawal|dampedOscillator", "tau1", "tau1S" },
            new[] { "kind:linAgrawal|dampedOscillator", "tau2", "tau2S" },
            new[] { "kind:linAgrawal", "taub", "taubS" },
            new[] { "*", "dnDT", "dnDTPerK" },
        };

        /// <summary>Rename the keys of <see cref="Renames"/> everywhere in a system file or run spec (recursively); one note per kind of rename.</summary>
        public static void RenameKeys(JsonValue v, List<string> notes)
        {
            var seen = new HashSet<string>();
            Walk(v, notes, seen);
        }

        private static void Walk(JsonValue v, List<string> notes, HashSet<string> seen)
        {
            if (v.Kind == JsonKind.Array) { foreach (var item in v.AsArray()) Walk(item, notes, seen); return; }
            if (v.Kind != JsonKind.Object) return;
            var o = v.AsObject();
            foreach (var r in Renames)
            {
                if (!o.Has(r[1]) || o.Has(r[2]) || !InScope(o, r[0])) continue;
                var val = o[r[1]];
                o.Remove(r[1]);
                o.Set(r[2], val);
                if (seen.Add(r[1] + ">" + r[2])) notes?.Add("\"" + r[1] + "\" is now \"" + r[2] + "\" (the unit is in the key name); the old name still works but is deprecated");
            }
            foreach (var kv in o.ToList()) Walk(kv.Value, notes, seen);
        }

        private static bool InScope(JsonObject o, string scope)
        {
            if (scope == "*") return true;
            int colon = scope.IndexOf(':');
            string key = scope.Substring(0, colon);
            var s = o.GetString(key);
            return s != null && Array.IndexOf(scope.Substring(colon + 1).Split('|'), s) >= 0;
        }

        /// <summary>Migrate a system file in place (and return it). Notes describe every change.</summary>
        public static JsonObject MigrateSystem(JsonObject o, List<string> notes)
        {
            RenameKeys(o, notes);
            int v = o.GetInt("version", 1);
            if (v > SystemVersion) throw new JsonException("System file version " + v + " is newer than this engine supports (" + SystemVersion + ").");
            if (v < 2)
            {
                // v1 -> v2: no element changes; the version number marks files that went through the v2 checks
                o.Set("version", 2);   // silent: nothing else changed
            }
            return o;
        }

        /// <summary>Migrate a run spec in place (and return it). Notes describe every change.</summary>
        public static JsonObject MigrateRun(JsonObject o, List<string> notes)
        {
            RenameKeys(o, notes);
            int v = o.GetInt("version", 1);
            if (v > RunVersion) throw new JsonException("Run spec version " + v + " is newer than this engine supports (" + RunVersion + ").");
            if (v < 2)
            {
                var ins = o.GetArray("instruments");
                if (ins != null)
                    foreach (var iv in ins)
                    {
                        if (iv.Kind != JsonKind.Object) continue;
                        var i = iv.AsObject();
                        string kind = i.GetString("kind", "");
                        if (string.Equals(kind, "nearFieldCamera", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "farFieldCamera", StringComparison.OrdinalIgnoreCase))
                        {
                            bool far = kind.StartsWith("far", StringComparison.OrdinalIgnoreCase);
                            var imaging = i.GetObject("imaging") ?? new JsonObject();
                            imaging.Set("plane", far ? "far" : "near");
                            foreach (var k in new[] { "focalLengthM", "propagateM", "magnification" })
                                if (i.TryGet(k, out var val)) { imaging.Set(k, val); i.Remove(k); }
                            i.Set("kind", "camera");
                            i.Set("imaging", imaging);
                            notes?.Add("instrument " + (i.GetString("name") ?? kind) + ": \"" + kind + "\" is now \"camera\" with \"imaging\": {\"plane\": \"" + (far ? "far" : "near") + "\"}");
                        }
                    }
                o.Set("version", 2);
            }
            return o;
        }
    }
}
