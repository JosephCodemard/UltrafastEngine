# S2 Core and spikes: handoff

Branch `dev/2026-11-s2-core` (from `dev/2026-11`). Measurements and decisions: [spikes.md](spikes.md). This page: what was shipped,
what was not, and what the `ao` plugin (S3) may rely on.

## Shipped

| primitive | where | API as shipped |
|---|---|---|
| **C1 `modulator`** | `Optics/ModulatorElement.cs`, `Engine/CompilerModulator.cs`, `Schema/BuiltInElements.cs` | element `type: "modulator"`; keys in [reference/modulator.md](../../reference/modulator.md); Python `ultrafast.build.modulator`, `ultrafast.components.Modulator` |
| **C2 in-memory maps** | same | `phaseRad` / `opdM` / `amplitude` as a nested array (numpy converted by the builder), a callback `"@name"` (`uf.run(..., fields={"name": f})`, `f(x, y, z)` at the cell centres) or an NPZ `file`. The three are tested identical to 1e-12 |
| **C3 spectral sampling** | `Transverse/TransverseProfile.cs` (`TransverseOps.InterpolationError`), modulator record | existing `transverse.spectralSamples` is the sparse sampler; new record value `spectralSamplingError` and a warning above 0.05 |
| **C4 coherent tiles** | `Transverse/TileSource.cs`, `FromShape` | `initialProfile.kind = "tiles"`; Python `ultrafast.sources.TilesProfile` / `build.tiles_profile` |
| **C5 zonal outputs** | `Instruments/CameraInstruments.cs` | `wavefrontSensor` arrays `slope_x_rad`, `slope_y_rad` `[x][y]` next to the existing `wavefront_waves`, `x_m`, `y_m` |

## Not shipped, and why

* **Streaming over frequency (C3).** The spike showed a full 1920 x 1080 panel with a 30 fs pulse needs 32 s and 3.6 GB at 16 stored
  frequencies, so the requirement is met without it. It is needed only for padded 4096 x 4096 grids with K >= 16.
* **A compile cache (C2).** A 128 x 128 trial costs 10 ms without a file map and 46 ms with a new file; callbacks and arrays remove the file cost.
  Compile time was not measured separately.
* **GPU parity (C6).** No device was available. The modulator and the tile source produce arrays on the CPU and feed the spatial
  propagation; no GPU test was written. Needs a machine with CUDA / OpenCL.
* **Cylindrical lens / anamorphic optics** (S1 finding): not added. `lens` is still round.
* **S5-tiles as a Branched system** was not measured; the tile source is tested against the analytic array factor instead (spikes section 5).
* **Roadmap test "cw equals the pulse at the carrier (1e-9)"** is met to **2e-5**: a 1 ns pulse has a 0.4 GHz bandwidth, so the
  optical-path phase differs from the carrier's by ~1e-6 of its value across the band. The test says so.

## What the `ao` plugin may rely on

* A map for a device is computed by the plugin (influence functions, voltage curves, Delta-n(lambda)) and given as `opdM` (surface or path)
  or `phaseRad` + `phaseWrapRad` + `quantisationLevels`; `chromatic: "opd"` makes a wrapped map a grating; the core does not wrap unless asked.
* For a deformable mirror use `opdM` = 2 cos(aoi) x surface height (the surface-to-wavefront factor is the plugin's).
* `fillFactor`, `crosstalkSigmaCells`, `zeroOrderLeak` and the gap / outside transmissions exist; a hex actuator layout is not a cell grid: map it to
  a square cell grid (the plugin resamples) or use several modulators.
* Use at least 2 (better 4) grid cells per device cell, with the array edges on grid-cell edges (`offsetXM` half a grid step) to avoid the partial-cell
  amplitude loss (booked as `clip`).
* Tile delays need `spectralSamples` large enough: spacing of stored frequencies x delay under ~0.5 rad.
* A control loop calls the engine with a callback map; the spikes give 10 ms per 128^2 trial.

## Numbers (also in spikes.md)

1920 x 1080: 3.3 s / 1.05 GB (cw), 15.7 s / 1.74 GB (K = 8), 32 s / 3.6 GB (K = 16). First-order efficiency of an N = 4, 8, 16 blaze
within 0.6 % of the sampled staircase and 2 % of the continuum formula. Tile combining within 2 %.

## Changelog lines

Written into `CHANGELOG.md` (Unreleased, Added). `docs/testing.md` rows added for `ModulatorTests` and `TileSourceTests`.
`PROJECT_STATUS.md` test counts are left to the merging session (S5).

## Known sharp edges

* A grid cell split between two device cells with different phases transmits less than 1 (one complex number per cell). Not a bug: the missing
  power is spatial frequency the grid cannot carry. See the modulator page.
* The modulator multiplies by `exp(i phi omega / omega_design)` exactly; it does not protect against an unresolved phase: raise `spectralSamples` when `spectralSamplingError` warns.
* A callback map is evaluated once at compile time, not per frequency.
* `outsideTransmission` defaults to 0: light beyond the array is blocked (a small DM in a large grid clips the beam).
