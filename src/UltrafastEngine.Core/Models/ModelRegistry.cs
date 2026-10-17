using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Models
{
    /// <summary>
    /// Creates model instances from <see cref="ModelSpec"/>s and holds user callbacks.
    ///
    /// Each category ("index", "attenuation", "timeResponse", "filter", "beam", "delayedResponse",
    /// "aperture") has a table of factories keyed by <c>kind</c>. The built-in kinds are registered by
    /// <see cref="CreateDefault"/>; add your own with <see cref="Register{T}"/>:
    /// <code>
    /// var reg = ModelRegistry.CreateDefault();
    /// reg.Register&lt;IRefractiveIndex&gt;("index", "myGlass", (spec, r) =&gt; new MyGlassIndex(spec.GetRequired("temperatureK")));
    /// </code>
    /// Callbacks (from C#, Python or C) are registered by name and referenced from JSON as
    /// <c>{"kind":"callback","name":"..."}</c> or the shorthand string <c>"@name"</c>.
    /// </summary>
    public sealed class ModelRegistry
    {
        private readonly Dictionary<string, Dictionary<string, Func<ModelSpec, ModelRegistry, object>>> _factories =
            new Dictionary<string, Dictionary<string, Func<ModelSpec, ModelRegistry, object>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Delegate> _callbacks = new Dictionary<string, Delegate>(StringComparer.Ordinal);

        public static readonly string[] Categories = { "index", "attenuation", "timeResponse", "filter", "beam", "delayedResponse", "aperture" };

        public void Register<T>(string category, string kind, Func<ModelSpec, ModelRegistry, T> factory) where T : class
        {
            if (!_factories.TryGetValue(category, out var t)) _factories[category] = t = new Dictionary<string, Func<ModelSpec, ModelRegistry, object>>(StringComparer.OrdinalIgnoreCase);
            t[kind] = (s, r) => factory(s, r);
        }

        public IEnumerable<string> KindsFor(string category) =>
            _factories.TryGetValue(category, out var t) ? t.Keys.OrderBy(k => k) : Enumerable.Empty<string>();

        public T Create<T>(string category, ModelSpec spec) where T : class
        {
            if (spec == null) return null;
            if (!_factories.TryGetValue(category, out var t) || !t.TryGetValue(spec.Kind, out var f))
                throw new ModelException(spec.Origin + ": unknown " + category + " model kind \"" + spec.Kind + "\". Known kinds: " + string.Join(", ", KindsFor(category)) + ".");
            try
            {
                return (T)f(spec, this);
            }
            catch (ModelException) { throw; }
            catch (Exception ex)
            {
                throw new ModelException(spec.Origin + ": could not build " + category + " model " + spec.Describe() + ": " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ callbacks
        public void RegisterCallback(string name, Delegate callback)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Callback name must be non-empty.");
            _callbacks[name] = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        private readonly HashSet<string> _carrierResolved = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Register a time-response callback and declare whether it varies on the optical-cycle scale
        /// (the engine then uses the carrier-resolved field representation for elements it acts on).</summary>
        public void RegisterTimeResponse(string name, TimeResponseCallback callback, bool carrierResolved)
        {
            RegisterCallback(name, callback);
            if (carrierResolved) _carrierResolved.Add(name); else _carrierResolved.Remove(name);
        }

        public bool IsCarrierResolved(string name) => name != null && _carrierResolved.Contains(name);

        public bool HasCallback(string name) => _callbacks.ContainsKey(name);
        public IEnumerable<string> CallbackNames => _callbacks.Keys;

        public T GetCallback<T>(string name, string origin) where T : Delegate
        {
            if (!_callbacks.TryGetValue(name ?? "", out var d))
                throw new ModelException(origin + ": no callback named \"" + name + "\" is registered" + (_callbacks.Count == 0 ? " (none are)." : " (registered: " + string.Join(", ", _callbacks.Keys) + ")."));
            if (d is T t) return t;
            throw new ModelException(origin + ": callback \"" + name + "\" has type " + d.GetType().Name + ", but this property needs a " + typeof(T).Name + ".");
        }

        /// <summary>A copy sharing factories but with its own callback table (each run gets one).</summary>
        public ModelRegistry Fork()
        {
            var r = new ModelRegistry();
            foreach (var kv in _factories) r._factories[kv.Key] = new Dictionary<string, Func<ModelSpec, ModelRegistry, object>>(kv.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _callbacks) r._callbacks[kv.Key] = kv.Value;
            foreach (var n in _carrierResolved) r._carrierResolved.Add(n);
            return r;
        }

        // ------------------------------------------------------------------ built-ins
        private static readonly object _defaultLock = new object();
        private static ModelRegistry _default;

        private static readonly List<Action<ModelRegistry>> _configurators = new List<Action<ModelRegistry>>();

        /// <summary>Plugins: add models to every default registry (applied now to the cached default and to all later forks).</summary>
        public static void AddDefaultConfigurator(Action<ModelRegistry> configure)
        {
            lock (_defaultLock)
            {
                _configurators.Add(configure);
                if (_default != null) configure(_default);
            }
        }

        /// <summary>A registry with every built-in kind (and every plugin model) registered. Returns a fresh fork each call.</summary>
        public static ModelRegistry CreateDefault()
        {
            Plugins.PluginManager.EnsureLoaded();
            lock (_defaultLock)
            {
                if (_default == null)
                {
                    _default = new ModelRegistry(); BuiltInModels.RegisterAll(_default);
                    foreach (var c in _configurators) c(_default);
                }
                return _default.Fork();
            }
        }

        /// <summary>Machine-readable list of all kinds per category (for docs and `ultrafast info`).</summary>
        public JsonObject Describe()
        {
            var o = new JsonObject();
            foreach (var c in Categories) o.Set(c, JsonValue.FromArray(KindsFor(c)));
            return o;
        }
    }

    public sealed class ModelException : Exception
    {
        public ModelException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Registers all built-in model kinds.</summary>
    internal static class BuiltInModels
    {
        public static void RegisterAll(ModelRegistry r)
        {
            // ---- refractive index
            r.Register<IRefractiveIndex>("index", "constant", (s, _) => new ConstantIndex(s.GetRequired("value")));
            r.Register<IRefractiveIndex>("index", "sellmeier", (s, _) => new SellmeierIndex(s.Json.GetDoubleArray("B"), s.Json.GetDoubleArray("C"), s.GetDouble("A", 1.0), s.Json.GetString("name")));
            r.Register<IRefractiveIndex>("index", "cauchy", (s, _) => new CauchyIndex(s.Json.GetDoubleArray("coefficients")));
            r.Register<IRefractiveIndex>("index", "table", (s, _) => new TableIndex(WavelengthsM(s), s.Json.GetDoubleArray("n")));
            r.Register<IRefractiveIndex>("index", "expression", (s, _) => new ExpressionIndex(s.Json.GetRequiredString("expr", s.Origin), s.Params()));
            r.Register<IRefractiveIndex>("index", "callback", (s, reg) => new CallbackIndex(s.Json.GetString("name"), reg.GetCallback<IndexCallback>(s.Json.GetString("name"), s.Origin)));
            r.Register<IRefractiveIndex>("index", "ciddor", (s, _) => new CiddorAirIndex(s.GetDouble("temperatureC", 20), s.GetDouble("pressurePa", 101325), s.GetDouble("humidity", 0), s.GetDouble("co2ppm", 450)));
            r.Register<IRefractiveIndex>("index", "gas", (s, _) => new GasIndex(s.Json.GetRequiredString("species", s.Origin), s.GetDouble("pressureBar", 1.0), s.GetDouble("temperatureK", 293.15)));
            r.Register<IRefractiveIndex>("index", "refractiveindex", (s, _) => new RefractiveIndexInfoIndex(RefractiveIndexInfoPage.Load(s.Json.GetString("page") ?? s.Json.GetRequiredString("file", s.Origin)),
                s.GetDouble("pressureBar", double.NaN), s.GetDouble("temperatureK", double.NaN), s.GetDouble("dnDT", 0), s.Json.GetBool("relativeToAir", false)));
            r.Register<IAttenuation>("attenuation", "refractiveindex", (s, _) => new RefractiveIndexInfoIndex(RefractiveIndexInfoPage.Load(s.Json.GetString("page") ?? s.Json.GetRequiredString("file", s.Origin)),
                s.GetDouble("pressureBar", double.NaN), s.GetDouble("temperatureK", double.NaN)));
            r.Register<IRefractiveIndex>("index", "library", (s, _) => MaterialLibrary.BuiltIn(s.Json.GetRequiredString("name", s.Origin)).Index);

            // ---- attenuation (1/m)
            r.Register<IAttenuation>("attenuation", "constant", (s, _) => new ConstantAttenuation(s.GetRequired("value")));
            r.Register<IAttenuation>("attenuation", "none", (s, _) => null);
            r.Register<IAttenuation>("attenuation", "table", (s, _) =>
            {
                var w = WavelengthsM(s);
                double[] a = s.Json.GetDoubleArray("alphaPerM");
                if (a == null)
                {
                    var ti = s.Json.GetDoubleArray("internalTransmission") ?? throw new ModelException(s.Origin + ": table attenuation needs alphaPerM or internalTransmission + thicknessM.");
                    double d = s.GetRequired("thicknessM");
                    a = ti.Select(x => -Math.Log(Math.Max(1e-300, x)) / d).ToArray();
                }
                return new TableAttenuation(w, a);
            });
            r.Register<IAttenuation>("attenuation", "expression", (s, _) => new FunctionAttenuation(new ExpressionIndex(s.Json.GetRequiredString("expr", s.Origin), s.Params())));
            r.Register<IAttenuation>("attenuation", "callback", (s, reg) => new FunctionAttenuation(new CallbackIndex(s.Json.GetString("name"), reg.GetCallback<IndexCallback>(s.Json.GetString("name"), s.Origin))));

            // ---- delayed (Raman) response
            r.Register<IDelayedResponse>("delayedResponse", "none", (s, _) => null);
            r.Register<IDelayedResponse>("delayedResponse", "silica", (s, _) => new DampedOscillatorResponse(12.2e-15, 32e-15));
            r.Register<IDelayedResponse>("delayedResponse", "dampedOscillator", (s, _) => new DampedOscillatorResponse(s.GetRequired("tau1"), s.GetRequired("tau2")));
            r.Register<IDelayedResponse>("delayedResponse", "expression", (s, _) =>
            {
                var e = CompiledExpression.Compile(s.Json.GetRequiredString("expr", s.Origin), new[] { "t" }, s.Params());
                return new FunctionDelayedResponse("expression \"" + e.Text + "\"", t => e.Evaluate(new Dictionary<string, double[]> { { "t", t } }, t.Length));
            });
            r.Register<IDelayedResponse>("delayedResponse", "callback", (s, reg) =>
            {
                var cb = reg.GetCallback<ArrayCallback>(s.Json.GetString("name"), s.Origin);
                return new FunctionDelayedResponse("callback @" + s.Json.GetString("name"), t => { var h = new double[t.Length]; cb(t, h); return h; });
            });

            // ---- time-domain response Delta n(t)
            r.Register<ITimeDomainResponse>("timeResponse", "expression", (s, _) => new ExpressionTimeResponse(s));
            r.Register<ITimeDomainResponse>("timeResponse", "callback", (s, reg) =>
                new CallbackTimeResponse(s.Json.GetString("name"), reg.GetCallback<TimeResponseCallback>(s.Json.GetString("name"), s.Origin), s.Json.GetBool("carrierResolved", reg.IsCarrierResolved(s.Json.GetString("name")))));
            r.Register<ITimeDomainResponse>("timeResponse", "twoPhotonAbsorption", (s, _) => new TwoPhotonAbsorptionResponse(s.GetRequired("beta")));
            r.Register<ITimeDomainResponse>("timeResponse", "chi2", (s, _) => new Chi2Response(s.GetRequired("deffPmPerV") * 1e-12, s.GetDouble("periodM", 0), s.GetDouble("n0", double.NaN)));
            r.Register<ITimeDomainResponse>("timeResponse", "kerr", (s, _) => new ExtraKerrResponse(s.GetRequired("n2")));

            // ---- spectral filters
            r.Register<ISpectralFilter>("filter", "table", (s, _) => TableFilter.FromSpec(s));
            r.Register<ISpectralFilter>("filter", "expression", (s, _) => new ExpressionFilter(s));
            r.Register<ISpectralFilter>("filter", "gaussian", (s, _) => new ShapeFilter(s, superGaussianOrder: 1));
            r.Register<ISpectralFilter>("filter", "superGaussian", (s, _) => new ShapeFilter(s, superGaussianOrder: s.Json.GetInt("order", 2)));
            r.Register<ISpectralFilter>("filter", "constant", (s, _) => new ConstantFilter(s.GetDouble("value", s.GetDouble("transmission", 1.0))));
            r.Register<ISpectralFilter>("filter", "callback", (s, reg) => new CallbackFilter(s.Json.GetString("name"), reg.GetCallback<FilterCallback>(s.Json.GetString("name"), s.Origin)));

            // ---- beams
            r.Register<IBeamModel>("beam", "gaussian", (s, _) => GaussianBeam.FromSpec(s));
            r.Register<IBeamModel>("beam", "sampled", (s, _) => SampledBeam.FromSpec(s));
            r.Register<IBeamModel>("beam", "constant", (s, _) => ConstantBeam.FromSpec(s));
            r.Register<IBeamModel>("beam", "expression", (s, _) => new ExpressionBeam(s));
            r.Register<IBeamModel>("beam", "callback", (s, reg) => new CallbackBeam(s.Json.GetString("name"), reg.GetCallback<BeamCallback>(s.Json.GetString("name"), s.Origin)));

            // ---- apertures
            r.Register<IAperture>("aperture", "circular", (s, _) => new CircularAperture(s.GetRequired("radiusM")));
            r.Register<IAperture>("aperture", "rectangular", (s, _) => new RectangularAperture(s.GetRequired("halfWidthXM"), s.GetDouble("halfWidthYM", s.GetRequired("halfWidthXM"))));
            r.Register<IAperture>("aperture", "none", (s, _) => null);
            SpatialField.Register(r);
        }

        internal static double[] WavelengthsM(ModelSpec s)
        {
            var j = s.Json;
            if (j.Has("wavelengthsM")) return j.GetDoubleArray("wavelengthsM");
            if (j.Has("wavelengthsUm")) return j.GetDoubleArray("wavelengthsUm").Select(x => x * 1e-6).ToArray();
            if (j.Has("wavelengthsNm")) return j.GetDoubleArray("wavelengthsNm").Select(x => x * 1e-9).ToArray();
            throw new ModelException(s.Origin + ": table needs wavelengthsM (or wavelengthsUm / wavelengthsNm).");
        }
    }
}
