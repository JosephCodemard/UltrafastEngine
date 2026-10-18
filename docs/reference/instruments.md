# Instruments

Instruments read the pulse at a location (`"at": "end"`, `"input"`, or an element id) and report what a real
device would show, including its resolution. They never modify the pulse. Values go to `summary.json ->
instruments[]`, arrays to `result.npz` (prefixed with the instrument's `name`) and to `<name>.csv`.
Example 17 (`docs/examples/17-instrument-tour`) uses every kind once. Every option of every instrument is listed in the
[option reference](options.md#instruments).

**Every instrument is a device model.** Its parameters (resolution, range, sampling, acceptance, detector) are
declared with units and defaults, and every output carries a `settings` block with the value of each parameter that
was used -- given or default -- so a results file says exactly which instrument it describes:

```jsonc
"instruments": [ { "name": "spec", "kind": "spectrometer", "values": { "fwhmM": 1.1e-8, ... },
                   "settings": { "resolutionM": 2e-10, "points": 2048, "normalize": "peak", "rangeM": "default: the spectrum's support", ... } } ]
```

**Several of the same.** Instruments can be repeated anywhere, also several of the same kind at one place. Give
each a `name`; a repeated or missing name is made unique (`spectrometer_plate`, `spectrometer_plate_2`, ...), with a
warning when a name you gave is used twice.

| kind | parameters | reports |
|---|---|---|
| `energyMeter` (`powerMeter`) | `rangeM` [min, max], `polarizerDeg`, `repRateHz` | energyJ, averagePowerW, transmissionFromInput |
| `spectrometer` | `resolutionM` (Gaussian FWHM), `rangeM`, `points`, `polarizerDeg`, `normalize` (`peak` / `area` / `none` = J/m), `db`, `wavelengthOffsetM` | spectrum vs wavelength; peak, centroid, FWHM, RMS and -10 dB widths |
| `scope` (`photodiode`) | `riseTimeS` or `bandwidthHz`, `points` | detected power vs time (impulse-response limited), FWHM, peak |
| `autocorrelator` | `type`: `intensity` (default) or `interferometric`; `rangeS`, `points`, `resolutionS` (delay resolution), `shgAcceptanceFwhmM` (crystal bandwidth) | trace; AC FWHM and deconvolved FWHM (Gaussian sqrt2, sech^2 1.543); IAC peak-to-background (8:1) |
| `frog` | `type`: `shg` (default) or `pg`; `points` (default 256), `spectralResolutionM`, `shgAcceptanceFwhmM` | trace [delay, frequency], axes, marginals |
| `dscan` | `material` + `insertionM` [min, max], or `gddRangeS2` [min, max]; `steps`, `spectralResolutionM`, `shgAcceptanceFwhmM` | SHG trace vs insertion/GDD, integrated SHG, FWHM vs scan, optimum |
| `contrast` | `resolutionS`, `windowS`, `dynamicRangeDb` | intensity in dB; pre/post-pulse contrast; pedestal energy fraction |
| `polarimeter` | `points`, `extinctionRatio` | Stokes s1, s2, s3 (integrated and vs wavelength), degree of polarisation, azimuth, ellipticity |
| `pulseSummary` | | every scalar pulse metric (see results.md) |
| `compressor` | `optimizeTod` (true), `maxN` | ideal compressor: the GDD/TOD that maximises peak power, compressed FWHM, Strehl |
| `conversion` | `at`: an active element (crystal) | energy and photon number of the main output and every by-product, aux energy in, efficiencies (vs input, vs all energy in), by-product centroids |
| `phaseMatchingCurve` | `at`: a crystal; `scan`: `angle` (`rangeDeg`), `wavelength` (`rangeM`), `temperature` (`rangeK`), `period` (`rangeM`); `points` | low-conversion sinc²(ΔkL/2) and Δk vs the scan variable, peak, FWHM ([Crystals](crystals.md)) |
| `delayScan` | `at`: a crystal with aux beams; `fromS`, `toS`, `points` | re-runs the crystal on the pulse that entered it for each aux delay: output energy vs delay, optimum |
| `beamProfile` | `at`: after an element with a transverse model | fluence vs radius (radial) or x / y cuts and map (cartesian); second-moment radius, M², true peak fluence ([Spatial models](transverse.md)) |
| `crossCorrelator` (`thirdOrderCorrelator`) | `rangeS`, `stepS` / `points`, `fineStepS` + `fineRangeS`, `dynamicRangeDb` (110), `noise`, `resolutionS`, `shgAcceptanceFwhmM`, `polarization`, `probeDelaysS` | third-order correlation vs delay (linear and dB, with and without noise), pre/post-pulse contrast and their delays, contrast at chosen delays ([below](#crosscorrelator)) |

<!-- anchors for links from the generated option reference -->
<span id="autocorrelator"></span><span id="beamprofile"></span><span id="compressor"></span><span id="contrast"></span><span id="conversion"></span><span id="delayscan"></span><span id="dscan"></span><span id="energymeter"></span><span id="frog"></span><span id="phasematchingcurve"></span><span id="polarimeter"></span><span id="pulsesummary"></span><span id="scope"></span><span id="spectrometer"></span><span id="wavefrontsensor"></span>

## Parameters every instrument shares

| key | what it does |
|---|---|
| `at`, `name` | where the instrument sits, and its name in the results |
| `sampling` | what part of the beam it sees: near / far field, pinhole, fibre, slit ([below](#sampling-where-the-instrument-looks)) |
| `filter` | a spectral filter model in front of it -- optics, a band-pass, the detector's spectral response; applied to the field before it is measured (any [filter model](models.md#kinds-per-category): `constant`, `table`, `gaussian`, `expression`, `callback`) |
| `detector` | the read-out of the recorded trace ([below](#detector-model)) |
| `measured` | your recording, for comparison and calibration (fitting plugin) |

### Detector model

```jsonc
{ "kind": "spectrometer", "resolutionM": 0.2e-9,
  "filter": { "kind": "table", "wavelengthsM": [900e-9, 1100e-9], "transmission": [0.6, 0.9] },      // grating + detector response
  "detector": { "calibration": 1.0, "noise": 0.01, "dynamicRangeDb": 40, "bitDepth": 12, "seed": 1 } }
```

| `detector` key | meaning |
|---|---|
| `calibration` | reading / true value (e.g. 1.03 for a power meter reading 3 % high) |
| `noise` | multiplicative noise, relative rms |
| `noiseFloor` or `dynamicRangeDb` | additive noise rms relative to the trace peak |
| `bitDepth` | ADC quantisation, full scale at the trace maximum |
| `seed` | random seed: the same seed gives the same noise |

It acts on the recorded arrays and readings of the spectrometer, scope, autocorrelator, FROG, d-scan and energy
meter, and the width and peak readings (FWHM, centroid, peak) are then re-evaluated on the detected trace. Cameras
(`sensor`) and the cross-correlator have detector models of their own. Leave the detector out when you fit a model to
measured data: the recording has the noise, the model should not.

### Resolution and acceptance

* **Spectrometers** (`resolutionM`, FROG and d-scan `spectralResolutionM`): Gaussian instrument function of the given
  FWHM in wavelength.
* **SHG crystals** (`shgAcceptanceFwhmM` on autocorrelator, FROG, d-scan, cross-correlator): the phase-matching window
  of the doubling crystal, as a Gaussian of the given FWHM in SH wavelength, applied to the sum-frequency signal. In a
  FROG it narrows the frequency marginal; an intensity autocorrelation of a Gaussian pulse with linear chirp keeps its
  shape (only the signal drops) -- check the FROG or d-scan when the crystal may be too thick.
* **Delay** (`resolutionS` on autocorrelator, contrast, cross-correlator): Gaussian smoothing along delay.
* **Detection bandwidth** (scope `riseTimeS` / `bandwidthHz`).
* **Polarisers** (polarimeter `extinctionRatio`, analyser `polarizerDeg` on spectrometers and energy meters).

## crossCorrelator

A third-order cross-correlator (Sequoia, Tundra, ...) measures the contrast of a pulse over 10+ orders of magnitude:
the pulse is frequency doubled, the SH gate G(t) = |E²(t)|² is scanned across the fundamental, and the third harmonic
is detected,

S(τ) = ∫ I(t) G(t - τ) dt.

```jsonc
{ "kind": "crossCorrelator", "name": "xc", "at": "end",
  "rangeS": [-200e-12, 100e-12], "stepS": 0.5e-12, "fineStepS": 20e-15, "fineRangeS": 3e-12,
  "dynamicRangeDb": 110, "probeDelaysS": [-50e-12, -10e-12] }
```

* The gate is the pulse itself (the SH of the dominant polarisation), so the gate's own pedestal is folded in, as in
  the real device. A post-pulse at +T of relative intensity ε also appears as a weaker artefact (~ε²) at -T through
  the gate.
* The detector floor is `dynamicRangeDb` below the peak (or `noiseFloor`), with a fluctuating background unless
  `"noise": false`; `resolutionS` adds the delay resolution of the scanner, `shgAcceptanceFwhmM` the finite SH
  bandwidth of the doubling crystal.
* Reports: `fwhmS`, `noiseFloor`, `dynamicRangeDb`, `prePulseContrastDb` / `prePulseDelayS` and
  `postPulseContrastDb` / `postPulseDelayS` (strongest feature outside `windowS`), `contrastAt_m50ps_db` ... for each
  of `probeDelaysS`; arrays `delay_s`, `signal`, `signal_db`, `signal_ideal`.

The pulse needs its pedestal: give the input one ([run spec: pedestal](run-spec.md#pedestal)), let ghosts of plates
and ND filters ([components](components.md#ghost-reflections)) and nonlinear conversion of post-pulses create
pre-pulses, and widen the window when the features are far from the peak. [Tutorial 5](../tutorials/05-contrast.md)
follows a pre-pulse through an amplifier chain.


## Sampling: where the instrument looks
<span id="sampling"></span>

By default an instrument sees the **whole beam**: the spectrum, autocorrelation or power integrated over the full
transverse profile. Real instruments often do not. A spectrometer might be fed by a fibre at the focus of a lens,
a photodiode might sit behind an iris, and an autocorrelator might only accept the centre of the beam. When the
pulse varies across the beam (SPM is stronger on axis, a Kerr lens, spatial chirp, a thermal lens), these all give
different answers. Add `"sampling"` to any non-imaging instrument:

```jsonc
{ "kind": "spectrometer", "name": "fibre_spec", "at": "plate",
  "sampling": { "plane": "far", "focalLengthM": 0.1,
                "aperture": { "kind": "fibre", "radiusM": 25e-6, "na": 0.22 } } }
```

| key | default | meaning |
|---|---|---|
| `plane` | `near` | `near`: the beam as it is at that point. `far`: the focal plane of an ideal lens of `focalLengthM` (the Fourier transform of each frequency's field). |
| `focalLengthM` | 0.2 | far field only |
| `propagateM` | 0 | near field: free-space propagation before the aperture (negative: back towards a waist) |
| `aperture.kind` | `pinhole` if an aperture is given, else `integrated` | `integrated` (whole beam), `pinhole`, `iris` (both circular), `fibre` (circular core, plus an NA check), `slit` (a vertical strip of `widthM` across the whole beam), `point` (the local spectrum at one point; shape only, no transmission) |
| `aperture.radiusM` | 50 µm (fibre 25 µm) | pinhole / iris / fibre core radius |
| `aperture.widthM` | 2 × radius | slit width |
| `aperture.centerXM`, `centerYM` | 0 | aperture position (off-axis sampling) |
| `aperture.na` | 0.22 | fibre: warns if the focused beam's convergence angle exceeds the NA |

**What is computed.** For every frequency ω, the field E(x, y, ω) on the chosen plane is integrated over the
aperture. This gives a power transmission T(ω) and an aperture-averaged spectral phase φ(ω). The instrument then
measures the pulse with its spectrum multiplied by √T(ω)·e^{iφ(ω)}. So a spectrometer shows the local spectrum,
and an autocorrelator or FROG shows the local pulse, including the spectral phase variation across the aperture.

* The field comes from the **carried transverse profile** after an element with a `transverse` model. On a 1-D
  line, it is an ideal Gaussian from the instrument's `"beam"`, the element's beam model or the run's input beam
  (the camera rules, see [Camera](#camera)). A 1-D Gaussian has the
  same spectrum everywhere in the near field, so sampling only matters there through the wavelength dependence of
  the far-field spot. That spot is smaller for shorter wavelengths, so a small fibre in the far field couples the
  blue side slightly better.
* The far field uses the exact Fourier (cartesian) or Hankel (radial) integral of each frequency's field, scaled
  by λf, as the far-field camera does. It is normalised so that ∫|u|² dA equals the near-field power.
* The instrument reports `sampling` (a description) and `samplingTransmission` (the fraction of the pulse energy
  that passes the aperture).
* Imaging instruments (`camera`, `wavefrontSensor`, `beamProfile`, `spaceTime`) ignore `sampling`, because they
  already image a plane of their own (`imaging`).

Example 11 shows the effect clearly: after a self-focusing plate, a 15 µm pinhole on axis sees a 13 % wider
spectrum and an 11 % shorter autocorrelation than the whole beam.

## Recorded data (`measured`, fitting plugin)

With the fitting plugin installed, any instrument can be given your recording (`"measured": "data/spectrum.csv"`,
a CSV, NPZ or JSON file). The run compares it with the simulated reading, and a `"fit"` in the run spec calibrates
the model against it. See [Fitting plugin](../plugins/fitting.md).

## Camera

One instrument, `camera`, images the beam. `imaging` says **what** the sensor sees, `sensor` describes the
**detector**:

```jsonc
{ "kind": "camera", "name": "focus", "at": "plate",
  "imaging": { "plane": "far", "focalLengthM": 0.5, "magnification": 1 },
  "sensor":  { "pixels": [256, 256], "pixelSizeM": 5.5e-6, "bitDepth": 12, "readNoiseCounts": 2,
               "backgroundCounts": 40, "qe": { "kind": "table", "wavelengthsM": [700e-9, 900e-9], "transmission": [0.6, 0.3] } } }
```

| `imaging` key | default | meaning |
|---|---|---|
| `plane` | `near` | `near`: the beam at the instrument's location; `far`: the focal plane of an ideal lens of `focalLengthM` (exact Fourier / Hankel integral of every frequency's field, scaled by λf); `relay`: the near field after the `relay` steps |
| `propagateM` | 0 | near field: free-space propagation before imaging (negative: back to a waist, e.g. to image a focus) |
| `focalLengthM` | 1 | far field: focal length |
| `magnification` | 1 | imaging magnification onto the sensor (pixel size / magnification = pixel size in the object plane) |
| `relay` | — | list of thin lenses `{"focalLengthM": f}` and gaps `{"gapM": d}`, applied in order with exact diffraction on the field's grid (good for relays that do not change the beam size much; use `magnification` for large image scales) |

| `sensor` key | default | meaning |
|---|---|---|
| `pixels` | 256 | n or [nx, ny] |
| `pixelSizeM` | auto | pixel pitch on the sensor; auto fits ~5 beam radii across |
| `centerXM`, `centerYM` | 0 | sensor centre in the object plane |
| `qe` | 1 | quantum efficiency vs wavelength: any spectral-filter model (`constant`, `table`, `gaussian`, `expression`, `"@callback"`); the image weights each colour by it |
| `bitDepth` | 0 (ideal) | ADC bits; with bits the image is quantised and a raw `counts` array is added |
| `fullScaleJPerM2` | 1.1 × peak | fluence at full scale |
| `readNoiseCounts` | 0 | read noise (rms counts) |
| `backgroundCounts` | 0 | dark / background offset (in `counts`; removed again in the fluence image) |
| `hotPixelFraction` | 0 | fraction of stuck (saturated) pixels |
| `noiseSeed` | 1 | random seed |

`bandM` [λmin, λmax] (top level) adds a band-pass filter. Sensor keys may also be written at the instrument's top
level (as in format version 1). The kinds `nearFieldCamera` and `farFieldCamera` of version-1 files are migrated
automatically to `camera` with `imaging.plane` `near` / `far` ([Schemas & migrations](schema.md)).

**Reports.** Fluence image `fluence_j_m2` [x][y] (J/m²) with axes `x_m`, `y_m`, and `counts` with a bit depth;
centroid, 1/e² radii (D4σ/2) in x and y, ellipticity, peak fluence, energy in the frame, saturated-pixel fraction,
`imagingPlane`; near field: M²; far field: divergence half-angles (radius / f), energy inside the 1/e² ellipse
and the Strehl ratio (peak vs the same near-field amplitude with a flat wavefront).

**Where the field comes from.** After an element with a transverse model (`"transverse": {"model": "radial" |
"cartesian"}`) the camera images the real simulated beam. On a 1-D element it images an ideal Gaussian from the
instrument's `"beam"`, the element's beam model or the run's input beam, and says so in its notes.

## Wavefront sensor

| kind | parameters | reports |
|---|---|---|
| `wavefrontSensor` | `pixels` (lenslets, 64), `wavelengthM` (default: spectral centroid), `threshold` (1 % of peak fluence), `apertureRadiusM` (default the 1/e² radius), `halfWidthM`, `zernikeTerms` (15, max 36), `propagateM`, `bandM` | wavefront map in waves (piston and tilt removed), Zernike coefficients (Noll, rms-normalised, waves), rms / PV, rms without defocus, radius of curvature from defocus (R > 0 diverging; the equivalent in air: R in a medium / n), astigmatism, coma, spherical |

Validated against Gaussian optics (`DiagnosticsTests`): near-field radius and refocused waist < 1 %, far-field
radius λf/(πw0) < 0.5 %, wavefront curvature R(z) < 1 % (radial) / 2 % (cartesian), Zernike fit exact;
`PluginTests.Camera_UnifiedImaging_*`: a 2f relay reproduces the far field (< 3 %), the old kinds match `camera`.

## Virtual instruments

Virtual instruments read things no real device can measure directly -- the complete complex field, its absolute
phase, both time and frequency at once, the field at every point of the beam vs time. They are flagged
`"virtual": true` in their values and drawn with a square (□) marker and *(virtual)* titles in the plots, so their
output is never mistaken for a lab measurement. (Ideal-device instruments such as `pulseSummary`, `conversion`,
`phaseMatchingCurve` and `delayScan` are also shown as virtual.)

| kind | parameters | reports |
|---|---|---|
| `wigner` | `points` (256 time rows), `gateFwhmS` (> 0: spectrogram with a Gaussian gate), `maxN` | Wigner distribution W(t, ω) [time][frequency] (normalised to max |W|; negative values are interference terms), its time and frequency axes; the time marginal is |A(t)|² |
| `fieldProbe` | `threshold` (1e-3), `realField` (false), `maxN` | temporal phase and instantaneous wavelength vs time, group delay and GDD vs wavelength, carrier-envelope phase, linear chirp rate; with `realField` the real field Re[A(t) e^(-iω0t)] on the optical cycle |
| `spaceTime` | `points` (512), `maxRadiusM` (2.5 w), `axis` (`x`/`y`, cartesian) | intensity I(r, t) and spectral fluence S(r, λ) across the beam, arrival time, local FWHM and centroid wavelength vs position; pulse-front delay and spatial chirp at r = w, on-axis vs r = w duration |

## Where an instrument sits

`ultrafast-plot` numbers the instruments in system order (input first, then element by element, end last), draws
them on the system schematic (`system.png`, and the strip at the top of `overview.png`) and prefixes every panel
title with the number and location, e.g. `③ photodiode stretched · after probe_stretched`. Put instruments wherever
you would put them on the table -- `"at": "input"`, an element id (after that element), `"end"` -- or add a
`probe` element as a named place in the beam (see `docs/examples/02-stretcher-compressor/run.json`).

Conventions: Stokes s3 = +1 for the `"rcp"` input polarisation; FROG/d-scan use the x polarisation.

Add your own by implementing `IInstrument` and calling `InstrumentFactory.Register("myKind", spec => new MyInstrument())`
([Extending](extending.md)).
