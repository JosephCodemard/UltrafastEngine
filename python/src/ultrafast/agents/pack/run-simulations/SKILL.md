---
name: run-simulations
description: How to drive UltrafastEngine - write .ufs system files and run.json specs, run them from the CLI or Python, pick grids and physics switches, use measured data and studies, and read summary.json / result.npz. Use when asked to simulate a system, reproduce a measurement, or build an example.
---
# Running UltrafastEngine

Full guide: `docs/agents.md`; every key with unit and default: `docs/reference/options.md` (large -- grep it for a
key instead of reading it); examples to copy: `docs/examples/*/` (README + .ufs + run.json).

## Workflow

1. Start from the closest example. `ultrafast describe system.ufs` shows what each element becomes.
2. Write with `ULTRAFAST_STRICT_OPTIONS=1` so typos fail. `ultrafast validate system.ufs run.json`.
3. `ultrafast run system.ufs run.json -o results` (CLI: `dotnet src/UltrafastEngine.Cli/bin/Release/net10.0/ultrafast.dll`
   in the repo). Read the notes and warnings it prints -- they explain grids, satellites, cleaning, B estimates.
4. Python: `import ultrafast as uf; from ultrafast import build as b` -- builders return typed dicts;
   `res = uf.run(b.system(...), b.run_spec(...))`; `res.summary`, `res["inst_array"]`, `res.instrument(name)`,
   `res.comparisons`. Figures: `ultrafast-plot results` or `uf.plot(res, "overview" | "measurements")`.

## Essentials

* SI everywhere (wavelengths 1.03e-6 m); keys carry units; `gddFs2` etc. are explicit fs-unit variants.
* Nonlinear elements need a beam (`beam` on the element or `inputBeam` in the run): constant (radiusM/areaM2),
  gaussian (w0M, zWaistM, m2), or a `transverse` model for real spatial effects.
* Polarisation: `"x"`, `"rcp"`, `{ "angleDeg": 0, "ellipticity": tan χ }` (+1 right circular) or a Jones vector.
  Elliptical light uses the vector Kerr + Raman model (NER).
* Measured input: `"pulse": {"spectrum": {"csv": "s.csv", "background": "auto", "resolutionM": 0.5e-9,
  "extrapolate": "gaussian"}, "energyJ": ...}`. Instrument recordings: `"measured": {"file": "osa.csv",
  "background": "auto", "resolutionM": 0.5e-9}` -> `summary.comparisons`.
* Grid: leave `grid.auto` on; for many ghosted plates the window follows `grid.satelliteLevel`; fix the grid
  (`"auto": false, "n", "windowS"`) only for studies or to match another code.
* Many runs: `ultrafast study study.json` (frozen grid, prefix cache, parallel) instead of loops. Fits / backward
  propagation: fitting plugin (`docs/plugins/fitting.md`).

## Reading results

`summary.json`: `output` (energyJ, fwhmS, transformLimitFwhmS, peakPowerW, spectralFwhmM, pulseGddS2,
edgeEnergyFraction), `elements[]` (bIntegralRad, energyOutJ, criticalPowerRatio, lidtMargin, notes), `instruments[]`
(values, settings, arrays in result.npz as `<name>_<array>`), `comparisons[]`, `lossBudget`, `totals`, `warnings`.
Sanity: energy balance (`totals.energyTransmission`), `edgeEnergyFraction` < 1e-6, B-integral vs your estimate
k0·n2·I·L, TL vs the input spectrum width.
