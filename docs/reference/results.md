# Results

A run returns a `RunResult`. `ultrafast run ... -o DIR` (or `RunResult.WriteAll`, `Result.write()` in Python, `uf_result_write`) writes the files below. Which files appear is set by the run spec's [`outputs`](options.md#outputs) switches. Use this page to find where a number is in `summary.json` and what each validity code means.

## Keys

| file | content | written when |
|---|---|---|
| `summary.json` | everything below except the arrays | `outputs.json` (default true) |
| `provenance.json` | where every model and value came from | `outputs.json` |
| `result.npz` | every array (`numpy.load`), float64 | `outputs.npz` |
| `elements.csv`, `spectrum.csv`, `temporal.csv`, `<instrument>.csv` | the per-element table, input and output spectra (with unwrapped phase) and temporal power cropped to where the energy is, instrument traces | `outputs.csv` |
| `fit.json` | fitted parameters ± σ, correlations, cost history, χ² per instrument, predictions ([Fitting plugin](../plugins/fitting.md#calibration-fit)) | runs with `"fit"` |

`outputs.maxPoints` crops arrays and CSV files to that many points; 0 (default) writes the full grid, including the complex envelopes. When the input and output grids differ (a carrier change), the CSV files are `spectrum_input.csv` and `spectrum.csv`, `temporal_input.csv` and `temporal.csv` (the output keeps the plain name).

Top-level keys of `summary.json`:

| key | content |
|---|---|
| `format`, `version`, `engine`, `system`, `run`, `elapsedS` | identity and run time |
| `grid`, `outputGrid` | the input grid and the final pulse's grid |
| `input`, `output` | [pulse metrics](#pulse-metrics-input-output-and-per-element-pulse) |
| `totals` | `energyTransmission`, `arrivalTimeS`, `systemGroupDelayS`, `systemGddS2`, `systemTodS3`, `bIntegralRad` (sum over elements), `bIntegralPulseAveragedRad`, `bIntegralBeamAveragedRad`, `lengthM` |
| `energyBudget` (also `lossBudget`) | [energy by mechanism](#energy-budget) |
| `tail` | delayed-light ledger, only with `grid.tail`: `n`, `windowS`, `dtS`, `factor`, `energyJ`, `fractionOfOutput`, `edgeEnergyFraction`, `exactSteps`, `separateSteps` ([Delayed light](delayed-light.md)); its energy is not part of the budget |
| `elements` | [one record per element](#per-element-records) |
| `graph` | node and packet bookkeeping of a Branched system ([Branches](branches.md)) |
| `instruments`, `comparisons`, `measuredChi2` | [instrument readings](instruments.md); [comparisons](#comparisons-and-fit) |
| `probes`, `arrays` | names of the probes that exist; a manifest of every array (name, shape, unit, description) |
| `notes`, `warnings`, `validity` | messages and [validity entries](#validity-validity) |
| `fidelityReport`, `unverifiedInputs` | the [fidelity](fidelity.md) assessment; inputs with an unverified status |
| plugin keys | for example `fit` |

## Pulse metrics (`input`, `output`, and per element `pulse`)

energyJ, peakPowerW, fwhmS, rmsWidthS, transformLimitFwhmS, temporalStrehl (peak / TL peak), spectralFwhmM,
spectralRmsWidthM, centroidWavelengthM, peakWavelengthM, timeBandwidthProduct, pulseGddS2 / pulseTodS3 (cubic
fit of the spectral phase above -10 dB), centroidTimeS, edgeEnergyFraction (energy in the outer 5 % of the samples at each end of
the window, 10 % in total: wrap-around check), peakCount (separate intervals above half maximum: 1 for a single pulse; above 1 the FWHM spans several
pulses and the run warns for the output), fwhmEnergyFraction (energy between the outermost half-maximum crossings: 0.761 for a Gaussian,
1/√2 for sech²), mainLobeFwhmS, mainLobeEnergyFraction and satellitePeakCount ([below](#main-lobe-of-a-multi-peaked-pulse)),
stokes [s1, s2, s3], degreeOfPolarization. For a multi-peaked pulse use mainLobeFwhmS, rmsWidthS, temporalStrehl or the trace, not fwhmS.

### Main lobe of a multi-peaked pulse

`fwhmS` is the span between the **outermost** half-maximum crossings. Once a satellite, a pre-pulse or a pedestal rises above 50 % of the
peak, `fwhmS` jumps to include it: in a multipass-cell soliton compressor a pulse whose main peak stays about 10 fs long can read more than
100 fs. Three fields describe the main peak alone. They are reported wherever `fwhmS` is reported for a pulse: `input`, `output`, every
element's `pulse`, and the `pulseSummary` instrument (the `compressor` instrument reports `compressedMainLobeFwhmS` and
`compressedMainLobeEnergyFraction` for its compressed pulse).

* `mainLobeFwhmS`: the main lobe is the contiguous run of time samples with power at least half the maximum that contains the global
  maximum (its first sample if the maximum repeats). `mainLobeFwhmS` is the distance between its two half-maximum crossings, each linearly
  interpolated between the samples on either side, as for `fwhmS`. It equals `fwhmS` for a single pulse and is smaller when another peak also
  crosses half maximum. If the lobe reaches the edge of the time window it is cut at the edge sample: the width is then a finite lower bound, and the multi-peak warning says "at least" and asks for a larger window. `mainLobeEnergyFraction` is computed to the window edge in that case.
* `mainLobeEnergyFraction`: the energy of the main lobe over the energy of the whole window. Starting at each half-maximum crossing and walking
  outward, the lobe ends at the first sample that is a local minimum of the power (the next sample outward is higher), or at the point where
  the power falls to 5 % of the peak (linearly interpolated), whichever comes first; at the window edge if neither occurs. The energy is the
  integral of the linearly interpolated power between those two bounds. For a Gaussian the bounds are the 5 % points,
  t = ±(FWHM/2) √(ln 20 / ln 2), and the fraction is erf(√ln 20) = 0.9856. For a satellite well separated from the main pulse the fraction is
  the main pulse's share of the energy times 0.9856; for overlapping lobes the bound is the minimum between them.
* `satellitePeakCount`: `peakCount` − 1, the number of separate half-maximum intervals other than the main lobe (0 for a single pulse).

When the output has more than one peak above half maximum the run warns and quotes both widths. Tests:
`ValidityTests.MainLobe_TouchingTheWindowEdge_IsFiniteAndFlagged` (a half lobe at the edge gives 0.5 of the FWHM, flagged), `ValidityTests.MainLobe_OfAGaussian_IsTheFwhm_AndHoldsErfOfSqrtLn20`, `ValidityTests.MainLobe_OfADoublePulse_IsTheSinglePulseWidth_WhileTheFwhmSpansBoth`.
The figures `ultrafast.plotting.evolution`, `elements` and `animate_evolution` plot or quote `mainLobeFwhmS` when the summary has it
(older summaries without it fall back to `fwhmS`).

## Validity (`validity`)

One entry per element and check that crossed its threshold: `{code, elementId, value, threshold, message}`. Codes and thresholds: `zeroFrequency`
(more than 1e-7 of the energy at ω ≤ 0 on the envelope grid: nonlinear broadening reached zero frequency), `criticalPower` (peak power above 0.3 P_cr in a 1-D element),
`kerrLensCollapse` (the Kerr lens collapses the beam inside the element), `lidt` (`lidtMargin` below 1: peak fluence above the scaled LIDT), `windowEdge` (`edgeEnergyFraction` above 1e-3). Each check also gives one
human warning in `warnings`, which names every element where it fired (a 46-pass cell gives one warning listing the passes, not only the first).
`physics.strictValidity: true` or the environment variable `ULTRAFAST_STRICT_VALIDITY=1` turns any entry into an error after the run
(`ValidityException`; the C# exception keeps the whole result). The zero-frequency check fires only when the output spectrum reaches the blocked
bins at ω ≤ 0 (before 2026-11-17 any energy deficit, including the stepper's own error, was reported as reaching zero frequency).

## Per-element records

index, id, type, label, lengthM, cumulativeLengthM, energyInJ/OutJ, lossJ by mechanism, arrivalTimeS (lab-frame
carrier delay so far), `own` and `system` (each `{groupDelayS, gddS2, todS3}`: the element's own and the cumulative system dispersion; GD at the grid centre, where the pulse sits and the arrival time is counted;
GDD and TOD at the requested carrier wavelength, not at the grid centre, which is snapped to the frequency step and can be up to half a step away:
a 1000 fs² element with 2000 fs³ TOD reports 1000 fs², not 1004 fs² at the snapped centre of a 30 fs run), bIntegralRad and
cumulativeBIntegralRad (the on-axis peak, [nonlinear](nonlinear.md)), bIntegralPulseAveragedRad and bIntegralBeamAveragedRad (1-D elements), peakIntensityWPerM2, peakFluenceJPerM2, lidtFluenceJPerM2, lidtMargin, outsideDataFraction,
criticalPowerRatio, `solver` (method, representation, accepted/rejected steps, min/max step), `terms` (the linear terms applied), `pulse` (the metrics after the element) and `notes`. Active elements add:

* `carrierWavelengthM` -- the main beam's carrier after the element (changes at crystals with another `output`);
* `representation` -- "1-D coupled waves, 3 fields x 1024 samples", "radial Hankel 96 points x 512 samples", ...;
* `beam` -- `radiusXM`, `radiusYM` (about the centroid), `m2` (along x) after elements that produce a transverse profile;
* `values` -- element-specific numbers (crystal: angles, deff, Δk, QPM period, `conversionEfficiency`,
  `energy_<field>_J`, `manleyRoweResidual`; spatial bulk: `peakFluenceOutJPerM2`, `onAxisFluenceOutJPerM2`);
* `byproducts` -- pulse metrics of every recorded by-product (`idler`, `pump`, ...);
* `aux` -- energy of each aux beam; `arrays` -- names of the element's own arrays.

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
`fractionOfInput` is relative to the run's input energy (it can exceed 1 when aux beams bring in more). Delayed-light energy (`tail`) is booked separately.

## Plotting

`ultrafast-plot <results folder>` draws all of this (see [Python > Plotting results](python.md#plotting-results)).

## Arrays

`input_*` / `output_*`: `time_s`, `power_w`, `power_x_w`, `power_y_w`, `omega_rad_s`, `wavelength_m`, `spectrum`
(per unit omega), `phase_x_rad`, and (only with `outputs.maxPoints` = 0) the complex spectral envelopes `ax_re`, `ax_im`,
`ay_re`, `ay_im` in grid order (the time-domain envelope, convention e^{-iωt}, is `numpy.fft.fftshift(numpy.fft.fft(numpy.fft.ifftshift(a)))`). `elements`: the per-element table as a matrix (the columns are listed in the array's description in the manifest).
`probe_<id>_*`: compact snapshots at probes (cropped to the support, <= 2048 points); by-products of active
elements are always snapshotted as `probe_<id>_<field>_*`. When a probe in a Branched system is passed by more than one
packet, each packet gets its own set, in the order the packets arrived: `probe_<id>_packet1_*`, `probe_<id>_packet2_*`, ...
(a probe seen by one packet keeps `probe_<id>_*`). `summary.probes` lists the names that exist, so `probe_<name>_spectrum`
is always a valid array. Array names in `result.npz` are unique. Instrument arrays are prefixed with the instrument name.

From C#, `RunResult` also keeps the full `PulseField` at every element (`At[id]`) and every by-product
(`At["<id>#<field>"]`), the input and output pulses (each with its own `Grid`, and `Profile` when a transverse
model ran) and the compiled plan.

## Validation

`ValidityTests` checks the main-lobe width and energy fraction against the Gaussian value erf(√ln 20) = 0.9856, the double-pulse width, the window-edge case, the validity entries (one per pass of a multipass cell) and the pulse- and beam-averaged B-integral against their analytic fractions of the peak (2/3 for sech²).

## Limits

* The summary keeps one record per element, not per step; use a `probe` or an instrument for a snapshot inside an element.
* `mainLobeFwhmS` is a lower bound when the lobe touches the window edge; widen the grid.
* Validity thresholds are fixed; `physics.strictValidity` only turns the entries into an error.
