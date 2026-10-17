# Tutorial 1 -- Your first simulation

**Goal:** describe a simple optical system, send a pulse through it, and look at what comes out.

A simulation needs two files:

* a **system file** (`.ufs`, JSON with comments): the optics, i.e. what is on the table;
* a **run spec** (`run.json`): the pulse you send in, the instruments you look with, and numerical settings.

Keeping them apart lets you run the same system with many pulses, and the same pulse through many systems.

## 1. The system file

`docs/examples/tutorial/plate.ufs`:

```jsonc
{
  "format": "ultrafast-system",
  "version": 1,
  "name": "tutorial-plate",
  "elements": [
    { "id": "plate", "type": "bulk", "material": "FusedSilica", "lengthM": 5e-3,
      "beam": { "kind": "gaussian", "w0M": 60e-6, "zWaistM": 2.5e-3 } },
    { "id": "air", "type": "gap", "lengthM": 0.05 }
  ]
}
```

* `elements` is the beam path in order. Each element has a unique `id` (you will use it to place instruments)
  and a `type`.
* `bulk` is a piece of material: here 5 mm of fused silica. The engine knows its dispersion, its nonlinear index
  n2 and its Raman response ([Materials](../reference/materials.md)).
* `beam` tells a nonlinear element how big the beam is: a Gaussian focused to w0 = 60 µm (1/e² radius) at
  2.5 mm from the start of the element, i.e. the middle of the plate. The beam size sets the intensity, and
  therefore the self-phase modulation (SPM).
* `gap` is free space (air by default).
* **Units**: SI everywhere, and the key names say which unit (`lengthM` in metres, `durationFwhmS` in seconds).
  `//` comments are allowed.

The full list of element types is in [System file](../reference/system-file.md).

## 2. The run spec

`docs/examples/tutorial/run.json`:

```jsonc
{
  "format": "ultrafast-run",
  "name": "first-run",
  "pulse": { "shape": "gaussian", "centerWavelengthM": 800e-9, "durationFwhmS": 40e-15,
             "energyJ": 200e-9, "repRateHz": 1e3, "polarization": "x" },
  "instruments": [
    { "kind": "spectrometer" },
    { "kind": "autocorrelator" },
    { "kind": "pulseSummary" }
  ]
}
```

* `pulse`: a transform-limited 40 fs Gaussian pulse at 800 nm, 200 nJ, at 1 kHz. Add
  `"chirp": {"gddFs2": 300}` for a chirped pulse, or use `"shape": "spectrum"` with your own measured spectrum
  ([Run spec](../reference/run-spec.md#pulse)).
* `instruments`: what you measure. Without `"at"` they sit at the end of the system. `pulseSummary` reports every
  pulse metric.
* The time/frequency grid is sized automatically. You only need `"grid"` to override it.

`ultrafast template run.json` writes a fully commented run spec to start from.

## 3. Check and run

```bash
cd docs/examples/tutorial
ultrafast validate plate.ufs run.json          # checks the files; prints what the engine plans to do
ultrafast run plate.ufs run.json -o results-1
```

```
system: tutorial-plate (2 elements, 1 nonlinear)
  Estimated total B-integral (input peak power, no losses): 0.844 rad; spectral margin 3.
  Grid auto-sized: N = 512, window = 1.707 ps, dt = 3.333 fs.
  ...
done in 0.08 s -> results-1
  energy 1.998E-07 J (T = 99.91 %), FWHM 51.1 fs (TL 29.29 fs), peak 3.837E+06 W, B = 0.766 rad
  WARNING: Element 'plate': peak power is 1.85 x the critical power for self-focusing; spatial effects (Kerr
  lensing, filamentation) are not modelled in 1-D -- use "transverse": {"model": "radial"} on this element.
```

What it says:

* the **B-integral** (the nonlinear phase) is 0.77 rad. SPM has broadened the spectrum: the transform limit
  dropped from 40 fs to 29 fs;
* the pulse is now 51 fs long. It is no longer transform-limited, because the plate's dispersion and SPM have
  chirped it;
* the **warning** is important. At this power the beam would self-focus, which a one-dimensional (1-D)
  simulation cannot show. Tutorial 2 adds a spatial model.

Always read the warnings. They point out the cases where the model you chose is not good enough for the physics
you are asking for.

## 4. The results

`results-1/` contains:

| file | content |
|---|---|
| `summary.json` | every scalar: input/output pulse, per element (energy, B-integral, dispersion, ...), per instrument, notes, warnings |
| `result.npz` | every array (`numpy.load`): spectra, pulses, instrument traces |
| `<instrument>.csv` | each instrument's trace as CSV (e.g. `spectrometer.csv`: wavelength, spectrum) |
| `elements.csv`, `spectrum.csv`, `temporal.csv`, `provenance.json` | the per-element table, input/output spectra and pulses, where every value came from |

The instruments report (from `summary.json → instruments`):

| instrument | value |
|---|---|
| `spectrometer` | spectral FWHM 25.1 nm (input: 23.5 nm) |
| `autocorrelator` | AC FWHM 66.7 fs → 47.2 fs deconvolved (Gaussian) |
| `pulseSummary` | FWHM 51.1 fs, transform limit 29.3 fs, energy 199.8 nJ |

Details: [Results](../reference/results.md).

## 5. Plot

```bash
ultrafast-plot results-1           # -> results-1/plots/*.png and report.pdf
```

`overview.png` shows the system, the pulse and spectrum in and out, the spectral phase, and energy / FWHM / B
through the system. `instruments.png` has one panel per instrument. `system.png` is a schematic with the
numbered instrument positions. `ultrafast-plot --help` lists the options: dark theme, SVG/PDF, comparing several
runs.

## 6. Try

* Change `energyJ` to 400e-9 and watch the spectrum broaden (B-integral ×2).
* Add `"chirp": {"gddFs2": -200}` to the pulse: the plate's dispersion recompresses it.
* `ultrafast describe plate.ufs run.json` shows which model is used for every element and why.

Next: [Tutorial 2 -- Diagnostics](02-diagnostics.md).
