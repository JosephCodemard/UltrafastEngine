# Parameter studies

A study runs a system many times with parameters varied: scans, optimisation, sensitivity analysis and coherence from repeated noisy runs. Use it to find where a setting stops working or which parameter a result depends on. A study is a third file, `study.json`, next to the system file and the run spec.

```jsonc
// study.json
{
  "format": "ultrafast-study",
  "system": "line.ufs", "run": "run.json",
  "mode": "scan",                                    // scan | optimize | sensitivity | coherence
  "sampling": "sobol", "samples": 128, "seed": 1,    // scan: grid | random | lhs | sobol
  "parallel": 4,                                     // points run at the same time
  "parameters": [
    { "path": "element:plate/lengthM", "min": 2e-3, "max": 8e-3 },
    { "path": "pulse/energyJ", "min": 1e-6, "max": 1e-4, "scale": "log" },
    { "path": "material:FusedSilica/n2", "min": 2.2e-20, "max": 3.0e-20 }
  ],
  "outputs": ["pulse:end/fwhmS", "record:plate/bIntegralRad", "instrument:xc/prePulseContrastDb"]
}
```

```bash
ultrafast study study.json -o study-results [--parallel 4] [--backend cuda]
```

```python
r = uf.study(system, spec, parameters=[{"path": "element:plate/lengthM", "min": 2e-3, "max": 8e-3, "points": 7}],
             outputs=["record:plate/bIntegralRad"], mode="scan")
```

## Keys

`format` (`"ultrafast-study"`) is optional and not checked. Defaults:

| key | default | meaning |
|---|---|---|
| `mode` | `scan` | `scan`, `optimize`, `sensitivity` or `coherence` |
| `sampling` | `grid` | scan sampler: `grid`, `random`, `lhs`, `sobol` |
| `samples` | 64 | points for `random`, `lhs`, `sobol` (ignored by `grid`) |
| `seed` | 1 | seed of the samplers |
| `parallel` | 1 | points run at the same time |
| `freezeGrid`, `prefixCache` | true | see [Speed](#speed) |
| `ensemble`, `cache`, `arrayOutputs` | see [Speed](#speed) | |

## Parameters

| path | changes |
|---|---|
| `element:ID/key` | an element value, through an override (anything an override can change: lengths, angles, n2, coatings by name ...) |
| `element:ID/a/b` | a nested value of the element (edited in the system JSON), e.g. `element:cell/beam/w0M` |
| `material:NAME/property` | a material property (override): `n2`, ... |
| `type:NAME/property` | every element of one type (override target `type:`) |
| `pulse/key[/sub]` | the run's pulse, e.g. `pulse/chirp/gddFs2` |
| `run/a/b`, `system/a/b` | anything else in the run spec / system file |

Each has `min` and `max` (`scale`: linear or log), or a list of `values`; `points` for grids (default 5); `nominal` for the reference run and local sensitivities. The default nominal value is the arithmetic mean of `min` and `max`, the geometric mean with `scale: log`, and the middle entry of `values`. An unknown path prefix is an error that names the accepted forms.

## Outputs

| output | value |
|---|---|
| `pulse:LOC/metric` | pulse summary at `LOC` (an element id, a probe id, `input`, `end`, or `id#field` for a by-product): `fwhmS`, `peakPowerW`, `energyJ`, `spectralFwhmM`, `mainLobeFwhmS`, ... The metric names are the pulse-summary fields of [Results](results.md#pulse-metrics-input-output-and-per-element-pulse), case-insensitive |
| `record:ID/field` | element record: a value key (`trackedBeamRadiusXM`, `peakElectronDensityM3`, ...), a loss mechanism, or a field of the record by its C# name, case-insensitive (`BIntegralRad`, `EnergyOutJ`, `PeakFluenceJPerM2`; the arrival time is `ArrivalS`, not the summary's `arrivalTimeS`); `out.metric` for the summary after the element |
| `instrument:NAME/key` | any scalar an instrument reports |
| `energy:LOC` | energy at a location |

## Modes

An unknown `mode` is an error: `study mode must be scan, optimize or sensitivity` (the message does not list `coherence`).

* **scan**: `grid` (every combination of `points` per parameter), `random`, `lhs` (Latin hypercube), `sobol`
  (low-discrepancy, up to 16 parameters).
* **optimize**: `"objective": {"output": "...", "goal": "min" | "max" | "target", "target": v, "maxEvaluations": 200, "tolerance": 1e-4}`
  -- Nelder-Mead on the scaled box, started at the nominal values. The objective output is added to `outputs`. `analysis` holds `optimum` (parameter values), `objective`, `goal`, `value` and `evaluations`.
* **sensitivity**: `"sensitivity": {"method": "local", "stepFraction": 0.01}` (central differences: derivative and
  elasticity d ln y / d ln p at the nominal point) or `{"method": "sobol", "samples": 256}` (global first-order and
  total indices, Saltelli / Jansen estimators, N (d + 2) runs for d parameters; the sequence has 2 d dimensions, so at most 8 parameters). `analysis.sensitivities[output][parameter]` is `{first, total}` for `sobol`.
* **coherence**: `"coherence": {"trials": 10, "at": "end"}` runs the system `trials` times with independent quantum noise (`pulse.noise`, seed + trial) and reports the first-order spectral coherence |g12(λ)| (Dudley, Genty and Coen, Rev. Mod. Phys. 78, 1135 (2006)). It needs no `parameters` or `outputs`. `analysis.coherence` holds `meanCoherence`, `wavelength_m`, `g12` and `spectrum`; `coherence.csv` has the same curves. See [Fibres](fibers.md#noise-and-coherence).

## Output files

`-o DIR` gets `study.json` (`format: "ultrafast-study-result"`, `parameters`, `outputs`, `evaluations`, `elapsedS`, `analysis`, `notes`, `rows`), `study.csv` (one row per point; parameter and output columns and a last `error` column, empty for a point that ran), `study.npz` (with `arrayOutputs`) and `coherence.csv` (coherence mode). A point that fails keeps its row with the message in `error` and NaN outputs.

## Speed

* The grid is frozen at the nominal run's plan (`freezeGrid`, default true), so every point uses the same samples and
  results are comparable.
* **Whole arrays** (`arrayOutputs`): `["spec/spectrum"]` keeps an instrument's array per point in `study.npz`, with one row per point. Give `output` to write it. [Example 34](../examples/Diagnostics/modelled-instruments/README.md) builds a FROG trace this way.
* **Prefix caching** (`prefixCache`, default true): when every varied parameter acts on elements after some index,
  each point restarts there from the nominal run's pulse instead of recomputing the elements before. In a Branched system a node cache replaces the restart, and `prefixCache` only controls whether finished nodes are reused.
* `parallel` runs points at the same time (each run also uses multiple cores for its FFTs).
* **Device ensembles** (`"ensemble"`, default on with a GPU back-end): when the run's `compute.backend` is `cuda`, `opencl` or `auto` with a
  device, the points of a batch run together, and their 1-D envelope elements are integrated as one ensemble, up to `ensemble` points (default
  64) per set of kernel launches. Each point keeps its own adaptive steps, so the results equal a point-by-point run to rounding (checked to
  1e-15). `"ensemble": 0` (or `false`) switches it off; a number sets the largest ensemble. Elements that cannot join (spatial, χ(2),
  user-response kernels, fixed steps, a different grid or Raman kernel) run alone; the study notes say how many propagations ran in how many
  launches. Measured gains: [Speed](speed.md#device-ensembles-for-studies).
* **The frozen grid serves every point.** Before the study the planner runs (compile only) for the two ends of each parameter range
  and for all minima / all maxima together; the frozen grid takes the longest window at the finest sample spacing among them
  (rounded up to a power of two), and a note says when that is larger than the nominal plan. A point is therefore never computed on a grid
  too short or too coarse for it just because the nominal point was fine.
* **Instruments are skipped** when no output (`instrument:...`, `arrayOutputs`) reads one: a scan of the pulse width does not run the FROG
  at every point. The note says how many were skipped.
* **Point cache and resume** (`"cache": "folder"`): every finished point is stored under a hash of the system, the run (with its frozen
  grid), the parameter values, the output list and the engine version. Run the same study again, or after an interruption, and only the
  missing points are computed (a note counts the points read from the cache). Any change of the system or run gives new keys, so a stale value
  is never read. Not used with `arrayOutputs`.
* **Linear scans** (compressor length, GDD, angle) are already cheap: a linear element is one transfer-function product, so a separate
  "linear fast path" was measured and not built (see [Speed](speed.md#parameter-studies-against-a-loop)).
* **Fidelity:** a study uses the run spec's [`fidelity`](fidelity.md); scan in `quick`, confirm the best points in `full`.
* Python: [`uf.scan`](python.md#scans) gives a lazy, shaped array per output.

## Validation

* `SpeedStudyTests.Study_ScanWithPrefixCacheMatchesFullRuns`: B = γ P L is linear in L, with and without the prefix cache (1e-9, 1 %).
* `SpeedStudyTests.Study_OptimizeAndSensitivities`: optimisation of a target B; elasticities of B to L and n2 equal 1; Sobol indices of an influential and a non-influential parameter.
* `SpeedStudyTests.Sobol_SequenceIsStratified`: stratification of the Sobol sequence.
* `StudyCacheTests.Study_FrozenGridServesTheWholeRange`, `Study_PointCacheResumesAndInvalidates`, `EnsembleGate_BatchesWaitingThreads_AndNeverWaitsForFinishedOnes`: the frozen grid, the point cache and the ensemble gate.
* `GpuTests.StudyPoints_AsOneDeviceEnsemble_MatchTheCpuStudy`: device ensembles equal point-by-point runs.
* `FiberTests.Coherence_LostInLongPulseSupercontinuum` (slow): the coherence study against Dudley et al. 2006.

## Limits

* Parameters are numbers; a study does not vary a string or a list element count.
* The Sobol sampler stops at 16 dimensions, the Sobol sensitivity method at 8 parameters.
* A point that fails is reported in its row, not raised; check the `error` column.
