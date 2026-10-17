using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UltrafastEngine.Engine;
using UltrafastEngine.Instruments;
using UltrafastEngine.Models;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Plugins
{
    /// <summary>
    /// A plugin: an assembly (plus optional Python module and docs) that adds capabilities to the engine without the
    /// core depending on it -- elements, instruments, models, run modes (e.g. the fitting plugin's "fit"), hooks
    /// that post-process instrument readings, result sections and files, CLI commands and console reports.
    /// See docs/reference/plugins.md. Implement this interface on a public class with a parameterless constructor
    /// and name it in the plugin's <c>plugin.json</c> ("entry"), or register it in code with
    /// <see cref="PluginManager.Register"/> (the only way in NativeAOT builds).
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Unique, lower-case identifier ("fitting", "zemax").</summary>
        string Id { get; }
        /// <summary>Human-readable name.</summary>
        string Name { get; }
        /// <summary>Plugin version (semantic versioning).</summary>
        string Version { get; }
        /// <summary>One sentence describing what the plugin adds.</summary>
        string Description { get; }
        /// <summary>Called once when the plugin is loaded: register everything the plugin provides.</summary>
        void Register(PluginContext context);
    }

    /// <summary>Compiles one element of a plugin-provided type into linear terms, a nonlinear segment and/or an
    /// active element (see <see cref="CompiledElement"/>). <paramref name="services"/> gives access to materials,
    /// overrides (<c>services.Num</c>, <c>services.Str</c>), the model registry, provenance and warnings.</summary>
    public delegate void ElementCompiler(CompilerServices services, SystemElement element, CompiledElement compiled);

    /// <summary>What an instrument hook sees after an instrument has measured.</summary>
    public sealed class InstrumentHookContext
    {
        public InstrumentOutput Output { get; internal set; }
        public JsonObject Spec { get; internal set; }
        public RunResult Result { get; internal set; }
        public CompiledRun Compiled { get; internal set; }
        /// <summary>Folder that relative file names in the run spec are resolved against.</summary>
        public string BaseDirectory => Compiled?.Run?.BaseDirectory;
    }

    /// <summary>Runs after every instrument (e.g. compares it with recorded data). Exceptions are turned into
    /// warnings naming the instrument; the run continues.</summary>
    public interface IInstrumentHook
    {
        /// <summary>Instrument-spec keys this hook handles (e.g. "measured"). An instrument that uses one of these
        /// keys when no plugin claims it gets a warning that names the missing plugin capability.</summary>
        IReadOnlyList<string> Keys { get; }
        void AfterMeasure(InstrumentHookContext context);
    }

    /// <summary>A whole different way of running a spec, selected by a top-level key of the run spec (the
    /// fitting plugin's "fit"). The first registered mode whose key is present runs instead of a plain run.</summary>
    public interface IRunMode
    {
        /// <summary>Top-level run-spec key that selects this mode.</summary>
        string Key { get; }
        string Description { get; }
        RunResult Run(SystemDescription system, RunSpec run, ModelRegistry registry, ProgressCallback progress, CancellationToken cancel);
    }

    /// <summary>Adds sections to summary.json and files to the results folder (e.g. fit.json).</summary>
    public interface IResultExtension
    {
        void ContributeSummary(RunResult result, JsonObject summary);
        void Write(RunResult result, string directory);
    }

    /// <summary>Prints plugin-specific lines after a CLI run (e.g. comparison tables, fitted parameters).</summary>
    public interface IRunReporter
    {
        void Report(RunResult result, TextWriter output);
    }

    /// <summary>A subcommand of the <c>ultrafast</c> CLI provided by a plugin (<c>ultrafast zemax-demo ...</c>).</summary>
    public interface ICliCommand
    {
        string Name { get; }
        /// <summary>Usage text shown by <c>ultrafast help</c> (first line: the command line).</summary>
        string Usage { get; }
        int Run(string[] args, TextWriter output, TextWriter error);
    }

    /// <summary>Everything a plugin can register. Handed to <see cref="IPlugin.Register"/>.</summary>
    public sealed class PluginContext
    {
        internal PluginContext(IPlugin plugin) { Plugin = plugin; }
        public IPlugin Plugin { get; }

        /// <summary>A new element type, usable as <c>"type": name</c> in system files. <paramref name="spec"/>
        /// declares its parameters for the schema, validation and reference docs.</summary>
        public void RegisterElement(string type, ElementCompiler compiler, Schema.ComponentSpec spec = null)
        {
            PluginManager.ElementCompilers[type] = compiler;
            if (spec != null) Schema.OptionRegistry.Add(spec.WithOrigin(Plugin.Id));
        }

        /// <summary>A new instrument kind, usable as <c>"kind": name</c> in run specs.</summary>
        public void RegisterInstrument(string kind, Func<JsonObject, IInstrument> factory, Schema.ComponentSpec spec = null)
        {
            InstrumentFactory.Register(kind, factory);
            if (spec != null) Schema.OptionRegistry.Add(spec.WithOrigin(Plugin.Id));
        }

        /// <summary>Models (index, timeResponse, filter, beam, ...) added to every default model registry.</summary>
        public void RegisterModels(Action<ModelRegistry> configure) => ModelRegistry.AddDefaultConfigurator(configure);

        public void RegisterInstrumentHook(IInstrumentHook hook) => PluginManager.InstrumentHooks.Add(hook);
        public void RegisterRunMode(IRunMode mode) => PluginManager.RunModes.Add(mode);
        public void RegisterResultExtension(IResultExtension extension) => PluginManager.ResultExtensions.Add(extension);
        public void RegisterReporter(IRunReporter reporter) => PluginManager.Reporters.Add(reporter);
        public void RegisterCommand(ICliCommand command) => PluginManager.Commands[command.Name] = command;
        /// <summary>A compute back-end (e.g. a GPU): selected with the run spec's "compute": {"backend": name}.</summary>
        public void RegisterBackend(Numerics.IComputeBackend backend) => Numerics.ComputeBackends.Register(backend);
        /// <summary>A pulse-retrieval algorithm (FROG, d-scan, ...): available to "ultrafast retrieve" and the API.</summary>
        public void RegisterRetrieval(Retrieval.IRetrieval retrieval) => Retrieval.Retrievals.Register(retrieval);

        /// <summary>Declares extra keys the plugin accepts in run specs (top level, e.g. "fit") or instrument specs
        /// (e.g. "measured"), so validation does not flag them as unknown.</summary>
        public void RegisterSpecKeys(Schema.ComponentSpec keys) => Schema.OptionRegistry.Add(keys.WithOrigin(Plugin.Id));
    }

    /// <summary>The <c>plugin.json</c> manifest of an installed plugin folder.</summary>
    public sealed class PluginManifest
    {
        public string Id, Name, Version, Description, MinEngineVersion, Entry, Folder;
        /// <summary>Assembly paths relative to the folder, per target framework ("net10.0", "net48").</summary>
        public Dictionary<string, string> Assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Python module that the <c>ultrafast</c> package imports (figures, builders), relative "python" folder.</summary>
        public string PythonModule;
        public List<string> Commands = new List<string>();

        public static PluginManifest Load(string folder)
        {
            var path = Path.Combine(folder, "plugin.json");
            var o = JsonValue.ParseFile(path).AsObject();
            var m = new PluginManifest
            {
                Folder = Path.GetFullPath(folder),
                Id = o.GetRequiredString("id", path), Name = o.GetString("name"), Version = o.GetString("version", "0.0.0"),
                Description = o.GetString("description", ""), MinEngineVersion = o.GetString("engine"), Entry = o.GetString("entry"),
            };
            var asm = o.GetObject("assemblies");
            if (asm != null) foreach (var kv in asm) m.Assemblies[kv.Key] = kv.Value.AsString();
            var py = o.GetObject("python");
            if (py != null) m.PythonModule = py.GetString("module");
            var cmds = o.GetArray("commands");
            if (cmds != null) foreach (var c in cmds) m.Commands.Add(c.AsString());
            return m;
        }
    }

    /// <summary>A loaded (or failed) plugin, as listed by <c>ultrafast plugins list</c>.</summary>
    public sealed class PluginInfo
    {
        public string Id, Name, Version, Description, Source, Error;
        public bool Loaded => Error == null;
    }

    /// <summary>
    /// Finds, loads and registers plugins. Search order (first wins for a given id): plugins registered in code;
    /// <c>ULTRAFAST_PLUGIN_PATH</c> (folders separated by the OS path separator); <c>plugins/</c> next to the engine
    /// assembly; the user folder (<c>~/.ultrafast/plugins</c>, or <c>ULTRAFAST_HOME/plugins</c>). Set
    /// <c>ULTRAFAST_NO_PLUGINS=1</c> to run the bare core. Loading happens once, on first use of the engine.
    /// </summary>
    public static class PluginManager
    {
        private static readonly object Lock = new object();
        private static bool _loaded;
        private static readonly List<PluginInfo> _infos = new List<PluginInfo>();
        private static readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static readonly Dictionary<string, ElementCompiler> ElementCompilers = new Dictionary<string, ElementCompiler>(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<IInstrumentHook> InstrumentHooks = new List<IInstrumentHook>();
        internal static readonly List<IRunMode> RunModes = new List<IRunMode>();
        internal static readonly List<IResultExtension> ResultExtensions = new List<IResultExtension>();
        internal static readonly List<IRunReporter> Reporters = new List<IRunReporter>();
        internal static readonly Dictionary<string, ICliCommand> Commands = new Dictionary<string, ICliCommand>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<PluginInfo> Plugins { get { EnsureLoaded(); lock (Lock) return _infos.ToList(); } }
        public static IEnumerable<string> ElementTypes { get { EnsureLoaded(); return ElementCompilers.Keys.ToList(); } }
        public static IReadOnlyList<IInstrumentHook> Hooks { get { EnsureLoaded(); return InstrumentHooks.ToList(); } }
        public static IReadOnlyList<IRunMode> Modes { get { EnsureLoaded(); return RunModes.ToList(); } }
        public static IReadOnlyList<IResultExtension> Extensions { get { EnsureLoaded(); return ResultExtensions.ToList(); } }
        public static IReadOnlyList<IRunReporter> RunReporters { get { EnsureLoaded(); return Reporters.ToList(); } }
        public static IReadOnlyDictionary<string, ICliCommand> CliCommands { get { EnsureLoaded(); return new Dictionary<string, ICliCommand>(Commands, StringComparer.OrdinalIgnoreCase); } }

        public static bool TryGetElementCompiler(string type, out ElementCompiler compiler) { EnsureLoaded(); return ElementCompilers.TryGetValue(type, out compiler); }

        /// <summary>Is a plugin with this id loaded?</summary>
        public static bool Has(string id) { EnsureLoaded(); lock (Lock) return _ids.Contains(id); }

        /// <summary>Register a plugin from code (before or after discovery). A second plugin with the same id is ignored.</summary>
        public static bool Register(IPlugin plugin, string source = "code")
        {
            lock (Lock)
            {
                if (!_ids.Add(plugin.Id)) return false;
                var info = new PluginInfo { Id = plugin.Id, Name = plugin.Name, Version = plugin.Version, Description = plugin.Description, Source = source };
                try { plugin.Register(new PluginContext(plugin)); }
                catch (Exception ex) { info.Error = "Register failed: " + ex.Message; _ids.Remove(plugin.Id); }
                _infos.Add(info);
                return info.Error == null;
            }
        }

        /// <summary>The folders searched for plugins, in order.</summary>
        public static IReadOnlyList<string> SearchPaths()
        {
            var dirs = new List<string>();
            var env = Environment.GetEnvironmentVariable("ULTRAFAST_PLUGIN_PATH");
            if (!string.IsNullOrEmpty(env)) dirs.AddRange(env.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));
            try
            {
                var here = Path.GetDirectoryName(typeof(PluginManager).Assembly.Location);
                if (!string.IsNullOrEmpty(here)) dirs.Add(Path.Combine(here, "plugins"));
            }
            catch { }
            dirs.Add(UserPluginFolder());
            return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Where <c>ultrafast plugins install</c> puts plugins: ULTRAFAST_HOME/plugins or ~/.ultrafast/plugins.</summary>
        public static string UserPluginFolder()
        {
            var home = Environment.GetEnvironmentVariable("ULTRAFAST_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ultrafast");
            return Path.Combine(home, "plugins");
        }

        /// <summary>Discover and load the plugins in the search paths (once).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Lock)
            {
                if (_loaded) return;
                _loaded = true;
                if (Environment.GetEnvironmentVariable("ULTRAFAST_NO_PLUGINS") == "1") return;
                foreach (var dir in SearchPaths())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var folder in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        if (File.Exists(Path.Combine(folder, "plugin.json"))) LoadFolder(folder);
                }
            }
        }

        /// <summary>Load one plugin folder (a folder holding plugin.json). Returns the info (check <c>Error</c>).</summary>
        public static PluginInfo LoadFolder(string folder)
        {
            PluginManifest m;
            try { m = PluginManifest.Load(folder); }
            catch (Exception ex) { var bad = new PluginInfo { Id = Path.GetFileName(folder), Source = folder, Error = "bad plugin.json: " + ex.Message }; lock (Lock) _infos.Add(bad); return bad; }
            lock (Lock)
            {
                if (_ids.Contains(m.Id)) return _infos.First(i => string.Equals(i.Id, m.Id, StringComparison.OrdinalIgnoreCase));
                var info = new PluginInfo { Id = m.Id, Name = m.Name ?? m.Id, Version = m.Version, Description = m.Description, Source = m.Folder };
                if (m.MinEngineVersion != null && CompareVersions(EngineInfo.Version, m.MinEngineVersion) < 0)
                {
                    info.Error = "needs engine " + m.MinEngineVersion + " or newer (this is " + EngineInfo.Version + ")";
                    _infos.Add(info); return info;
                }
                string tfm = RuntimeFramework();
                if (!m.Assemblies.TryGetValue(tfm, out var rel))
                {
                    // a Python-only or tool-only plugin (no assembly for this runtime): listed, nothing to register
                    if (m.Assemblies.Count == 0) { _ids.Add(m.Id); _infos.Add(info); return info; }
                    info.Error = "no assembly for " + tfm; _infos.Add(info); return info;
                }
                try
                {
                    // an assembly with the same name already loaded (referenced by the host): use it instead of a second copy
                    string simple = Path.GetFileNameWithoutExtension(rel);
                    var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => string.Equals(x.GetName().Name, simple, StringComparison.OrdinalIgnoreCase))
                              ?? LoadAssembly(Path.Combine(m.Folder, rel));
                    Type t = m.Entry != null ? asm.GetType(m.Entry, true) : asm.GetTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x) && !x.IsAbstract);
                    if (t == null) throw new InvalidOperationException("no IPlugin implementation in " + rel);
                    var plugin = (IPlugin)Activator.CreateInstance(t);
                    if (!string.Equals(plugin.Id, m.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("plugin id '" + plugin.Id + "' does not match plugin.json id '" + m.Id + "'");
                    _ids.Add(plugin.Id);
                    plugin.Register(new PluginContext(plugin));
                    info.Name = plugin.Name; info.Version = plugin.Version; info.Description = plugin.Description;
                }
                catch (Exception ex)
                {
                    _ids.Remove(m.Id);
                    var inner = ex is ReflectionTypeLoadException rtl && rtl.LoaderExceptions.Length > 0 ? rtl.LoaderExceptions[0] : ex.InnerException ?? ex;
                    info.Error = (ex is PlatformNotSupportedException ? "dynamic loading is not available in this build (NativeAOT): register the plugin in code. " : "") + inner.Message;
                }
                _infos.Add(info);
                return info;
            }
        }

        /// <summary>Load a plugin assembly into the same load context as the engine (hosts such as Python's hostfxr
        /// load the engine into an isolated context; the plugin must see the same UltrafastEngine.Core).</summary>
        private static Assembly LoadAssembly(string path)
        {
#if NETFRAMEWORK
            return Assembly.LoadFrom(path);
#else
            var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(PluginManager).Assembly) ?? System.Runtime.Loader.AssemblyLoadContext.Default;
            return alc.LoadFromAssemblyPath(Path.GetFullPath(path));
#endif
        }

        /// <summary>"net10.0" on .NET (Core), "net48" on .NET Framework / Mono.</summary>
        public static string RuntimeFramework()
        {
#if NETFRAMEWORK
            return "net48";
#else
            return "net10.0";
#endif
        }

        /// <summary>Compares "2.0.0-alpha.2" style versions (pre-release ordered alpha &lt; beta &lt; rc &lt; release).</summary>
        public static int CompareVersions(string a, string b)
        {
            (int[] n, string pre) P(string v)
            {
                var parts = v.Split(new[] { '-' }, 2);
                var nums = parts[0].Split('.').Select(x => int.TryParse(x, out var k) ? k : 0).Concat(Enumerable.Repeat(0, 3)).Take(3).ToArray();
                return (nums, parts.Length > 1 ? parts[1] : null);
            }
            var (na, pa) = P(a); var (nb, pb) = P(b);
            for (int i = 0; i < 3; i++) if (na[i] != nb[i]) return na[i].CompareTo(nb[i]);
            if (pa == pb) return 0;
            if (pa == null) return 1;
            if (pb == null) return -1;
            var sa = pa.Split('.'); var sb = pb.Split('.');
            int c = string.CompareOrdinal(sa[0], sb[0]);
            if (c != 0) return c;
            int ia = sa.Length > 1 && int.TryParse(sa[1], out var x1) ? x1 : 0, ib = sb.Length > 1 && int.TryParse(sb[1], out var x2) ? x2 : 0;
            return ia.CompareTo(ib);
        }

        /// <summary>Copy a plugin folder (or a .zip of one) into the user plugin folder. Returns the installed path.</summary>
        public static string Install(string source, bool overwrite = true)
        {
            string tmp = null, folder = source;
            if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tmp = Path.Combine(Path.GetTempPath(), "uf-plugin-" + Guid.NewGuid().ToString("N"));
                System.IO.Compression.ZipFile.ExtractToDirectory(source, tmp);
                folder = File.Exists(Path.Combine(tmp, "plugin.json")) ? tmp : Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "plugin.json")));
                if (folder == null) throw new InvalidOperationException(source + ": no plugin.json inside");
            }
            var m = PluginManifest.Load(folder);
            var dest = Path.Combine(UserPluginFolder(), m.Id);
            if (Directory.Exists(dest)) { if (!overwrite) throw new InvalidOperationException("plugin '" + m.Id + "' is already installed at " + dest); Directory.Delete(dest, true); }
            CopyDirectory(folder, dest);
            if (tmp != null) try { Directory.Delete(tmp, true); } catch { }
            return dest;
        }

        /// <summary>Remove a plugin from the user plugin folder.</summary>
        public static bool Remove(string id)
        {
            var dest = Path.Combine(UserPluginFolder(), id);
            if (!Directory.Exists(dest)) return false;
            Directory.Delete(dest, true);
            return true;
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
