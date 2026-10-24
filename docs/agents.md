# For AI agents

This page is for an AI agent (or a person scripting one) that drives UltrafastEngine: writing system files and run
specs, running them, reading results, and changing the code. The short version is in `AGENTS.md` at the repository
root and `llms.txt` (an index of the documentation).

## The model in one paragraph

A **system file** (`.ufs`, JSON with comments) lists elements in beam order: `bulk`, `gap`, `plate`, `mirror`,
`lens`, `gratingPair`, `crystal`, `gain`, ... A **run spec** (`run.json`) gives the input pulse, physics switches,
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

## Changing the code

* Layout: `src/UltrafastEngine.Core` (engine), `src/UltrafastEngine.Cli`, `src/UltrafastEngine.Native` (C API),
  `python/src/ultrafast`, `plugins/` (fitting, zemax), `tests/UltrafastEngine.Tests` (custom runner, no NuGet).
* Zero NuGet dependencies; the core must build for `net10.0` and `net48` (no newer APIs without `#if NET`).
* Every element/option lives in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs`; after changing one, regenerate
  `ultrafast schema system -o schemas/ufs.schema.json`, `ultrafast schema run -o schemas/run.schema.json`,
  `ultrafast reference -o docs/reference/options.md`, `python python/tools/gen_types.py` and
  `python python/tools/gen_api_docs.py`. Tests fail if any of these is stale.
* Tests: `dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -c Release` (`-- --slow` for all, `-- Name`
  to filter); Python: `python python/tools/build_engine.py && pytest python/tests`. See [Testing](testing.md).
* Extension points (index models, responses, elements, instruments, retrievals, compute back-ends, FFT providers)
  and plugins: [Extending](reference/extending.md), [Plugins](reference/plugins.md).
