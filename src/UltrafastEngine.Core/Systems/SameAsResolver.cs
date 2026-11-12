// SameAsResolver.cs -- elements that are the same physical object as another element ("sameAs").
//
//   SameAsResolver.Resolve(elements)   -> JsonObject[]   one expanded body per element, in order
//   SameAsResolver.InstanceKeys                           the keys a copy may set itself
//
// { "id": "pass2", "sameAs": "pass1" } is a copy of element "pass1": the same type and the same physical numbers (a transform's
// operators and their fit flags, a mirror's figure, a plate's damage). It is expanded from the JSON when the system is read,
// so a fit that writes new numbers into the source and rebuilds the system moves every copy with it. The copy is otherwise an
// ordinary element: it compiles on its own, sees its own input beam, spectrum and energy, and gets its own record and loss
// booking. Nothing is shared at run time (a gain medium's inversion, a device's state are per instance).
// A copy may set only the per-instance keys (InstanceKeys): where it sits (links, ports), whether it is enabled, and the
// geometry that differs from pass to pass (angle of incidence, pointing). The keys of the source for these are not inherited.
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Systems
{
    public static class SameAsResolver
    {
        /// <summary>Keys that belong to one instance of an element rather than to the physical object.</summary>
        public static readonly string[] InstanceKeys =
        {
            "id", "label", "enabled", "next", "inputs", "loop", "surfaces",   // identity and wiring
            "aoiDeg", "aoiRad", "tiltXRad", "side", "output"                    // geometry that may differ per pass (side: the face a splitter is met on)
        };

        /// <summary>Expand every <c>sameAs</c> element into a full body (a clone of its source with the copy's instance keys).
        /// Elements without <c>sameAs</c> are returned as they are. Throws <see cref="JsonException"/> for an unknown source,
        /// a cycle, or a key a copy may not set.</summary>
        public static JsonObject[] Resolve(JsonArray elements)
        {
            int n = elements.Count;
            var objs = elements.Select(v => v.AsObject()).ToArray();
            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < n; k++) { string key = objs[k].GetString("id") ?? ("e" + k); if (!byId.ContainsKey(key)) byId[key] = k; }
            var done = new JsonObject[n];
            var busy = new bool[n];
            for (int k = 0; k < n; k++) Expand(k, objs, byId, done, busy);
            return done;
        }

        private static JsonObject Expand(int k, JsonObject[] objs, Dictionary<string, int> byId, JsonObject[] done, bool[] busy)
        {
            if (done[k] != null) return done[k];
            var eo = objs[k];
            if (!eo.Has("sameAs")) return done[k] = eo;
            string id = eo.GetString("id") ?? ("e" + k), src = eo.GetString("sameAs");
            string ctx = "elements[" + k + "] '" + id + "'";
            if (busy[k]) throw new JsonException(ctx + ": sameAs forms a cycle.");
            if (!byId.TryGetValue(src, out int j)) throw new JsonException(ctx + ": sameAs \"" + src + "\" is not the id of an element.");
            if (j == k) throw new JsonException(ctx + ": sameAs points at the element itself.");
            busy[k] = true;
            var source = Expand(j, objs, byId, done, busy);
            busy[k] = false;

            var copy = JsonValue.Parse(source.ToJson(false)).AsObject();
            foreach (var key in InstanceKeys) copy.Remove(key);
            copy.Remove("sameAs");
            foreach (var kv in eo)
            {
                if (kv.Key == "sameAs") continue;
                if (kv.Key == "type")   // older files repeat the type of the source: allowed when it is the same
                {
                    if (!string.Equals(kv.Value.AsString(), source.GetString("type"), StringComparison.OrdinalIgnoreCase))
                        throw new JsonException(ctx + ": sameAs needs an element of the same type (\"" + src + "\" is \"" + source.GetString("type") + "\").");
                    continue;
                }
                if (!InstanceKeys.Contains(kv.Key))
                    throw new JsonException(ctx + ": \"" + kv.Key + "\" cannot be set on a sameAs element, it is shared with \"" + src
                        + "\". A copy may set only: " + string.Join(", ", InstanceKeys) + ".");
                copy.Set(kv.Key, kv.Value);
            }
            return done[k] = copy;
        }
    }
}
