# Parameter studies

A study runs a system many times with parameters varied: scans, optimisation and sensitivity analysis.

```jsonc
// study.json
{
  "format": "ultrafast-study",
  "system": "line.ufs", "run": "run.json",
  "mode": "scan",                                    // scan | optimize | sensitivity
  "sampling": "sobol", "samples": 128, "seed": 1,    // scan: grid | random | lhs | sobol
  "parallel": 4,                                     // points run at the same time
  "parameters": [
    { "path": "element:plate/lengthM", "min": 2e-3, "max": 8e-3 },
    { "path": "pulse/energyJ", "min": 1e-6, "max": 1e-4, "scale": "log" },
    { "path": "material:FusedSilica/n2", "min": 2.2e-20, "max": 3.0e-20 }
  ],
  "outputs": ["pulse:end/fwhmS", "record:plate/BIntegralRad", "instrument:xc/prePulseContrastDb"]
}
```

```bash
ultrafast study study.json -o study-results         # study.json + study.csv (one row per point)
```

```python
r = uf.study(system, spec, parameters=[{"path": "element:plate/lengthM", "min": 2e-3, "max": 8e-3, "points": 7}],
             outputs=["record:plate/BIntegralRad"], mode="scan")
```

## Parameters

| path | changes |
|---|---|
| `element:ID/key` | an element value, through an override (anything an override can change: lengths, angles, n2, coatings by name ...) |
| `element:ID/a/b` | a nested value of the element (edited in the system JSON), e.g. `element:cell/beam/w0M` |
| `material:NAME/property` | a material property (override): `n2`, ... |
| `pulse/key[/sub]` | the run's pulse, e.g. `pulse/chirp/gddFs2` |
| `run/a/b`, `system/a/b` | anything else in the run spec / system file |

Each has `min` and `max` (`scale`: linear or log), or a list of `values`; `points` for grids; `nominal` (default the
middle) for the reference run and local sensitivities.

## Outputs

| output | value |
|---|---|
| `pulse:LOC/metric` | pulse summary at an element id, `input` or `end`: `fwhmS`, `peakPowerW`, `energyJ`, `spectralFwhmM`, ... |
| `record:ID/field` | element record: `BIntegralRad`, `EnergyOutJ`, `ArrivalS`, `PeakFluenceJPerM2`, a loss mechanism, or a value key (`trackedBeamRadiusXM`, `peakElectronDensityM3`, ...); `out.metric` for the summary after the element |
| `instrument:NAME/key` | any scalar an instrument reports |
| `energy:LOC` | energy at a location |

## Modes

* **scan**: `grid` (every combination of `points` per parameter), `random`, `lhs` (Latin hypercube), `sobol`
  (low-discrepancy, up to 16 parameters).
* **optimize**: `"objective": {"output": "...", "goal": "min" | "max" | "target", "target": v, "maxEvaluations": 200}`
  -- Nelder-Mead on the scaled box. The result has `analysis.optimum`.
* **sensitivity**: `"sensitivity": {"method": "local", "stepFraction": 0.01}` (central differences: derivative and
  elasticity d ln y / d ln p at the nominal point) or `{"method": "sobol", "samples": 256}` (global first-order and
  total indices, Saltelli / Jansen estimators, N (d + 2) runs).

## Speed

* The grid is frozen at the nominal run's plan (`freezeGrid`, default true), so every point uses the same samples and
  results are comparable.
* **Whole arrays** (`arrayOutputs`): `["spec/spectrum"]` keeps an instrument's array per point in `study.npz`, with one row per point. Give `output` to write it. [Example 34](../examples/34-modelled-instruments/README.md) builds a FROG trace this way.
* **Prefix caching** (`prefixCache`, default true): when every varied parameter acts on elements after some index,
  each point restarts there from the nominal run's pulse instead of recomputing the elements before.
* `parallel` runs points at the same time (each run also uses multiple cores for its FFTs).

Tests: `SpeedStudyTests` (B ∝ L in a scan with and without the cache; optimisation of a target B; elasticities of
B to L and n2 equal 1; Sobol indices of an influential and a non-influential parameter; stratification of the
Sobol sequence).
