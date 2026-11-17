# Memory

A run keeps more than its answer. This page says what it keeps, how the engine estimates it before the run, how to set a limit,
and what to change when a run, a scan or a fit is too large for the machine.

## What a run keeps

| part | size | notes |
|---|---|---|
| pulses | (enabled elements + 1) x 2 polarisations x N x 16 B | the pulse after every element, and the input. `N` is the grid size (`grid.n`, or the planned one) |
| transverse profiles | M x `spectralSamples` x 16 B each (x 2 for vector fields) | only with a spatial model (`transverse.model` radial or cartesian). `M` is `points` (radial) or `nx` x `ny` (cartesian) |
| solver workspace | 1-D: 24 arrays of N complex samples; spatial: 8 arrays of M x (frequency block) samples | `transverse.frequencyBlock` streams the frequencies and cuts the spatial part |
| delayed-light tail | (6 + places) x 2 x N_long x 16 B | only with `grid.tail`; N_long is the long grid |
| instrument read-outs | 16 arrays of N doubles per instrument | a lower bound; spectrograms and traces with two axes need more |

`outputs.keepProfiles` decides how many profiles stay in the result: `"all"` (default) keeps the profile after every spatial element,
`"used"` keeps it only where an instrument reads it, and before such an element, and at the end. With a 1920 x 1080 panel and 48
spectral samples a single profile is 1.6 GB, so `"all"` over ten spatial elements is the difference between a run and no run.
A branched system (`topology: branched`) keeps all profiles whatever you ask.

## The estimate

The engine counts the parts above when it compiles the run, before any physics is done:

* `ultrafast validate system.ufs run.json` prints it as a note (and as a warning or an error when it is too large);
* every run and every study point carries it as the compile note `Estimated memory of one run: ...`;
* Python: `uf.estimate_memory(system, run)` returns the numbers (`totalBytes`, `estimate` by part, `availableBytes`, `exceedsLimit`);
* the command `{"command": "memory", "system": ..., "run": ...}` does the same for any client.

The estimate is a planning figure. In the tests the managed memory a finished run retains lies between a tenth of it and all of it
(the solver workspace is freed after the run, and pulses that an element leaves unchanged are shared). It does not include the
.NET runtime, the instruments' two-dimensional outputs, or the other runs of a parallel study.

## Limits and warnings

* `compute.memoryLimitBytes` (default 0: none). A run whose estimate is above it fails at compile time, before the first element,
  with the figures and what to change. `validate` reports it as an error.
* Above 80 % of the **free** physical memory the compile gives a warning (`Memory: ...`). The figure is `GCMemoryInfo`
  (container limits respected) on .NET, `GlobalMemoryStatusEx` on .NET Framework / Windows and `/proc/meminfo` on Linux. Where it
  cannot be found (some sandboxes) there is no warning.
* Python: `uf.scan(...)` and `uf.run(...)` with a `fit` section check the estimate first (`memory="warn"` | `"raise"` | `"off"`),
  multiplied by the number of runs alive at once (`parallel`). `warn` issues a `uf.MemoryWarning`; a run above its own
  `compute.memoryLimitBytes` always raises `MemoryError`.

## Studies and fits

* Every point of a study runs with `outputs.keepProfiles: "used"` unless the run spec sets it itself (the study outputs read scalars and
  1-D pulses, never a profile that no instrument reads). The nominal run keeps what the run spec says, and `"all"` when the point cache
  resumes from it.
* A fit evaluates the system many times. It keeps the **latest** and the **best** evaluation in full, a slimmed copy (instrument readings
  and element records, no pulses or profiles) of the Jacobian runs that the uncertainties need, and for all others only the residual
  vector and the cost. Evaluations run with `"used"` unless the run spec sets `outputs.keepProfiles`; set it to `"all"` if the result
  of the fit must carry every profile. The number of evaluations, the fitted values and their uncertainties are the same as with
  unlimited retention (tested bit for bit).

## What to change when a run is too large

1. `outputs.keepProfiles: "used"` (spatial runs; the largest single saving).
2. `transverse.frequencyBlock: 4` (or 8): the linear spatial elements propagate that many stored frequencies at a time. The result
   does not depend on it.
3. Fewer `transverse.points` / `nx`, `ny`, or `transverse.spectralSamples`.
4. A smaller grid: a narrower bandwidth, a shorter window, `grid.maxN`, or `fidelity: quick` for scans (docs/reference/fidelity.md).
5. For scans and fits: a smaller `parallel`. Each parallel worker holds a whole run.
6. In Python, `result.close()` (or `with uf.run(...) as result:`) gives the engine's copy of a result back at once.

## GPU

The GPU back-ends read the **free** device memory (CUDA: `cuMemGetInfo`, which also counts other processes; OpenCL: total minus the
buffers of this process), refuse an element that needs more than 80 % of it (it runs on the CPU, with the figures in the element's
notes), and run fewer concurrent propagations than `compute.deviceSlots` when that many would not fit together. See
[docs/plugins/gpu.md](../plugins/gpu.md#memory).
