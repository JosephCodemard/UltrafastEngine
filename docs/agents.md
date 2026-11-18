# For AI agents

This page is for an AI agent (or a person scripting one) that drives UltrafastEngine: writing system files and run
specs, running them, reading results, and changing the code. The short version is in `AGENTS.md` at the repository
root and `llms.txt` (an index of the documentation).

## The model in one paragraph

A **system file** (`.ufs`, JSON with comments) lists elements in beam order (or, in a Branched system, with the link `next` from each element): `bulk`, `gap`, `plate`, `mirror`,
`lens`, `gratingPair`, `crystal`, `gain`, ... ([supported elements](capabilities.md#supported-elements)) A **run spec** (`run.json`) gives the input pulse, physics switches,
instruments and overrides. The engine compiles both into linear and nonlinear operators on a frequency grid that it
sizes itself, propagates the pulse, and writes `summary.json` (scalars), `result.npz` (arrays), CSVs and
`provenance.json` (where every number came from). Every option has a documented default; nothing is guessed silently
-- unknown keys produce warnings with suggestions.

## Discover before you write

| question | ask the engine |
|---|---|
| every element type, option, unit, default | `docs/reference/options.md`, or `ultrafast schema system` / `ultrafast schema run` (JSON Schema) |
| what is built in (materials, crystals, models, element types, instruments) | `ultrafast info` (JSON; Python `uf.capabilities()`) |
| materials and refractiveindex.info pages | `ultrafast materials list`, `ultrafast materials show <name>` |
| is my file valid | `ultrafast validate system.ufs run.json` (exit code ≠ 0 on errors) |
| a commented starting point | `ultrafast template run.json`; system files: the [examples](examples/README.md) |
| what the engine will do with each element | `ultrafast describe system.ufs` |
| Python signatures | `docs/reference/python-api.md`; TypedDicts in `ultrafast.types` |

Set `ULTRAFAST_STRICT_OPTIONS=1` to turn unknown-key warnings into errors while you are writing files: a typo then
fails instead of being ignored.

## Run and read

```bash
ultrafast run system.ufs run.json -o results      # exit 0 on success; warnings on stderr and in summary.json
```

`results/summary.json`:

* `output`: the final pulse (`energyJ`, `fwhmS`, `peakPowerW`, `transformLimitFwhmS`, `spectralFwhmM`, `pulseGddS2`, ...);
* `elements[]`: per element `id`, `bIntegralRad`, `energyOutJ`, `peakFluenceJPerM2`, `lidtMargin`,
  `criticalPowerRatio`, `notes`, `values` (e.g. `trackedBeamRadiusXM`, `peakElectronDensityM3`);
* `instruments[]`: `name`, `kind`, `values` (scalars), `settings` (every device parameter with the value used); arrays are in `result.npz` as `<name>_<array>`;
* `lossBudget`, `energyBudget`, `totals`, `warnings`, `notes`, `grid`.

Python: `res = uf.run(system, spec)`; `res.summary`, `res["name_array"]`, `res.instrument("name")`;
`uf.load("results")` opens a folder without the engine.

## Branched systems

For beam splitters, interferometers, pump arms and loops, set `"topology": "branched"`, and give every element `next` ([Branched systems](reference/branches.md)). Check the layout with `ultrafast validate system.ufs --graph`. Read the budget from `summary["graph"]`: `energyClosureJ` should be at rounding level.

## Many runs

Use a **study**, not a loop: it freezes the grid (comparable results), reuses the unchanged part of the system and
runs in parallel ([Parameter studies](reference/studies.md)). Parameter paths: `element:ID/key`, `material:NAME/n2`,
`pulse/energyJ`, `pulse/chirp/gddFs2`; outputs: `pulse:end/fwhmS`, `record:ID/BIntegralRad`,
`instrument:NAME/key`. Recorded data is core: an instrument's `"measured"` (compared on the recording's axis, range
and resolution; `result.comparisons`) and measured input spectra (`pulse.spectrum`, with `background`), see
[Measured data](reference/measured-data.md). Fitting against measured data is the fitting plugin's `"fit"`: mark transform numbers with `{"value", "fit": true}`
or list paths; results in `fit.json` (layers, influence, provenance) and `fitted-system.ufs`. `"backward"` propagates a
measured field to the input ([Fitting](plugins/fitting.md)).

## Facts that are easy to get wrong

* `steadyState` loops need gain. A passive cavity is a `ringDown` loop, with `tolerance` near the square of the accuracy you want.
* `beam.m2` is along x on a cartesian grid; `beamProfile` also gives `m2Y`.
* Port c of a 50:50 Mach-Zehnder with equal arms is dark: all the light leaves by d.

* SI units everywhere, with the unit in the key (`lengthM`, `durationFwhmS`, `gddFs2` is the explicit exception
  in fs²). Wavelengths in metres: `1.03e-6`, not `1030`.
* `beam` on an element or `inputBeam` in the run is needed for any nonlinearity (intensity = power / area). With a
  Gaussian `inputBeam` the beam is tracked through lenses and curved mirrors; elements with their own `beam` use it.
* 1-D runs warn above the critical power for self-focusing; that is a warning about the model, not an error. Use a
  `transverse` model when spatial effects matter.
* Gases: `n2` of N2 and air is the electronic part; the rotational Raman response is separate.
* SHG-FROG retrieval cannot determine the sign of the chirp.
* The grid planner sizes the time window for stretched pulses and satellites (ghosts, pedestals). Override `grid`
  only with a reason; check `edgeEnergyFraction` in `output` (should be < 1e-6).
* Synthetic data with known answers for testing an analysis: [Assets](assets/README.md).

## Adaptive optics and digital twins

Two plugins, driven from Python (load them with `uf.plugins.load_python_plugins()`; the examples show the pattern):

* **`ao`**: `from ultrafast_ao import DmBench, Slm, DeformableMirror, SpectralShaper, Aopdf, tiled_array`. A deformable mirror and an SLM are
  elements that compile into the core `modulator`; big maps are passed as a callback (`"phaseTargetRad": "@name"`, `fields={"name": f(x, y, z)}`) or an NPZ
  file, never as JSON. The algorithms are NumPy: `ultrafast.algorithms.holography` (Gerchberg-Saxton, weighted GS), `.control` (modal and zonal
  reconstruction, interaction and control matrices, integrator loop) and `.sensorless` (`spgd`, `modal_search`, `transmission_matrix`). Rules that save
  time: choose the lens `f = N dx^2 / lambda` so that focal pixels are FFT bins; keep patterns in the inner half of the window; set `grid.windowS` to
  cover a shaper's side pulses (`2 pi / pixel bandwidth`); raise `transverse.spectralSamples` until `spectralSamplingError` is small for a pulse.
  Examples: [AdaptiveOptics](examples/AdaptiveOptics/README.md); reference page: [ao](plugins/ao.md).
* **`twin`**: `ultrafast twin init|fit|validate|predict|design|track|check` (set `ULTRAFAST_PYTHON`) or `ultrafast_twin` from Python. A twin is the fit across
  conditions (the fitting plugin) plus a validation report: **always mark at least one condition `heldOut`**, read the verdict (`ok`, `marginal`,
  `fits-training-only`, `unvalidated`) before trusting a prediction, and give the drift tracker the expected drift in physical units. `ultrafast twin serve <folder>` starts
  the [MCP server](#mcp-server) on the project folder (the twin-specific tools are not in it yet). Examples: [DigitalTwin](examples/DigitalTwin/README.md); reference page: [twin](plugins/twin.md).

## MCP server

`ultrafast-mcp` is a local [Model Context Protocol](https://modelcontextprotocol.io) server for agents that cannot or should not run a shell (decision E8).
It ships in the wheel (`pip install ultrafast-engine`), needs no other package, speaks JSON-RPC 2.0 over **stdio only** (no network port) and is limited to
**one workspace folder**. It speaks the stateless revision 2026-07-28 (`server/discover`, per-request `_meta`) and the handshake revisions
2025-11-25, 2025-06-18, 2025-03-26 and 2024-11-05 (`initialize`), so current clients work. Start it by hand with `ultrafast-mcp --workspace DIR`
(also `python -m ultrafast.mcp`, or `ultrafast twin serve DIR`); options `--budget-s` (time budget of one run or scan, default 300) and
`--memory-limit-gb` (default 8).

**Claude Code** (project `.mcp.json`, or `claude mcp add ultrafast -- ultrafast-mcp --workspace /path/to/workspace`):

```json
{ "mcpServers": { "ultrafast": { "command": "ultrafast-mcp", "args": ["--workspace", "/path/to/workspace"] } } }
```

**Codex** (`~/.codex/config.toml`):

```toml
[mcp_servers.ultrafast]
command = "ultrafast-mcp"
args = ["--workspace", "/path/to/workspace"]
```

If `ultrafast-mcp` is not on the `PATH`, use `"command": "python", "args": ["-m", "ultrafast.mcp", "--workspace", "..."]` with the interpreter that has the package.

| tool | what it does |
|---|---|
| `describe` | the elements of a system file (id, type, main numbers), topology, validation notes |
| `validate` | errors, warnings and notes of a system (and run spec) without propagating |
| `run` | one propagation; writes `.ultrafast-mcp/<name>/`; returns the output pulse, per-element B-integral and fluence, instrument values, `warnings`, `validity`, the `fidelityReport` and advice. `fidelity`: `quick`, `standard`, `full` |
| `scan` | a grid of one to four parameters (at most 400 points) with the output arrays; `fidelity` as above |
| `explain_warning` | what a warning text or a validity code means and what to do |
| `plot` | a figure of a finished run as PNG (`overview`, `system`, `instruments`, ...; styles `ultrafast`, `ultrafast-dark`, `paper`, `science`) |

Resources: `ultrafast://schema/run`, `ultrafast://schema/system` (JSON Schemas), `ultrafast://fidelity` (the levels and the workflow), `ultrafast://examples` (index).

**The workflow the server asks for: explore in `quick`, confirm in `full`.** Scan or run in `quick`; re-run the one to three points you will report in `full`;
read `warnings` and `validity` of every result (`explain_warning`); say which numbers came from which level ([Fidelity](reference/fidelity.md)).

**What the server refuses** (a tool result with `isError: true` and the reason; the agent can correct the call):

* strings that start with `@` (callbacks and field references), `kind` / `type` of `callback`, `python`, `expression` or `user`: no user code runs. Files you name are read and checked like inline objects;
* paths outside the workspace, paths through a symbolic link or Windows junction, and any string in a system or run that is absolute, goes up with `..` or ends in a data extension (`.csv`, `.npz`, `.json`, ...) unless it resolves inside the workspace (relative paths in a file are taken from that file's folder);
* names for result folders that are not 1 to 64 letters, digits, `_`, `-`, `.`; unknown, missing or mistyped arguments;
* scan paths that do not start with `pulse/`, `element:` or `instrument:`; more than 4 axes or 400 points; a run spec with a `fit`, `study` or `backward` section;
* a request above 4 MB, and a run or scan whose memory estimate is above the limit or 60 % of the free memory (before it starts).

**How it runs.** The server process only parses messages and applies these rules. The engine runs in one persistent child process with an empty private
`ULTRAFAST_HOME` (no user plugins or settings) and the workspace as working directory. A call that uses its time budget is **killed**; the next call starts a new
child. Calls are handled one at a time. The sandbox is the policy above plus a separate process, not an operating-system jail: run the server as a user that may
read only what you want the agent to see. Still on the roadmap: `compare_to_measured`, `fit`, prompts, an agent pack and evals ([roadmap](roadmap.md)).

## Changing the code

* Layout: `src/UltrafastEngine.Core` (engine), `src/UltrafastEngine.Cli`, `src/UltrafastEngine.Native` (C API),
  `python/src/ultrafast`, `plugins/` (fitting, gpu, hitran, ao, twin; zemax is experimental), `tests/UltrafastEngine.Tests` (custom runner, no NuGet).
* Zero NuGet dependencies; the core must build for `net10.0` and `net48` (no newer APIs without `#if NET`).
* Every element/option lives in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs`; after changing one, regenerate
  `ultrafast schema system -o schemas/ufs.schema.json`, `ultrafast schema run -o schemas/run.schema.json`,
  `ultrafast reference -o docs/reference/options.md`, `python python/tools/gen_types.py` and
  `python python/tools/gen_api_docs.py`. Tests fail if any of these is stale.
* Tests: `dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -c Release` (`-- --slow` for all, `-- Name`
  to filter); Python: `python python/tools/build_engine.py && pytest python/tests`. First-time setup and which build to pick: [Install and build](install.md) (the "For AI agents" box). See [Testing](testing.md).
* Extension points (index models, responses, elements, instruments, retrievals, compute back-ends, FFT providers)
  and plugins: [Extending](reference/extending.md), [Plugins](reference/plugins.md).
