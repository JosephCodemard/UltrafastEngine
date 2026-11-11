# Modulator: pixelated phase and amplitude devices

`modulator` is a thin element on the carried transverse profile whose transmission comes from a **grid of cells**: a spatial light
modulator (SLM), the surface of a deformable mirror, a tile mask. It is the core primitive; device catalogues (actuator
influence functions, liquid-crystal curves) belong in plugins that compute the map and hand it to this element.

It needs a cartesian transverse grid (`"model": "cartesian"`), like the `mask` file kind, and acts at every stored frequency.

```jsonc
{ "id": "slm", "type": "modulator",
  "cellsX": 1920, "cellsY": 1080, "pitchXM": 8e-6,           // the panel: cells, pitch (pitchYM defaults to pitchXM)
  "phaseRad": "@hologram",                                    // an [x][y] array, a callback, or "file"
  "designWavelengthM": 800e-9,                                // the wavelength at which phaseRad holds
  "phaseWrapRad": 6.2831853, "quantisationLevels": 256,       // wrap and write with 8 bits
  "fillFactor": 0.95, "crosstalkSigmaCells": 0.4,             // pixel structure
  "chromatic": "index", "deltaIndexCauchy": [0.15, 2e-14] }   // liquid crystal: dn = A + B / lambda^2
```

## Parameters

| key | unit | default | meaning |
|---|---|---|---|
| `cellsX`, `cellsY` | | from the map | cells along x and y; the shape of an array or file map fixes them |
| `pitchXM`, `pitchYM` | m | `pitchYM` = `pitchXM` | cell pitch (required) |
| `offsetXM`, `offsetYM`, `rotationRad` | m, rad | 0 | centre of the array and its rotation about the centre |
| `phaseRad` | rad | 0 | commanded phase at `designWavelengthM`: `[cellsX][cellsY]`, `"@name"`, or in the NPZ as `phase_rad` |
| `opdM` | m | | the commanded **optical path** instead (NPZ `opd_m`); not both |
| `amplitude` | | 1 | amplitude transmission per cell (NPZ `amplitude`) |
| `file` | | | NPZ with `phase_rad` or `opd_m`, and `amplitude`, shaped `[cellsX][cellsY]`; relative to the system file |
| `designWavelengthM` | m | the carrier | wavelength at which `phaseRad` holds |
| `chromatic` | | `opd` | `opd` (also `phaseAtDesign`), `fixedPhase` or `index` ([below](#chromatic-rules)) |
| `deltaIndexCauchy` | `[1, m², m⁴]` | | `index` rule: Δn = A + B/λ² + C/λ⁴ as `[A, B, C]` (one to three terms) |
| `deltaIndex`, `deltaIndexWavelengthsM` | `1`, m | | `index` rule: a table of Δn over wavelength (linear in λ between points, held constant outside) |
| `phaseWrapRad` | rad | 0 (off) | wrap the commanded phase to `[0, phaseWrapRad)` |
| `quantisationLevels` | | 0 (continuous) | levels over `phaseWrapRad` (2π if that is 0); the phase is rounded to the nearest level |
| `fillFactor` | | 1 | active fraction of the pitch in x and y; the rest is a gap |
| `gapTransmission`, `outsideTransmission` | | 0 | amplitude transmission of the gaps and of the plane outside the array |
| `zeroOrderLeak` | | 0 | amplitude fraction η that passes unmodulated (phase 0) in the active cells: `t = a[(1 − η) e^{iφ} + η]`; a flat map stays lossless, and the zero order of a blaze has power `|η + (1 − η) c₀|²` |
| `crosstalkSigmaCells` | | 0 | fringing-field cross-talk: a Gaussian blur of the written phase and amplitude (σ in cells, edge values repeated) |
| `crosstalkKernel` | | | an explicit cross-talk kernel instead: odd-length non-negative weights in cells (a negative weight could make gain), centre in the middle, normalised to unit sum, applied along x and then along y (e.g. a measured fringing profile) |

Cell `(ix, iy)` is centred at `((ix − (cellsX − 1)/2) pitchXM, (iy − (cellsY − 1)/2) pitchYM)` before the offset and rotation;
`x` is the first index (the same `[x][y]` layout as every map in the engine).

**Order of operations on the command:** phase from `phaseRad` or `k_design · opdM` → wrap → quantise → cross-talk blur → the
device's transmission. The blur acts on the *written* phase, so it rounds the 2π jumps of a wrapped hologram, which is what
fringing fields do and why the efficiency of a real SLM falls near the jumps.

## Three ways to give a map

* **An array** in the system file (`"phaseRad": [[...], ...]`; the Python builder converts numpy arrays). Fine for small maps.
* **A callback** `"phaseRad": "@name"`: `f(x, y, z) → values` evaluated at the cell centres when the run compiles (z = 0, device frame), registered like
  a position-dependent field: `uf.run(system, spec, fields={"name": f})` in Python, `registry.RegisterCallback("name", (FieldCallback)...)` in C#.
  No file and no JSON, so a 1920 × 1080 map costs one array. A trial of a 128 × 128 loop costs 10 ms with a callback or array against 46 ms with a new file each time ([spikes](../design/history.md)).
  **Loops:** every run compiles once and reads the callback then, so a control loop that changes what the callback returns gets the new map at
  each trial (tested: N trials equal N runs from N map files to 1e-12). The C API registers the same callback with `uf_run_set_field_callback`.
* **A file**: `"file": "map.npz"` with `phase_rad` (or `opd_m`) and `amplitude`.

## How a cell reaches the grid

The simulation cell takes the **average of the device over the cell**: a cell wholly inside one active device cell gets its
value exactly; a cell crossing an edge, a gap or the array border gets the mean of 6 × 6 sub-samples (edge weights in steps of 1/6, so an unaligned edge is off by up to 1/12 of the jump). The result is the
pixel-integration effect of the real device. Consequences, measured in the tests:

* a **grid cell holds one complex number**, so a cell split between two device cells with different phases transmits slightly less than
  1 (the missing power is spatial frequency the grid cannot carry). It is booked as `clip`. Align device cell edges with grid cell edges
  (e.g. `offsetXM` half a grid step) and use at least 2 (better 4) grid cells per device cell for a clean result; the element warns below 2.
* with `fillFactor < 1` the cell structure makes **replica orders** at `λ/pitch` with the envelope set by the active area.
* a **blazed N-level staircase** diffracts `[sin(π/N)/(π/N)]²` of the light into the first order (N = 8: 94.96 %).

## Chromatic rules

| `chromatic` | phase at frequency ω | use for |
|---|---|---|
| `opd` (default; also `phaseAtDesign`, the plan's name: a phase given at the design wavelength and scaled with ω) | `φ_design · ω / ω_design` | a **non-dispersive optical path**: a mirror surface, a surface-relief or index device. Phase delay and group delay are both OPD/c; a liquid-crystal retardance, whose group and phase delays differ, is only approximated. A *wrapped* map is then a sawtooth in path, i.e. a real grating |
| `fixedPhase` | `φ_design` | a device whose phase does not depend on frequency (geometric / Pancharatnam–Berry phase) |
| `index` | `φ_design · ω Δn(ω) / (ω_design Δn(ω_design))` | a **liquid-crystal cell**: the retardance is 2π Δn(λ) d / λ, so the phase follows the dispersion of the index modulation Δn (extended Cauchy `deltaIndexCauchy`, or a `deltaIndex` table). Only the ratio Δn(λ)/Δn(λ_design) matters; a constant Δn is the `opd` rule. Assumes the voltage scales Δn without changing its dispersion shape (Δn_eff(V, λ) = g(V) Δn(λ)); the cell's bias retardance, common to all pixels, is left out (a piston with its own group delay); a table is held constant outside its range, which puts a kink in the group delay at its ends |

Reference behaviour (tests): a smooth ramp `k θ x` under `opd` deflects every colour by the same angle θ; the same ramp *wrapped* at 2π
(a blazed grating of period `p`) leaves at `λ/p`, so the first order moves in proportion to λ (measured 760/800/840 nm in the
[spike](../design/history.md), both as a `mask file` and as a thin `bulk` with an index profile); under `fixedPhase` the same ramp
deflects by `θ λ/λ_design`. Under `index` a wrapped blaze has the depth `2π α` with `α = ω Δn(ω) / (ω_design Δn(ω_design))` at each
frequency, so its first-order efficiency off the design wavelength follows the staircase of that depth (tested at 1.2 µm, α = 0.833 against
0.858 for `opd`). Device data (a measured Δn(λ), voltage curves) stays in plugins, which hand the core the map and the Δn table.

## Spectral sampling

The profile is stored at `transverse.spectralSamples` frequencies and interpolated between them, so a strongly chromatic device needs
enough samples. The element reports **`spectralSamplingError`** (the largest distance of a stored sample from the line through its
two neighbours, in units of the field norm; it falls about 1/K² with the number K of samples) and warns above 0.05. It cannot see a phase step near a multiple of 2π per stored frequency, so the element also warns when the phase changes by more than π/2 between stored frequencies. Compare K and 2K
for the quantity you care about. Measured (30 fs, 800 nm, a smooth random screen, error of the focal-plane map against K = 96): 8 to 16
samples give 1e-3 or better up to 30 rad of phase, K ≥ 32 for 100 rad ([spikes](../design/history.md)).

**Size and time** (CPU, Apple silicon): a 1920 × 1080 panel with a 30 fs pulse takes 32 s and 3.6 GB at K = 16, 3.3 s and 1.05 GB for one
frequency (cw). A 4096 × 4096 grid takes 20 s and 3.4 GB for one frequency. For long chains on large grids see
[large grids](transverse.md#large-grids-streaming-and-memory) (`transverse.frequencyBlock`, `outputs.keepProfiles`).

## Validation

| test | reference | tolerance |
|---|---|---|
| `PhaseRamp_MovesTheFocus_ByFocalLengthTimesAngle` | a tilt moves the focus by f θ; a phase device is lossless | 1 % |
| `QuantisedBlaze_FirstOrderEfficiency_FollowsSincOfOneOverN` | `[sin(π/N)/(π/N)]²`, N = 4, 8, 16 (and the exact sampled staircase) | 0.6 % |
| `FillFactor_SetsTheTransmission_AndTheReplicaOrders` | open fraction; replica / zero order = |c₁/c₀|² = 1/9 at λ f / pitch | 1e-6; 1 % |
| `WrappedRamp_DispersesLikeAGrating_UnwrappedRampDoesNot` | `x₁ ∝ λ` wrapped; constant angle unwrapped | 1.5 % |
| `FixedPhase_RampDeflectionScalesWithWavelength` | `θ λ/λ₀` | 1 % |
| `Maps_InlineFileAndCallback_AreIdentical` | the same device three ways | 1e-12 |
| `CwRun_EqualsThePulse_AtTheCarrier` | cw equals a 1 ns pulse (4 stored frequencies) | 2e-5 |
| `OneStoredFrequency_IsAtTheCarrier_AndTheCwPatternMatches` | one stored frequency sits at the carrier; the cw pattern matches up to the cw line's grid offset (1.2e-6) | 1e-7; 3e-6 |
| `SpectralSamplingError_FallsWith*` | 1/K² law; warning when under-sampled | ratio 2.5-6 |
| `SpectralSamplingError_BoundsTheTrueErrorOfTheFocalMap` | the reported error ≥ the true focal-map error against K = 64, at K = 8, 16, 32 | bound |
| `QuadraticPhase_FocusesLikeAThinLens` | a phase −k x²/2f focuses to λ f/(π w) at f, as the `lens` element | 2 %; 1 % |
| `IndexRule_WithConstantDeltaN_IsTheOpdRule`, `PhaseAtDesign_IsTheOpdRule` | constant Δn = the `opd` rule; the alias | 1e-12; exact |
| `IndexRule_ScalesThePhaseByTheDispersion_AndSetsTheBlazeEfficiency` | `index` = `fixedPhase` × α; blaze efficiency at depth α | 1e-12; 1 % |
| `CrosstalkKernel_BlursTheWrittenMap_ByDiscreteConvolution` | `[1, 2, 1]/4` convolution; σ equals its own kernel | 1e-12 |
| `Rotation_TurnsTheCellGrid_AboutTheArrayCentre` | a cell centre maps to R(θ)(u, v) | 1e-12 |
| `EdgeCellsAndGaps_AreAreaAverages` | half-covered edge cell; fill ½ with gap amplitude g passes ½ + g²/2 | 1e-12; 1e-6 |
| `Trials_WithACallbackMap_EqualRunsFromMapFiles` | N loop trials = N file runs | 1e-12 |
| `ZeroOrderLeak_IsPassive_AndSetsTheZeroOrder`, `Quantisation_WrapsTheTopHalfLevel_ToLevelZero` | lossless flat map, η² zero order; top half-level → level 0 | 1e-9; 1 % |
| `WavefrontSensor_SlopeMaps_*` | tilt θ everywhere; defocus slopes x/R, y/R | 1 %; 2 % |
| `GpuTests.TilesAndModulator_IntoANonlinearPlate_MatchTheCpu` | tiles + modulator + a Kerr plate on the device = CPU (skipped without a double-precision OpenCL / CUDA device) | 1e-9 |
