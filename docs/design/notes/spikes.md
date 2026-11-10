# S2 spikes: measurements that decide the core design

Run 2026-11-10 on the owner's Mac (Apple silicon, 8 cores, 16 GB, CPU back-end, .NET 10). Scripts: `docs/design/notes/spike-scripts/`
(run with the repo `.venv` and `ULTRAFAST_LIBRARY` set to the hosted library, see `verification-environment`).

**Gate G0 verdict: no spike contradicts the plan; one retires its biggest risk (scale).** The owner should still read the
decisions in section 8.

## 1. S1-scale: a full SLM panel (`spike_scale.py`)

Gaussian beam (w = 2 mm) through a `mask file` (phase map, 8 µm pitch) and a 0.2 m gap, read by `beamProfile`.

| grid | stored frequencies K | wall time | peak memory |
|---|---|---|---|
| 512 x 512 | 1 (cw) | 0.6 s | 0.20 GB |
| **1920 x 1080** | 1 (cw) | **3.3 s** | **1.05 GB** |
| **1920 x 1080** | 8 (30 fs pulse) | 15.7 s | 1.74 GB |
| **1920 x 1080** | 16 (30 fs pulse) | **32.1 s** | **3.6 GB** |
| 4096 x 4096 | 1 (cw) | 19.9 s | 3.4 GB |

Time is linear in K (about 2 s per frequency), memory grows by about 0.23 GB per frequency at 1920 x 1080. **A full 1920 x 1080
panel with a 30 fs pulse fits a 16 GB laptop in half a minute at K = 16.** Only a 4096 x 4096 padded grid with K >= 16
(estimated 8-13 GB) needs streaming.

## 2. S2-sparse: how many stored frequencies (`spike_sparse.py`)

800 nm / 30 fs, 128 x 128, a smooth random phase screen (peak 6, 30, 100 rad at the design wavelength, so the phase scales with
frequency: the harshest chromatic case), far-field camera. Error = relative L2 difference of the focal-plane fluence map against a
dense run with K = 96.

| K | 6 rad | 30 rad | 100 rad |
|---|---|---|---|
| 1 | 8.4e-2 | 1.3e-1 | 8.4e-2 |
| 4 | 1.4e-3 | 2.0e-2 | 4.6e-2 |
| 8 | 1.7e-4 | 2.0e-3 | 2.8e-3 |
| 16 | 7.9e-5 | 1.6e-4 | 2.0e-3 |
| 32 | 1.1e-4 | 3.0e-4 | 4.2e-4 |
| 48 | 8.7e-4 | 1.6e-3 | 1.6e-3 |

* **Sparse spectral sampling already exists**: it is `transverse.spectralSamples` (default 48, profile interpolated between stored
  frequencies). No new sampler is needed; what is missing is an error estimate.
* K = 8 to 16 gives 1e-3 or better up to 30 rad of phase. At 100 rad (16 waves) K >= 32 is needed.
* The error is **not monotone**: K = 48 is worse than K = 16. The reference (K = 96) is itself a sampled run, and the bin edges of
  the 48- and 96-sample sets differ, so differences near 1e-3 are the floor of this comparison, not a measurement of K = 48.
* Rule of thumb that fits the table: the stored frequencies must resolve the phase change across the band,
  K >= 2 x (peak phase in waves x relative bandwidth x number of waves of spread) -- stated in the docs as "compare K and 2K".

## 3. S3-chromatic: is a wrapped phase map a grating? (`spike_chromatic.py`)

A blazed grating (period 32 µm, sawtooth optical path of depth 800 nm) as (a) a `mask file` holding the **wrapped** phase at
800 nm and (b) a 10 µm `bulk` of vacuum with a sawtooth `indexProfile`. cw at three wavelengths; first-order position in the
far field (f = 0.2 m) and first-order efficiency:

| lambda | position, mask / index / theory f lambda / p | efficiency mask / index / theory sinc^2(lambda0/lambda - 1) |
|---|---|---|
| 760 nm | 4.802 / 4.767 / 4.750 mm | 0.949 / 0.996 / 0.991 |
| 800 nm | 5.018 / 5.001 / 5.000 mm | 0.922 / 0.999 / 1.000 |
| 840 nm | 5.250 / 5.250 / 5.250 mm | 0.916 / 0.996 / 0.993 |

* **Both routes disperse like a grating (position proportional to lambda) and follow the blaze curve.** The existing `mask file`
  rule (phase_rad at the design wavelength, scaled by omega / omega_design) is the right model for a wrapped phase, **provided the
  file holds the wrapped phase**: the wrap is then a sawtooth in optical path, which is what a blazed surface is.
* The mask is 3-8 % less efficient than theory: its sub-pixel average over a cell (6 x 6 points) is a real pixel-integration effect at
  8 samples per period (sinc^2(1/8) = 0.95); the point-sampled index profile has none. A `modulator` must therefore state its sampling
  rule (average over the cell, with the fill factor) rather than leave it to the grid.
* The centroid positions have ~0.7 % scatter from the order's finite width (centroid over an asymmetric window), not from the model.
* A phase that does **not** scale with frequency (geometric / Pancharatnam-Berry, a mirror's tilt as pure phase) is not covered by
  `mask file`; a liquid-crystal cell needs phase = 2 pi dn(lambda) d / lambda. These are the three chromatic rules of section 8.

## 4. S4-loop: cost per trial (`spike_loop.py`)

128 x 128, one frequency, mask, gap, `beamProfile`, through the Python API:

| | time per trial |
|---|---|
| no mask | 10 ms |
| a new `mask file` per trial (np.savez + read + 6 x 6 cell average) | 46 ms |

A loop of 1000 trials costs 46 s today and would cost about 10-15 s with an in-memory map. The compile cache is worth a further
saving only if compile dominates the 10 ms floor (not measured separately); the in-memory map is the main gain (4x).

## 5. S5-tiles: not measured as a Branched system

Seven tiles as a Branched system against a coherent sum was **not** run: a Branched system recombines arms with a Gaussian mode
overlap at one plane (docs/reference/branched.md), so it cannot give the cartesian far field of an array. The tile source
(C4) is therefore tested against the analytic array factor instead (N^2 peak, exp(-sigma^2), lambda / pitch lobes).

## 6. S6-multi-condition fit (for S4, not built here)

`Fitter.Run(system, run, ...)` in `plugins/fitting/src/Fitting.cs` takes **one** system and **one** run spec; the data are the
`measured` blocks of that run's instruments, and the cost is the stacked residual over them. A joint fit over several operating
conditions (energies, angles) therefore needs a new level above `Run`: a list of (run overrides, instrument data) sharing the
fitted parameters, evaluated one run per condition per cost call. The Jacobian machinery (`Eval`, `PseudoInverse`,
`report.Warnings` for undetermined parameters) works on stacked residuals and can be reused. Held-out conditions are a flag on the list.

## 7. S7-repetition (for the Pockels comment)

* `repRateHz` (on the pulse or on a gain / amplifier element) switches on the steady-state iteration in
  `Gain/GainElements.cs` (lines ~361 and ~529, `SteadyState`, `MaxPulses`, `Tolerance`): a fast energy model over pulses, then the full
  model for the last one. This confirms the plan: repetition is a property of the gain model only.
* `grep -i pockels|pulsePicker` over `src/` finds nothing: **there is no switch model** (extinction ratio, edges, leakage). The
  S6 sketch in the roadmap stands.

## 8. Decisions (as built)

| topic | decision | alternatives rejected |
|---|---|---|
| Streaming over frequency (C3) | **Not built.** 1920 x 1080 at K = 16 takes 32 s / 3.6 GB, so the SLM requirement is met by the existing profile storage. Streaming is only needed for padded 4096 x 4096 grids with K >= 16 (estimated 8-13 GB). It stays an option with the numbers above. | Build streaming first: the largest change in the executor, for a case no stated requirement needs |
| Sparse sampling (C3) | **Reuse `transverse.spectralSamples`.** The modulator reports `spectralSamplingError` (largest distance of a stored sample from the line through its neighbours; about 1/K^2, measured 1.43, 0.45, 0.10, 0.032 at K = 4, 8, 16, 32 for 40 rad) and warns above 0.05. This is a *conservative proxy* (it skips a whole bin), not the true error: for the same screen the true error of the focal map at K = 16 was 1.6e-4 (30 rad, spike 2). It is blind to phase steps near a multiple of 2 pi per stored frequency, so a second warning fires when the step exceeds pi/2. Rule in the docs: compare K and 2K. | A new sampler; adaptive frequency selection (no evidence it beats uniform bins) |
| Chromatic rules | Two values. `opd` (default): the written phase is an optical path, `phi(omega) = phi_design omega / omega_design`; a **wrapped** map is therefore a real grating (spike 3). `fixedPhase`: `phi(omega) = phi_design` (geometric phase). The input may be `phaseRad` at a design wavelength or `opdM` directly. | A third `index` mode with a built-in Delta-n(lambda) table: liquid-crystal dispersion is device data and belongs to the plugin, which can hand the core the wrapped `opdM`. A "wrap inside the core" mode: would hide the physics; the plugin wraps the way the device does |
| Sampling | The simulation cell gets the average of the device over the cell (6 x 6 sub-samples, so edge weights come in steps of 1/6; a fast path when the whole cell is in one active device cell). `fillFactor`, gaps, `crosstalkSigmaCells` (Gaussian blur of the written phase). | Point sampling (spike 3 shows the pixel effect is real) |
| In-memory maps (C2) | Array in the spec (Python converts numpy), **callback `"@name"`** at the cell centres (no file, no JSON), or NPZ file. A compile cache is **not** built: the floor of a 128^2 trial is 10 ms and the file map was the cost (46 ms). | A C-API-only route (Python could not use it) |
| Tiles (C4) | `initialProfile.kind = "tiles"` in the transverse code: complex sum of Gaussian / tophat tiles with piston, tilt, delay per tile, normalised to unit energy per stored frequency. | A Branched system (spike 5: it recombines arms by a Gaussian mode overlap at one plane and cannot give an array's far field) |
| Zonal outputs (C5) | `wavefrontSensor` gains `slope_x_rad` and `slope_y_rad` [x][y]; the wavefront map was already a stable array. | |
| GPU parity (C6) | **Not done** (no device in this session). The modulator and tile source run on the CPU path; the GPU back-end applies to the spatial propagation, which they feed. A GPU test is a TODO for a machine with a device. | |
