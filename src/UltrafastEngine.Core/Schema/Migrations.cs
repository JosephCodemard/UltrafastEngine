using System;
using System.Collections.Generic;
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
        public const int SystemVersion = 2;
        /// <summary>Current format version of run specs.</summary>
        public const int RunVersion = 2;

        /// <summary>Migrate a system file in place (and return it). Notes describe every change.</summary>
        public static JsonObject MigrateSystem(JsonObject o, List<string> notes)
        {
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
