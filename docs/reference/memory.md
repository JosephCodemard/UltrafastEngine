# Memory

A run keeps more than its answer: the pulse after every element and, with a spatial model, a transverse profile per
spatial element. This page says what a run keeps, how the engine estimates it before the run (`Engine/MemoryEstimate.cs`),
how to set a limit, and what to change when a run, a scan or a fit is too large for the machine.

## Keys

* `compute.memoryLimitBytes` (B; default 0 = no limit): a run whose estimate is above it fails at compile time, before
  the first element, with the figures and what to change. `ultrafast validate` reports it as an error.
* `outputs.keepProfiles`: `"all"` (default) keeps the profile after every spatial element; `"used"` drops a profile as
  soon as no instrument, later element or the output needs it ([Transverse](transverse.md#large-grids-streaming-and-memory)).
* `transverse.frequencyBlock` (default 0 = all at once): linear spatial elements propagate the stored frequencies in
  blocks of this many; the result does not depend on it.
* Python: `memory="warn"` (default), `"raise"` or `"off"` on `uf.scan(...)` and on `uf.run(...)` of a run spec with a
  `fit` section.

Defaults and types: [options](options.md#compute), [outputs](options.md#outputs).

## Model

### What a run keeps

| part | estimated size | notes |
|---|---|---|
| pulses | (enabled elements + 1) × 2 polarisations × N × 16 B | the pulse after every element, and the input. N is the grid size (`grid.n`, or the planned one) |
| transverse profiles | (kept profiles + 1) × M × `spectralSamples` × 16 B, × 2 for `polarization: "vector"` | only with a spatial model. M is `points` (radial) or `nx` × `ny` (cartesian) of the largest spatial element; the + 1 is the profile in flight. Kept profiles: every spatial element with `"all"`; with `"used"` the bound min(spatial elements, max(1, instruments) + 1) |
| solver workspace | the larger of 24 arrays of N complex samples (1-D) and 8 arrays of M × (frequency block) complex samples (spatial) | `transverse.frequencyBlock` cuts the spatial part |
| delayed-light tail | (6 + places) × 2 × N_long × 16 B | only with `grid.tail`, and not counted in a branched system. Places: the end, every instrument and every probe. N_long is the long grid, capped by `grid.maxN` |
| instrument read-outs | 16 arrays of N doubles per instrument | a lower bound; spectrograms and traces with two axes need more |

A single profile on a 1920 × 1080 grid with 48 stored frequencies is 1920 × 1080 × 48 × 16 B = 1.6 GB, so `"all"`
over ten spatial elements is the difference between a run and no run. In a branched system (`topology: branched`)
`"used"` keeps the profile of every element that feeds an instrument's element or the output (the links of the graph
decide, not the order of the file).

### The estimate

The engine counts the parts above when it compiles the run, before any physics is done:

* `ultrafast validate system.ufs run.json` prints it as a note (and as a warning or an error when it is too large);
* every run and every study point carries the compile note `Estimated memory of one run: ...`;
* Python: `uf.estimate_memory(system, run)` returns `totalBytes`, `estimate` (bytes by part), `availableBytes`
  (−1: unknown), `limitBytes`, `exceedsLimit` and `warning`;
* the command `{"command": "memory", "system": ..., "run": ...}` does the same for any client.

The estimate is a planning figure. In the tests the managed memory a finished run retains lies between a tenth of it
and all of it (the solver workspace is freed after the run, and pulses that an element leaves unchanged are shared). It
does not include the .NET runtime, the instruments' two-dimensional outputs, or the other runs of a parallel study.

### Free memory and warnings

* Above 80 % of the **free** physical memory the compile gives a warning (`Memory: ...`), whatever the limit.
* The free memory comes from `GCMemoryInfo` (available minus the memory load; container limits respected) in the .NET 10
  build on every operating system. The .NET Framework 4.8 build uses `GlobalMemoryStatusEx` on Windows and
  `MemAvailable` of `/proc/meminfo` elsewhere (Mono on Linux). Where none gives a figure (some sandboxes) there is no
  warning.
* Python `uf.scan(...)` and `uf.run(...)` with a `fit` section check the estimate first, multiplied by the number of runs
  alive at once (`parallel`, `fit.parallel`): `"warn"` issues a `uf.MemoryWarning`, `"raise"` raises `MemoryError`,
  and a run above its own `compute.memoryLimitBytes` raises `MemoryError` in every mode except `"off"`. The lower-level
  `uf.study(...)` does not check.

### Studies and fits

* Every point of a study runs with `outputs.keepProfiles: "used"` unless the run spec sets it (the study outputs read
  scalars and 1-D pulses). The nominal run keeps `"all"` while the study's `prefixCache` is on (the default), because the
  points resume from its pulses and profiles; with `prefixCache: false` it keeps what the run spec says.
* A fit evaluates the system many times. It keeps the **latest** and the **best** evaluation in full; every other one,
  including the Jacobian runs that the uncertainties need, is slimmed to its instrument readings, comparisons and element
  records (no pulses, profiles, probe snapshots or compiled run), and the residual cache holds numbers only. Evaluations
  run with `"used"` unless the run spec sets `outputs.keepProfiles`; set it to `"all"` if the result of the fit must carry
  every profile.

### What to change when a run is too large

1. `outputs.keepProfiles: "used"` (spatial runs; the largest single saving).
2. `transverse.frequencyBlock: 4` (or 8): the linear spatial elements propagate that many stored frequencies at a time.
3. Fewer `transverse.points` / `nx`, `ny`, or `transverse.spectralSamples`.
4. A smaller grid: a narrower bandwidth, a shorter window, `grid.maxN`, or [`fidelity: quick`](fidelity.md) for scans.
5. For scans and fits: a smaller `parallel`. Each parallel worker holds a whole run.
6. In Python, `result.close()` (or `with uf.run(...) as result:`) gives the engine's copy of a result back at once.

### GPU

The GPU back-ends accept an element when its stepper needs at most 80 % of the device's **free** memory (CUDA:
`cuMemGetInfo`, which also counts other processes; OpenCL: the total minus the buffers this process holds); otherwise it
runs on the CPU, with both figures in its notes. `compute.deviceSlots` is lowered to floor(0.8 × total / needed), at
least 1, when that many propagations of this size would not fit together. Details: [GPU](../plugins/gpu.md#memory).

## Validation

* The estimate equals the hand count of each part for a 1-D run, a radial run with `"all"` and `"used"`, and a cartesian
  vector run (`MemoryEstimateTests.Estimate_OneDimensional_MatchesTheHandCount`, `..._Radial_MatchesTheHandCount_AllAndUsed`,
  `..._CartesianVector_MatchesTheHandCount`).
* A limit below the estimate fails at compile, above it passes; a fake free memory gives the 80 % warning
  (`MemoryEstimateTests.Limit_BelowTheEstimate_FailsAtCompile_AboveItPasses`, `..._FreeMemory_Above80Percent_Warns_NotBelow`).
* Retained memory of a real run: 0.1 × estimate < retained ≤ estimate
  (`MemoryEstimateTests.RealRun_RetainedManagedMemory_IsBelowTheEstimate_AndNotFarBelow`).
* Study points with `"used"` give the same outputs as with `"all"` (`MemoryEstimateTests.Study_PointsWithUsedProfiles_GiveTheSameOutputsAsAllProfiles`).
* Fits: the same evaluations, iterations, χ², parameters, uncertainties and predictions as values recorded before the
  memory changes, to 1e-9 relative (`FittingMemoryTests.Fit_ResultIsBitIdenticalToTheUnboundedCacheVersion`); at most
  four full results are alive at once over 28 evaluations (`FittingMemoryTests.Fit_ManyEvaluations_KeepABoundedNumberOfFullResults`).
* GPU decisions with a fake device size (`GpuMemoryTests`).

## Limits

* The estimate counts profiles of the largest spatial element only, and with `"used"` a bound, not the exact set.
* `polarization: "auto"` that becomes vector at run time is counted as scalar (half the profile size).
* The delayed-light tail of a branched system and the two-dimensional instrument outputs are not counted.
* The peak memory *during* a run is set by the .NET garbage collector, which returns large freed arrays late; the
  estimate describes what is held, not the peak ([Transverse](transverse.md#large-grids-streaming-and-memory)).
