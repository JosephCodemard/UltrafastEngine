using UltrafastEngine.Models;

namespace UltrafastEngine.Plugins.Hitran
{
    /// <summary>
    /// The HITRAN plugin (plugins/hitran): the index/attenuation model kind <c>"hitran"</c>, line-by-line Voigt absorption and
    /// Kramers-Kronig-consistent dispersion of H2O, CO2 and O2 from HITRAN line lists the user downloads
    /// (python -m ultrafast_hitran). Use it as the lab air: <c>"environment": {"air": {"kind": "hitran", "applyToGaps": true, ...}}</c>.
    /// Docs: docs/plugins/hitran.md.
    /// </summary>
    public sealed class HitranPlugin : IPlugin
    {
        public const string PluginVersion = "2.1.8-beta";
        public string Id => "hitran";
        public string Name => "HITRAN: line-by-line absorption and dispersion of gases";
        public string Version => PluginVersion;
        public string Description => "Voigt lines of H2O, CO2 and O2 from HITRAN data as an air model: absorption and Kramers-Kronig-consistent dispersion in every air gap.";

        public void Register(PluginContext ctx)
        {
            ctx.RegisterModels(r =>
            {
                r.Register<IRefractiveIndex>("index", "hitran", (s, _) => HitranAirIndex.FromSpec(s));
                r.Register<IAttenuation>("attenuation", "hitran", (s, _) => HitranAirIndex.FromSpec(s));
            });
        }

        /// <summary>Registers the plugin in-process (hosts that cannot load plugin folders, e.g. the NativeAOT library).</summary>
        public static void RegisterStatic()
        {
            try { PluginManager.Register(new HitranPlugin(), "built-in"); } catch { }
        }
    }
}
