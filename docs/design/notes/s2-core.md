# S2 Core and spikes: handoff

Branch `dev/2026-11-s2-core` (from `dev/2026-11`). Measurements and decisions: [spikes.md](spikes.md). This page: what was shipped,
what was not, and what the `ao` plugin (S3) may rely on.

## Shipped

| primitive | where | API as shipped |
|---|---|---|
| **C1 `modulator`** | `Optics/ModulatorElement.cs`, `Engine/CompilerModulator.cs`, `Schema/BuiltInElements.cs` | element `type: "modulator"`; keys in [reference/modulator.md](../../reference/modulator.md): cell grid (`cellsX/Y`, `pitchX/YM`, `offsetX/YM`, `rotationRad`), `phaseRad` / `opdM` / `amplitude`, `fillFactor`, `gapTransmission`, `outsideTransmission`, `zeroOrderLeak`, `crosstalkSigmaCells` or `crosstalkKernel`, `quantisationLevels`, `phaseWrapRad`, `chromatic` = `opd` (= `phaseAtDesign`) / `fixedPhase` / `index` (`deltaIndexCauchy` or `deltaIndex` + `deltaIndexWavelengthsM`); Python `ultrafast.build.modulator`, `ultrafast.components.Modulator` |
| **C2 in-memory maps** | same | `phaseRad` / `opdM` / `amplitude` as a nested array (numpy converted by the builder), a callback `"@name"` (`uf.run(..., fields={"name": f})`; C API `uf_run_set_field_callback`; `f(x, y, z)` at the cell centres) or an NPZ `file`. The three are tested identical to 1e-12; a callback is read at every compile, so **N loop trials = N file runs** (1e-12) |
| **C3 streaming and sparse sampling** | `Transverse/SpatialPropagation.cs` (`ExecuteLinear`), `Engine/Executor.cs` (`ProfilesInUse`), `Transverse/TransverseProfile.cs` (`TransverseOps.InterpolationError`) | `transverse.frequencyBlock` (blocks of stored frequencies in linear spatial elements; = all at once to 1e-12), `outputs.keepProfiles: "used"`; the sparse sampler is `transverse.spectralSamples`; record value `spectralSamplingError` (tested to bound the true focal-map error at K = 8, 16, 32) and warnings above 0.05 or a phase step over pi/2 |
| **C4 coherent tiles** | `Transverse/TileSource.cs`, `FromShape` | `initialProfile.kind = "tiles"`; Python `ultrafast.sources.TilesProfile` / `build.tiles_profile`. Delay sign: `delaysS > 0` = later (tested against a `delay` element) |
| **C5 fast path and zonal outputs** | `Instruments/CameraInstruments.cs`, `TransverseOps.SampleBins` | `spectralSamples: 1` stores the carrier (bug fixed: it was the low band edge); `wavefrontSensor` arrays `slope_x_rad`, `slope_y_rad` `[x][y]` next to `wavefront_waves`, `x_m`, `y_m` (tilt and defocus tested) |
| **C6 GPU parity** | `tests/.../GpuTests.cs` | `TilesAndModulator_IntoANonlinearPlate_MatchTheCpu` (1e-9); **skipped on this Mac** (OpenCL without FP64, no PoCL wheel); the body was checked CPU against CPU |
| cylindrical lens (S1 finding) | existing `lens` | `focalLengthXM`, `focalLengthYM` were already there; a test now covers the cylindrical case (`focalLengthYM: 1e30`) |

## Not shipped, and why

* **Whole-executor streaming** (instruments accumulating per frequency, so no full profile ever exists). Not needed: 1920 x 1080 at K = 16
  fits in 3.6 GB, and `keepProfiles: "used"` keeps the live set at about one profile. The process peak is set by the .NET GC (2.7-3.4 GB at
  1920 x 1080 x 16 for every block size), not by the propagator.
* **A compile cache (C2).** A 128 x 128 trial costs 10 ms including the compile; the file map was the cost (46 ms), and callbacks remove it.
* **GPU parity on a device**: the test exists and skips; run it on a CUDA / FP64 OpenCL machine or Linux with PoCL.
* **S5-tiles as a Branched system** was not measured; a Branched system cannot give an array's far field (spikes section 5). The tile source
  is tested against the array theorem instead.
* **Roadmap test "cw equals the pulse at the carrier (1e-9)"**: the stored frequency of a pulse is within 1e-7 of the carrier (one grid bin of a
  1 ns grid). A cw source's single bin is snapped to its grid lattice, 1.2e-6 off the requested carrier (printed in the run notes), so cw and
  pulse focal patterns agree to 3e-6, not 1e-9. Moving the cw line would change every cw result for a 1e-6 effect; not done.

## What the `ao` plugin may rely on

* A map for a device is computed by the plugin (influence functions, voltage curves) and given as `opdM` (surface or path)
  or `phaseRad` + `phaseWrapRad` + `quantisationLevels`; `chromatic: "opd"` makes a wrapped map a grating; the core does not wrap unless asked.
* A liquid-crystal SLM with a dispersive birefringence: `chromatic: "index"` with the plugin's Delta-n as `deltaIndexCauchy` [A, B_m2, C_m4]
  or a `deltaIndex` table; the phase map is the one written at `designWavelengthM`.
* For a deformable mirror use `opdM` = 2 cos(aoi) x surface height (the surface-to-wavefront factor is the plugin's).
* `fillFactor`, `crosstalkSigmaCells`, `zeroOrderLeak` and the gap / outside transmissions exist; a hex actuator layout is not a cell grid: map it to
  a square cell grid (the plugin resamples) or use several modulators.
* Use at least 2 (better 4) grid cells per device cell, with the array edges on grid-cell edges (`offsetXM` half a grid step) to avoid the partial-cell
  amplitude loss (booked as `clip`).
* Tile delays need `spectralSamples` large enough: spacing of stored frequencies x delay under ~0.5 rad.
* A control loop calls the engine with a callback map; the spikes give 10 ms per 128^2 trial.

## Numbers (also in spikes.md)

1920 x 1080: 3.3 s / 1.05 GB (cw, Python); on a 2048 x 1280 grid (C#) 1.9 s, 260 MB held after the run with `keepProfiles: "used"` against 491 MB with `all`; 15.7 s / 1.74 GB (K = 8),
32 s / 3.6 GB (K = 16). Live set after a five-element 1024 x 1024 x 16 run: 1587 MB (`all`) -> 336 MB (`used`). First-order efficiency of
an N = 4, 8, 16 blaze within 0.6 % of the sampled staircase and 2 % of the continuum formula. Tile combining within 2 %.

## Changelog lines

Written into `CHANGELOG.md` (Unreleased: Added, Fixed). `docs/testing.md` rows for `ModulatorTests`, `TileSourceTests`, `SpatialStreamingTests`.
`PROJECT_STATUS.md` counts, `docs/capabilities.md`, `ROADMAP.md` and `docs/roadmap.md` updated by this session at the owner's request (it merges
S2 into `dev/2026-11`).

## Known sharp edges

* A grid cell split between two device cells with different phases transmits less than 1 (one complex number per cell). Not a bug: the missing
  power is spatial frequency the grid cannot carry. See the modulator page.
* The modulator multiplies by `exp(i phi omega / omega_design)` exactly; it does not protect against an unresolved phase: raise `spectralSamples` when `spectralSamplingError` warns.
* A callback map is evaluated once per run (at compile time), not per frequency.
* A `delay` element moves the arrival time (the frame), not the field; a tile delay is relative to the frame and moves the field.
* `outsideTransmission` defaults to 0: light beyond the array is blocked (a small DM in a large grid clips the beam).
