# Plugin system

A **plugin** adds capabilities to the engine without the core depending on it: element types, instrument kinds,
models, run modes, hooks that post-process instrument readings, summary sections and result files, CLI commands,
console reports, and (optionally) a Python module with builders and figures. The first-party plugins are
[fitting](../plugins/fitting.md) and [zemax](../plugins/zemax.md); the live-data plugin and ML plugins follow
after the stable release.

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
[p.id for p in uf.plugins.installed()]       # ['fitting', 'zemax']
fitting = uf.plugins.module("fitting")       # the plugin's Python module, or None
```

**Search order** (the first folder that has a plugin id wins):

| | folder |
|---|---|
| 1 | `ULTRAFAST_PLUGIN_PATH` (folders separated by `:` on macOS/Linux, `;` on Windows) |
| 2 | `plugins/` next to the engine assembly (the CLI's folder; `ultrafast/bin/hosted/plugins` in the Python package) |
| 3 | `artifacts/plugins` of a source checkout (Python package used from the checkout) |
| 4 | the user folder: `$ULTRAFAST_HOME/plugins`, default `~/.ultrafast/plugins` (where `install` copies to) |

`ULTRAFAST_NO_PLUGINS=1` runs the bare core. Plugins are loaded once, on first use of the engine. A plugin that
fails to load is listed with its error and otherwise ignored; it never stops the engine.

**What happens without a plugin.** A run spec with `"fit"` or `"backward"`, or a system with a `transform` element,
stops with a message naming the fitting plugin; an unknown element type or CLI command says which plugin provides it
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
  "engine": "2.1.8-beta",
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
| `engine` | minimum engine version (`2.1.8-beta`); older engines list the plugin as not loaded |
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
| `RegisterModels(configure)` | model kinds in every `ModelRegistry` (index, attenuation, timeResponse, filter, beam, delayedResponse, aperture) | |
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

`PluginTests` registers a test plugin with an element and an instrument and runs them; checks that the first-party
plugins are registered and their installable folders load; installs and removes a plugin in a temporary
`ULTRAFAST_HOME`; refuses a plugin that needs a newer engine; orders pre-release versions.
