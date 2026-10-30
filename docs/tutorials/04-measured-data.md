# Tutorial 4 -- Your own measured data

**Goal:** bring recordings from the lab into the simulation, compare them with the model, let the engine find the
numbers you do not know well (with error bars), and predict what you did not measure.

We use the plate of tutorial 1. Imagine you put it in your beam and recorded, after the air gap:

* a spectrum, exported by the spectrometer software as CSV;
* an intensity autocorrelation, as CSV;
* a power-meter reading.

You know the laser's centre wavelength and bandwidth, but not exactly its pulse energy at the plate or its residual
chirp. Files: `docs/examples/tutorial/make_data.py`, `data/`, `run-measured.json`, `run-fit.json`.

## 1. Preparing the files

`make_data.py` writes the example recordings. They are simulated with an energy of 260 nJ and a chirp of
+120 fs², with noise added, and the run files do not know these values. With your own data, you skip this step and
copy your exports into `data/`.

**CSV.** One header row of column names, then numbers. Comment lines starting with `#` are fine, and the separator
is detected (comma, semicolon, tab, spaces). **Put the unit in the column name**:

```text
# exported by MySpectrometer, integration 10 ms
wavelength_nm,counts
740.00,13.2
740.25,17.7
...
```

```text
delay_ps,signal
-0.3,-0.00031
-0.297,0.0016
...
```

Recognised units include `nm`, `um`, `mm`, `fs`, `ps`, `ns`, `fs2`, `nj`, `uj`, `mj`, `mw` (the full table is in
[Measured data](../reference/measured-data.md#units-from-column-names)). The engine finds the columns by name
(`wavelength…`, `delay…`, `counts`/`signal`/`intensity`…). If your names differ, map them:
`"columns": {"x": "Lambda", "y": "I"}` with `"xUnit": "nm"`. A column called `sigma` or `err` gives per-point
uncertainties.

**NPZ** is best for 2-D traces (FROG, d-scan, camera images) and for anything you already have in NumPy:

```python
np.savez("data/dscan.npz", insertion_mm=z, wavelength_nm=lam, trace=T)   # T.shape == (len(z), len(lam))
```

(`np.savez_compressed` works too.)

**JSON** holds scalar readings, with the instrument's value names: `{"energyJ": 2.62e-7}`, or
`{"energyJ": [2.62e-7, 5e-9]}` with its uncertainty.

## 2. Comparing: `run-measured.json`

Add `"measured"` to the instruments that have data:

```jsonc
"instruments": [
  { "kind": "spectrometer", "name": "spectrum", "at": "end",
    "measured": { "file": "data/spectrum_end.csv", "background": "auto", "sigma": 0.005 } },
  { "kind": "autocorrelator", "name": "ac", "at": "end",
    "measured": { "file": "data/autocorrelation_end.csv", "sigma": 0.01 } },
  { "kind": "powerMeter", "name": "power", "at": "end",
    "measured": { "file": "data/power_end.json", "valueSigma": 0.03 } },
  { "kind": "pulseSummary", "name": "summary", "at": "end" }
]
```

* `background: "auto"` subtracts the dark level, i.e. the median of the data at both ends of the wavelength axis.
* `sigma` is the noise of each point, relative to the peak. It should be your real noise level. Here the spectrum
  is good to 0.5 % and the autocorrelation to 1 %. The power meter is calibrated to 3 %.
* Curves are compared after normalising (by default, the simulation to its peak and the data scaled onto it), so
  the arbitrary counts do not matter. Absolute numbers are compared through `values` or a JSON file.
* File names are relative to the run file.

```bash
python make_data.py                                   # only for this tutorial
ultrafast run plate.ufs run-measured.json -o results-4a
```

```
  measured data:
    spectrum               spectrometer   chi^2 2.474E+04  (481 points)  rms error 3.59 %
    ac                     autocorrelator chi^2 837.3      (201 points)  rms error 2.04 %
    power                  energyMeter    chi^2 63.1       (1 points)
      energyJ: measured 2.623E-07 +- 7.9E-09, simulated 1.998E-07 (-7.9 sigma)
```

**Reading it:** χ² is the sum of ((simulated − measured)/σ)². A model that agrees within the noise has χ² close to
the number of points, i.e. a reduced χ² of about 1. Here the spectrum's χ² is 50 times too large and the power
meter disagrees by 8σ, so our assumed 200 nJ is wrong. The autocorrelation was recorded over ±300 fs, the simulated
autocorrelator covers ±150 fs. Beyond that its reading has decayed to zero, so the 100 recorded points outside are
compared with zero, and the comparison's notes say so (`summary.json → comparisons[].notes`). Widen the instrument's
range to simulate them. `ultrafast-plot results-4a` draws `measurements.png`: the
data with the simulation on top, the residuals, and the scalar readings in units of σ.

## 3. Calibrating: `run-fit.json`

Add a `"fit"` block that names what may change, by its path in the files:

```jsonc
"fit": {
  "parameters": [
    { "name": "energy", "path": "pulse/energyJ",      "min": 50e-9, "max": 500e-9, "unit": "J" },
    { "name": "chirp",  "path": "pulse/chirp/gddFs2", "initial": 0, "min": -400, "max": 400, "unit": "fs^2" }
  ]
}
```

(`initial` is needed for the chirp because `run.json` does not have one yet.)

```bash
ultrafast run plate.ufs run-fit.json -o results-4b
ultrafast-plot results-4b
```

```
fit: calibrate parameters against the measured instruments ...
  fit: chi^2 2.567E+04 -> 582.9 (683 points, reduced 0.856), 4 iterations, 16 runs
  energy                                 2.6476E-07 +- 1.29E-09     (start 2E-07)
  chirp                                     115.456 +- 3.08         (start 0)
  spectrum                           rms error 3.59 % -> 0.46 %
  ac                                 rms error 2.06 % -> 0.92 %
  power                              chi^2 63.1 -> 0.0749
```

| | true (used to make the data) | fitted |
|---|---|---|
| energy | 260 nJ | 264.8 ± 1.3 nJ |
| chirp | +120 fs² | +115 ± 3 fs² |

The reduced χ² is 0.86, so the model explains the data down to the noise. The chirp is found within 1.5 error bars.
The energy is 1.8 % high, with an error bar of 0.5 %: the power meter's reading in the data is itself about 1 % high
(262.3 nJ), and the energy and chirp are correlated, so a lower chirp is paid for with more energy. Error bars describe random noise, not a miscalibrated instrument.

`fit.png` shows the cost per iteration, the correlation between the two parameters (−0.87: more energy and more
chirp both change the SPM, so they are partly interchangeable) and χ² per instrument before and after.

How it works: the engine reruns the whole simulation with adjusted parameters (Levenberg–Marquardt, 16 runs here),
minimising the total χ² over all measured instruments. The uncertainties come from the curvature of χ² at the
minimum. The rules for paths, bounds and the warnings it gives (strong correlations, parameters the data do not
determine, values at a bound) are in [Calibration fit](../plugins/fitting.md#calibration-fit).

What you can fit: anything numeric in the system or run file, such as `element:compressor/separationM`,
`element:amp/pump/powerW`, `element:plate/beam/w0M`, or `material:FusedSilica/n2` (applied as an override).

## 4. Predicting

After the fit, every instrument and element value carries an uncertainty, propagated from the fitted parameters.
That includes instruments without data, like `summary` here:

| prediction (`fit.json → predictions`) | value ± 1σ |
|---|---|
| output FWHM (`summary.fwhmS`) | 56.53 ± 0.05 fs |
| transform limit (`summary.transformLimitFwhmS`) | 26.65 ± 0.05 fs |
| peak power (`summary.peakPowerW`) | 4.62 ± 0.03 MW |
| B-integral in the plate (`plate.bIntegralRad`) | 0.966 ± 0.006 rad |

To predict what another instrument would show (a FROG before the plate, a camera somewhere else), add it to the
run without `"measured"`. The prediction can be upstream of the data, at the input, or downstream.

## 5. A real case: example 21

`docs/examples/21-measured-data` does this for a few-cycle line: a far-field **fibre** spectrum after an SPM plate
(sampled as in tutorial 2), an autocorrelation, a **d-scan** saved as NPZ, and a power meter. Three parameters are
fitted: input chirp, energy and compressor GDD. It shows a typical result: the instruments after the compressor fix
the *sum* of input chirp and compressor GDD very well, but each one less well. The fit warns about this correlation
and suggests what to measure to separate them.

## Checklist for your own data

1. **Model the instruments as built**: the same place (`"at"`), the same sampling (a fibre-coupled spectrometer is
   not a whole-beam one), the same resolution (`resolutionM`) and range.
2. **Units in the column names**, or `xUnit` / `yUnit`.
3. **Dark level**: `background: "auto"` needs dark edges in the data. Otherwise give the number.
4. **Honest σ**: estimate the noise from repeated recordings. Use `weight` to balance an instrument with thousands
   of points (a trace) against a single reading.
5. **Few parameters, sensible bounds**, starting close to reality.
6. After fitting, **look at the residuals** (`measurements.png`). Structure in them means the model is missing
   something: a physical effect, or an instrument detail.

Everything here is static: record, then compare or fit. All options are in
[Fitting plugin](../plugins/fitting.md).
