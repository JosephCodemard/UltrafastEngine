# Measured data

Simulations are compared with measurements in two places, both part of the engine (no plugin needed):

* the **input pulse** can be a measured spectrum ([`pulse.spectrum`](#measured-input-spectra));
* any **instrument** can be given the recording it should reproduce ([`measured`](#comparing-instruments-with-recordings)),
  and the run reports how well the simulated reading matches it.

Two facts shape both. **Every instrument has a fixed resolution and samples at particular points**, so a
recording is compared with the simulated instrument as the recording instrument would have seen it. **A
recording rarely covers the whole simulation grid**: a spectrometer stops at its range, an autocorrelator scans a
few hundred femtoseconds. Only the part both cover is compared, and the run says how much was left out.

Fitting parameters to the recordings (`"fit"`), fittable `transform` elements and backward propagation come from
the [fitting plugin](../plugins/fitting.md), which uses these comparisons.

## Measured input spectra

```jsonc
"pulse": { "spectrum": { "csv": "data/pharos.csv", "wavelengthUnit": "nm",
                         "background": "auto",          // subtract the dark floor
                         "resolutionM": 0.5e-9,         // the recording spectrometer
                         "extrapolate": "gaussian" },   // tails beyond the recorded range
           "energyJ": 200e-6 }
```

| key | default | meaning |
|---|---|---|
| `wavelengthsM` / `wavelengthsNm`, `intensity`, `phaseRad` | | the data as arrays (intensity per unit wavelength; phase optional) |
| `csv`, `wavelengthUnit` | `nm` | or a file: wavelength, intensity, optional phase (rad); header and `#` lines skipped |
| `background` | `none` | a number to subtract (intensity units), `"auto"` (median of the darker edge), `"min"` or `"none"` |
| `threshold` | 0 | after subtraction, values below this fraction of the peak are set to zero (noise in the wings) |
| `resolutionM` / `resolutionNm` | 0 | resolution (Gaussian FWHM) of the spectrometer that recorded it |
| `deconvolve`, `deconvolveIterations` | false, 30 | remove that resolution (Richardson–Lucy) before use |
| `extrapolate` | `zero` | outside the recorded range: no light (`zero`) or Gaussian tails fitted to the data (`gaussian`) |

### Cleaning

Before use, the rows are cleaned and each step is reported in the run notes:

1. rows with a non-finite value or a non-positive wavelength are dropped (a warning);
2. rows are sorted by wavelength -- digitised curves often have a few points out of order, and files may be in
   descending order;
3. rows that repeat a wavelength (spacing below 1/1000 of the median spacing) are merged by averaging intensity and
   phase.

A repeated wavelength used to make the interpolation divide by zero and turn every result into NaN, with only a
vague grid warning. It is now merged, and a pulse whose energy is still not finite stops the run with a clear
message.

### Background

A flat floor in a recorded spectrum is not harmless. Over a wide recording it adds a broad pedestal of bandwidth,
which shortens the transform limit and puts energy into a long, weak temporal background. A 0.66 % floor over a
950–1110 nm recording of a 6.5 nm wide spectrum shortens the transform limit from 240 fs to 26 fs. The run warns when
the floor (the lower of the two edge medians) is above 0.3 % of the peak and `background` is not set.

`"background": "auto"` takes the median of the first and of the last 5 % of the points and uses the **lower** of the
two. A spectrum cut off on one side by the spectrometer (an MPC output stopping at 1100 nm with light still there)
therefore still gives its true floor from the dark side. Give a number if both edges carry light, and use `threshold`
to zero the remaining noise in the wings.

### Resolution and range

The input description says what the spectrum is: "measured 950.0-1110.0 nm (1200 points, background 0.656 %
removed, resolution 0.5 nm, deconvolved, with phase, Gaussian tails outside)". A recording narrower than the
spectrum loses the wings: use `extrapolate: "gaussian"` when the spectrum is cut off, and check the light near the
edges. A measured spectrum replaces the input shape and propagates forward only; to learn what the *system* does and
reuse it for other inputs, use transform elements ([fitting plugin](../plugins/fitting.md)).

## Comparing instruments with recordings

**Model the recording instrument as it is**: the spectrometer's `resolutionM` and its pixel axis, the
autocorrelator's delay steps, the cross-correlator's scan grid and noise floor, the camera's pixels. The common
instrument key [`axis`](instruments.md#axis-where-readings-are-taken) sets where any curve instrument takes its
readings: explicit points, from / to / step, pixel integration and a Gaussian resolution.

Give an instrument a `"measured"` entry: a file name, or an object with options.

```jsonc
{ "kind": "spectrometer", "name": "spec", "at": "end",
  "measured": "data/spectrum.csv" }                         // shortest form

{ "kind": "autocorrelator", "name": "ac", "at": "end",
  "measured": { "file": "data/ac.csv", "sigma": 0.02, "background": "auto", "normalize": "peak", "resolutionS": 5e-15 } }

{ "kind": "powerMeter", "name": "pm", "at": "end",
  "measured": { "values": { "energyJ": [1.41e-7, 4e-9] } } }   // scalar readings: value or [value, sigma]
```

The run then:

1. reads the file and converts the axes to SI units (from the column names);
2. keeps the measured points inside the simulated instrument's range, and those beyond an edge where the simulated
   reading has decayed to zero (the rest are counted in the notes);
3. blurs the simulated reading to the recording's resolution if the recording is coarser;
4. interpolates the simulated reading onto the measured axis and normalises both;
5. reports the residuals, χ² and an rms error.

Comparing costs no extra simulation. Nothing is fitted unless the run has a `"fit"` block (fitting plugin).

### Resolution and range

* **Resolution.** State the recording's resolution as `resolutionM` (spectral, spatial) or `resolutionS` (time,
  delay). If the simulated instrument is finer (an ideal spectrometer, or a smaller `resolutionM`), the simulated
  curve is blurred by the missing Gaussian width √(rec² − sim²) before comparing, with a note. If the simulated
  instrument is coarser, the mismatch is reported: set the instrument's resolution to the recording's, or fit it
  (`instrument:NAME/resolutionM`, fitting plugin). For 2-D traces the mismatch is reported only: set the trace
  instrument's own resolution.
* **Range.** Points of the recording outside the simulated instrument's axis are left out when the simulated
  reading is still significant at that edge (a limited `axis`, the end of the grid): there the simulation knows
  nothing. Widen the instrument's axis (or the grid) if they matter. Where the simulated reading has decayed to zero
  at its edge (instruments such as the autocorrelator crop their output to the signal), the points beyond are
  compared with zero -- a recording's flat, dark wings are information. The notes count both. `rangeX` restricts the comparison further on purpose (e.g. to leave out a
  region with stray light).

## File formats

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
instrument's value names (see [Instruments](instruments.md)). Each value is either a number or `[value, sigma]`.

## Units from column names

The part after the last `_` is the unit:

| suffix | factor | | suffix | factor |
|---|---|---|---|---|
| `nm`, `um`, `mm`, `cm` | 1e-9, 1e-6, 1e-3, 1e-2 | | `fs2`, `ps2`, `fs3` | 1e-30, 1e-24, 1e-45 |
| `fs`, `ps`, `ns`, `us`, `ms` | 1e-15 … 1e-3 | | `pj`, `nj`, `uj`, `mj` | 1e-12 … 1e-3 |
| `uw`, `mw`, `kw` | 1e-6, 1e-3, 1e3 | | `thz` | 1e12 |

Anything else (or no suffix) is taken as SI. A wavelength column without a unit and with values above 1e-3 is
assumed to be in nm (with a note). Override with `"xUnit"`, `"yUnit"`, `"rowUnit"`, `"colUnit"` (e.g. `"um"`).

## Which columns are used

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

## Options

| key | default | meaning |
|---|---|---|
| `file` | | CSV / NPZ / JSON, relative to the run file |
| `columns` | auto | see above |
| `normalize` | `peak` | `peak`: the simulation is scaled to a maximum of 1 and the data are scaled onto it by least squares, which is robust against noise on the measured maximum. `maximum`: both divided by their maxima. `area`: both to unit area. `scale`: the simulation times a best-fit factor (reported as `measuredScale`), with the data left in their own units. `none`: absolute comparison (data in SI units of the simulated reading). |
| `sigma` | 0.02 | uncertainty of each point, relative to the data peak (when there is no sigma column) |
| `background` | none | a number to subtract, `"min"`, or `"auto"`: for a curve, the median of the first and of the last 5 % of the points, taking the lower (darker) end -- a recording cut off on one side still gives its dark level; for a trace, the median at the edges of the columns. Use a number if your data have signal at both edges. |
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
| `resolutionM`, `resolutionS` | | the recording instrument's resolution (Gaussian FWHM, in the unit of the axis): compared with the simulated instrument's (`resolutionM` / `spectralResolutionM`, `resolutionS` / `riseTimeS`, `axis.resolution`); see [Resolution](#resolution-and-range) |
| `matchResolution` | `true` | curves: when the recording is coarser than the simulated instrument, blur the simulated reading by the missing width (FWHM √(rec² − sim²)) before comparing; `false`: only report the mismatch |

**Choosing σ.** χ² only means something if σ is the real noise level. The *reduced* χ² (χ² per point) should be
close to 1 for a good model with the right σ. If it is much below 1, σ is too large; if it is far above 1, the model
misses something or σ is too small. The fit uses σ to weight instruments against each other, so a 2-D trace with
thousands of points dominates a scalar reading unless you set `weight`.

## Outputs

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

pulse = b.measured_spectrum_csv("data/pharos.csv", 200e-6, background="auto", resolution_m=0.5e-9)
spec = b.run_spec(pulse, instruments=[
    b.instrument("spectrometer", name="osa", resolutionM=0.1e-9,
                 measured=b.measured("data/osa.csv", background="auto", resolution_m=0.5e-9)),
    b.instrument("autocorrelator", name="ac", measured=b.measured("data/ac.csv", sigma=0.02)),
    b.instrument("powerMeter", name="pm", measured=b.measured(values={"energyJ": [1.41e-7, 4e-9]}))])
res = uf.run(system, spec)

for c in res.comparisons:                       # list of ultrafast.measured.Comparison (TypedDict)
    print(c["instrument"], c["chi2"], c.get("rmsError"), c["notes"])
osa = uf.measured.curve(res, "osa")             # measured_x, measured_y, simulated_on_measured, measured_sigma
uf.plot(res, "measurements")                    # measured vs simulated, residuals, scalar readings
```

| function | |
|---|---|
| `b.measured_spectrum(wavelengths_m, intensity, energy, phase_rad, background=, threshold=, resolution_m=, deconvolve=, extrapolate=)` | input pulse from arrays |
| `b.measured_spectrum_csv(csv, energy, wavelength_unit=, background=, ...)` | input pulse from a file |
| `b.measured(file, values=, sigma=, normalize=, background=, columns=, range_x=, x_unit=, y_unit=, resolution_m=, resolution_s=, match_resolution=, scale=, sigma_db=, floor_db=, weight=)` | an instrument's `measured` entry (typed `ultrafast.types.Measured`) |
| `res.comparisons`, `res.comparison(name)` | the comparisons (also on a results folder opened with `uf.load`) |
| `uf.measured.curve(res, name)`, `uf.measured.trace(res, name)`, `uf.measured.total_chi2(res)` | the compared arrays as NumPy |
| `ultrafast.plotting.measurements(res)` | the `measurements` figure (`ultrafast-plot` draws it for runs with measured data) |

`ultrafast_fitting.measured` and `ultrafast_fitting.comparisons` (fitting plugin) are the same functions, kept for
scripts written against earlier versions.
