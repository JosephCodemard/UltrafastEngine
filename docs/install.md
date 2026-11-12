# Install and build

Pick the row that matches what you want. Every command below is run from a terminal; on Windows use `setup.bat` where the
table says `./setup.sh`.

| I want ... | Do this | Needs |
|---|---|---|
| to **use** the CLI, Python package or plugins, without compiling | [Install a release](#install-a-release-no-compiling) (`install.py`) | Python 3.9+, internet |
| to **build from source** and run the CLI | `./setup.sh --cli` | .NET 10 SDK, Python 3.10+ |
| the **Python package** from source | `./setup.sh --python --venv .venv` | the same |
| only **some plugins** | `./setup.sh --cli --python --plugins fitting hitran` | the same |
| the **C / C++ library** | `./setup.sh --cpp` | the same + a C toolchain (clang, gcc or MSVC) |
| to **develop** the engine | [Developing](#developing-the-engine) | the same |
| **release packages** for other computers | `./build.sh` ([Releasing](releasing.md)) | the same |

Check what your machine has with `./setup.sh --doctor`.

!!! tip "For AI agents"
    1. `python3 build/setup.py --doctor` shows which tools are missing; install the .NET 10 SDK first if it is.
    2. `python3 build/setup.py --cli --python --venv .venv --check` builds everything and smoke-tests it (about a minute).
    3. Run `.venv/bin/python docs/examples/GettingStarted/quickstart/quickstart.py` (Windows: `.venv\Scripts\python.exe`).
    4. If `import ultrafast` runs an old engine, a stale NativeAOT library shadows the new build: rerun with `--clean-native`.
    5. Never touch `ZOSAPI*.dll`. Do not publish anywhere.

## Install a release (no compiling)

`install.py` uses only the Python standard library. Download it from the [releases page](https://github.com/JosephCodemard/UltrafastEngine/releases)
(or from the repository) and choose parts:

```bash
python install.py --list                         # components, their sizes, what is installed
python install.py --preset cli                   # just the `ultrafast` program
python install.py --preset cli-plugins           # the program and every plugin
python install.py --preset python                # the Python package (needs the .NET 10 runtime)
python install.py --preset python-native         # the Python package with the NativeAOT engine (no .NET needed, no plugin folders)
python install.py --preset full                  # program, plugins, Python package, schemas, docs, examples
python install.py --include cli plugins.fitting python examples.GettingStarted --dest ~/uf   # pick exactly
python install.py --uninstall --include examples                                             # remove part (or all)
```

| preset | components |
|---|---|
| `cli` | `cli` |
| `cli-plugins` | `cli`, `plugins` |
| `python` | `python` |
| `python-native` | `python-native` |
| `cpp` | `cpp` |
| `full` | `cli`, `plugins`, `python`, `schemas`, `docs`, `examples` |

A preset is shorthand for `--include`; you can combine them. A second run adds to the same folder, `--version X` upgrades it, and
`--uninstall` removes exactly the files it wrote. The full component list and the release layout are in [Releasing](releasing.md).
The Python wheel is installed from the downloaded file, never from PyPI (its dependencies, `numpy`, are).

## Build from source

You need the **.NET 10 SDK** (`dotnet --version` starts with `10.`) and **Python 3.10+**. On Windows, Visual Studio 2026 with the
*.NET desktop development* workload also works (see [Getting started](getting-started.md#visual-studio-windows)).

```bash
git clone https://github.com/JosephCodemard/UltrafastEngine.git && cd UltrafastEngine
./setup.sh --cli --python --venv .venv --check
```

`build/setup.py` (run by `setup.sh` / `setup.bat`) is the one entry point. It builds what you name, nothing else:

| option | builds | result |
|---|---|---|
| `--cli` (default) | the `ultrafast` program and the chosen plugins | `artifacts/cli/<platform>/ultrafast` |
| `--python` | the engine for Python, then `pip install -e python` | `python/src/ultrafast/bin/` |
| `--python-engine native` | the NativeAOT engine instead (no .NET at run time; `gpu`, `hitran`, `ao` are compiled in) | `python/src/ultrafast/bin/native/` |
| `--cpp` | the C API library | `artifacts/native/<platform>/`, header `src/UltrafastEngine.Native/include/ultrafast.h` |
| `--plugins A B` | only these plugins, plus the ones they require (`twin` needs `fitting`); `all` (default) or `none` | in the CLI / Python build |
| `--plugin-only A` | rebuild one plugin and refresh it in the builds you already made | |
| `--venv DIR` | create / use a virtual environment for the Python package | |
| `--check` | smoke test: the CLI runs, `import ultrafast` works, plugins are listed | |
| `--net48` | also build the plugins for .NET Framework 4.8 (OpticStudio; Mono is needed on macOS / Linux) | |
| `--clean-native` | delete an older NativeAOT library that would shadow the hosted Python engine | |
| `--dry-run` | print the commands only | |

Add the CLI to your path: `export PATH="$PWD/artifacts/cli/osx-arm64:$PATH"` (use your platform name; `--doctor` prints it).

### Plugins

A plugin is a folder (`plugin.json`, assemblies, optional Python module). Building one lays it out in `artifacts/plugins/<id>/`;
nothing is registered globally.

```bash
./setup.sh --cli --plugins fitting hitran            # a CLI with these two only
./setup.sh --plugin-only gpu                         # rebuild one plugin and refresh it in artifacts/cli and the Python build
dotnet build plugins/fitting/src/UltrafastEngine.Plugins.Fitting.csproj -c Release -f net10.0   # the same by hand
ultrafast plugins install artifacts/plugins/fitting  # copy into your user plugin folder (~/.ultrafast/plugins)
```

How the engine finds plugins, and how to write one: [Installing plugins](plugins/installing.md), [Plugin API](reference/plugins.md).
Two details that surprise people:

* In a source checkout the Python package also scans `artifacts/plugins/`, so `uf.plugins.installed()` lists every plugin you ever built;
  the engine itself loads those in `bin/hosted/plugins/` (hosted) or the ones compiled in (NativeAOT: `gpu`, `hitran`, `ao`).
* The **zemax** plugin never contains the ZOS-API: its OpticStudio extension compiles against your own OpticStudio installation
  ([Zemax plugin](plugins/zemax.md)).

### Python: hosted or native engine

The Python package loads the engine as a native library. Two builds exist:

| | hosted (default) | native (`--python-engine native`) |
|---|---|---|
| needs at run time | the .NET 10 runtime and `clr_loader` | nothing |
| plugins | all, as folders | `gpu`, `hitran`, `ao` compiled in |
| build | any OS, fast | a C toolchain, slower |
| use it for | development, all plugins | the pip wheel, sharing |

`import ultrafast` tries `bin/native` first. If you switch from native to hosted, an old `bin/native` library keeps winning;
`setup.py` warns and `--clean-native` removes it (or set `ULTRAFAST_LIBRARY` to the library you want).
Python-side details: [Python](reference/python.md).

### Pip

A pip-installable wheel is a release artifact: `./build.sh --python` (hosted) or `--python-engine native` writes
`dist/.../ultrafast_engine-<version>-py3-none-any.whl`, and `install.py --preset python` installs it. The metadata and extras
(`hosted`, `plot`, `animate`, `test`) are ready; **nothing is uploaded to PyPI** until the owner decides to release.

## Developing the engine

```bash
./setup.sh --cli --python --venv .venv --dev         # builds, installs pytest too
dotnet build UltrafastEngine.sln -c Release          # everything incl. all plugins (net10.0 + net48; ~1 min)
dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll PulseCalculator   # filter by class / method
python3 python/tools/build_engine.py                 # after ANY C# or plugin-Python change, before Python tests
.venv/bin/python -m pytest -q python/tests
python3 -m mkdocs build --strict
```

More: [Testing](testing.md), [Releasing](releasing.md), `CONTRIBUTING.md`.

## Troubleshooting

| symptom | cause and fix |
|---|---|
| `.NET 10 SDK is required` | install it from dotnet.microsoft.com; `global.json` pins 10.0.x |
| `import ultrafast` fails to find a library | no engine is built: `./setup.sh --python`; or set `ULTRAFAST_LIBRARY` |
| Python runs an old engine (`uf.version()` is not the repository version), or a function is missing, or `dlsym ... symbol not found` | a stale NativeAOT library `python/src/ultrafast/bin/native` is loaded before your new build: `./setup.sh --python --clean-native` |
| `uf.plugins.installed()` lists plugins you did not build | the checkout's `artifacts/plugins` is scanned; delete the folders you do not want |
| net48 build errors on macOS / Linux | install Mono, or skip net48 (the default for `setup.py`) |
| NativeAOT publish fails | install a C toolchain (Xcode command line tools, `build-essential`, MSVC) |
