# Plugin system

A **plugin** adds capabilities to the engine without the core depending on it: element types, instrument kinds,
models, run modes, hooks that post-process instrument readings, summary sections and result files, CLI commands,
console reports, and (optionally) a Python module with builders and figures. The first-party plugins are
[fitting](../plugins/fitting.md), [ao](../plugins/ao.md), [twin](../plugins/twin.md), [zemax](../plugins/zemax.md), [gpu](../plugins/gpu.md) and [hitran](../plugins/hitran.md); a live-data plugin and ML plugins are planned (`docs/design/future-plugins.md`).

## Keys

A plugin is a folder with a `plugin.json` ([below](#plugin-folder-layout)); the engine adds no keys for it except the ones the plugin declares through the option registry.

## Using plugins

Installing, checking and troubleshooting plugins is described step by step in [Installing plugins](../plugins/installing.md).

```bash
ultrafast plugins list              # installed plugins, versions, where they were found, load errors
ultrafast plugins paths             # the folders searched
ultrafast plugins install my-plugin/        # or my-plugin.zip: copies it into the user plugin folder
ultrafast plugins remove my-plugin
```

```python
import ultrafast as uf
[p.id for p in uf.plugins.installed()]       # ['ao', 'fitting', 'gpu', 'hitran', 'twin', 'zemax'] (those installed)
fitting = uf.plugins.module("fitting")       # the plugin's Python module, or None
```

**Search order** of the engine (`ultrafast plugins paths`; the first folder that has a plugin id wins):

| | folder |
|---|---|
| 1 | `ULTRAFAST_PLUGIN_PATH` (folders separated by `:` on macOS/Linux, `;` on Windows) |
| 2 | `plugins/` next to the engine assembly (the CLI's folder; `ultrafast/bin/hosted/plugins` in the Python package) |
| 3 | the user folder: `$ULTRAFAST_HOME/plugins`, default `~/.ultrafast/plugins` (where `install` copies to) |

A source build places plugins in `plugins/` beside the engine assembly (`build/Plugin.targets` copies them there and into `artifacts/plugins/<id>/`). The Python helper
`uf.plugins.search_paths()` also lists `artifacts/plugins` of a source checkout. `ULTRAFAST_NO_PLUGINS=1` runs the bare core. Plugins are loaded once, on first use of the engine. A plugin that
fails to load is listed with its error and otherwise ignored; it never stops the engine.

**What happens without a plugin.** A run spec with `"fit"` or `"backward"`, or a system with a `transform` element,
stops with a message naming the fitting plugin; the element types of the ao plugin (`slm`, `aopdf`, `deformableMirror`, `spectralShaper`) name the ao plugin; an unknown element type or CLI command says which plugin provides it
when it is a first-party one. Recorded data (an instrument's `"measured"`, a measured input spectrum) is part of the
core and works without any plugin ([Measured data](measured-data.md)).

**NativeAOT builds** (the self-contained C library) cannot load assemblies at run time: plugins must be compiled in
and registered in code (`PluginManager.Register(new FittingPlugin())`). The hosted engine (Python's default, the
CLI, .NET hosts) loads them dynamically.

## Plugin folder layout

```
my-plugin/
  plugin.json
  lib/net10.0/MyPlugin.dll        # .NET 10 build
  lib/net48/MyPlugin.dll          # .NET Framework 4.8 build (optional)
  python/my_plugin/__init__.py    # optional Python module
  tools/...                       # optional programs (the zemax plugin ships UltrafastEngine.Zemax.exe here)
  README.md
```

`plugin.json`:

```json
{
  "id": "my-plugin",
  "name": "My plugin",
  "version": "1.0.0",
  "description": "One sentence: what it adds.",
  "engine": "1.0.0",
  "entry": "MyCompany.MyPlugin.Plugin",
  "assemblies": { "net10.0": "lib/net10.0/MyPlugin.dll", "net48": "lib/net48/MyPlugin.dll" },
  "python": { "module": "my_plugin" },
  "commands": ["my-command"]
}
```

| key | meaning |
|---|---|
| `id` | unique, lower-case; must equal `IPlugin.Id` |
| `version` | the plugin's version |
| `engine` | minimum engine version (`1.0.0`); older engines list the plugin as not loaded |
| `entry` | full name of the `IPlugin` class (optional: the first `IPlugin` in the assembly) |
| `assemblies` | assembly per target framework; a plugin without assemblies is Python-only |
| `python.module` | module imported from `python/` by the `ultrafast` package |
| `commands` | informational list of CLI commands |

## Writing a plugin (C#)

Reference `UltrafastEngine.Core` (do not ship it in the plugin folder: it is the host's) and implement
`UltrafastEngine.Plugins.IPlugin`:

```csharp
using UltrafastEngine.Plugins;
using UltrafastEngine.Schema;

public sealed class Plugin : IPlugin
{
    public string Id => "my-plugin";
    public string Name => "My plugin";
    public string Version => "1.0.0";
    public string Description => "A flat attenuator element and a peak-power instrument.";

    public void Register(PluginContext ctx)
    {
        // an element type: compile it into linear terms / a nonlinear segment / an active element
        ctx.RegisterElement("attenuator",
            (services, element, compiled) => compiled.Post.Add(new FilterTerm(new ConstantFilter(services.Num(element, "transmission", 0.5)), "attenuator")),
            new ComponentSpec("element", "attenuator", "A flat loss.").Num("transmission", "", "Power transmission", "0.5"));

        // an instrument kind
        ctx.RegisterInstrument("peakPower", spec => new PeakPowerMeter(),
            new ComponentSpec("instrument", "peakPower", "Peak power."));

        // models available in every model registry (index, timeResponse, filter, beam, ...)
        ctx.RegisterModels(reg => reg.Register<ISpectralFilter>("filter", "myFilter", (s, r) => new MyFilter(s)));
    }
}
```

Declaring a `ComponentSpec` for every element and instrument puts its options into the
[JSON Schemas](schema.md), the [option reference](options.md) and the unknown-key checks automatically.
`services.Num(element, key, default)` and `services.Str(...)` read values *through overrides*, so run-spec
overrides work for plugin elements like for built-in ones.

### Extension points

| `PluginContext` method | what it adds | used by |
|---|---|---|
| `RegisterElement(type, compiler, spec)` | an element type (`ElementCompiler(services, element, compiled)`) | |
| `RegisterInstrument(kind, factory, spec)` | an instrument kind (`IInstrument`) | |
| `RegisterModels(configure)` | model kinds in every `ModelRegistry` (categories `index`, `attenuation`, `timeResponse`, `filter`, `beam`, `delayedResponse`, `aperture`, `field`) | |
| `RegisterInstrumentHook(hook)` | `IInstrumentHook.AfterMeasure` runs after every instrument whose spec has one of the hook's `Keys` (after the core's `measured` comparison) | |
| `RegisterRunMode(mode)` | `IRunMode.Run` replaces a plain run when the run spec has the mode's top-level `Key` | fitting (`fit`) |
| `RegisterResultExtension(ext)` | sections in `summary.json` and extra files in the results folder | fitting (`fit`, `fit.json`, `backward`) |
| `RegisterReporter(reporter)` | lines printed by `ultrafast run` | fitting |
| `RegisterCommand(command)` | an `ultrafast <name>` subcommand (`ICliCommand`) | zemax (`zemax-demo`, ...) |
| `RegisterSpecKeys(spec)` | extra keys for the checks and schemas: category `runTop` (top-level run-spec keys) or `instrumentCommon` (keys of every instrument) | fitting |
| `RegisterBackend(backend)` | a compute back-end (`IComputeBackend`) selectable with `run.compute.backend` ([Speed](speed.md)) | GPU back-ends |
| `RegisterRetrieval(retrieval)` | a pulse-retrieval method (`IRetrieval`) for `ultrafast retrieve` / `uf.retrieve` ([Pulse retrieval](retrieval.md)) | |

Plugins attach their own data to a result in `RunResult.Extensions[key]` (the fitting plugin offers typed access
with the extension method `result.Fit()`). Comparisons with recorded data are core: `result.Comparisons`
(`UltrafastEngine.Measured.Comparison`).

### Interfaces a plugin implements

| interface | members |
|---|---|
| `IPlugin` | `Id`, `Name`, `Version`, `Description`, `Register(PluginContext)` |
| `IInstrumentHook` | `Keys` (the instrument-spec keys it handles), `AfterMeasure(InstrumentHookContext)` |
| `IRunMode` | `Key`, `Description`, `Run(system, run, registry, progress, cancel)` returning a `RunResult` |
| `IResultExtension` | `ContributeSummary(result, summary)`, `Write(result, directory)` |
| `IRunReporter` | `Report(result, output)` |
| `ICliCommand` | `Name`, `Usage` (first line: the command line), `Run(args, output, error)` returning the exit code |

### Building the folder

The first-party plugins import `build/Plugin.targets`, which lays out `artifacts/plugins/<id>/` after every build
(`PluginId` and `PluginFolder` set in the project). Your own plugin can import it too, or produce the same layout
by hand.

## Writing the Python part

`python/my_plugin/__init__.py` may define `register(host)`; `host.register_figure(name, maker, after=None)` adds a
figure to `ultrafast-plot` and `ultrafast.plotting.report` (`maker(result)` returns a matplotlib figure, or None
when the result has no data for it). Anything else in the module is the plugin's Python API
(`uf.plugins.module("my-plugin")`).

## Tests

`PluginTests` registers a test plugin with an element and an instrument and runs them (`Register_*`); checks that the first-party
plugins are registered and their installable folders load; installs and removes a plugin in a temporary
`ULTRAFAST_HOME` (`Install_AndRemove_InTheUserFolder`); refuses a plugin that needs a newer engine (`TooNewPlugin_IsReportedNotLoaded`); orders pre-release
versions (`VersionComparison_OrdersPreReleases`). The twin plugin's command is checked in the same file (`TwinPlugin_RegistersItsCommand_*`).

## Limits

* NativeAOT builds load no plugin at run time; compile the plugin in and register it in code.
* A plugin that needs a newer engine than the one installed is listed as not loaded, with the reason.
* The `plugin.json` `commands` key is informational; the engine takes the commands the plugin registers.
