# Tutorial 2 -- Diagnostics

**Goal:** measure the pulse where you would on a real table: before and after the plate, with a spectrometer, an
autocorrelator, a FROG, a camera and a d-scan. Then see why it matters *where in the beam* an instrument looks.

We keep `plate.ufs` from tutorial 1 and only change the run spec: `docs/examples/tutorial/run-diagnostics.json`.

## 1. Placing instruments

Every instrument has an `"at"`: `"input"`, an element `id` (meaning just after that element), or `"end"`. Give
each one a `"name"`, which is used in the results and plots.

```jsonc
"instruments": [
  { "kind": "energyMeter",    "name": "E_in",       "at": "input" },
  { "kind": "spectrometer",   "name": "spec_in",    "at": "input" },
  { "kind": "spectrometer",   "name": "spec_plate", "at": "plate" },
  { "kind": "autocorrelator", "name": "ac_plate",   "at": "plate" },
  { "kind": "frog",           "name": "frog_plate", "at": "plate", "points": 128 },
  { "kind": "camera",         "name": "nf_plate",   "at": "plate", "pixels": 128 },
  { "kind": "dscan",          "name": "dscan_end",  "at": "end", "material": "FusedSilica", "insertionM": [-12e-3, 2e-3], "steps": 36 },
  { "kind": "compressor",     "name": "compressor", "at": "end" },
  { "kind": "pulseSummary",   "name": "summary_end","at": "end" }
]
```

Instruments never change the pulse. They are cheap, so add as many as you like. Every kind and its parameters
are listed in [Instruments](../reference/instruments.md). Example 17 uses all of them. Some are *virtual*: they show things no
real device measures, such as `wigner`, `fieldProbe` and `spaceTime`. They are marked as virtual in the plots.

To place instruments at a point with no element, add a `{"id": "here", "type": "probe"}` element to the system:
it does nothing to the pulse but gives the place a name. `"probes": "all"` in the run spec also keeps a snapshot
of the pulse after every element (the `probes.png` heat maps).

## 2. A spatial model

Tutorial 1 warned that the beam would self-focus. One line in the run spec gives every bulk and gap element a
radial (r, ω) grid:

```jsonc
"physics": { "transverse": { "model": "radial", "points": 160, "radiusM": 1.2e-3 } }
```

Now diffraction, the Kerr lens, and SPM that depends on the radius are all included, and the beam profile travels
from element to element. The grid has to hold the beam everywhere. Here 160 points over 1.2 mm (7.5 µm spacing)
resolve the 60 µm focus and the beam as it diverges to ~0.23 mm in the air. The engine warns when energy reaches
the edge of the grid. See [Spatial models](../reference/transverse.md); a system file can also set `transverse` per element.

```bash
ultrafast run plate.ufs run-diagnostics.json -o results-2
ultrafast-plot results-2
```

The run takes about 3 s instead of 0.1 s, and the self-focusing warning is gone.

| | 1-D (tutorial 1) | radial |
|---|---|---|
| B-integral | 0.77 rad | 0.84 rad (the Kerr lens tightens the beam slightly) |
| spectrum at the end, whole beam | 25.1 nm | 24.2 nm |
| beam after the plate / after the air | (not modelled) | w = 58 µm, M² = 1.03 / w = 230 µm |
| best compression (`compressor`) | | −271 fs² → 33.4 fs |
| d-scan optimum | | −8 mm of fused silica (≈ −290 fs²), 33.5 fs |

The 1-D model applies one effective intensity to the whole beam. The radial model resolves it: the centre of the
beam broadens more and the edges less, and the spectrum averaged over the beam is narrower.

## 3. Where the instrument looks: sampling

Now the spectrum depends on where in the beam you look. A real spectrometer is often fed by a fibre or a pinhole,
so it does not collect the whole beam. Tell the instrument with `"sampling"`:

```jsonc
// a 10 um pinhole on axis, right after the plate (near field)
{ "kind": "spectrometer", "name": "spec_pinhole", "at": "plate",
  "sampling": { "plane": "near", "aperture": { "kind": "pinhole", "radiusM": 10e-6 } } },
// a fibre (25 um core) in the focal plane of an f = 100 mm lens (far field)
{ "kind": "spectrometer", "name": "spec_fibre_ff", "at": "plate",
  "sampling": { "plane": "far", "focalLengthM": 0.1, "aperture": { "kind": "fibre", "radiusM": 25e-6, "na": 0.22 } } }
```

| spectrometer after the plate | sees | spectral FWHM |
|---|---|---|
| `spec_plate` | the whole beam | 24.2 nm |
| `spec_pinhole` | 6.7 % of the energy: the centre of the beam | **26.1 nm** |
| `spec_fibre_ff` | 0.67 %: the low spatial frequencies (mostly the large, weakly broadened part of the beam) | 23.7 nm |

So the same pulse can give spectra that differ by 10 %, depending only on how the light gets into the
spectrometer. Model your instrument the way it is built, especially when you compare with measurements
(tutorial 4).

The apertures are `pinhole`, `iris`, `fibre`, `slit` (`widthM`), `point`, or `integrated` (the default); move
them with `centerXM` / `centerYM`. In the near field, `propagateM` adds free-space propagation before the aperture.
Sampling works for any non-imaging instrument: spectrometer, autocorrelator, FROG, d-scan, scope, power meter.
Each sampled instrument also reports `samplingTransmission`. Details:
[Sampling](../reference/instruments.md#sampling-where-the-instrument-looks). Example 11 shows a stronger case: an
autocorrelator on axis sees an 11 % shorter pulse than one that collects the whole beam.

## 4. The plots

* `instruments.png`: one panel per instrument, numbered in beam order. The numbers match the markers on
  `system.png`.
* `probes.png`: the spectrum and pulse after every element.
* `overview.png`: as before, now with the beam radius.

## 5. Try

* Move the pinhole off axis: `"centerXM": 60e-6`. The spectrum narrows to 23.2 nm, because the wing sees little SPM.
* Replace the pinhole by a `"slit"` with `"widthM": 20e-6`. A slit averages along a line, so the result lies
  between the pinhole and the whole beam.
* Add `{ "kind": "spaceTime", "at": "plate" }` (virtual): the pulse duration and spectrum as a function of radius.

Next: [Tutorial 3 -- Larger systems](03-larger-systems.md).
