# AGENTS.md

Instructions for AI coding agents working in this repository. Full guide: [docs/agents.md](docs/agents.md);
documentation index: [llms.txt](llms.txt).

## What this is

UltrafastEngine 2.1.8-beta: a C# engine for nonlinear ultrafast pulse propagation through optical systems, with a
CLI (`ultrafast`), a C API, a Python package (`ultrafast`) and plugins (fitting, zemax, gpu). Inputs are a system file
(`.ufs`) and a run spec (`run.json`), both JSON with comments; outputs are `summary.json`, `result.npz`, CSVs.

## Build and test

```bash
dotnet build UltrafastEngine.sln -c Release
dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -c Release            # C# tests (-- --slow: all; -- Name: filter)
python3 python/tools/build_engine.py && python3 -m pytest python/tests             # Python
python3 -m mkdocs build --strict                                                   # docs
./build.sh --help                                                                  # release packages (docs/releasing.md)
```

Claude Code users: `CLAUDE.md` (imports this file), folder `CLAUDE.md` files, and `.claude/` (commands, subagents,
skills, hooks) -- see [docs/claude-code.md](docs/claude-code.md).

## Rules

* No NuGet packages. The core targets `net10.0` and `net48`: guard newer APIs with `#if NET`.
* Every element type and option is declared in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs`. After changing one,
  regenerate (tests check all five):
  `ultrafast schema system -o schemas/ufs.schema.json`, `ultrafast schema run -o schemas/run.schema.json`,
  `ultrafast reference -o docs/reference/options.md`, `python3 python/tools/gen_types.py`,
  `python3 python/tools/gen_api_docs.py`.
* SI units, with the unit in the key name (`lengthM`, `energyJ`, `gddFs2`).
* Renamed keys keep working through migrations (`src/UltrafastEngine.Core/Schema/Migrations.cs`) with a deprecation
  warning; do not remove old names.
* New physics comes with a test against an analytic result or a published value, and a docs page section.
* Examples live in `docs/examples/`; they must run with `ULTRAFAST_STRICT_OPTIONS=1` (unknown keys are errors).
* Do not commit `bin/`, `obj/`, `artifacts/`, `site/`. Never overwrite, modify or redistribute the ZOS-API DLLs in
  `plugins/zemax/src/UltrafastEngine.Zemax/lib/` (Ansys property, supplied by the user's OpticStudio installation).

## Where things are

| | |
|---|---|
| engine | `src/UltrafastEngine.Core` (`Engine/` compiler, executor, planner, studies; `Physics/`, `Optics/`, `Materials/`, `Instruments/`, `Retrieval/`, `Numerics/`) |
| CLI / C API | `src/UltrafastEngine.Cli`, `src/UltrafastEngine.Native` (`include/ultrafast.h`) |
| Python | `python/src/ultrafast` (`build.py` builders, `types.py` generated TypedDicts) |
| plugins | `plugins/fitting`, `plugins/zemax`, `plugins/gpu` (CUDA / OpenCL back-ends) |
| docs | `docs/` (MkDocs), reference in `docs/reference/` |
| synthetic test data with answers | `docs/assets/` |
