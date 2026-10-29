# Releasing: compiled packages without the source

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

## Output

`dist/UltrafastEngine-<version>-<platform>/` plus `.zip` (Windows, portable) or `.tar.gz` (macOS, Linux, keeps the
executable bits):

| folder | contents | needs on the target computer |
|---|---|---|
| `bin/` | `ultrafast` command-line program, `bin/plugins/<id>/` | nothing (self-contained); `--framework-dependent`: the .NET 10 runtime |
| `python/` | `ultrafast_engine-<ver>-py3-none-any.whl` (hosted engine) or a platform wheel (native engine) | Python ≥ 3.10 + NumPy; hosted: the .NET 10 runtime and `clr_loader` (`pip install "...whl[hosted]"`) |
| `cpp/` | `include/ultrafast.h`, `lib/` (`ultrafast.dll` + `ultrafast.lib` / `libultrafast.so` / `libultrafast.dylib`), `example/` (CMake) | nothing (NativeAOT library) |
| `csharp/` | `lib/net10.0/` and `lib/net48/UltrafastEngine.Core.dll` (+ XML docs), `plugins/` | .NET 10 or .NET Framework 4.8 |
| `zemax/` | OpticStudio extension `UltrafastEngine.Zemax.exe` (Windows x64), `install-zemax.bat` | OpticStudio; run `install-zemax.bat` once |
| top level | `README.md` (what is inside, how to start), `LICENSE`, `CHANGELOG.md`, `schemas/`, `manifest.json` (every file with SHA-256), optional `examples/`, `docs/` | |

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
`~/.ultrafast/plugins`). The NativeAOT library (C API, native wheel) cannot load plugins at run time; the `gpu`
plugin is compiled into it when selected (`-p:UF_WITH_GPU=false` otherwise). The GPU back-ends load the vendor
libraries (CUDA driver, NVRTC, OpenCL) from the target computer at run time; nothing NVIDIA-owned is packaged.

## The ZOS-API rule

`ZOSAPI.dll`, `ZOSAPI_Interfaces.dll` and `ZOSAPI_NetHelper.dll` belong to Ansys. The packager never copies any
`ZOSAPI*` file into a package. The extension finds `ZOSAPI.dll` / `ZOSAPI_Interfaces.dll` in the user's OpticStudio
installation at run time; `install-zemax.bat` copies the `ZOSAPI_NetHelper.dll` of the user's own installation next to
the program. Check a package with `find dist -iname "ZOSAPI*"` (no output).

## Checking a package

```bash
dist/UltrafastEngine-*/bin/ultrafast plugins list
dist/UltrafastEngine-*/bin/ultrafast run docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json -o /tmp/r
python3 -m venv /tmp/v && /tmp/v/bin/pip install "dist/UltrafastEngine-*/python/*.whl[hosted]" && /tmp/v/bin/python -c "import ultrafast; print(ultrafast.version())"
cmake -S dist/UltrafastEngine-*/cpp/example -B /tmp/c -DULTRAFAST_DIR=$PWD/dist/UltrafastEngine-*/cpp && cmake --build /tmp/c
```

In Claude Code, `/release <options>` runs the packager and these checks.
