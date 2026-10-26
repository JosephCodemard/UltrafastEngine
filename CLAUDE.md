# CLAUDE.md -- UltrafastEngine

Project memory for Claude Code. Loaded at the start of every session; keep it short. Detail lives in the
folder-level `CLAUDE.md` files (loaded only when you work in that folder), in the skills under `.claude/skills/`
(loaded only when relevant) and in `docs/`. Guide for humans: [docs/claude-code.md](docs/claude-code.md).

@AGENTS.md

## Ground rules (from the owner)

* Edit the repository directly; never hand over zip files of source.
* **Never overwrite, modify, copy into a package or redistribute the ZOS-API DLLs** (`ZOSAPI*.dll`, Ansys property).
  A hook blocks edits under `plugins/zemax/src/UltrafastEngine.Zemax/lib/`; the packager filters them out.
* Every change comes with tests and docs, written for humans and for AI agents. Keep everything modular.
* Python must keep full autocompletion: builders return the generated TypedDicts (`ultrafast.types`).
* Must run on Apple silicon (arm64 macOS), Windows and Linux; .NET 10 and .NET Framework 4.8 (OpticStudio).
* Physical accuracy first: a physics change needs an analytic or published reference value in a test.
* Plain, precise English in docs and messages. SI units with the unit in the key name.

## Commands (fast path first)

```bash
dotnet build UltrafastEngine.sln -c Release                                  # everything (~1 min)
dotnet build tests/UltrafastEngine.Tests -c Release -f net10.0               # just the tests + what they need
dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll NerTests Measured   # filter: substring of Class.Method
dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --slow              # all 213 (~4 min)
python3 python/tools/build_engine.py        # after ANY C# or plugin-Python change, before Python tests
cd python && PYNLO_SRC=$PWD/third_party/pynlo/src ULTRAFAST_SLOW=1 python3 -m pytest -q tests   # (get pyNLO: python3 tools/get_pynlo.py)
python3 -m mkdocs build --strict            # docs: 0 warnings required
./build.sh --help                           # release packages (docs/releasing.md)
```

Slash commands do the multi-step jobs: `/test`, `/regen`, `/verify`, `/physics-change`, `/new-option`,
`/bug-report`, `/release`, `/example-check`. The `test-runner` subagent runs long builds/tests and returns only
failures (keeps your context small); `physics-reviewer` audits a diff for physics/units; `docs-sync` updates docs
after a code change.

## Token economy

* **Do not read generated files** -- they are large and derived: `schemas/*.json` (440 kB), `python/src/ultrafast/types.py`
  (180 kB), `docs/reference/options.md` (95 kB), `docs/reference/python-api.md`. Read the source instead
  (`src/UltrafastEngine.Core/Schema/BuiltIn*.cs`, `python/src/ultrafast/build.py`) and regenerate with `/regen`.
  Edits to them are blocked by a hook.
* Never read `bin/`, `obj/`, `artifacts/`, `dist/`, `site/`, `results*/`, `*.npz`, `CHANGELOG.md` history.
* Grep before reading; read line ranges of the big files (`Engine/Compiler.cs` 1.3k lines, `Physics/Nonlinear.cs`,
  `Instruments/Instruments.cs`, `Crystals/CrystalElement.cs`).
* Run the test filter for what you touched, then the full suite once at the end (or hand it to `test-runner`).
* Test output is long: pipe through `grep -E "FAIL|passed"` and `grep -v "^   at"`.

## Where to look

| task | start at |
|---|---|
| how the engine works | `docs/concepts.md` (units, sign conventions, compile → execute, grids) |
| physics equations, solvers, NER | `docs/reference/nonlinear.md`; skill `engine-physics` |
| an option / element / instrument | `Schema/BuiltIn*.cs` (declaration) → `Engine/Compiler*.cs` (compile) → `Physics/` or `Instruments/` |
| measured data (input spectra, comparisons) | `Pulses/MeasuredSpectrumPreparation.cs`, `Measured/Measurements.cs`, `docs/reference/measured-data.md` |
| grid sizing, satellites | `Engine/GridPlanner.cs`, `Compiler.SatelliteRoom` |
| driving the engine (writing .ufs / run.json) | `docs/agents.md`; skill `run-simulations` |
| Python API | `python/CLAUDE.md` |
| plugins (fitting, zemax, gpu) | `plugins/CLAUDE.md` |
| speed: CPU FFT providers, compute back-ends, GPU | `docs/reference/speed.md`, `docs/plugins/gpu.md`; tests `ComputeTests`, `GpuTests` |
| tests | `tests/UltrafastEngine.Tests/CLAUDE.md` |
| status / what is verified | `PROJECT_STATUS.md`; history in `CHANGELOG.md` (top section only) |

## Definition of done

1. Code builds for net10.0 and net48 (`dotnet build UltrafastEngine.sln -c Release`, 0 errors).
2. New behaviour has a test (analytic or published reference for physics); the full C# suite passes; Python tests
   pass after `build_engine.py`; pyNLO cross-checks still pass if propagation changed.
3. Generated files regenerated (`/regen`) if any option, element, instrument or Python builder changed.
4. Docs: reference page section, `CHANGELOG.md` (Unreleased), `docs/testing.md` row for new tests,
   `PROJECT_STATUS.md` counts; example READMEs re-checked if their numbers change (`/example-check`).
5. `mkdocs build --strict` with 0 warnings; examples run with `ULTRAFAST_STRICT_OPTIONS=1`.
