# Fitting plugin: measured data and calibration

!!! note "Plugin"
    These features are provided by the **fitting plugin** (`plugins/fitting`), which ships with the engine and is
    loaded automatically when installed (`ultrafast plugins list`). The core engine works without it. A next
    version built around parameterised field transforms is on the [roadmap](../roadmap.md). To model *where* an instrument
    looks (fibre, pinhole, near / far field) see [Sampling](../reference/instruments.md#sampling-where-the-instrument-looks),
    a core feature.

The plugin adds two things:

1. **[Measured data](#measured-data-comparing-with-recordings)**: give an instrument your recording (CSV, NPZ or
   JSON) and the run compares it with the simulated reading.
2. **[Calibration fit](#calibration-fit)**: let the engine adjust uncertain numbers (input chirp, energy, a
   compressor setting, a material's n2, ...) until the simulated instruments match the recordings. It then
   reports those numbers with error bars and **predicts** every other instrument and element with error bars,
   upstream or downstream of the data.

Everything is static: you record once, then compare or fit. Example 21 (`docs/examples/21-measured-data`) is a complete
worked case, and [Tutorial 4](../tutorials/04-measured-data.md) walks through it step by step.

## Measured data: comparing with recordings

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

## Calibration fit

Add `"fit"` to the run spec to find the values of uncertain parameters from the measured instruments:

```jsonc
"fit": {
  "parameters": [
    { "name": "input chirp",    "path": "pulse/chirp/gddFs2",        "min": -500,  "max": 1500,   "unit": "fs^2" },
    { "name": "input energy",   "path": "pulse/energyJ",             "min": 50e-9, "max": 300e-9, "unit": "J" },
    { "name": "compressor GDD", "path": "element:compressor/gddFs2", "min": -1500, "max": 0,      "unit": "fs^2" }
  ],
  "maxIterations": 20, "tolerance": 1e-4, "freezeGrid": true
}
```

### Parameters

| key | default | meaning |
|---|---|---|
| `path` | required | the number to vary (see below) |
| `name` | the path | label in reports and plots |
| `initial` | the value in the files | start value |
| `min`, `max` | unbounded | bounds (strongly recommended: they also set the scale) |
| `scale` | (max − min) / 4, else \|initial\| | typical size of a change; the fit works in units of `scale` |
| `step` | 1e-3 × scale | finite-difference step for the derivatives |
| `unit` | | label only (`fs^2`, `J`, ... are shown in engineering units in the plots) |

**Paths** address a number in the system or run file:

| path | where |
|---|---|
| `pulse/energyJ`, `pulse/chirp/gddFs2`, `pulse/durationFwhmS`, `pulse/centerWavelengthM` | the run's `pulse` |
| `element:ID/lengthM`, `element:amp/pump/powerW`, `element:compressor/gddFs2` | a property of the element with that id (nested objects with `/`) |
| `element:amp/betweenPasses/materials/0/lengthM` | a number inside a list (index from 0) |
| `material:FusedSilica/n2` | a material property, applied as an [override](../reference/models.md) |
| `run/physics/...`, `system/...` | anything else in the run or system file |

If a path does not exist yet it is created, so give `initial` in that case.

### What the fit does

* **Weighted least squares.** The fit minimises χ² = Σ ((simulated − measured)/σ)² over every point of every
  measured instrument. It uses Levenberg–Marquardt on the scaled variables, with the bounds enforced and
  forward-difference derivatives: one extra run per parameter per iteration. A typical fit takes 10 iterations,
  i.e. (number of parameters + 2) × 10 runs.
* **Frozen grid.** The time/frequency grid is fixed for all runs (`freezeGrid`, default on) at twice the
  automatically planned bandwidth. This prevents the grid from changing between nearby parameter values, which
  would add numerical noise to the derivatives.
* **Uncertainties.** These come from the covariance C = (JᵀJ)⁻¹ at the optimum, computed by a pseudo-inverse so
  that directions the data cannot resolve do not explode. If the reduced χ² is above 1, they are scaled up by
  √(reduced χ²), because the data scatter more than the stated σ.
* **Diagnostics** (in `warnings`):
  * correlations above 0.95: the data fix a combination of the parameters better than each one;
  * parameters that do not affect any measured instrument;
  * unidentifiable combinations (null directions of JᵀJ);
  * parameters that end at a bound;
  * no convergence within `maxIterations`.

### Predictions

After the fit, the best run is the run result: all instruments, elements, probes and plots are at the fitted
values. In addition, each number gets a 1σ uncertainty by linear propagation, σ² = gᵀ C g, where g is the
derivative of that number with respect to the parameters:

* every scalar of every instrument, measured or not, as `<value>Sigma` next to the value and in
  `fit.json → predictions["<instrument>.<value>"]`;
* per element: `energyOutJ`, `fwhmS`, `peakPowerW`, `spectralFwhmM`, `bIntegralRad`
  (`predictions["<element>.<quantity>"]`);
* a 1σ band on each compared curve or trace, `<name>_simulated_sigma`, drawn in `measurements.png`.

To predict an instrument you do not have, add it to the run without `"measured"`, such as a FROG at the input or a
`pulseSummary` in the middle of the line. The data constrain it through the model, both upstream and downstream
of where they were taken.

### Outputs

* `fit.json`: `converged`, `iterations`, `evaluations` (runs), `points`, `chi2Initial`, `chi2Final`,
  `reducedChi2`, `parameters` (name, path, unit, initial, value, sigma, min, max, atBound), `correlation`
  (matrix), `costHistory`, `instruments` (χ² and rms error before and after, per instrument), `predictions`,
  `warnings`, `notes`.
* `summary.json → fit`: the same report, inside the summary.
* The CLI prints the fitted parameters ± σ and each instrument before → after.
* `ultrafast-plot` draws `fit.png`: parameters, cost per iteration, correlation matrix and χ² per instrument.
* Python: `res.summary["fit"]`, or `json.load(open("results/fit.json"))` after `res.write("results")`.
  `uf.run(system, spec)` fits automatically when the spec has a `"fit"` block.

### Practical advice

* **Start close.** The fit finds a local minimum. Start from your best estimates and set realistic bounds.
* **Fit few parameters.** Three to five well-chosen ones are better than ten. Each one costs a run per iteration,
  and parameters the data cannot separate only produce correlation warnings.
* **Check the correlations.** A correlation near ±1 means you need a measurement that separates the two parameters,
  usually an instrument at another place in the line (see example 21).
* **Model the instrument.** If your spectrometer is fibre-coupled, give it the same `sampling`. A fit that compares
  a whole-beam simulation with an on-axis measurement will bias the parameters to explain the difference.
* **Look at the residuals** in `measurements.png`. Structure in them, rather than noise, means the model misses
  something.

## Limitations

* Static only: one set of recordings, compared or fitted once. Live updating is planned (see the
  [roadmap](../roadmap.md)).
* The fit uses a linearised uncertainty estimate (Gaussian approximation around the optimum). Strongly non-linear
  problems or multiple minima need a check, e.g. a loop over start values in Python.
* Sampling uses the field of the carried profile (or a Gaussian model) at the instrument's location. An aperture
  in the beam that clips what is passed on to later elements is an element (`aperture`), not sampling.
