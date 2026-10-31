# Results

`ultrafast run ... -o DIR` (or `RunResult.WriteAll`, `Result.write()` in Python, `uf_result_write`) writes:

| file | content |
|---|---|
| `summary.json` | engine/grid info (`grid`: input grid, `outputGrid`: the final pulse's grid), input and output pulse metrics, totals, energy budget, one record per element, instruments, notes, warnings, and a manifest of all arrays (name, shape, unit, description) |
| `result.npz` | every array (`numpy.load`), float64 |
| `provenance.json` | where every model/value came from |
| `elements.csv` | the per-element table |
| `spectrum.csv`, `temporal.csv` | input/output spectra (+ unwrapped phase) and temporal power, cropped to where the energy is |
| `<instrument>.csv` | instrument traces (2D traces as their own CSV) |
| `fit.json` | only for runs with `"fit"`: fitted parameters ± σ, correlations, cost history, χ² per instrument, predictions ([Fitting plugin](../plugins/fitting.md#calibration-fit)) |

## Pulse metrics (`input`, `output`, and per element `pulse`)

energyJ, peakPowerW, fwhmS, rmsWidthS, transformLimitFwhmS, temporalStrehl (peak / TL peak), spectralFwhmM,
spectralRmsWidthM, centroidWavelengthM, peakWavelengthM, timeBandwidthProduct, pulseGddS2 / pulseTodS3 (cubic
fit of the spectral phase above -10 dB), centroidTimeS, edgeEnergyFraction (energy in the outer 5 % of the
window: wrap-around check), stokes [s1, s2, s3], degreeOfPolarization.

## Per-element records

index, id, type, label, lengthM, cumulativeLengthM, energyInJ/OutJ, lossJ by mechanism, arrivalTimeS (lab-frame
carrier delay so far), own and cumulative system dispersion (GD, GDD, TOD at the carrier), bIntegralRad and
cumulative, peakIntensityWPerM2, peakFluenceJPerM2, lidtFluenceJPerM2, lidtMargin, outsideDataFraction,
criticalPowerRatio, solver statistics (method, representation, accepted/rejected steps, min/max step), the
list of linear terms applied, and notes. Active elements add:

* `carrierWavelengthM` -- the main beam's carrier after the element (changes at crystals with another `output`);
* `representation` -- "1-D coupled waves, 3 fields x 1024 samples", "radial Hankel 96 points x 512 samples", ...;
* `beam` -- `radiusXM`, `radiusYM` (about the centroid), `m2` (along x) after elements that produce a transverse profile;
* `values` -- element-specific numbers (crystal: angles, deff, Δk, QPM period, `conversionEfficiency`,
  `energy_<field>_J`, `manleyRoweResidual`; spatial bulk: `peakFluenceOutJPerM2`, `onAxisFluenceOutJPerM2`);
* `byproducts` -- pulse metrics of every recorded by-product (`idler`, `pump`, ...).

## Comparisons and fit

Runs with measured data add `comparisons[]` (one per measured instrument: χ², reduced χ², rms error, scalar
readings, notes) and `measuredChi2` to `summary.json`. The arrays are `<instrument>_measured_x` / `_measured_y` /
`_simulated_on_measured` (NaN outside the simulated instrument's range) / `_measured_sigma` (curves) and
`_measured_rows` / `_measured_cols` / `_measured_trace` (traces). See [Measured data](measured-data.md). Fits
(fitting plugin) add `fit` (the content of `fit.json`), every instrument value gets a `<value>Sigma`, and curves get
`_simulated_sigma` and `_nominal_on_measured`; see [Fitting plugin](../plugins/fitting.md).

## Energy budget

`energyBudget` (also written as `lossBudget` for compatibility) sums every element's ledger by mechanism:
`{ "absorption": {"energyJ": .., "fractionOfInput": ..}, ... }`. Losses are positive. Active elements add
**negative** entries for energy brought in by aux beams (`from pump (aux)`) and positive `to <field>` entries for
energy leaving as by-products, so for every element: energy in − energy out = sum of its entries.
`fractionOfInput` is relative to the run's input energy (it can exceed 1 when aux beams bring in more).

## Plotting

`ultrafast-plot <results folder>` draws all of this (see [Python > Plotting results](python.md#plotting-results)).

## Arrays

`input_*` / `output_*`: `time_s`, `power_w`, `power_x_w`, `power_y_w`, `omega_rad_s`, `wavelength_m`, `spectrum`
(per unit omega), `phase_x_rad`, and (full-resolution exports) the complex spectral envelopes `ax_re`, `ax_im`,
`ay_re`, `ay_im` in grid order. `elements`: the per-element table as a matrix (columns in the manifest).
`probe_<id>_*`: compact snapshots at probes (cropped to the support, <= 2048 points); by-products of active
elements are always snapshotted as `probe_<id>_<field>_*`. Instrument arrays are prefixed with the instrument name.
When the input and output grids differ, `spectrum.csv` / `temporal.csv` are written per pulse.

From C#, `RunResult` also keeps the full `PulseField` at every element (`At[id]`) and every by-product
(`At["<id>#<field>"]`), the input and output pulses (each with its own `Grid`, and `Profile` when a transverse
model ran) and the compiled plan.
