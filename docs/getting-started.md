# Getting started

## Repository layout

```
UltrafastEngine.sln                  Visual Studio 2026 solution (or `dotnet build`; see Requirements)
src/UltrafastEngine.Core             the physics engine (net10.0 + net48, no dependencies)
src/UltrafastEngine.Native           C API exports (+ include/ultrafast.h); NativeAOT -> ultrafast.dll/.so/.dylib
src/UltrafastEngine.Cli              `ultrafast` command line (Windows/macOS/Linux)
plugins/fitting                      fitting plugin: measured data and calibration (C# + Python module)
plugins/zemax                        zemax plugin: OpticStudio sampling (any OS), the OpticStudio program (Windows), ZOS-API stubs
schemas/                             JSON Schemas of system files and run specs (editor completion)
build/                               shared MSBuild logic (plugin layout, Mono reference assemblies)
artifacts/plugins                    installable plugin folders, laid out by every build
tests/UltrafastEngine.Tests          test runner (console app, no NuGet packages)
python/                              the `ultrafast-engine` Python package (+ tests incl. pyNLO cross-check)
docs/examples/                            runnable examples (system files, run specs, Python, C++)
docs/                                this documentation (MkDocs: `mkdocs serve`)
```

## Requirements

* **.NET 10 SDK** (10.0.100 or later; `global.json` rolls forward to newer 10.0 feature bands). .NET 10 is the
  current LTS release (supported to Nov 2028); .NET 8 support ends in Nov 2026, so it is not targeted.
* Windows: **Visual Studio 2026** (the version that supports .NET 10) with the **.NET desktop development** workload (and **Desktop development with
  C++** for the NativeAOT library and the C++ example).
* The Zemax binding additionally needs **.NET Framework 4.8** (in Windows) and **OpticStudio Professional or
  Premium** (ZOS-API licence).
* Python 3.10+ with NumPy for the Python package; `clr_loader` for the hosted (development) mode.

No NuGet packages are needed for building and testing; `nuget.config` lists only nuget.org, which is used
solely by the NativeAOT publish (it downloads the runtime packs and the ILCompiler on first use).

**Visual Studio 2022?** VS 2022 does not officially support .NET 10 projects. Use VS 2026, or build from the
command line (`dotnet build UltrafastEngine.sln`), which works with any editor. The .NET Framework 4.8 targets
(Zemax binding and the net48 builds of the core) need the .NET Framework 4.8 developer/targeting pack, which the
.NET desktop workload installs.

## Visual Studio (Windows)

1. Open `UltrafastEngine.sln`. Build (Ctrl+Shift+B).
2. Set **UltrafastEngine.Tests** as start-up project and run (Ctrl+F5): all tests should pass.
3. Set **UltrafastEngine.Cli** as start-up project; in *Debug properties* use the arguments
   `run ..\..\examples\01-spm-plate\plate.ufs ..\..\examples\01-spm-plate\run.json -o results`.
4. **Zemax binding**: the project compiles against the ZOS-API assemblies in `plugins/zemax/src/UltrafastEngine.Zemax/lib`
   (`ZOSAPI.dll`, `ZOSAPI_Interfaces.dll`, `ZOSAPI_NetHelper.dll`; git-ignored, as they are Ansys property) or in `C:\Program Files\Ansys Zemax OpticStudio 2025 R1`;
   set the MSBuild property `ZosApiDir` for another location. Without ZOS-API it builds against stubs (with a
   warning) so the rest of the solution still builds. See [Zemax plugin](plugins/zemax.md).
5. **Native library** (C/C++/Python without .NET):
   `dotnet publish src\UltrafastEngine.Native -c Release -r win-x64 -o artifacts\native\win-x64`.

## macOS / Linux

```bash
dotnet build UltrafastEngine.sln                      # everything (the Zemax binding against stubs)
dotnet run --project tests/UltrafastEngine.Tests -f net10.0
dotnet run --project src/UltrafastEngine.Cli -- run docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json -o results
```

The .NET Framework 4.8 targets (Core, tests, Zemax binding) build on macOS/Linux when Mono is installed
(its reference assemblies are used automatically); the net48 test runner then runs with `mono`. They are not
needed for normal work on a Mac.

A convenient alias: `dotnet publish src/UltrafastEngine.Cli -c Release -o ~/bin/ultrafast-cli` and put that on
your `PATH` as `ultrafast`.

## Python

```bash
pip install -e "python[hosted,plot,test]"
python python/tools/build_engine.py            # hosted build (needs the .NET 10 SDK; uses clr_loader at run time)
python python/tools/build_engine.py --aot      # or: NativeAOT library, no .NET needed at run time
python -m pytest python/tests
python docs/examples/python/quickstart.py
```

See [Python](reference/python.md).

## Your first run

```bash
ultrafast run docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json -o results
```

`results/` then holds `summary.json` (everything scalar, per element and per instrument), `result.npz`
(every array; `numpy.load`), `provenance.json`, `elements.csv`, `spectrum.csv`, `temporal.csv` and one CSV per
instrument. `ultrafast validate <system.ufs>` checks a system without running it, `ultrafast describe` lists
what the engine will do with each element, and `ultrafast template run.json` writes a commented run spec.

Next: the [tutorials](tutorials/index.md) walk you from this first run to diagnostics, larger systems and
comparing with your own measured data. Then the examples in `docs/examples/` (README there), e.g. `docs/examples/08-shg-lbo` → `docs/examples/09-nopcpa-bbo` for frequency
conversion and OPCPA, and `ultrafast crystal BBO --process shg --fundamental 1030e-9` for the phase-matching
calculator. Plot any results folder with `ultrafast-plot results`.
