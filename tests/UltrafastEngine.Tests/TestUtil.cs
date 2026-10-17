using System;
using System.Linq;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Specs;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Tests
{
    internal static class U
    {
        public const double C = 299792458.0;
        public static double W(double lambdaM) => 2 * Math.PI * C / lambdaM;

        public static JsonObject O(params object[] kv)
        {
            var o = new JsonObject();
            for (int i = 0; i < kv.Length; i += 2)
            {
                var v = kv[i + 1];
                JsonValue jv;
                switch (v)
                {
                    case JsonValue j: jv = j; break;
                    case double d: jv = d; break;
                    case int n: jv = n; break;
                    case bool b: jv = b; break;
                    case string s: jv = s; break;
                    case double[] a: jv = JsonValue.FromArray(a); break;
                    case null: jv = JsonValue.Null; break;
                    default: throw new ArgumentException("unsupported " + v.GetType());
                }
                o.Set((string)kv[i], jv);
            }
            return o;
        }

        public static JsonArray A(params JsonValue[] items) => new JsonArray(items);

        public static SystemDescription Sys(params JsonObject[] elements)
        {
            var o = O("format", "ultrafast-system", "version", 1, "name", "test");
            o.Set("elements", new JsonArray(elements));
            return SystemDescription.FromJson(o);
        }

        public static RunSpec Spec(JsonObject pulse, JsonObject extra = null)
        {
            var o = O("format", "ultrafast-run");
            o.Set("pulse", pulse);
            if (extra != null) foreach (var kv in extra) o.Set(kv.Key, kv.Value);
            return RunSpec.FromJson(o);
        }

        public static JsonObject Gauss(double lambda, double fwhm, double energy) =>
            O("shape", "gaussian", "centerWavelengthM", lambda, "durationFwhmS", fwhm, "energyJ", energy);

        public static RunResult Go(SystemDescription s, RunSpec r, ModelRegistry reg = null) => Simulation.Run(s, r, reg);

        public static double RmsSpectralWidth(UltrafastEngine.Pulses.PulseField p)
        {
            var (_, rms) = PulseMetrics.Moments(p.Grid.OmegaAbsolute, p.SpectralIntensity());
            return rms;
        }

        public static double SpectralCentroid(UltrafastEngine.Pulses.PulseField p) => PulseMetrics.Moments(p.Grid.OmegaAbsolute, p.SpectralIntensity()).Mean;
        public static double TemporalCentroid(UltrafastEngine.Pulses.PulseField p) => PulseMetrics.Moments(p.Grid.Time, PulseMetrics.TemporalPower(p)).Mean;

        /// <summary>A dispersionless nonlinear test material (constant index).</summary>
        public static JsonObject FlatMaterial(double n2, double n = 1.45) => O("index", O("kind", "constant", "value", n), "n2", n2);
    }
}
