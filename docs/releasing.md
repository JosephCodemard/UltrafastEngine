# Releasing: compiled packages, release assets and the installer

`build.sh` (macOS / Linux) and `build.bat` (Windows) compile UltrafastEngine and lay out release folders you can
copy to another computer: the programs and libraries you choose, the plugins you choose, no source code. Both call
`build/package.py`.

```bash
./build.sh                                               # CLI for this machine, all plugins
./build.sh --cli --python --cpp --csharp --plugins fitting zemax gpu hitran --platform osx-arm64 win-x64 linux-x64
./build.sh --all --plugins none --platform host          # everything, core only
./build.sh --list                                        # platforms, plugins, what this machine can build
```

```bat
build.bat --cli --python --plugins transform zemax --platform win64 x86 --zos-api-dir "C:\Program Files\Ansys Zemax OpticStudio 2025 R1"
```

Requirements: the .NET 10 SDK and Python 3.9+ (only the standard library; the wheel is written directly). The
first build for a platform other than this machine's downloads that platform's .NET runtime pack from nuget.org.

## A release is the whole repository, in pieces you can take separately

A release on GitHub has three kinds of file: the **source archive** (the full repository), one **asset per component and platform**
(`cli-2.2.0-osx-arm64.tar.gz`, `python-2.2.0.tar.gz`, `examples.Branched-2.2.0.tar.gz` ...), and `manifest.json` (every asset with its size,
SHA-256, the platform and what it needs) with **`install.py`**. GitHub cannot serve part of an asset, so the pieces are separate assets;
for source folders `install.py` can instead use git's partial clone (below). Maintainers build the assets; users run `install.py`.

### Components

A name selects the component and all its children (`examples` is every `examples.<Group>`; `plugins` every plugin). `all` is everything built for
the platform **except** `media` and `python-native`, which must be named. Dependencies come along (shown by `--list`).

| name | contents | needs |
|---|---|---|
| `cli` (per platform) | the `ultrafast` program, self-contained, in `bin/` | |
| `plugins.<id>` (`fitting`, `zemax`, `gpu`, `ao`, `twin`, `hitran`) | the plugin folder in `bin/plugins/<id>` | `cli`; `plugins.twin` also `plugins.fitting` |
| `python` | the hosted-engine wheel, `pip install`ed by the installer | .NET 10 runtime at run time |
| `python-native` (per platform) | the wheel with the NativeAOT engine (no plugins) | conflicts with `python`; counts as `python` for dependencies |
| `cpp` (per platform) | C API: header, native library, CMake example | |
| `csharp` | `UltrafastEngine.Core.dll` for net10.0 and net48, plugin assemblies | |
| `zemax` (win-x64) | the OpticStudio extension, built only where your ZOS-API is found | |
| `schemas`, `docs` | JSON schemas; the Markdown documentation (not examples, papers, design notes) | |
| `examples.<Group>`, `examples.shared` | `docs/examples/<Group>/`; the helper files in `docs/examples/` that every group imports | `examples.shared`, `python` |
| `media` | the figures and animations (png, gif, svg) under `docs/examples` and `docs/papers` | |
| `tests`, `component-tests` | test sources and data (C#, Python), no binaries; `tests/components` | `python` |
| `papers` | `docs/papers` (replications; never the licensed PDFs) | |
| `source` | every tracked file of the repository | |

A name deeper than a component picks part of it: `examples.Branched.pump-probe` installs that example, plus the files that sit directly in
`docs/examples/Branched/` (the group's helpers) and, through `examples.shared`, the shared ones. Source components use the repository paths
(`docs/examples/...`), so a release install and a git checkout give the same tree.

### install.py

Standard library only; it is in the repository and attached to every release.

```bash
python install.py --list                                                  # components, sizes for this platform, what is installed
python install.py --include python tests examples.Branched --dest ~/uf    # only these (and what they need)
python install.py --include examples.Branched.pump-probe --dest ~/uf      # adds to the same folder
python install.py --include all --exclude media --version 2.2.0
python install.py --include docs examples.Fibres --source git             # source folders by git partial clone (see below)
python install.py --uninstall --include tests --dest ~/uf                 # remove part ...
python install.py --uninstall --dest ~/uf                                 # ... or all
```

| option | meaning |
|---|---|
| `--include`, `--exclude` | component names; no `--include` is an error (the installer never guesses) |
| `--version X`, `--latest` | the release (default: latest); `--base-url` reads `manifest.json` and the assets from another folder or URL (`file://` too) |
| `--platform auto` | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`; a component without an asset for it is skipped under `all`, an error when you name it |
| `--dest DIR` | default `./UltrafastEngine` |
| `--source release\|git` | release assets (default), or `git clone --filter=blob:none --sparse --branch v<version>` and a cone-mode `sparse-checkout` of the folders of the selected source components |
| `--no-pip`, `--python EXE` | wheels are installed with `EXE -m pip install --find-links <dest>/python ultrafast-engine[hosted]` (never from PyPI; the wheel's dependencies are); `--no-pip` only unpacks them |
| `--dry-run`, `--force` | show what would happen; overwrite or remove files even if changed |

What it does: it downloads **only** the chosen assets and checks each SHA-256 before anything is written (a mismatch installs nothing), unpacks them,
and writes `<dest>/.ultrafast-install.json` with every file, its hash, the component and the version. Therefore:

* **Uninstall removes exactly those files** and the folders it emptied; a file you changed is left in place with a warning, a file it did not install is never touched
  (an existing file that is not in the record is not overwritten either, without `--force`). Removing a component that another installed one needs is refused.
* A second `--include` **adds** to an install; installing a newer `--version` of a component **upgrades** it (the old unmodified files are removed first).
* The ZOS-API is never installed, removed or packaged; an archive that holds one is refused.
* With `--source git` the tag pins the version. Cone mode brings the files in the parent folders of a selected folder (that is how one example gets its shared helpers), and
  figures cannot be separated from their folders, so `media` has no effect there. Uninstall shrinks the sparse set; git keeps files with local changes.
  Built components (`cli`, wheels, `cpp` ...) are not in git: ask for them from the release.

### Building the assets

`build/package.py` takes the same names; the old flags are aliases (`--cli` = `--components cli`, also `--python --cpp --csharp --zemax --examples --docs`).

```bash
./build.sh --components cli python plugins schemas docs examples tests papers --platform linux-x64 osx-arm64 win-x64
./build.sh --components cpp python-native --platform osx-arm64      # NativeAOT: on the target OS (the owner's Mac for macOS)
./build.sh --components all media source --exclude papers --dry-run # the plan only
python build/release_components.py merge dist/release-2.2.0/manifest.json dist/a/manifest.json dist/b/manifest.json   # manifests of several jobs
python build/release_components.py check-zosapi dist/release-2.2.0   # fails if any archive or wheel holds a ZOSAPI file
```

Output: `dist/release-<version>/` with `<component>-<version>[-<platform>].tar.gz` (reproducible: sorted, fixed timestamps), `manifest.json`, `install.py`.
Source components are built from `git ls-files`, so untracked files, results folders and the licensed paper PDFs can not get in. The writer refuses a
`ZOSAPI*` file (the tracked `ZosApiStubs` sources are ours and allowed), and the packager checks every archive and every wheel inside one again at the end.
Built components reuse the steps of the single-folder packages above, so a platform-independent component is built once.

### The release workflow

`.github/workflows/release.yml` runs on a `v*` tag (or by hand, which only builds and tests). Job `managed` builds everything that is plain files or pure .NET, and cross-publishes
the CLI for win-x64, linux-x64, linux-arm64, osx-arm64 and osx-x64; job `native` builds `cpp` and `python-native` on Linux x64 / arm64 and Windows and runs the C example;
job `publish` merges the manifests, runs the ZOS-API gate, installs a selection into a temporary folder (`cli`, `schemas`, `examples.Branched`), runs the CLI and an example
in strict mode, uninstalls and checks the folder is empty, installs the wheel in a fresh virtual environment (`ultrafast.devices()`, the quickstart), and attaches the assets
to a **draft** release. The NativeAOT components for macOS are built on the owner's Mac and added with `gh release upload`. **A tag is created only when the owner asks.**
The workflow has not run on GitHub yet. Rehearsed by hand on macOS: the asset build, the manifest merge, the ZOS-API gate, and the installer install / run / uninstall steps. Not run: the Windows and Linux NativeAOT builds, the CMake example, the wheel in a fresh environment, the upload (see [session-f](design/notes/session-f.md)).

## Single-folder packages (`--cli --python ...`)

The original mode: one folder per platform with the components you choose, no source.

### Output

`dist/UltrafastEngine-<version>-<platform>/` plus `.zip` (Windows, portable) or `.tar.gz` (macOS, Linux, keeps the
executable bits):

| folder | contents | needs on the target computer |
|---|---|---|
| `bin/` | `ultrafast` command-line program, `bin/plugins/<id>/` | nothing (self-contained); `--framework-dependent`: the .NET 10 runtime |
| `python/` | `ultrafast_engine-<ver>-py3-none-any.whl` (hosted engine) or a platform wheel (native engine) | Python ≥ 3.10 + NumPy; hosted: the .NET 10 runtime and `clr_loader` (`pip install "...whl[hosted]"`) |
| `cpp/` | `include/ultrafast.h`, `lib/` (`ultrafast.dll` + `ultrafast.lib` / `libultrafast.so` / `libultrafast.dylib`), `example/` (CMake) | nothing (NativeAOT library) |
| `csharp/` | `lib/net10.0/` and `lib/net48/UltrafastEngine.Core.dll` (+ XML docs), `plugins/` | .NET 10 or .NET Framework 4.8 |
| `zemax/` | OpticStudio extension `UltrafastEngine.Zemax.exe` (Windows x64), `install-zemax.bat` | OpticStudio; run `install-zemax.bat` once |
| top level | `README.md` (what is inside, how to start), `LICENSE`, `NOTICE`, `CITATION.cff`, `CHANGELOG.md`, `schemas/`, `manifest.json` (every file with SHA-256), optional `examples/`, `docs/` | |

## Options

| option | meaning |
|---|---|
| `--cli`, `--python`, `--cpp`, `--csharp`, `--zemax`, `--all` | components (default: `--cli`) |
| `--plugins ID ...` | `fitting` (aliases `transform`, `fit`), `zemax`, `gpu` (aliases `cuda`, `opencl`), `all` (default), `none` |
| `--platform RID ...` | `win-x64` (`win64`), `win-x86` (`x86`, `win32`), `win-arm64`, `linux-x64` (`linux`), `linux-arm64`, `linux-musl-x64`, `osx-arm64` (`mac`), `osx-x64` (`mac-intel`), `portable` (framework-dependent, any OS), `host` (this machine; default) |
| `--framework-dependent` | the CLI uses an installed .NET 10 runtime (about 8 MB instead of about 75 MB) |
| `--single-file` | the CLI as one executable |
| `--python-engine hosted\|native` | hosted (default): one wheel for every OS, loads plugins, needs the .NET runtime. native: NativeAOT library in a platform wheel, nothing else needed, **no plugins** |
| `--zos-api-dir DIR` | your OpticStudio folder, to build the extension (the ZOS-API assemblies are never packaged) |
| `--examples`, `--docs` | include `docs/examples` and the documentation |
| `--examples-wheel` | build only `ultrafast_examples-<ver>-py3-none-any.whl`: the examples as package data and the `ultrafast-examples` command (`list`, `copy`, `run`). No .NET needed. Install it next to the engine wheel with `pip install --find-links <folder> ultrafast-examples` (also the extra `ultrafast-engine[examples]`); it is never uploaded to PyPI |
| `--symbols` | keep `.pdb` debug symbols |
| `--test` | run the C# tests first |
| `--out DIR`, `--no-archive`, `--version LABEL`, `--dry-run`, `-v` | output folder, no zip/tar, version label, print commands only, full build output |

## What can be built where

| component | built on | for |
|---|---|---|
| CLI, C#, hosted Python wheel | any OS | every platform (pure .NET; the self-contained CLI takes the target's runtime pack) |
| C API (`--cpp`), native Python wheel | the target OS | NativeAOT does not cross-compile between operating systems; build the macOS package on the Mac, the Windows one on Windows. Cross-architecture on the same OS works with the matching C toolchain. Windows needs Visual Studio's "Desktop development with C++"; Linux / macOS need clang |
| OpticStudio extension | Windows (or anywhere with your ZOS-API folder) | Windows x64 |

Steps that cannot run are skipped with a warning (listed at the end and in `manifest.json`); the rest of the
package is still built.

## Plugins in packages

The selected plugins go into `bin/plugins/`, the hosted wheel (`ultrafast/bin/hosted/plugins/`) and
`csharp/plugins/`. Users add others with `ultrafast plugins install <folder or zip>` (copies into
`~/.ultrafast/plugins`). The NativeAOT library (C API, native wheel) cannot load plugins at run time; the `gpu`,
`hitran` and `ao` plugins are compiled into it when selected (`-p:UF_WITH_GPU=false`, `UF_WITH_HITRAN=false`, `UF_WITH_AO=false` otherwise; the `twin` plugin is Python plus a CLI command and needs nothing). The GPU back-ends load the vendor
libraries (CUDA driver, NVRTC, OpenCL) from the target computer at run time; nothing NVIDIA-owned is packaged.

## The ZOS-API rule

`ZOSAPI.dll`, `ZOSAPI_Interfaces.dll` and `ZOSAPI_NetHelper.dll` belong to Ansys. The packager never copies any
`ZOSAPI*` file into a package. The extension finds `ZOSAPI.dll` / `ZOSAPI_Interfaces.dll` in the user's OpticStudio
installation at run time; `install-zemax.bat` copies the `ZOSAPI_NetHelper.dll` of the user's own installation next to
the program. Check a package with `find dist -iname "ZOSAPI*"` (no output).

## Checking a package

```bash
dist/UltrafastEngine-*/bin/ultrafast plugins list
dist/UltrafastEngine-*/bin/ultrafast run docs/examples/GettingStarted/spm-plate/plate.ufs docs/examples/GettingStarted/spm-plate/run.json -o /tmp/r
python3 -m venv /tmp/v && /tmp/v/bin/pip install "dist/UltrafastEngine-*/python/*.whl[hosted]" && /tmp/v/bin/python -c "import ultrafast; print(ultrafast.version())"
cmake -S dist/UltrafastEngine-*/cpp/example -B /tmp/c -DULTRAFAST_DIR=$PWD/dist/UltrafastEngine-*/cpp && cmake --build /tmp/c
```

In Claude Code, `/release <options>` runs the packager and these checks.
