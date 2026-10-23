# Fitting plugin: transforms, measured data, fitting and backward propagation

!!! note "Plugin"
    These features come from the **fitting plugin** (`plugins/fitting`). It ships with the engine and is loaded
    automatically when installed (check with `ultrafast plugins list`; installing plugins is described in
    [Installing plugins](installing.md)). The core engine works without it.

A model of an optical system is only as good as what it knows. The design says the compressor matches the stretcher;
the lab says the pulse has 800 fs² left over, the amplifier narrowed the spectrum and an etalon added a post-pulse.
The fitting plugin closes that gap in four steps:

1. **[Transform elements](#transform-elements)** (`"type": "transform"`) are physical changes to the beam that you
   insert where they happen: spectral phase (dispersion), spectral amplitude (gain narrowing, filters), energy,
   polarisation errors, contrast degradation (replicas, phase noise, ASE pedestals) and wavefront errors. Each is a
   physical operator on the complex field, so spectrum, pulse, contrast and beam stay consistent with each other.
2. **[Measured data](#measured-data-comparing-with-recordings)** (`"measured"` on any instrument) compares a
   simulated instrument with your recording: CSV, NPZ or JSON, curves, 2-D traces and scalar readings, at the
   recording instrument's resolution and sampling.
3. **[Fitting](#fitting)** (`"fit"` in the run spec) finds the transform numbers you marked, and any other number in
   your files, from all recordings at once. It reports them with uncertainties in three layers (simulated → inferred
   → believed) and with an influence map, and it writes `fitted-system.ufs`. That file is your system with the
   fitted transforms in place, ready to run on **new inputs** with higher fidelity than the design alone.
4. **[Backward propagation](#backward-propagation)** (`"backward"` in the run spec) runs the system in reverse. Give
   it the field measured at the output and it finds the field at every plane upstream, up to the input.

How this relates to the core: a measured **input** spectrum (`"pulse": {"shape": "spectrum", ...}`, see the
[run spec](../reference/run-spec.md#pulse)) is a core feature. It replaces the input shape and propagates forward
only. Transforms are the opposite tool. They describe what the system does to *any* pulse, so they are fitted once
and then reused with other inputs, and they can be undone for backward propagation.

Worked example: [`docs/examples/26-transforms`](../examples/26-transforms/README.md) fits a CPA line's residual
dispersion, gain narrowing, post-pulse and ASE pedestal from a spectrum, a d-scan, a cross-correlator and a power
meter. It then applies the fitted line to a new seed and propagates the measured output back to the seed. Example 21
([`21-measured-data`](../examples/21-measured-data/README.md)) and [Tutorial 4](../tutorials/04-measured-data.md)
fit numbers of an existing system by path.

## Quick start

```jsonc
// system file: what the design does not know, as a transform with numbers to fit
{ "id": "lab", "type": "transform", "operators": [
    { "kind": "spectralPhase",
      "gddFs2": { "value": 0, "fit": true, "min": -3000, "max": 3000 },
      "todFs3": { "value": 0, "fit": true, "min": -6e4, "max": 6e4 } },
    { "kind": "spectralAmplitude", "shape": "gaussian",
      "fwhmNm": { "value": 20, "fit": true, "min": 5, "max": 60 } },
    { "kind": "replica", "delayPs": 6.0, "relativeIntensityDb": { "value": -50, "fit": true, "min": -90, "max": -20 } }
] }
```

```jsonc
// run spec: recordings on the instruments, and a fit block (no parameter list needed)
"fit": {},
"instruments": [
  { "kind": "spectrometer", "resolutionM": 0.2e-9, "measured": { "file": "data/spectrum.csv", "background": "auto" } },
  { "kind": "dscan", "gddRangeS2": [-2e-26, 2e-26], "measured": "data/dscan.npz" },
  { "kind": "crossCorrelator", "rangeS": [-15e-12, 15e-12], "measured": { "file": "data/xc.csv", "scale": "log" } }
]
```

```bash
ultrafast run line.ufs run-fit.json -o results-fit              # fit.json, fitted-system.ufs, plots with ultrafast-plot
ultrafast run results-fit/fitted-system.ufs new-seed.json -o new  # the fitted line on a new input
```

## Transform elements

```jsonc
{ "id": "ID", "type": "transform", "operators": [ { "kind": "...", ... }, ... ], "regularization": 1e-3 }
```

The operators are applied in order. Put the element where the change happens: after the stretcher for its residual
dispersion, after the amplifier for gain narrowing, at the end for an etalon in front of the diagnostics. Several
transform elements in one system are fine; each one's numbers are fitted together.

### Operators

| kind | what it is | parameters (all may be [fit objects](#marking-parameters-for-fitting)) |
|---|---|---|
| `spectralPhase` | dispersion: φ(ω) = GDD Δ²/2 + TOD Δ³/6 + FOD Δ⁴/24 + 5th Δ⁵/120 about the centre wavelength, plus optional spline knots | `gddFs2`, `todFs3`, `fodFs4`, `fifthFs5` (or `gddS2`, `todS3`, `fodS4`); `centerWavelengthM` / `Nm` (default: the element's carrier); `knots`: `{wavelengthsM` or `wavelengthsNm`, `phaseRad}` |
| `spectralAmplitude` | power transmission T(λ): gain narrowing, filters, spectral clipping | `shape`: `gaussian` / `superGaussian` (`fwhmM` / `fwhmNm`, `centerWavelengthM` / `Nm`, `order`), `tilt` (`tiltDbPerNm`, `curvatureDbPerNm2`), `spline` (`knots`: wavelengths with `transmission` or `transmissionDb`), `model` (any [filter model](../reference/models.md)); `peak`; `passive` (default true: T ≤ 1); `causal` (adds the minimum phase, see below) |
| `energy` | a flat power transmission | `transmission` or `gainDb` |
| `delay` | a pure group delay (matters only relative to other beams) | `delayS` or `delayFs` |
| `jones` | polarisation errors: retarder → diattenuator → rotation | `retardanceRad`, `axisDeg`, `diattenuation` (power lost by the weak axis, 0…1), `diattenuationAxisDeg`, `rotationDeg` |
| `replica` | a coherent pre- (negative delay) or post-pulse from a double reflection or an etalon: H = 1 + √r e^{i(Δτ + φ + GDD Δ²/2)} | `delayS` / `delayFs` / `delayPs`; `relativeIntensity` or `relativeIntensityDb`; `phaseRad`; `gddFs2` |
| `phaseNoise` | correlated spectral phase and amplitude noise: the coherent pedestal of stretchers and shapers | `rmsRad`, `amplitudeRms`, `correlationNm` / `correlationRadPerS`, `seed` |
| `pedestal` | an incoherent background that no linear filter makes: an ASE plateau or exponential wings | `type`: `ase` (`relativeIntensity` / `relativeIntensityDb` or `energyFraction`, `durationS`) or `exponential` (`level` / `levelDb`, `riseS`, `fallS`); `bandwidthFwhmNm`; `seed` |
| `wavefront` | wavefront error: Zernike terms (Noll index → waves rms) and pointing | `zernike`: `{"4": 0.1, "11": 0.02}`; `radiusM` (default: the beam's 1/e² radius); `wavelengthM` (default: the carrier); `tiltXRad`, `tiltYRad` |

The parameter reference with types and defaults is generated in the [option reference](../reference/options.md)
(`fitting:operator`).

**Linear and field operators.** All operators except `pedestal` and `wavefront` are linear: a 2×2 Jones transfer
H(ω). Consecutive linear operators compile into one ordinary linear term of the engine. The grid planner therefore
sees their dispersion and replica delays, they appear in the loss budget (mechanism `transform`), and backward
propagation inverts them exactly. `pedestal` adds light and `wavefront` acts on the transverse profile, so they run on
the whole field. Linear operators placed *between* field operators run inside that step, where the grid planner
cannot see their dispersion; put large GDD before or after the field operators.

**Causality (Kramers–Kronig).** A physical filter cannot change the amplitude without changing the phase. With
`"causal": true`, `spectralAmplitude` adds the minimum phase belonging to its amplitude, from the folded cepstrum
(the Bode gain–phase relation). Its impulse response is then causal, and the delay a real band-pass filter imposes
on the edges of the spectrum is part of the model. The relation is evaluated over the simulation band.

**Wavefront.** On a 1-D beam (the default temporal model) a wavefront error does not change the pulse. The element
reports `wavefrontRmsWaves` and a Strehl estimate exp(−(2π σ)²), with a note. When the beam carries a transverse
profile ([spatial models](../reference/transverse.md)), the optical path difference is applied to it at every
frequency. Downstream spatial elements, cameras, wavefront sensors and far-field samplings then see it. Radial models
apply only the rotationally symmetric terms (defocus, spherical aberration).

**Your own operators.** Derive from `TransformOperator` (C#), implement `Transfer` (linear) or `Apply` and `Invert`
(field operators), and register a factory with `TransformOperators.Register("myKind", spec => new MyOperator())` from
your plugin's `Register`. Read its numbers with `FitValues.Num(Spec, "key", default)` so they can be fit objects. Put
its keys under `"params"`, which is free-form in the schema; fit objects there are found like any other. Learned or
measured transfer matrices fit here.

## Marking parameters for fitting

Any number of an operator can be written as a **fit object** in place of the number:

```jsonc
"gddFs2": { "value": 0, "fit": true, "min": -3000, "max": 3000, "prior": 500, "scale": 200, "unit": "fs^2" }
```

| key | meaning |
|---|---|
| `value` | the number: used by plain runs, and the start of a fit |
| `fit` | `true` to fit it (default false: the object is then just a number with metadata) |
| `min`, `max` | bounds (recommended: they also set the scale) |
| `prior` | Gaussian prior: a σ (mean = `value`) or `{"mean": ..., "sigma": ...}` |
| `scale`, `step` | typical change (default (max − min)/4, else a per-quantity default such as 500 fs² for GDD), finite-difference step (1e-3 × scale) |
| `name`, `unit` | labels for reports (default `ID.kind.key`, unit from the key suffix) |

Alternatively, list names in the operator. This form is convenient for arrays such as spline knots:

```jsonc
{ "kind": "spectralPhase", "knots": { "wavelengthsNm": [720, 750, 775, 800, 825, 855, 890], "phaseRad": [2, 1, 0.3, 0, 0.3, 1, 2] },
  "fit": ["knots.phaseRad"], "bounds": { "knots.phaseRad": [-10, 10] }, "smoothness": 0.01 }
```

`fit` names keys (dotted for nested ones). An array fits every element, each with the given `bounds` and `priors`.
`smoothness` adds a penalty on the second differences of fitted knots (their curvature), which keeps a spline phase
or transmission from oscillating between data points. The splines use not-a-knot ends, so a pure GDD or TOD phase is
reproduced exactly by the knots.

A plain run (no `"fit"` block) uses the `value` of every fit object. It notes how many parameters are marked, so the
same system file serves for fitting and for prediction.

<span id="calibration-fit"></span>

## Fitting

```jsonc
"fit": {
  "estimator": "levenbergMarquardt",          // or "cmaes", "mcmc", or one a plugin registers
  "parameters": [                             // optional: numbers by path, in addition to the marked ones
    { "path": "instrument:spec/resolutionM", "initial": 1e-9, "min": 1e-10, "max": 5e-9 },
    { "path": "pulse/energyJ", "min": 1e-4, "max": 2e-3, "prior": { "mean": 1e-3, "sigma": 5e-5 } }
  ],
  "maxIterations": 30, "freezeGrid": true, "parallel": 1
}
```

**What is fitted.** Every number marked in a transform element is fitted, plus every entry of `parameters`. Paths
address any number in the files:

| path | where |
|---|---|
| `pulse/energyJ`, `pulse/chirp/gddFs2`, `pulse/centerWavelengthM` | the run's `pulse` |
| `element:ID/lengthM`, `element:amp/pump/powerW`, `element:ID/operators/0/gddFs2` | a property of an element (nested objects and lists with `/`, list indices from 0) |
| `instrument:NAME/resolutionM`, `instrument:NAME/detector/calibration` | an instrument of the run (by name, else by kind): **instrument-response parameters** such as resolution or calibration are fitted like the physics |
| `material:FusedSilica/n2` | a material property, applied as an [override](../reference/models.md) |
| `run/physics/...`, `system/...` | anything else in the run or system file |

Path entries take `name`, `initial` (default: the value in the files), `min`, `max`, `scale`, `step`, `unit`,
`prior`.

### Estimators

| estimator | what it does | runs | use when |
|---|---|---|---|
| `levenbergMarquardt` (default) | local, gradient-based least squares on scaled variables with bounds; forward-difference Jacobians | ~ (n + 1) × iterations | you start reasonably close; most fits |
| `cmaes` | covariance-matrix adaptation evolution strategy: global, derivative-free; finishes with Levenberg–Marquardt (`polish`) | population × generations (4 + 3 ln n per generation, `maxIterations` 60) | several minima, a poor start, noisy costs; give `min`/`max` for every parameter |
| `mcmc` | affine-invariant ensemble sampler (emcee stretch move) of the posterior exp(−χ²/2), started from a Levenberg–Marquardt optimum | walkers × steps (default max(2n+2, 8) × 200) | you need credible intervals and non-Gaussian uncertainties |

Options are in the fit block. All estimators take `maxIterations`, `tolerance` and `seed`. CMA-ES also takes
`population`, `sigma0` (initial step in units of each parameter's scale), `polish` and `polishIterations`. MCMC also
takes `walkers`, `steps`, `burnIn` (fraction, 0.5), `thin`, `start` (`optimum` | `initial`) and `spread`.
`parallel: k` runs k simulations at a time: Jacobian columns, CMA-ES populations, MCMC half-ensembles. Each
simulation already uses all cores for its FFTs, so this helps most for small grids.

**Your own estimator.** Implement `IEstimator` (`Name`, `Description`, `Estimate(FitProblem, options)`) and call
`Estimators.Register(new MyEstimator())` from a plugin. The problem gives the residual function (one simulation
per call), bounds, scales and steps. Return the best point, plus posterior samples if you have them. Ensemble
Kalman, Bayesian-optimisation and learned estimators plug in this way.

### What the fit does

* **Weighted least squares.** The fit minimises χ² = Σ ((simulated − measured)/σ)² over every point of every
  measured instrument, plus the priors ((x − mean)/σ)² and the smoothness penalties. Data compared in dB
  (`"scale": "log"`) contribute (Δ dB / σ_dB)².
* **Frozen grid.** The time–frequency grid is fixed for all runs (`freezeGrid`, default on) at twice the planned
  bandwidth, so the grid does not jump between nearby parameter values. Set it to false when you give a fixed grid
  yourself (`"grid": {"auto": false, ...}`). That keeps random operators (ASE pedestals, phase noise) on exactly the
  same realisation as your forward runs.
* **Uncertainties.** These come from the covariance C = (JᵀJ)⁻¹ at the optimum, computed by a pseudo-inverse so
  that directions the data cannot resolve do not explode. If the reduced χ² is above 1, they are scaled up by
  √(reduced χ²). With `mcmc` they come from the posterior samples instead.
* **Diagnostics** (in `warnings`):
  * correlations above 0.95;
  * parameters that affect no instrument, or that only their prior determines;
  * unidentifiable combinations (null directions of JᵀJ);
  * parameters at a bound;
  * no convergence;
  * a low MCMC acceptance rate.

### The three result layers

`fit.json → layers` keeps the three states of knowledge apart:

| layer | what it is |
|---|---|
| `simulated` | the system as described, at the initial values: χ² per instrument and every instrument reading. This is what the design predicts. |
| `inferred` | the best fit: the same readings with the fitted values (the run result itself). This is what the data say the system is. |
| `believed` | the fitted values with their uncertainty: σ, 68 % and 95 % intervals (Gaussian from the covariance, or credible intervals and medians from MCMC samples), and the predictions with 1σ for every instrument and element |

The measurement figure draws the simulated layer as a dashed curve ("before the fit") next to the fitted one and
its 1σ band.

### Influence map

`fit.json → influence` gives, for each parameter, the share of its Fisher information that each instrument
provides (Σ J² over that instrument's points divided by the total), with `priors` as its own entry. It shows which
measurement determines which number: in example 26 the d-scan fixes the GDD (99 %), the cross-correlator fixes the
post-pulse and pedestal levels (98–100 %), and the FOD is half data, half prior. A parameter that is 100 % `priors`
is not measured at all. `fit.png` draws the map.

### Predictions

The best run is the run result: all instruments, elements, probes and plots are at the fitted values. In addition,
each number gets a 1σ uncertainty by linear propagation, σ² = gᵀ C g:

* every scalar of every instrument, measured or not (`<value>Sigma` next to the value, and
  `predictions["<instrument>.<value>"]`);
* per element: `energyOutJ`, `fwhmS`, `peakPowerW`, `spectralFwhmM`, `bIntegralRad`;
* a 1σ band on each compared curve or trace (`<name>_simulated_sigma`).

To predict an instrument you do not have, add it to the run without `"measured"`.

### Outputs

| file / key | contents |
|---|---|
| `fit.json` (and `summary.json → fit`) | `estimator`, `converged`, `iterations`, `evaluations`, `points`, `chi2Initial`, `chi2Final`, `dataChi2Final`, `reducedChi2`, `parameters` (name, path, origin, unit, initial, value, sigma, bounds, atBound, prior), `correlation`, `costHistory`, `instruments` (χ² and rms error before / after), `predictions`, `layers`, `influence`, `provenance`, `warnings`, `notes` |
| `fitted-system.ufs` | your system file with the fitted values in place of the fit objects and the operators' `fit` lists removed: a plain system for new inputs. `source.fitted` records the fitted values with σ and points to `fit.json`. |
| `fitted-run.json` | the run spec with fitted run-side values (pulse, instrument responses, material overrides) and the grid used |
| `provenance` (in fit.json) | engine and plugin versions, estimator and options, start time and duration, each data file with its **SHA-256**, and the origin of every parameter (marked in a transform, or listed by path) |
| arrays | `<name>_nominal_on_measured` (simulated layer), `<name>_simulated_on_measured`, `<name>_simulated_sigma` |

The CLI prints the parameters with σ and each instrument before → after. `ultrafast-plot` draws `measurements.png`
and `fit.png`.

### Applying the fitted transforms to new inputs

```bash
ultrafast run results-fit/fitted-system.ufs new-input.json -o results-new
```

The fitted system is an ordinary system file. Change the seed, the energy or the downstream elements, and the
transforms carry what the data taught the model. In example 26 the design predicts 114 fs and 730 µJ for a new
70 fs seed, while the fitted line predicts 150 fs and 460 µJ, with the post-pulse and pedestal included.

## Backward propagation

```jsonc
"pulse": { "shape": "spectrum", "energyJ": 2.6e-4, "spectrum": { "csv": "data/output_field.csv" } },   // measured at the output
"backward": { "from": "end", "to": "input", "regularization": 1e-3, "active": "error" }
```

The run's pulse is the field measured at plane `from` (an element id, after that element, or `"end"`), typically a
spectrum with the phase from a FROG or d-scan retrieval. The engine undoes the elements between `from` and `to` in
reverse order and records the field at every plane in between. Instruments placed `at` those planes measure it
(`"at": "input"` is the reconstructed input), and the reconstructed field is written to `reconstructed-pulse.json`
as a pulse object you can paste into a new run spec.

| element | how it is undone |
|---|---|
| linear terms (dispersion, materials, gratings, coatings, filters, polarisation optics, transform linear operators) | regularised inverse of the Jones transfer: H⁻¹ ≈ (HᴴH + ε²)⁻¹Hᴴ with ε = `regularization` × the largest transfer. Bins where the transmission is below ~ε are not recoverable; the record reports the fraction of the spectral energy there |
| nonlinear segments (Kerr, Raman, self-steepening in plates, gases, fibres) | integrated with z reversed, dA/ds = −L A − N(A, L − s), exact up to the step tolerance (absorption becomes gain and is capped at 1/1000 per element) |
| transform field operators | `wavefront` is undone exactly; an incoherent `pedestal` cannot be removed and is passed through with a note |
| active elements (amplifiers, crystals, spatial elements, user elements) | `active: "error"` (default) refuses; `"linearized"` undoes each as the filter A_out/A_in it was in a forward reference run of `referencePulse`; `"identity"` passes them through |

A transform element can set its own `regularization`. Backward propagation is deterministic and cheap: one pass,
the same cost as a forward run. It amplifies noise wherever the system attenuated the light. Give the measured
field over its full bandwidth, and prefer a larger `regularization` for noisy data.

## Measured data: comparing with recordings

**Every measurement has a finite resolution and is sampled at particular points.** Model the recording instrument
the way it is: the spectrometer's `resolutionM` and its pixel axis, the autocorrelator's delay steps, the
cross-correlator's scan grid and noise floor, the camera's pixels. The common instrument key
[`axis`](../reference/instruments.md#axis-where-readings-are-taken) sets where any curve instrument takes its
readings: explicit points, from/to/step, pixel integration and a Gaussian resolution. State the recording's
resolution in `measured` (`resolutionM` / `resolutionS`) and the plugin checks it against the simulated instrument.
If you do not know the resolution, fit it: `instrument:NAME/resolutionM` is a fit parameter like any other.

Give an instrument a `"measured"` entry: a file name, or an object with options.

```jsonc
{ "kind": "spectrometer", "name": "spec", "at": "end",
  "measured": "data/spectrum.csv" }                         // shortest form

{ "kind": "autocorrelator", "name": "ac", "at": "end",
  "measured": { "file": "data/ac.csv", "sigma": 0.02, "background": "auto", "normalize": "peak" } }

{ "kind": "powerMeter", "name": "pm", "at": "end",
  "measured": { "values": { "energyJ": [1.41e-7, 4e-9] } } }   // scalar readings: value or [value, sigma]
```

The run then:

1. reads the file and converts the axes to SI units (from the column names);
2. interpolates the simulated reading onto the measured axis;
3. normalises both;
4. reports the residuals, χ² and an rms error.

Nothing is fitted unless the run has a [`"fit"`](#calibration-fit) block. Comparing is cheap: it costs no extra
simulation.

### File formats

**CSV** (`.csv`, `.txt`, `.dat`): one header row with column names, then numbers. The separator (comma, semicolon,
tab or spaces) is detected, and lines starting with `#` are ignored. Without a header, the columns are called
`col0`, `col1`, ...

```
wavelength_nm,counts
700.0,24
700.5,29
...
```

**NPZ** (`.npz`, `numpy.savez`): named arrays. This is the format for 2-D traces:

```python
np.savez("dscan.npz", insertion_mm=z, wavelength_nm=lam, trace=T)    # T.shape == (len(z), len(lam))
np.savez("frog.npz", delay_fs=tau, wavelength_nm=lam, trace=F)       # rows = delay, columns = wavelength
```

Supported: `np.savez` and `np.savez_compressed`; dtypes float64/32, int64/32, uint8, bool; C or Fortran order.

**JSON** (`.json`): scalar readings, `{"energyJ": 1.44e-7, "fwhmS": [35e-15, 2e-15]}`. The names are the
instrument's value names (see [Instruments](../reference/instruments.md)). Each value is either a number or `[value, sigma]`.

### Units from column names

The part after the last `_` is the unit:

| suffix | factor | | suffix | factor |
|---|---|---|---|---|
| `nm`, `um`, `mm`, `cm` | 1e-9, 1e-6, 1e-3, 1e-2 | | `fs2`, `ps2`, `fs3` | 1e-30, 1e-24, 1e-45 |
| `fs`, `ps`, `ns`, `us`, `ms` | 1e-15 … 1e-3 | | `pj`, `nj`, `uj`, `mj` | 1e-12 … 1e-3 |
| `uw`, `mw`, `kw` | 1e-6, 1e-3, 1e3 | | `thz` | 1e12 |

Anything else (or no suffix) is taken as SI. A wavelength column without a unit and with values above 1e-3 is
assumed to be in nm (with a note). Override with `"xUnit"`, `"yUnit"`, `"rowUnit"`, `"colUnit"` (e.g. `"um"`).

### Which columns are used

The columns are found by name: the first column that equals, or starts with, one of these names followed by `_`.
Choose explicitly with `"columns": {"x": "lambda_nm", "y": "I", "sigma": "err"}` (curves) or
`{"rows": ..., "cols": ..., "trace": ...}` (traces).

| instrument | compared with | measured x / rows | measured y / cols | trace |
|---|---|---|---|---|
| `spectrometer` | spectrum vs wavelength | `wavelength`, `lambda`, `wl`, `x` | `spectrum`, `intensity`, `counts`, `signal`, `y` | |
| `autocorrelator` | trace vs delay | `delay`, `tau`, `x` | `signal`, `ac`, `intensity`, `y` | |
| `crossCorrelator` | contrast trace vs delay (use `"scale": "log"`) | `delay`, `tau`, `x` | `signal`, `intensity`, `y` (a column ending `_db`, or `"yUnit": "dB"`, is read as dB) | |
| `scope` | detected power vs time | `time`, `t`, `x` | `signal`, `power`, `voltage`, `y` | |
| `beamProfile` | fluence vs radius (or x cut) | `radius`, `r`, `x` (`x`, `position`) | `fluence`, `intensity`, `signal`, `y` | |
| `delayScan` | output energy vs aux delay | `delay`, `x` | `energy`, `signal`, `y` | |
| `phaseMatchingCurve` | efficiency vs the scan variable | `detuning`, `temperature`, `period`, `wavelength`, `x` | `efficiency`, `signal`, `y` | |
| `frog` | trace [delay × wavelength] | `delay`, `tau` | `wavelength`, `lambda` | `trace`, `frog` |
| `dscan` | trace [insertion or GDD × SHG wavelength] | `insertion`, `glass`, `z` (or `gdd`) | `wavelength`, `shg_wavelength`, `lambda` | `trace`, `dscan` |
| `camera` | fluence image [x × y] | `x` | `y` | `image`, `fluence`, `counts` |
| any (values) | scalar values | | | |

A CSV curve may have a `sigma` / `err` / `error` / `std` column with per-point uncertainties (in the y units).

Any other instrument, including your own, can be compared through `"simulated": {"x": "<array suffix>", "y": "<array
suffix>"}` (or `rows`/`cols`/`trace`). These name the instrument's own arrays to compare with, and the data columns are
then called `x`/`y` (or picked with `"columns"`).

### Options

| key | default | meaning |
|---|---|---|
| `file` | | CSV / NPZ / JSON, relative to the run file |
| `columns` | auto | see above |
| `normalize` | `peak` | `peak`: the simulation is scaled to a maximum of 1 and the data are scaled onto it by least squares, which is robust against noise on the measured maximum. `maximum`: both divided by their maxima. `area`: both to unit area. `scale`: the simulation times a best-fit factor (reported as `measuredScale`), with the data left in their own units. `none`: absolute comparison (data in SI units of the simulated reading). |
| `sigma` | 0.02 | uncertainty of each point, relative to the data peak (when there is no sigma column) |
| `background` | none | a number to subtract, or `"auto"`: the median of the data over the first and last 5 % of the axis (for traces: of the columns), where a recording is normally dark. Use a number if your data have signal at the edges. |
| `rangeX` | all | `[min, max]` in SI: compare only this part of the curve |
| `xUnit`, `yUnit`, `rowUnit`, `colUnit` | from names | unit overrides |
| `values` | | scalar readings `{name: value}` or `{name: [value, sigma]}`; can be combined with a file |
| `valueSigma` | 0.02 | relative uncertainty of values given without one |
| `valueWeight` | 1 | extra weight of the scalar residuals |
| `weight` | 1 | weight of the whole instrument in the fit's χ² |
| `simulated` | | which arrays of the instrument to compare with (see above) |
| `scale` | `linear` | `log`: compare in dB, so a −90 dB pedestal weighs as much as the peak (contrast and cross-correlator data, spectral wings) |
| `sigmaDb` | 1 | `scale: log`: uncertainty of each point in dB |
| `floorDb` | the data's minimum | `scale: log`: floor relative to the peak; values below it are clipped to it on both sides |
| `resolutionM`, `resolutionS` | | the recording instrument's resolution: compared with the simulated instrument's (`resolutionM` / `spectralResolutionM`, `resolutionS` / `riseTimeS`, `axis.resolution`), and a mismatch is reported |

**Choosing σ.** χ² only means something if σ is the real noise level. The *reduced* χ² (χ² per point) should be
close to 1 for a good model with the right σ. If it is much below 1, σ is too large; if it is far above 1, the model
misses something or σ is too small. The fit uses σ to weight instruments against each other, so a 2-D trace with
thousands of points dominates a scalar reading unless you set `weight`.

### Outputs

* `summary.json → comparisons[]`: one entry per measured instrument, with `instrument`, `kind`, `at`, `mode`
  (`curve`, `trace`, `values`), `points`, `chi2`, `reducedChi2`, `rmsError` (rms of simulated − measured over the
  data peak; for traces this is the FROG "G error"), `normalization`, `values` (measured, simulated, sigma,
  deviation) and `notes`. `summary.json → measuredChi2` is the sum.
* Instrument values: `measuredChi2`, `measuredRmsError`, `measuredScale` (with `normalize: scale`),
  `measured.<value>`.
* Arrays (`result.npz`, `<name>_...`): curves `measured_x`, `measured_y`, `simulated_on_measured`,
  `measured_sigma`; traces `measured_rows`, `measured_cols`, `measured_trace`, `simulated_on_measured`.
* The CLI prints a χ² / rms line per instrument. `ultrafast-plot` draws `measurements.png`: measured points and
  the simulated curve with the residual, measured / simulated / difference images for traces, and scalar readings
  in units of σ.

If a file cannot be read or does not fit the instrument, the run continues and gives a warning
(`Instrument X: measured data not compared: ...`).

## Python

```python
import ultrafast as uf
from ultrafast import build as b
fx = uf.plugins.module("fitting")          # or: import ultrafast_fitting as fx

line = b.system("cpa", [
    b.dispersion("stretcher", gdd_s2=1e-25),
    fx.transform("lab", [fx.spectral_phase(gdd_fs2=fx.fit_value(0, -3000, 3000)),
                         fx.spectral_amplitude("gaussian", fwhm_m=fx.fit_value(20e-9, 5e-9, 60e-9)),
                         fx.replica(6e-12, relative_intensity_db=fx.fit_value(-50, -90, -20))]),
    b.dispersion("compressor", gdd_s2=-1e-25)])
spec = b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-3),
                  instruments=[b.instrument("spectrometer", resolutionM=0.2e-9, measured=fx.measured("spectrum.csv", background="auto")),
                               b.instrument("crossCorrelator", rangeS=[-15e-12, 15e-12], measured=fx.measured("xc.csv", scale="log"))],
                  fit=fx.fit(estimator="levenbergMarquardt"))
res = uf.run(line, spec)
report = fx.fit_report(res)                # fit.json as a dict: parameters, layers, influence, provenance
fitted = fx.fitted_system(res)             # the system with the fitted values
new = uf.run(fitted, b.run_spec(b.pulse(1028e-9, 70e-15, energy=2e-3)))

back = uf.run(line, b.run_spec(measured_output_pulse, backward=fx.backward("end", "input")))
seed = fx.reconstructed_pulse(back)        # a pulse dict for new runs
```

Builders: `transform`, `spectral_phase`, `spectral_amplitude`, `energy`, `delay`, `jones`, `replica`, `phase_noise`,
`pedestal`, `wavefront`, `fit_value`, `fit`, `fit_parameter`, `backward`, `measured` (all with type hints).
Accessors: `comparisons`, `fit_report`, `fitted_system`, `reconstructed_pulse`. Core helpers for recordings:
`b.axis(...)` (instrument sampling) and `b.measured_spectrum(...)` (a measured input spectrum with its resolution).

## Practical advice

* **Put each transform where its physics is.** Residual dispersion of a stretcher belongs after the stretcher, a
  gain-narrowing filter after the amplifier. Placed this way the fitted transform still means something when you
  change the seed or a downstream element.
* **Fit levels in dB.** Contrast features span decades: use `relativeIntensityDb` and `levelDb`, and
  `"scale": "log"` on the cross-correlator data.
* **Choose instruments that separate parameters.** Look at the correlations and the influence map. A spectrum fixes
  amplitude but not phase, an autocorrelation cannot tell the sign of the chirp, and a d-scan or FROG can (SHG FROG
  is symmetric in time: start a phase fit away from zero, where the gradient vanishes).
* **Start close, or go global.** Levenberg–Marquardt finds the nearest minimum. If you are unsure, run `cmaes` with
  bounds; it finishes with Levenberg–Marquardt.
* **Add priors for what the data cannot see**, and smoothness for splines. The influence map tells you which
  parameters rest on priors.
* **Model the instrument**, including its resolution, sampling and where it looks (`sampling`). A fit that compares
  an on-axis measurement with a whole-beam simulation biases the parameters to explain the difference.
* **Look at the residuals** in `measurements.png`. Structure rather than noise means the model misses something,
  which is often a missing transform operator.

## Limitations

* Fits are static: one set of recordings, fitted once. Live re-fitting as data arrive is planned (see the
  [roadmap](../roadmap.md)).
* Uncertainties from the covariance are linearised; use `mcmc` when the posterior is not Gaussian.
* Backward propagation through amplifiers and crystals is linearised about a reference run. Saturated gain and
  frequency conversion cannot be undone exactly. Incoherent pedestals cannot be removed, and light the system
  absorbed completely is not recoverable.
* Wavefront operators change the pulse only when the beam carries a transverse profile (spatial models).
* General low-rank and learned field operators are not built in: add them as your own operator kinds (above).
