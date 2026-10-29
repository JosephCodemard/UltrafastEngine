# Run spec reference (`run.json`)

Every key with its type, unit and default is listed in the generated [option reference](options.md#run-spec);
editors complete and check run specs that start with `"$schema": ".../schemas/run.schema.json"` ([Schemas](schema.md)).

```jsonc
{
  "$schema": "schemas/run.schema.json",   // optional: editor completion
  "format": "ultrafast-run",
  "version": 2,
  "name": "my-run",
  "pulse": { ... },          // required
  "grid": { ... },
  "physics": { ... },
  "stepper": { ... },
  "probes": "all",           // "all" | "end" | ["id1", "id2"]
  "instruments": [ ... ],
  "overrides": [ ... ],      // models.md
  "inputBeam": { ... },      // Gaussian beam at the system input: tracked through every element (components.md)
  "compute": { ... },        // back-end and threads (speed.md)
  "fit": { ... },            // optional: calibrate parameters against measured instruments (plugins/fitting.md)
  "outputs": { ... }
}
```
`ultrafast template run.json` writes a commented example.

## `pulse`
| field | |
|---|---|
| `shape` | `gaussian`, `sech2`, `superGaussian` (+ `order`), `spectrum` |
| `centerWavelengthM` | carrier (for `spectrum`: default = intensity-weighted mean) |
| `durationFwhmS` | transform-limited intensity FWHM (or `bandwidthFwhmM`) |
| `energyJ` / `averagePowerW` + `repRateHz` / `peakPowerW` | pulse energy (one of) |
| `chirp` | `{ "gddS2", "todS3", "fodS4" }` spectral phase about the carrier (or `gddFs2`, `todFs3`, `fodFs4` in fs units) |
| `polarization` | `"x"`, `"y"`, `"45"`, `"-45"`, `"rcp"`, `"lcp"`, `{ "angleDeg", "ellipticity" }`, or `[exRe, exIm, eyRe, eyIm]` |
| `spectrum` | a measured spectrum: `{ "wavelengthsM", "intensity", "phaseRad" }` or `{ "csv": "file.csv", "wavelengthUnit": "nm" }` (columns: wavelength, intensity per unit wavelength, optional phase); `background` (a number, `"auto"`, `"min"`), `threshold`, `resolutionM` (the recording spectrometer's FWHM), `deconvolve` (remove it by Richardson–Lucy, `deconvolveIterations`, default 30) and `extrapolate` (`"zero"`: no light outside the measured wavelengths, default; `"gaussian"`: Gaussian tails fitted to the data). Rows are sorted, repeated wavelengths merged, non-finite rows dropped. See [Measured data](measured-data.md#measured-input-spectra) |
| `delayS` | time offset |
| `pedestal` | list of contrast components added to the pulse ([below](#pedestal)) |

A measured spectrum is known only over the wavelengths it covers and at the resolution it was recorded with. The run
says so in the input description ("measured 1008.8-1053.4 nm (504 points, background 0.66 % removed, resolution
0.2 nm, with phase, no light outside)") and reports every cleaning step in the notes; a floor above 0.3 % of the peak
that is not subtracted gives a warning, because a flat floor shortens the transform limit
([Measured data](measured-data.md#background)). It replaces the input shape and propagates forward only. To learn
what the *system* does to a pulse and reuse that knowledge with other inputs, use transform elements and the fit of
the [fitting plugin](../plugins/fitting.md), which can also propagate a measured output field backward to the input.

### `pedestal`

Real pulses sit on a pedestal and come with replicas. `pulse.pedestal` adds them to the ideal input, so contrast
instruments, ghosts and nonlinear elements work on a realistic field:

```jsonc
"pedestal": [
  { "kind": "prePulse",  "delayS": 12e-12, "relativeIntensity": 1e-6 },
  { "kind": "postPulse", "delayS": 30e-12, "relativeIntensity": 1e-4, "gddFs2": 2000 },
  { "kind": "ase", "durationS": 10e-9, "relativeIntensity": 1e-10, "bandwidthFwhmNm": 30 },
  { "kind": "phaseNoise", "rmsRad": 0.05, "correlationNm": 0.5 },
  { "kind": "exponential", "level": 1e-5, "riseS": 3e-12, "fallS": 5e-12 }
]
```

| kind | what | keys |
|---|---|---|
| `replica` / `prePulse` / `postPulse` | a copy of the pulse before (pre) or after (post) the main one; `replica` takes the sign of `delayS` | `delayS`, `relativeIntensity` (peak) or `relativeEnergy`, `phaseRad`, `gddFs2` / `gddS2` (a stretched replica) |
| `ase` | amplified spontaneous emission: a flat incoherent floor (random spectral phase) | `durationS` (10 ns), `relativeIntensity` or `energyFraction`, `bandwidthFwhmM` / `bandwidthFwhmNm`, `centerWavelengthM`, `seed` |
| `phaseNoise` | spectral phase (and amplitude) noise: the coherent pedestal of a stretcher-compressor | `rmsRad`, `amplitudeRms`, `correlationNm` / `correlationRadPerS`, `seed` |
| `exponential` | an incoherent exponential pedestal (scattering in a stretcher, ...) | `level` (at the peak), `riseS`, `fallS`, bandwidth as for `ase` |

The grid planner widens the time window to hold every component (a 10 ns ASE floor makes a long window: give
`grid.windowS` and accept the large grid, or drop the ASE term for the propagation and model it in the
instrument instead). The `contrast` and `crossCorrelator` instruments show the result.

## `grid`
| field | default | |
|---|---|---|
| `auto` | true | size the grid from pulse and system (concepts.md); the provisional window grows until a chirped input fits |
| `n`, `windowS` | 4096, 30 x TL | explicit values; with `auto` they are a minimum resolution |
| `maxN` | 4 194 304 | cap |
| `snapCarrier` | true | put the carrier on the frequency grid (required by carrier-resolved mode) |
| `spectralMargin` | auto | spectral span factor |
| `minSpanFraction` | 0.1 | nonlinear runs: minimum half-span / carrier |
| `minPointsPerFwhm` | 12 | samples across the transform-limited FWHM |
| `satelliteLevel` | 1e-8 | weakest ghost / replica satellite (relative intensity) the window makes room for, including combinations of satellites of several elements (a ghost of a ghost) and satellites that coincide in delay, summed in amplitude |
| `extendForSatellites` | false | with a manual window (`"auto": false`), grow the window (keeping dt) until the satellites fit |
| `strict` | false | a satellite or pedestal replica that wraps around the window is an error, not a warning |
| `tail` | false | `true` or `{windowS, warnEnergyFraction}`: the delayed-light ledger, a second long linear grid ([Delayed light](delayed-light.md)) |

**Satellites and the window.** Ghosts and replicas follow the main pulse by their delay. Making room for every
order of every plate would grow the window linearly with the number of plates: 20 plates with ghosts asked for
4.4 ns. The planner instead makes room for the longest delay at which the satellites of all elements together
reach `satelliteLevel`. 20 AR-coated plates (0.2 % per face) then need room for one ghost (about 1 ns of window with
the centred pulse); 20 uncoated ones, whose single ghosts add up coherently when the plates are identical, for
five. The note "Satellites: the window makes room for ..." says what was chosen. Weaker satellites wrap around the
window. Set `satelliteLevel` lower for contrast studies, or give `windowS` / `n` with `"auto": false` to set the
grid yourself. A manual window that is too small gets a specific warning for each satellite or pedestal replica that wraps
(its source, delay, level, energy and where it wraps to); `extendForSatellites` grows the window, `strict` makes it an
error, and `tail` carries the satellites on a second, long, linear grid instead ([Delayed light](delayed-light.md)).

## `physics`
| field | default | |
|---|---|---|
| `nonlinear` | true | master switch |
| `selfSteepening` | true | |
| `raman` | true | delayed response of materials that have one |
| `fieldMode` | `auto` | `envelope`, `carrier` (real field everywhere), `auto` (where a model asks) |
| `fieldHarmonic` | 3 | highest harmonic the carrier-resolved grid holds |
| `absoluteIndex` | true | add air dispersion to air paths quoted relative to air |
| `polarization` | `jones` | or `scalar` (s/p averaged) |
| `losses` | true | `false` removes flat loss terms (quick "what if lossless") |
| `beamTracking` | `auto` | Gaussian beam (q) tracking through the system: `auto` (on when there is a Gaussian `inputBeam`), `on`, `off` ([Components](components.md#gaussian-beam-tracking)) |
| `ionization` | false | photo-ionisation and plasma in every nonlinear bulk element: `true` or `{ "model": "ppt", ... }` ([Ionisation](ionization.md)) |
| `transverse` | none | default spatial model for every bulk/gap/crystal element that has none of its own: `{"model": "radial", "points": 48}` ([Spatial models](transverse.md)) |

## `stepper`
| field | default | |
|---|---|---|
| `method` | `adaptive` | ERK4(3)-IP with error control, or `rk4ip` (fixed steps) |
| `tolerance` | 1e-6 | adaptive: local relative error per step |
| `maxPhasePerStepRad` | 0.01 | fixed: max nonlinear phase per step; adaptive: initial step |
| `maxAreaChangePerStep` | 0.05 | limit on |d ln A_eff| per step (focusing beams) |
| `minStepM`, `maxStepM`, `maxSteps`, `steps` | | limits / explicit fixed step count |

## `compute`
| field | default | |
|---|---|---|
| `backend` | `cpu` | compute back-end for the nonlinear steps; plugins add others ([Speed](speed.md)) |
| `threads` | 0 | worker threads (0 = all cores) |

## `instruments`
A list of `{ "kind": ..., "at": "end" | "input" | element id, "name": ..., ...parameters }`; see
[Instruments](instruments.md). Instruments never change the pulse. Two keys work on every instrument:

* `"sampling"`: what part of the beam it sees (near / far field, pinhole, fibre, slit);
* `"measured"`: your recording to compare with (CSV / NPZ / JSON), compared on its own axis, range and resolution
  ([Measured data](measured-data.md)).

See [Sampling](instruments.md#sampling-where-the-instrument-looks) and [Measured data](measured-data.md).

## `fit` (fitting plugin)
Calibrates parameters of the system or the run against every instrument with `"measured"` data, then predicts
everything else with error bars:

```jsonc
"fit": { "parameters": [ { "name": "input chirp", "path": "pulse/chirp/gddFs2", "min": -500, "max": 1500, "unit": "fs^2" },
                         { "path": "element:compressor/gddFs2", "min": -1500, "max": 0 } ],
         "maxIterations": 20, "tolerance": 1e-4, "freezeGrid": true }
```

A run with `"fit"` takes (parameters + 2) runs per iteration. See [Fitting plugin](../plugins/fitting.md#calibration-fit).

## `outputs`
| field | default | |
|---|---|---|
| `json`, `npz`, `csv` | true | which files `WriteAll` / `ultrafast run` produce |
| `maxPoints` | 0 | cap exported traces (cropped to the energy support first, then decimated); 0 = full grid incl. complex fields |
