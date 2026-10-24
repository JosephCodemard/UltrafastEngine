# Installing plugins

Plugins are folders. Each one holds a `plugin.json` manifest, the assemblies for .NET 10 and .NET Framework 4.8, and
optionally a Python module and tools. The engine finds them at start-up in a few known places. Nothing is
registered globally and nothing is copied into the engine itself, so installing or removing a plugin is just
copying or deleting a folder.

## The first-party plugins (fitting, zemax)

**If you built the engine from source, they are already installed.** Every build of the CLI, the test runner and
the Python package builds both plugins and copies their folders next to the engine:

| you use | plugins are in |
|---|---|
| the CLI (`dotnet build UltrafastEngine.sln -c Release`) | `src/UltrafastEngine.Cli/bin/Release/net10.0/plugins/` |
| Python (`python python/tools/build_engine.py`) | `python/src/ultrafast/bin/hosted/plugins/` |
| any build | `artifacts/plugins/<id>/`: the installable layout, ready to copy elsewhere |

Check:

```bash
ultrafast plugins list
```
```
fitting    2.1.8-beta       Fittable "transform" elements, fits to recorded data with replaceable estimators ("fit") and backward propagation ("backward").
           .../net10.0/plugins/fitting
zemax      2.1.8-beta       Exports OpticStudio systems to .ufs files and ships the OpticStudio extension program.
           .../net10.0/plugins/zemax
```

```python
import ultrafast as uf
[p.id for p in uf.plugins.installed()]        # ['fitting', 'zemax']
fx = uf.plugins.module("fitting")             # the plugin's Python module (builders, figures), or None
```

To build only one plugin: `dotnet build plugins/fitting/src -c Release` lays out `artifacts/plugins/fitting`.

## Installing a plugin into your user folder

To make a plugin available to every engine on the machine (another CLI build, a Python environment, an
OpticStudio extension), install it into the user plugin folder:

```bash
ultrafast plugins install artifacts/plugins/fitting      # a plugin folder
ultrafast plugins install my-plugin-1.0.zip              # or a zip of one
ultrafast plugins paths                                  # where plugins are looked for
ultrafast plugins remove my-plugin
```

`install` copies the folder to `~/.ultrafast/plugins/<id>`. Set `ULTRAFAST_HOME` to put it elsewhere, in which case
the folder is `$ULTRAFAST_HOME/plugins`. Installing a newer version of a plugin replaces the older copy.

**Without copying:** point `ULTRAFAST_PLUGIN_PATH` at one or more folders that contain plugin folders. Separate
them with `:` on macOS and Linux, `;` on Windows. This is the convenient way to work on a plugin you are writing:

```bash
export ULTRAFAST_PLUGIN_PATH=$HOME/src/my-plugins/artifacts/plugins
```

## Where plugins are found

The search order is below. The first folder that provides a plugin id wins, so a plugin on `ULTRAFAST_PLUGIN_PATH`
overrides the copy that ships with the engine.

| | folder |
|---|---|
| 1 | `ULTRAFAST_PLUGIN_PATH` |
| 2 | `plugins/` next to the engine assembly (the CLI's folder; `ultrafast/bin/hosted/plugins` in the Python package) |
| 3 | `artifacts/plugins` of a source checkout (the Python package used from the checkout) |
| 4 | the user folder: `$ULTRAFAST_HOME/plugins`, default `~/.ultrafast/plugins` |

`ULTRAFAST_NO_PLUGINS=1` runs the bare core, which is useful to check that a problem is not caused by a plugin.

## Python

The Python package imports the Python module of every loaded plugin that declares one (`"python": {"module": ...}`
in `plugin.json`), from the plugin's `python/` folder. Its figures join `ultrafast-plot` and its builders become
available:

```python
fx = uf.plugins.module("fitting")     # also importable as `import ultrafast_fitting` once the engine has loaded it
```

The plugin's Python module needs nothing beyond the `ultrafast` package (NumPy, and matplotlib for figures).

## Troubleshooting

| symptom | cause and fix |
|---|---|
| `ultrafast plugins list` shows the plugin with an error `needs engine X or newer` | the plugin was built for a newer engine: update the engine or use a matching plugin version |
| `no assembly for net48` (or `net10.0`) | the plugin was built for one framework only: the CLI and Python run on .NET 10, the OpticStudio extension on .NET Framework 4.8; build the plugin for both (`<TargetFrameworks>net10.0;net48</TargetFrameworks>`) |
| `plugin id 'x' does not match plugin.json id 'y'` | `IPlugin.Id` and the manifest disagree: make them equal |
| a run spec with `"fit"` or `"backward"` stops with "needs the fitting plugin" | the fitting plugin is not found: run `ultrafast plugins paths` and check that one of those folders contains `fitting/plugin.json` |
| plugins do not load in the C library | NativeAOT builds cannot load assemblies at run time: register plugins in code (`PluginManager.Register(new FittingPlugin())`) and compile them in |
| a plugin crashes on load | it is listed with its error and otherwise ignored; the engine keeps running |

## Distributing your own plugin

Lay it out as described in [Plugin system](../reference/plugins.md#plugin-folder-layout): `plugin.json`, `lib/net10.0`
and `lib/net48`, optional `python/` and a `README.md`. If you import `build/Plugin.targets` (as the first-party
plugins do), `dotnet build` produces this layout in `artifacts/plugins/<id>`. Zip that folder and users install it
with `ultrafast plugins install your-plugin.zip`. Writing a plugin (elements, instruments, models, run modes,
transform operators, estimators, commands) is covered in [Plugin system](../reference/plugins.md) and
[Extending](../reference/extending.md).
