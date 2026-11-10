# AO plugin: adaptive optics, beam shaping and pulse shaping

The `ao` plugin adds four **parametric** device elements to the engine and a Python module with the algorithms that drive them.
Nothing in it is a vendor model: stroke, coupling, grey-level curves and birefringence are parameters you set, and the one preset
(`dispersion: "liquidCrystal"` without values) is labelled generic and unverified. Replace it with numbers from your device.

| piece | where | what |
|---|---|---|
| `deformableMirror` | C# element | actuator layout, Gaussian influence function, stroke, drive bits, surface-to-wavefront factor; compiles to a core `modulator` |
| `slm` | C# element | pixels, fill factor, grey levels through a voltage-to-phase curve, liquid-crystal dispersion, cross-talk; compiles to a core `modulator` |
| `spectralShaper` | C# element | pixelated Fourier-plane mask of a 4f pulse shaper, as a spectral transfer function |
| `aopdf` | C# element | programmed spectral phase and amplitude with a finite acoustic window, efficiency and a spurious replica |
| `tiledArray` | Python builder | the core tile source (`initialProfile` kind `tiles`) with per-channel piston, tilt, delay, amplitude. **Not an element** |
| algorithms | `ultrafast.algorithms` | `holography`, `control`, `sensorless` (NumPy only, usable without the plugin) |
| benches | `ultrafast_ao` | `DmBench`: a simulated AO bench that drives the engine; `combining_metrics`; NumPy twins of the devices |

```python
import ultrafast as uf
from ultrafast import build as b
from ultrafast_ao import DmBench

bench = DmBench(aberration={4: 0.3, 5: -0.2, 7: 0.15})     # waves rms, Noll index
print(bench.strehl())                                       # 0.31
D = bench.calibrate()                                       # poke every actuator (push-pull)
cmd, singular_values = bench.correct(D, regularisation=1e-2)
print(bench.strehl(cmd))                                    # 0.87
```

Install: the plugin is laid out by `dotnet build UltrafastEngine.sln` in `artifacts/plugins/ao`; `python3 python/tools/build_engine.py`
copies it and its Python module. Check with `ultrafast plugins list`.

## deformableMirror

A mirror whose surface is the sum of the actuator influence functions. The beam sees `2 cos(aoi) x surface` as an optical path (an
achromatic phase `k x OPD`, so the mirror does not disperse). Positive commands move the surface **away from the incoming beam** and
lengthen the path.

| key | unit | default | meaning |
|---|---|---|---|
| `layout` | | `square` | `square`, `hex` (odd rows shifted by half a pitch, row spacing pitch sqrt(3)/2), `ring` (ring k at radius k pitch), `explicit` |
| `actuatorsX`, `actuatorsY` | | `actuatorsY = actuatorsX` | square: x by y; hex: per row by rows |
| `ringCounts` | | | ring: actuators per ring, first entry the centre actuator |
| `positionsXM`, `positionsYM` | m | | explicit: positions |
| `pitchM` | m | required | actuator pitch |
| `commandsM` | m | 0 | **drive coefficient** per actuator: a list in layout order, or `[actuatorsX][actuatorsY]` (square). The surface is the sum of the influence functions, so one driven actuator among zeros reaches its command at its centre, but a uniform command gives a ripple of about `(1 + 2c + ...)^2` (1.7 at `c = 0.15`) times the command in the interior and 1 at the edge, not a flat piston. `strokeM` and `driveBits` act on the command |
| `influenceCoupling` | | 0.15 | surface at a neighbouring actuator when one is driven: the influence function is `exp(ln(c) (r/pitch)^2)`, so the value at one pitch is exactly `c` |
| `strokeM` | m | unlimited | commands are clipped to `+-strokeM`; clipped actuators are listed in the element's note |
| `driveBits` | | 0 | drive resolution over `+-strokeM` (needs `strokeM`) |
| `incidenceAngleRad` | rad | 0 | wavefront = `2 cos(aoi) x surface` |
| `clearApertureRadiusM` | m | 0 (footprint) | circular clear aperture |
| `marginPitches` | pitches | 0.5 | border around the outermost actuators; light outside the footprint is blocked |
| `reflectivity` | | 1 | power |
| `samplesPerActuator` | | 4 | surface cells per pitch (the modulator averages them onto the simulation grid) |
| `designWavelengthM` | m | carrier | wavelength at which the path becomes a phase |

Use at least two simulation cells per actuator. Hysteresis, creep and print-through are not modelled. A `driveBits` grid has `2^b - 1` steps over `+-strokeM`, so zero is not a level: a zero command becomes a uniform piston of half a step (no tilt).

## slm

A phase-only liquid-crystal panel. The written phase of a pixel is the **phase of its grey level at the design wavelength**; at another
wavelength it is scaled by the liquid crystal's dispersion.

| key | unit | default | meaning |
|---|---|---|---|
| `pixelsX`, `pixelsY` | | from the map | panel size (1920 x 1080 is supported: see [Speed](../reference/speed.md) and the budget below) |
| `pitchM`, `pitchYM` | m | required, `pitchM` | pixel pitch |
| `offsetXM`, `offsetYM`, `rotationRad` | m, rad | 0 | placement |
| `fillFactor`, `gapTransmission`, `outsideTransmission` | | 1, 0, 0 | active fraction, amplitude in the gaps and outside the panel |
| `greyLevels` | | 256 | number of levels |
| `lutKind` | | `linear` | `linear`: `maxPhaseRad k / levels`; `tanh`: S-shaped (`lutSteepness`); `table`: `lutPhaseRad` |
| `greyLevel` or `phaseTargetRad` | | | pixel values `[x][y]`, a callback `"@name"` or an NPZ `file` (`grey_level`, `phase_target_rad`). A phase target is wrapped to 2 pi and mapped to the **nearest grey level** of the curve by circular distance (a curve that spans more than 2 pi is searched at every image of the target) |
| `designWavelengthM` | m | carrier | wavelength at which the curve holds |
| `dispersion` | | `liquidCrystal` | `none`: the written phase is an optical path (scales with 1/lambda); `liquidCrystal`: scales with `omega dn(omega)` |
| `deltaIndexCauchy` | | generic preset | `dn = A + B/lambda^2 + C/lambda^4`, `[A, B_m2, C_m4]`. The preset `[0.1805, 1.6e-14]` is **generic and unverified** |
| `crosstalkSigmaCells`, `crosstalkKernel` | pixels | 0 | fringing field |
| `zeroOrderLeak` | | 0 | amplitude that passes unmodulated |

**Chromatic model.** The 2 pi reset written at the design wavelength `lambda_d` is `2 pi s(lambda)` at `lambda`, with
`s = lambda_d dn(lambda) / (lambda dn(lambda_d))`. A blazed ramp of period `n` pixels then sends the fraction `sinc^2(1 - s)` into the
first order (and the rest into the zero and second orders). `dispersion: "none"` gives `s = lambda_d / lambda`. The pixel phases go to
the core modulator unquantised, because the grey-level steps are already in the curve (a non-uniform curve is not a uniform quantiser).

**Large panels.** A 1920 x 1080 panel is passed as a callback or an NPZ file, not as a list. On the S2 grid (2048 x 1280, one frequency,
`outputs.keepProfiles: "used"`) a run takes about 2.4 s on the development laptop; the slow test allows the S2 limit of 13 s.

## spectralShaper

A Fourier-plane mask of a 4f line. Frequency maps to position `x = D (2 pi c / omega - lambda_c)` (exact in wavelength, `D` = `dispersionMPerM` taken as constant over the band; a real grating's `D` also varies with `lambda`, which is not modelled) (or
`focalLengthM x order x lines / cos(theta_d)` from a grating). Each frequency is a Gaussian spot of 1/e^2 field radius `w0`
(`spotRadiusM`, or `lambda f / (pi w_in)` from `inputBeamRadiusM`), so the transfer function is the overlap of the spot with the pixels
(erf differences): finite resolution, fill factor, gaps and phase quantisation all follow from it. **Modelling choice:** the overlap is with the spot's *field* profile `exp(-x^2/w0^2)` (the transfer function multiplies the field); weighting by the intensity profile instead would change the blur of the pixel edges (a first-harmonic factor `exp(-(pi w0/p)^2/2)` instead of `exp(-(pi w0/p)^2)`). The Weiner test uses the field overlap, so it checks the implementation of this choice, not the choice. The spot size ignores the `cos(theta_d)/cos(theta_i)` anamorphic factor of an oblique grating (give `spotRadiusM` directly for that). `offsetM` moves the pixel array relative to the spectrum.

| key | unit | default | meaning |
|---|---|---|---|
| `pixels`, `pixelPitchM` | | required | the mask |
| `fillFactor`, `gapTransmission`, `outsideTransmission` | | 1, 0, 0 | active fraction; amplitude in the gaps and beyond the array |
| `dispersionMPerM` | | from the grating | position per wavelength (1e6 = 1 mm/nm) |
| `gratingLinesPerMm`, `incidenceAngleRad`, `order`, `focalLengthM` | | | give these instead of `dispersionMPerM` |
| `spotRadiusM` or `inputBeamRadiusM` | m | required | resolution |
| `phaseRad`, `amplitude` | | 0, 1 | per pixel |
| `phaseWrapRad`, `greyLevels` | | 2 pi, 0 | wrapping and quantisation over 2 pi |
| `centerWavelengthM`, `offsetM` | m | carrier, 0 | array centre |

**The programmed delay stays in the transfer function**: a linear phase `tau (omega - omega_0)` moves the pulse by `tau` inside the time
window; only the carrier phase is taken out of the frame, so the arrival-time bookkeeping is unchanged. A pixelated mask makes side
pulses at `2 pi / (pixel bandwidth)`: **set `grid.windowS` to cover them**, the grid planner does not know about them (nor about a large programmed GDD, which stretches the pulse: nothing warns when the output wraps round the window). The spectral grid spacing is `2 pi / windowS`: keep at least ~10 samples per pixel, otherwise the sampled mask edges change the harmonics (a window of 8 ps with 1.6 ps side pulses, 5 samples per pixel, gave 2.5 % errors in the side pulses). With gaps (active
fraction `f`) the side pulses carry `[sin(pi f)/(pi f)]^2` of the main pulse (Weiner, Rev. Sci. Instrum. 71, 1929 (2000)).

## aopdf

| key | unit | default | meaning |
|---|---|---|---|
| `delayFs`, `gddFs2`, `todFs3`, `fodFs4` | fs, fs^2, fs^3, fs^4 | 0 | polynomial phase about `centerWavelengthM` |
| `spectralPhaseRad`, `amplitudeTable`, `tableWavelengthsM` | | | tables, linear in omega |
| `windowS` | s | 0 (unlimited) | components whose programmed group delay lies outside `+-windowS/2` are not diffracted |
| `efficiency` | | 1 | power; booked as the mechanism `aopdf` |
| `replicaAmplitude`, `replicaDelayS` | | 0 | spurious replica split off the main pulse: `H x (1 + a exp(i omega tau)) / sqrt(1 + a^2)`: a second pulse with `a^2` of the main pulse's energy, total energy = `efficiency` |

## tiledArray (Python)

```python
from ultrafast_ao import tiled_array, combining_metrics
prof = tiled_array(0.5e-3, layout="hex", rings=1, pitch_m=3e-3, piston_rad=[...], delays_s=[...])
# b.transverse("cartesian", ..., initial_profile=prof), then a lens, a gap of f and a beamProfile camera
```

The engine normalises the **total power**, so N in-phase tiles peak at N x a single tile of the same total power (each tile carries
P/N and the amplitudes add: N^2 x 1/N). Use an engine run of the same array with zero pistons and delays as the reference for
`combining_metrics` (`axis_over_reference`, `central_lobe_fraction`, `grating_lobe_fraction`). A delay moves the **envelope only**: the
on-axis fluence of N tiles of a Gaussian pulse (FWHM `tau_p`) with delays `tau_k` is `(1/N^2) sum_kl exp(-ln2 (tau_k - tau_l)^2 / tau_p^2)`
of the aligned value (derivation: the overlap of two Gaussian envelopes shifted by `tau` is `exp(-ln2 tau^2 / tau_p^2)`).

## Algorithms (`ultrafast.algorithms`)

| module | functions | reference |
|---|---|---|
| `holography` | `gerchberg_saxton` (optional quantisation in the loop), `weighted_gs`, `mraf`, `double_phase_encode`, `bolduc_encode`, `grey_levels`, `quantise_phase`, `focal_field` | Fienup, Appl. Opt. 21, 2758 (1982); Di Leonardo et al., Opt. Express 15, 1913 (2007); Pasienski and DeMarco, Opt. Express 16, 2176 (2008); Bolduc et al., Opt. Lett. 38, 3546 (2013) |
| `control` | `shack_hartmann` (lenslet FFT + windowed centroid, noise optional), `modal_fit`, `southwell_reconstructor`, `interaction_matrix`, `control_matrix` (Tikhonov or TSVD), `correct`, `integrator_loop` | Southwell, JOSA 70, 998 (1980) |
| `sensorless` | `spgd`, `hill_climb`, `modal_search`, `transmission_matrix`, `focus_phases` | Vorontsov and Sivokon, JOSA A 15, 2745 (1998); Vellekoop and Mosk, Opt. Lett. 32, 2309 (2007) |

Things measured while writing them (and stated in their tests): quantisation in the GS loop helps at 2 and 3 grey levels but **not** at 4
or 8 on the test target; SPGD with `gain 20, amplitude 0.2` on 16 channels diverges (gain 10 locks to 1.000 on 10 of 10 seeds);
modal search climbs by one `bias` per sweep until the metric is concave, because a peak-type metric is convex far from the optimum;
the phase-stepping transmission matrix recovers the phases exactly (mixed second difference: no reference arm, any output however
dark) but the amplitudes only relative within a row.

## What the tests check

| test (file) | reference |
|---|---|
| DM tilt ramp moves the focus; factor `2 cos(aoi)` (`AoDeviceTests`) | geometric optics, first-moment theorem; cos 60 deg = 0.5 |
| influence function at one pitch = coupling; hex neighbours (`AoDeviceTests`) | definition; lattice geometry |
| stroke, drive bits (`AoDeviceTests`) | clipping, `2^b - 1` steps |
| SLM blazed efficiency vs levels; binary grating orders (`AoDeviceTests`) | `[sin(pi/N)/(pi/N)]^2` (Goodman); sampled staircase Fourier weight |
| SLM at other wavelengths (`AoDeviceTests`) | `s(lambda)` from the Cauchy law; the first-order coefficient with phases scaled by `s` |
| phase target to the nearest level of a tanh curve (`AoDeviceTests`) | identical fields to 1e-12 |
| 1920 x 1080, 256 levels (`AoDeviceTests`, slow) | S2 scale budget; erf product of the overfilling Gaussian |
| aopdf delay, efficiency, GDD, window, replica (`AoDeviceTests`) | shift theorem; Diels and Rudolph chirp formula; erf of the cut spectrum |
| spectral shaper side pulses and delay (`AoDeviceTests`) | Weiner; shift theorem |
| Strehl, Maréchal; twin vs engine; fit residual vs actuators (`test_ao_plugin.py`) | exact on-axis field `|<a e^{i phi}>|^2`; `exp(-sigma^2)` |
| one-shot correction, closed loop, modal search (`test_ao_plugin.py`) | twin prediction; `(1 - g)^k` |
| tiled array N, pistons, delays, SPGD lock (`test_ao_plugin.py`) | `|sum e^{i phi}|^2 / N^2`; Gaussian envelope overlap |
| GS monotone, WGS uniformity, Bolduc first order, double phase (`test_ao_algorithms.py`) | Fienup 1982; Di Leonardo 2007; Bolduc 2013 |
| Shack-Hartmann spot shift `f dW/dx`; modal fit exact; Southwell exact for quadratics and second-order convergent (`test_ao_algorithms.py`) | geometric optics; analytic |
| transmission matrix and focusing enhancement `(pi/4)(N - 1) + 1` (`test_ao_algorithms.py`) | Vellekoop and Mosk 2007 (3000 outputs, tolerance 0.6 %) |

## Limits

* Devices are idealised unless you supply measured curves. No hysteresis, creep, thermal drift, polarisation dependence or SLM
  flicker.
* `commandsM`: see the note in the table. `ultrafast_ao.combining_metrics` uses the first Airy zero of the filled hex aperture as the central lobe.
* `control.modal_fit` evaluates the Zernike gradient at the lenslet centre, the sensor measures the mean slope over the subaperture: a bias of order `pitch^2/12 W'''` for Noll >= 7 that grows with the lenslet pitch (not tested; the pupil must be centred on `x_m`, `y_m` = 0).
* The `spectralShaper` is the 1-D spectral transfer function. Spatial chirp and space-time coupling of a misaligned 4f line (stage T3)
  are not modelled.
* A transverse device acts on the carried profile at every stored frequency; with a pulse, raise `transverse.spectralSamples` until
  `spectralSamplingError` is small ([Modulator](../reference/modulator.md)).
* The wavefront sensor of the bench is the engine's `wavefrontSensor` (an ideal zonal sensor); the physical lenslet model is
  `ultrafast.algorithms.control.shack_hartmann`, which needs a complex field you build yourself.
* The GPU parity of the modulator and tile source was not run on a device (S2 note).
