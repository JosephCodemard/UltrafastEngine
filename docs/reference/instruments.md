# Instruments

Instruments read the pulse at a location (`"at": "end"`, `"input"`, or an element id) and report what a real
device would show, including its resolution. They never modify the pulse. Values go to `summary.json ->
instruments[]`, arrays to `result.npz` (prefixed with the instrument's `name`) and to `<name>.csv`.
Example 17 (`docs/examples/Diagnostics/instrument-tour`) uses every kind once. Every option of every instrument is listed in the
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
| `energyMeter` (`powerMeter`) | `rangeM` [min, max], `polarizerDeg`, `repRateHz` | energyJ, averagePowerW, transmissionFromInput; with `rangeM` the edge bins count with the fraction of the bin inside the band (error falls as the square of the bin width) |
| `spectrometer` | `resolutionM` (Gaussian FWHM), `rangeM`, `points`, `polarizerDeg`, `normalize` (`peak` / `area` / `none` = J/m), `db`, `wavelengthOffsetM` | spectrum vs wavelength; peak, centroid, FWHM, RMS and -10 dB widths |
| `scope` (`photodiode`) | `riseTimeS` or `bandwidthHz`, `points` | detected power vs time (impulse-response limited), FWHM, peak |
| `autocorrelator` | `type`: `intensity` (default) or `interferometric`; `rangeS`, `points`, `resolutionS` (delay resolution), `shgAcceptanceFwhmM` (crystal bandwidth) | trace; AC FWHM and deconvolved FWHM (Gaussian sqrt2, sech^2 1.543); IAC peak-to-background (8:1; the delay axis is symmetric with an odd number of points, so it contains d = 0, step lambda0/(16 c); if `points` forces a coarser step a note says the fringes are under-sampled; the background is the separated-pulse level 2 sum I^2) |
| `frog` | `type`: `shg` (default), `pg` (polarisation gate) or `sd` (self-diffraction); `points` (default 256), `spectralResolutionM`, `shgAcceptanceFwhmM` | trace [delay, frequency], axes, marginals; `frogType` ([FROG geometries](#frog-geometries)) |
| `xfrog` (`sfgFrog`) | `reference` (the known gate pulse, a run-spec pulse object), `points`, `spectralResolutionM` | cross-correlation FROG trace at the sum frequency; the reference field is stored with it ([FROG geometries](#frog-geometries)) |
| `dscan` | `material` + `insertionM` [min, max], or `gddRangeS2` [min, max]; `steps`, `spectralResolutionM`, `shgAcceptanceFwhmM` | SHG trace vs insertion/GDD, integrated SHG, FWHM vs scan, optimum |
| `contrast` | `resolutionS`, `windowS`, `dynamicRangeDb` | intensity in dB; pre/post-pulse contrast; pedestal energy fraction |
| `polarimeter` | `points`, `extinctionRatio` | Stokes s1, s2, s3 (integrated and vs wavelength), degree of polarisation, azimuth, ellipticity |
| `pulseSummary` | | every scalar pulse metric (see results.md) |
| `compressor` | `optimizeTod` (true), `maxN` | ideal compressor: the GDD/TOD that maximises peak power, compressed FWHM, Strehl |
| `conversion` | `at`: an active element (crystal) | energy and photon number of the main output and every by-product, aux energy in, efficiencies (vs input, vs all energy in), by-product centroids |
| `phaseMatchingCurve` | `at`: a crystal; `scan`: `angle` (`rangeDeg`), `wavelength` (`rangeM`), `temperature` (`rangeK`), `period` (`rangeM`); `points` | low-conversion sinc²(ΔkL/2) and Δk vs the scan variable, peak, FWHM ([Crystals](crystals.md)) |
| `delayScan` | `at`: a crystal with aux beams; `fromS`, `toS`, `points` | re-runs the crystal on the pulse that entered it for each aux delay: output energy vs delay, optimum |
| `beamProfile` | `at`: after an element with a transverse model | fluence vs radius (radial) or x / y cuts and map (cartesian); second-moment radius about the centroid, M² (`m2`, and `m2Y` on 2-D grids), true peak fluence ([Spatial models](transverse.md)) |
| `crossCorrelator` (`thirdOrderCorrelator`) | `rangeS`, `stepS` / `points`, `fineStepS` + `fineRangeS`, `dynamicRangeDb` (110), `noise`, `resolutionS`, `shgAcceptanceFwhmM`, `polarization`, `probeDelaysS`, `gateArm` | third-order correlation vs delay (linear and dB, with and without noise), pre/post-pulse contrast and their delays, contrast at chosen delays ([below](#crosscorrelator)) |

<!-- anchors for links from the generated option reference -->
<span id="autocorrelator"></span><span id="beamprofile"></span><span id="compressor"></span><span id="contrast"></span><span id="conversion"></span><span id="delayscan"></span><span id="dscan"></span><span id="energymeter"></span><span id="frog"></span><span id="phasematchingcurve"></span><span id="polarimeter"></span><span id="pulsesummary"></span><span id="scope"></span><span id="spectrometer"></span><span id="wavefrontsensor"></span>

## Parameters every instrument shares

| key | what it does |
|---|---|
| `at`, `name` | where the instrument sits, and its name in the results |
| `sampling` | what part of the beam it sees: near / far field, pinhole, fibre, slit ([below](#sampling-where-the-instrument-looks)) |
| `filter` | a spectral filter model in front of it -- optics, a band-pass, the detector's spectral response; applied to the field before it is measured (any [filter model](models.md#kinds-per-category): `constant`, `table`, `gaussian`, `expression`, `callback`) |
| `detector` | the read-out of the recorded trace ([below](#detector-model)) |
| `axis` | where a curve instrument takes its readings: explicit points or a scan grid, pixel / bin width, resolution ([below](#axis-where-readings-are-taken)) |
| `measured` | your recording, compared on its own axis, range and resolution ([Measured data](measured-data.md)); used by fits |

Every instrument output carries a `settings` block with each parameter in effect (given or default), so a results
file states exactly which device -- resolution, range, sampling, detector -- produced each reading. Virtual
instruments (`pulseSummary`, `compressor`, `wigner`, `fieldProbe`, `spaceTime`, `beamProfile`, `conversion`) are
ideal by design: they report the field itself.

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
| `saturationLevel` | readings are clipped at this fraction of the trace peak (0: none) |
| `response` | spectral response of the detector, `{wavelengthsM, values}` (e.g. the quantum efficiency): spectral instruments only; linear in wavelength, held at the ends |
| `counting` | `"photons"`: the energy spectrum is first converted to photon counts (x lambda / lambda at the centre of the axis); default `"energy"` |
| `bitDepth` | ADC quantisation, full scale at the trace maximum |
| `seed` | random seed: the same seed gives the same noise |

It acts on the recorded arrays and readings of the spectrometer, scope, autocorrelator, FROG, d-scan and energy
meter, and the width and peak readings (FWHM, centroid, peak) are then re-evaluated on the detected trace. Cameras
(`sensor`) and the cross-correlator have detector models of their own. Leave the detector out when you fit a model to
measured data: the recording has the noise, the model should not.

### Detector presets

A detector you use again -- with its calibrated floor, saturation level, attenuation segments or quantum-efficiency table -- is defined once and
named. **The engine contains no instrument's numbers; they are yours (a manual, a calibration).** In the run spec:

```jsonc
"detectors": {
  "my-xc": { "instrument": { "saturationLevel": 0.9, "noiseFloor": 1e-11,
                             "segments": [ { "fromS": -2e-12, "toS": 2e-12, "attenuation": 1e-3 } ] } },     // fills what the instrument does not set
  "my-ccd": { "detector": { "response": { "wavelengthsM": [900e-9, 1000e-9, 1100e-9], "values": [0.45, 0.35, 0.08] },
                            "counting": "photons", "noise": 0.005, "bitDepth": 16 } }
},
"instruments": [ { "kind": "crossCorrelator", "detector": "my-xc" },
                 { "kind": "spectrometer", "detector": "my-ccd" },
                 { "kind": "spectrometer", "detector": { "preset": "my-ccd", "noise": 0.02 } },           // a preset with overrides
                 { "kind": "spectrometer", "detector": { "preset": "presets/ccd.json" } } ]                 // or a JSON file (relative to the run spec)
```

A preset has an `instrument` object (keys filled into an instrument that does not set them: `saturationLevel`, `segments`, `noiseFloor`,
`dynamicRangeDb`, `resolutionM`, ...; the instrument's own keys win) and a `detector` object (the read-out above; the instrument's keys given next
to `"preset"` win). An unknown preset is a warning that names it. Python: `build.detector_preset`, `build.detector`, `build.detector_response`,
`run_spec(detectors=...)`.

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
* **Sampling of the axis** (`axis`, every curve instrument): where readings are taken and what each integrates over
  ([below](#axis-where-readings-are-taken)).
* **Ranges and grids**: spectrometer `rangeM` and `points`; FROG `points`; d-scan `insertionM` / `gddRangeS2` and
  `steps`; cross-correlator `rangeS`, `stepS`, `fineStepS`, `dynamicRangeDb`; camera `pixels`, `pixelSizeM`,
  `bitDepth`; delay scan `fromS`, `toS`, `points`.

### Axis: where readings are taken

A real instrument reads at particular points: the pixels of a spectrometer, the delay steps of an autocorrelator or
cross-correlator, the samples of an oscilloscope. Each reading integrates over its pixel or bin. `axis` models
this for every curve instrument: `spectrometer`, `scope`, `autocorrelator`, `crossCorrelator`, `polarimeter`,
`contrast`, `delayScan`, `beamProfile` and `phaseMatchingCurve`.

```jsonc
{ "kind": "spectrometer", "resolutionM": 0.3e-9,
  "axis": { "values": [1020.1e-9, 1020.5e-9, ...], "binWidth": "pixel" } }      // your spectrometer's pixel wavelengths

{ "kind": "autocorrelator", "axis": { "from": -2e-12, "to": 2e-12, "step": 20e-15 } }   // a coarse delay scan

{ "kind": "scope", "axis": { "step": 50e-12, "binWidth": "pixel", "resolution": 200e-12 } }   // 20 GS/s sampling
```

| key | meaning |
|---|---|
| `values` | explicit reading positions, in the axis's SI unit (m for wavelengths, s for delays and time) |
| `from`, `to`, `step` / `points` | a regular grid (default: the simulated range and number of points) |
| `binWidth` | what each reading integrates over: a number, or `"pixel"` (the spacing to the neighbours); default: point readings |
| `resolution` | a Gaussian blur (FWHM) applied before binning, in addition to the instrument's own resolution keys |

All of the instrument's curves are resampled together. dB curves are recomputed from their linear counterparts, so
a cross-correlator keeps its noise floor and dynamic range. Readings outside the simulated range read zero (no light
there), with a note. The width and peak readings (FWHM, centroid, peak) are re-evaluated on the sampled curve, and a
`detector` then acts on the sampled readings. Recorded data (`measured`) are compared on their own axis in any
case. `axis` matters when the sampling itself changes what is measured (coarse steps, wide pixels),
and for exporting simulated data on your instrument's grid.

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
* **Delayed light.** With `grid.tail` ([Delayed light](delayed-light.md)) the SHG gate is made from the main field only,
  and the probe channel (linear in intensity) from main + tail: a ghost or an echo appears as the real device sees it.
* **Resolution.** With `resolutionS` the trace, the noise floor and the reported contrasts are all relative to the peak of the
  resolution-limited trace: smoothing smears a distant pre-pulse and the main peak alike, so a 1e-6 pre-pulse still reads
  -60 dB, and the floor stays `dynamicRangeDb` below the (smoothed) peak.
* **Elements in the gate arm only** (`gateArm`). A real correlator makes the second-harmonic gate in its own arm, and an
  element there (a plate whose ghosts replicate the gate, a filter, dispersion) does not touch the fundamental. Give
  `"gateArm": {"elements": [ ... ]}` (any element list of a system file, optional `"materials"`): the second harmonic of the
  dominant polarisation, with the crystal acceptance applied, is sent through them compiled at lambda0/2, so glass and
  coatings are evaluated at the second-harmonic wavelength, and the gate is `|E_SH|^2` of the result. The probe channel does not see
  them. The time window of the instrument is used: it must hold the replicas (`grid.windowS`, or `grid.tail`). A plate with
  `ghosts` gives replica k of relative level `R^(2k)` (R: the face reflectance at lambda0/2) at `-2 k n_g d / c` (the gate is
  delayed against the probe), as in the single-shot cross-correlator of Ma et al., High Power Laser Sci. Eng. 2018, doi 10.1017/hpl.2018.57 (Fig. 4c).
  Elements before the instrument in the system act on both arms. The run is linear: a nonlinear element in the gate arm sees an
  arbitrary power scale (the gate is scaled to the energy of the fundamental pulse).
* **Saturation and range stitching** (generic, set by you -- no instrument's numbers are built in). `saturationLevel` clips the
  reading (signal x attenuation + floor) at that fraction of the unattenuated peak; `segments` is a list of
  `{fromS, toS, attenuation (or gain), noiseFloor}`: the signal in that delay range is measured through the attenuation (a
  power factor) and divided by it afterwards, so its floor rises as 1/attenuation and a strong peak no longer saturates.
  Example: `"saturationLevel": 0.5, "segments": [{"fromS": -2e-12, "toS": 2e-12, "attenuation": 1e-3}]`. Notes count the
  saturated points.
* Reports: `fwhmS`, `noiseFloor`, `dynamicRangeDb`, `prePulseContrastDb` / `prePulseDelayS` and
  `postPulseContrastDb` / `postPulseDelayS` (strongest feature outside `windowS`), `contrastAt_m50ps_db` ... for each
  of `probeDelaysS`; arrays `delay_s`, `signal`, `signal_db`, `signal_ideal`.

The pulse needs its pedestal: give the input one ([run spec: pedestal](run-spec.md#pedestal)), let ghosts of plates
and ND filters ([components](components.md#ghost-reflections)) and nonlinear conversion of post-pulses create
pre-pulses, and widen the window when the features are far from the peak. [Tutorial 5](../tutorials/05-contrast.md)
follows a pre-pulse through an amplifier chain.


## FROG geometries
<span id="frog-geometries"></span>

`frog` `type` selects the gate (all with one x polarisation; delay `tau`, gate pulse delayed by `tau`):

| type | signal field | carrier of the signal |
|---|---|---|
| `shg` | `E(t) E(t - tau)` | `2 w0` |
| `pg` | `E(t) |E(t - tau)|^2` | `w0` |
| `sd` (self-diffraction) | `E(t)^2 E*(t - tau)` | `w0` |

SHG-FROG traces are symmetric in the delay and cannot tell `E(t)` from `E*(-t)`; PG and SD traces can. For a linear chirp
`omega(t) = w0 + b t` the intensity-weighted frequency centre of the row at delay `tau` is `w0 + (2/3) b tau` (PG) and
`w0 + (4/3) b tau` (SD) (Kane and Trebino, Opt. Lett. 18, 823 (1993), appendix C); `FrogKindsTests` checks the engine's traces against these.

`xfrog` (alias `sfgFrog`) gates the pulse with a **known reference** `R` of any carrier (`reference`: the pulse object of a run
spec, e.g. `{"shape": "gaussian", "centerWavelengthM": 800e-9, "durationFwhmS": 30e-15, "chirp": {"gddS2": 195e-30}}`): the
signal `E(t) R(t - tau)` is at the sum frequency `w0 + w_R`. The reading stores the type (`frogType`) and, for XFROG, the reference
field (`reference_time_s`, `reference_field_re`, `reference_field_im`, `reference_omega_rad_s`), so [retrieval](retrieval.md) from it
needs nothing else.

## singleShotCorrelator

Time-to-space cross-correlator (Ma et al., High Power Laser Sci. Eng. 6, e61 (2018)): the pulse under test and a sampling pulse (its second
harmonic, `samplingPulse: "secondHarmonic"`, or the pulse itself, `"fundamental"`, for an autocorrelator) cross at an angle in a crystal. The signal at
position x across the overlap measures the correlation S(tau) = integral I_PT(t) I_S(t - tau) dt at tau = tau0 + x * slope, so a camera reads a delay trace
in one shot.

| key | meaning |
|---|---|
| `crossingAngleDeg` or `externalCrossingAngleDeg` | full angle between the beams inside the crystal, or in air (symmetric incidence, each beam refracted with its own index) |
| `beamWidthM` | width W of the overlap across the signal; window T = W x slope |
| `crystalLengthM` | L; geometric resolution dtau = L \|(n_g,T - n_T sin^2 a) / cos a - (n_g,S - n_S sin^2 b) / cos b\| / c |
| `crystal` (default BBO) or `indexPt`, `indexSampling`, `groupIndexPt`, `groupIndexSampling` | ordinary (x) index of the library crystal, or given indices |
| `delayIndex` | `group` (default) or `phase`: with `phase` the resolution is exactly Ma et al.'s eq. (2), L \|n_T cos a - n_S cos b\| / c |
| `points`, `delayOffsetS`, `dynamicRangeDb`, `noise`, `seed` | camera pixels across W, delay at the centre, floor |

The angles a, b of the two inputs to the signal follow from k_T + k_S = k_signal with the phase indices (k_T sin a = k_S sin b). The slope
(n_T sin a + n_S sin b) / c is eq. (1): beams that enter from air at fixed angles keep their tangential wave number, so the phase index enters.
Outputs: `x_m`, `delay_s`, `signal`, `signal_db`, `signal_ideal`, `signal_raw`; values `windowS`, `resolutionGeometricS`, `resolutionPixelS`,
`resolutionS`, `delayPerPositionSPerM`, angles, `windowCoverage`, `fwhmS`, pre / post-pulse contrast.

**What is simulated.** The correlation of the temporal intensities at the instrument, a rectangle of width dtau (each position collects all depths of the
crystal) and the pixel average, with a flat-top overlap. Area is conserved: the raw signal integrated over delay equals (integral I_PT dt)(integral I_S dt),
`windowCoverage` reports how much of it falls inside the window. **What is not.** The beams are not propagated through the crystal here: no conversion
efficiency or depletion, no beam profile across the overlap, no walk-off, no air scatter of the signal, no stripe filter, no reflections between the crystal
faces. The engine's own spatial model (a Branched `sfg` crystal with two crossed arms on a Cartesian grid) does reproduce the mapping: the test
`SpatialSfgOfTwoCrossedBeams_PutsAReplicaAtTheInstrumentsPosition` puts replicas at x = T c / (2 sin(10 deg)) at 20 degrees (slow, about 2.5 minutes).

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

## Recorded data (`measured`)

Any instrument can be given your recording (`"measured": "data/spectrum.csv"`, a CSV, NPZ or JSON file). The run
compares it with the simulated reading on the recording's axis, only where the simulated instrument has readings,
after blurring the simulation to the recording's resolution when the recording is coarser. See
[Measured data](measured-data.md). A `"fit"` in the run spec ([fitting plugin](../plugins/fitting.md)) fits transform
elements and other parameters against it, and instrument-response parameters (`instrument:NAME/resolutionM`) can be
fitted too.

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
| `wavefrontSensor` | `pixels` (lenslets, 64), `wavelengthM` (default: spectral centroid), `threshold` (1 % of peak fluence), `apertureRadiusM` (default the 1/e² radius), `halfWidthM`, `zernikeTerms` (15, max 36), `propagateM`, `bandM` | wavefront map in waves (piston and tilt removed), **slope maps** `slope_x_rad`, `slope_y_rad` (λ ∂W/∂x and ∂y of the full wavefront, tilt kept, NaN outside the threshold, `[x][y]` on `x_m`, `y_m`: the zonal input of a control matrix), Zernike coefficients (Noll, rms-normalised, waves), rms / PV, rms without defocus, radius of curvature from defocus (R > 0 diverging; the equivalent in air: R in a medium / n), astigmatism, coma, spherical |

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
`probe` element as a named place in the beam (see `docs/examples/LaserSystems/stretcher-compressor/run.json`).

Conventions: Stokes s3 = +1 for the `"rcp"` input polarisation; FROG/d-scan use the x polarisation.

Add your own by implementing `IInstrument` and calling `InstrumentFactory.Register("myKind", spec => new MyInstrument())`
([Extending](extending.md)).
