# Oscillators and saturable absorbers

Mode-locked lasers are modelled by the `oscillator` element. It produces the steady-state output of a laser and replaces
the incoming beam with it, so place it first in the system. The run's `pulse` sets the grid (centre wavelength,
window). With `"start": "pulse"` it also seeds the laser.

```jsonc
{ "id": "laser", "type": "oscillator", "model": "haus",       // "parametric" | "haus" | "cavity"
  "repRateHz": 80e6,                                          // or "cavityLengthM" (linear: c / 2L; "ring": true: c / L)
  "outputCoupler": 0.05,                                      // power transmission
  "gain": { "smallSignalGain": 1.5, "saturationEnergyJ": 5e-9, "bandwidthM": 100e-9, "shape": "gaussian" },
  "lossPerRoundTrip": 0.02, "gddFs2": -500, "spmRadPerW": 1.5e-6,
  "absorber": { "kind": "kerrLens", "modulationDepth": 0.03, "kappaPerW": 3e-8 },
  "start": "noise", "roundTrips": { "max": 8000, "tolerance": 1e-5 } }
```

## Keys

The keys that matter; every key is in [options: `oscillator`](options.md#oscillator),
[`saturableAbsorber`](options.md#saturableabsorber-also-sesam) and [`resonator`](options.md#resonator).

| key | meaning |
|---|---|
| `model` (required) | `parametric`, `haus` or `cavity` |
| `repRateHz`, or `cavityLengthM` with `ring` | repetition rate; c / 2L for a linear cavity, c / L with `"ring": true` |
| `outputCoupler` | output-coupler power transmission, 0 to 1 exclusive (default 0.05) |
| `gain` | `haus`: the lumped gain (below); `cavity`: a gain medium with the keys of the gain element, plus `passesPerRoundTrip` (2) and `relaxation` (0.2) |
| `lossPerRoundTrip`, `gddFs2` / `gddS2`, `todFs3` / `todS3`, `spmRadPerW`, `absorber` | `haus`: the lumped round trip |
| `elements` | `cavity`: the elements run every round trip |
| `start`, `seed`, `seedEnergyJ` | start-up from noise or from the run's pulse |
| `roundTrips` | a number (the maximum, default 5000) or `{max (5000), tolerance (1e-5), checkEvery (25), snapshots (120)}` |
| `gridPoints` | samples the oscillator iterates on (default 4096; a power of two, at least 64) |
| `output` | `parametric`: the output pulse (run-spec pulse keys) |

`checkEvery`, `tolerance` and `snapshots` take effect only inside the `roundTrips` object, and `relaxation` and
`passesPerRoundTrip` only inside the `gain` object, although the option reference also lists them on the element.

## Model

### Models

| model | what it is | use it for |
|---|---|---|
| `parametric` | `"output": { pulse }`: you give the output pulse (as in the run spec) and the repetition rate | a known seed laser in front of an amplifier |
| `haus` | lumped master equation iterated round trip by round trip: saturated gain with a spectral shape, loss, GDD/TOD, SPM δ·P(t), saturable absorber, output coupler | soliton and fast-absorber mode-locking, parameter studies (fast: thousands of round trips per second) |
| `cavity` | a real gain medium (`"gain": {"medium": ...}` with a cw pump) plus any engine elements in `"elements"`, run in order every round trip | a specific laser design: crystals, chirped mirrors, prisms, SESAMs, Kerr media, filters |

**Gain (lumped).** `smallSignalGain` is the linear small-signal power gain G per round trip (it must be above 1);
the engine works with the ln power gain g0 = ln G (or give `smallSignalLnGain` = g0 directly), saturated as
g(E) = g0 / (1 + E/E_sat) with `saturationEnergyJ` (or with `saturationPowerW` on the average power). In the example
above G = 1.5, g0 = 0.405. It has a `lorentzian` (default), `gaussian` or `parabolic` spectral shape of FWHM `bandwidthM`
around `centerWavelengthM`. Give `fluenceSaturationEnergyJ` to get the Q-switching diagnostic.

**Gain (medium, cavity model).** The gain medium is described like the gain elements ([gain.md](gain.md)):
`medium`, `lengthM`, `pump` (cw), `modeRadiusM`, `passesPerRoundTrip` (2), `slices`. The inversion follows the
cw rate equations with the pump and the *average* intracavity power. The upper-state lifetime is much longer
than a round trip, so the inversion is relaxed towards that quasi-steady state by `relaxation` (0.2) per round
trip. Every pass applies the spectral gain, the dispersion and SPM of the host, and gain narrowing.

**Cavity elements.** Cavity elements are ordinary elements, compiled with ids `oscillatorId.elementId`. Any type
works except another oscillator:

* `chirpedMirror`, `dispersion`, `bulk` (with n2: SPM, Kerr lensing on a `transverse` grid);
* `saturableAbsorber`, `aperture` (soft or hard, acting on a carried profile), `filter`, `lens`.

The output coupler sits at the end of the list.

**Window.** The oscillator iterates on at most `gridPoints` (4096, a power of two of at least 64) samples of the run's grid, with the same time
step and carrier. The result is embedded back into the full window, so a long stretcher downstream does not
slow the oscillator down.

**Start-up.** `"start": "noise"` starts from one photon per mode (quantum noise, seeded by `seed`). It is the
default for `haus` and shows the real build-up. `"start": "pulse"` starts from the run's pulse (scaled to
`seedEnergyJ`); it is the default for `cavity` and is faster.

**Convergence.** Convergence is checked every `checkEvery` (25) round trips, up to `max` (5000). Steady state means
that, over the check interval of `checkEvery` round trips, three checks in a row find the output energy changed by at
most `tolerance` × E × `checkEvery`, the intensity FWHM by at most 10 × `tolerance` × FWHM × `checkEvery` plus one time
step, and the spectral centroid by less than 0.05 of a frequency step. The pulse is re-centred every round trip; the drift is reported as `roundTripTimingOffsetS`,
the correction to the round-trip time.

### Saturable absorbers

`"type": "saturableAbsorber"` (alias `sesam`) works anywhere in a system. In the Haus model it is the oscillator's
`absorber`.

| kind | loss q(t) | parameters |
|---|---|---|
| `slow` (SESAM) | dq/dt = −(q − q0)/τ_A − q P/E_sat,A | `modulationDepth` q0, `nonsaturableLoss`, `recoveryTimeS`, `saturationFluenceJPerM2` + `spotRadiusM` (or `areaM2`, `beam`) or `saturationEnergyJ` |
| `fast` | q0 / (1 + P/P_sat) | `modulationDepth`, `saturationPowerW` (or `saturationIntensityWPerM2` + spot) |
| `kerrLens` | max(0, q0 − κP) | `modulationDepth`, `kappaPerW`: the lumped self-amplitude modulation of Kerr-lens mode-locking |

The field is multiplied by exp(−(q(t) + q_ns)/2). The element reports `pulseLoss`, `saturationParameter`
(E/E_sat) and the arrays `timeS` and `loss` (q(t)). For short pulses on a slow absorber the absorbed fraction is
q0(1 − e^−S)/S.

**Spatial cavities and Kerr-lens mode-locking.** Cavity elements may carry a transverse profile (a Kerr medium
on a radial grid, gaps, lenses or curved mirrors, a soft or hard aperture). The profile goes round the cavity and
settles on the resonator's eigenmode: `LaserTests` checks a lens-guide cavity whose mode reproduces the
Gaussian-beam eigenmode, w0 = sqrt(λ z_R/π). Kerr-lens mode-locking from first principles needs a resonator that
focuses tightly into the crystal and runs near its stability edge, as real KLM lasers do. It then needs
thousands of spatial round trips (≈ 0.1 s each on a 32-point radial grid), so treat it as **experimental**. The
lumped `kerrLens` absorber is the practical model.

### Diagnostics

Values (`summary.json → elements[i].values`):

| value | meaning |
|---|---|
| `outputEnergyJ`, `averagePowerW`, `repRateHz` | output |
| `intracavityEnergyJ`, `intracavityAveragePowerW` | at the output coupler |
| `roundTrips`, `converged` | |
| `netGainPerRoundTrip`, `bIntegralPerRoundTripRad`, `gddPerRoundTripS2` | per round trip |
| `outputFwhmS`, `outputSpectralFwhmM`, `timeBandwidthProduct` | the output pulse |
| `solitonFwhmS`, `fwhmOverSoliton` | soliton area theorem τ = 3.53·\|GDD\|/(δE), δ = B / P_peak (negative GDD only) |
| `pulsesInCavity` | > 1 means multi-pulsing, and a warning is issued |
| `backgroundFraction` | energy more than 5 FWHM from the pulse: cw breakthrough or an unsettled state (warning above 5 %) |
| `qmlParameter` | E_p² / (E_sat,L E_sat,A ΔR) (Hönninger et al., JOSA B 16, 46 (1999)); below 1, Q-switched mode-locking is expected (warning) |
| `inversionFraction`, `smallSignalGainPerPass`, `absorbedPumpPowerW`, `opticalEfficiency` | cavity model |

The gain dynamics are relaxed rather than resolved, so the simulation itself does not show Q-switching;
`qmlParameter` tells you whether a real laser would.

Arrays (`el_<id>_*`):

* `roundTrip`, `outputEnergyPerRoundTrip`, `netGainPerRoundTrip`;
* snapshots `evolutionRoundTrip` × `evolutionTimeS` → `evolutionPower`;
* snapshots `evolutionRoundTrip` × `evolutionWavelengthM` → `evolutionSpectrum`.

`ultrafast-plot` draws them in `oscillator.png`.

### Resonators

A `resonator` is a passive cavity around nested elements, for its modes rather than for a laser pulse. Its `geometry`
is `ring` (the default: the elements once per round trip) or `linear` (forward and back, the end elements once):

```jsonc
{ "id": "cavity", "type": "resonator", "geometry": "ring",                     // "ring": once per round trip; "linear": forward and back, end elements once
  "transverse": { "model": "radial", "points": 256, "radiusM": 3.5e-3, "spectralSamples": 1 },    // optional: the Fox-Li iteration
  "roundTrips": { "max": 800, "tolerance": 1e-7 },
  "elements": [ { "id": "half1", "type": "gap", "lengthM": 0.3 }, { "id": "lens", "type": "lens", "focalLengthM": 0.5 },
                { "id": "stop", "type": "aperture", "radiusM": 1.1e-3 }, { "id": "half2", "type": "gap", "lengthM": 0.3 } ] }
```

* The tracker gives the round-trip matrix, and the element reports `stabilityX`, `stabilityY` ((A + D) / 2), `stable`, `modeRadiusXM`, `modeRadiusYM`, `modeCurvatureXPerM` at the
  reference plane (the start of the first element), `roundTripOpticalLengthM` and `freeSpectralRangeHz`. An unstable cavity gives a warning. A curved end mirror of a linear cavity
  is an element that acts once per round trip, like a lens of f = R / 2.
* With a `transverse` grid (given to nested gaps and bulks without one) the Fox-Li iteration (Bell Syst. Tech. J. 40, 453 (1961)) starts from a Gaussian and runs the round trip until the
  loss settles: `roundTripLoss`, `foxLiRadiusXM`, `foxLiRoundTrips`, `converged`. A cw source (`"shape": "cw"`) makes this one frequency.
* The output beam takes the mode: the tracked beam becomes the eigenmode, and the pulse carries the Fox-Li profile. Its energy is unchanged: this is a mode calculation, not a cavity ring-down.
  The `oscillator` element is the one for pulses building up in a cavity.

It is a nested sequence, not a graph: elements inside cannot be reached by a second beam. A graph loop is the alternative: see [Branched systems](branches.md#loops).

### Python

```python
from ultrafast.laser import Oscillator
osc = (Oscillator("laser", model="haus", rep_rate_hz=80e6, output_coupler=0.05)
       .simple_gain(1.5, saturation_energy_j=5e-9, bandwidth_m=100e-9, shape="gaussian")
       .loss(0.02).dispersion(gdd_fs2=-500).spm(1.5e-6)
       .absorber("kerrLens", modulation_depth=0.03, kappa_per_w=3e-8))
res = uf.run(osc.system(), b.run_spec(b.pulse(800e-9, 30e-15, energy=1e-12), grid={"auto": False, "n": 4096, "windowS": 8e-12}))
Oscillator.soliton_fwhm_s(-500e-30, 1.5e-6, 16.5e-9)   # area theorem
```

A cavity model in Python uses `.gain_medium(...)` and `.add(b.chirped_mirror(...), b.saturable_absorber(...))`.

## Validation

`LaserTests`:

* the soliton area theorem in the Haus model (10 %, `Haus_SolitonModeLocking_ObeysTheAreaTheorem`) and in a Yb:YAG +
  SESAM cavity (15 %, `Cavity_YbSesamOscillator_SolitonAndQmlDiagnostics`, which also checks a single pulse, the
  round-trip GDD to 5 % and QML stability);
* cw gain clamping, E = E_sat(g0/L − 1) (0.2 %, `Haus_CwLaser_GainClampsAtTheLoss`);
* slow-absorber saturation q0(1 − e^−S)/S (2 %, `Sesam_SlowAbsorber_SaturatesAsTheEnergyIntegral`);
* fast-absorber saturation, unsaturated loss 1 − exp(−q0) (1 %, `FastAbsorbers_LoseLessAtHighPower`);
* the multi-pulse and background detectors (`Diagnostics_CountPulsesAndBackground`);
* a lens-guide cavity whose carried profile settles on the Gaussian eigenmode (8 %).

`CavityAndCoherenceTests` (resonator): a lens guide's eigenmode (Kogelnik & Li), a two-mirror cavity, Fox-Li against the
ABCD mode (3 %), and the diffraction loss of plane circular mirrors, near 20 % at N = 1 and falling as N^(−3/2) (slow).

Examples: [Ti:sapphire KLM](../examples/LaserSystems/ti-sapphire-klm-haus/README.md) (Haus model, from noise) and a
[Yb:YAG laser system](../examples/LaserSystems/yb-laser-system/README.md) (SESAM oscillator, stretcher, amplifier,
compressor).

## Limits

* The gain dynamics are relaxed, not resolved: the simulation does not show Q-switching; `qmlParameter` says whether a
  real laser would.
* Kerr-lens mode-locking from first principles (spatial cavity) is experimental; the lumped `kerrLens` absorber is the
  practical model.
* A `resonator` is a mode calculation, not a ring-down: its output energy is unchanged. Its elements cannot be reached by
  a second beam; use a graph loop ([Branched systems](branches.md#loops)) for that.
* The oscillator works on at most `gridPoints` samples of the run's grid.
