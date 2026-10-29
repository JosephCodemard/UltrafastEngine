# Delayed light: tails, ghosts, echoes, scatter, ripple

Weak light that arrives long after the pulse -- the ring-down of gas absorption lines, plate ghosts, mirror-substrate
echoes, scatter, coating ripple -- needs time. A window that holds all of it on the nonlinear grid can be nanoseconds
long, and every nonlinear step then pays for it. This page describes how the engine carries such light, and the
elements that make it.

| | what | where |
|---|---|---|
| [Delayed-light ledger](#the-delayed-light-ledger) | a second, long, linear grid next to the nonlinear one | `grid.tail` |
| [Window planning](#window-planning) | which satellites wrap, how much energy; grow the window; strict mode | `grid.extendForSatellites`, `grid.strict` |
| [Plate ghosts](#plate-ghosts) | ghosts with wavelength-dependent coating reflectance | `plate` with `coating`, `ghosts` |
| [Mirror substrate echoes](#mirror-substrate-echoes) | light leaking through an HR coating, reflected by the back face | `mirror.substrate` |
| [Coating ripple](#coating-ripple) | random phase / amplitude ripple, pedestal within a few ps | `mirror.ripple` |
| [Delayed scatter](#delayed-scatter) | scatter returned as incoherent delayed light; analytic model from the roughness | `delayedScatter` |
| [Small-scale self-focusing](transverse.md#small-scale-self-focusing) | 2-D plate with seeded noise / ripple, Bespalov-Talanov check | `transverse.initialProfile.noise` |
| [Gas lines](../plugins/hitran.md) | HITRAN absorption and dispersion of air | hitran plugin, `environment.air` |
| [Detector saturation, stitching, response, presets](instruments.md#detector-presets) | a generic, user-configured read-out | instrument and `detectors` keys |

## The delayed-light ledger

```jsonc
"grid": { "tail": true }                      // window from the satellites (at least 8 x the main window)
"grid": { "tail": { "windowS": 2e-9 } }       // or give the window of the tail grid
```

**What it is.** Two grids with the **same time step and carrier**. The main grid is the usual nonlinear one, sized
for the pulse alone. The tail grid is `K` times longer (`K` a power of two, at least 2). The main frequency samples are
every `K`-th sample of the tail grid, and the main time axis is the central part of the tail's, so nothing is
interpolated. The field is always the sum `U = embed(A_main) + tail`.

**How a linear element acts.** It is applied to `U` exactly, on the tail grid. The result is then split by a smooth
window (1 inside 85 % of the main half-window, a cos² taper to 0 at its edge) into the new main field and the new tail.
The split only moves light between the two parts. It never changes `U`, so energy is conserved and light that left
the main window comes back (a stretcher followed by a compressor works with a window much smaller than the stretched
pulse). A linear element costs a few FFTs of the tail grid.

**How a nonlinear element acts.** The nonlinear solver runs on the main grid only. The tail passes through the
element's linear terms (dispersion, absorption, surfaces).

### Cost and smooth elements

A linear element is applied **exactly** on the tail grid (three long FFTs) only when it can have structure finer than the main window allows:
ghosts, echoes, scatter, ripple, coatings, gas lines. Terms that are smooth by construction -- dispersion, bulk glass and air without lines,
flat or power-law loss, bare Fresnel surfaces, design waveplates, polarisers, rotators, grating pairs -- have an impulse response much shorter than
the window, so main and tail are propagated **separately** (main on the main grid, the tail on the long grid by a multiplication), which gives the same
sum without the FFTs. `summary.json -> tail` counts both kinds (`exactSteps`, `separateSteps`). `grid.tail.exactSteps: true` forces the exact step
everywhere (the tests check both agree to 1e-9 in energy and 1e-6 in power). A term marks itself smooth with `LinearTerm.IsSmooth`; a custom term
that does not is treated exactly.

### Cross-phase of the tail by the main pulse

In a Kerr element the tail is not only dispersed and absorbed: to first order in the tail, `A = A_m + A_t` gives it the change
`i gamma (2 |A_m|^2 A_t + A_m^2 A_t*)` (cross-phase and the coherent four-wave term), `gamma = k0 n2 integral dz / A_eff`, with `|A_m|^2` the mean
of the powers entering and leaving the element and `A_m` the field entering. It acts on the part of the tail inside the main window (where the main
pulse is); the split puts everything near the pulse on the main grid, where the full nonlinear solver already treats it exactly, so the correction
is for tail that only reaches the pulse inside the element. It is first order in the B-integral and in the tail (scalar, no Raman), on by
default, and off with `grid.tail.crossPhase: false`. Test: for B = 0.11 it reproduces the difference between the full nonlinear solutions with and
without a weak overlapping replica to 7.5 % (the neglected terms are of order B); without it the error is 100 %.

**Costs.** Measured on one machine, 10 m of air with three water lines followed by a 3 mm Kerr plate (B = 0.06): the
same system on one 2 ns grid (N = 524 288) took 1.38 s; with the ledger (main N = 1024, tail N = 262 144) 0.28 s, same
B-integral. The saving grows with the number of nonlinear steps (the tail needs no steps). Memory is one tail grid of
complex `x` and `y` spectra (N = 2^18: 8 MB) plus one snapshot per probe, instrument location and `end`.

**What is neglected** (and so what to check):

* The tail's own nonlinear propagation (its self-phase, its mixing with itself), and cross-phase by the main pulse beyond the first-order term
  above (second order in B). The tail is at least 10⁴ weaker than the main pulse in the cases it is meant for. A note is added to the
  result when the tail holds more than `grid.tail.warnEnergyFraction` (default 0.01) of the energy.
* Interference of main and tail in nonlinear instruments. Autocorrelators, FROG and d-scan see the main field only
  (a note says so). Linear instruments (spectrometer, scope, contrast, polarimeter, energy meter) see main + tail.
* The tail passes active elements (crystals, gain, lens/spatial elements) through their linear Pre/Post terms only; a
  note names the element. An active element that changes the grid or carrier drops the tail (a warning gives its energy).

**Instruments.** `crossCorrelator`: the SHG gate is made from the **main field only**; the probe channel is linear in
intensity, so it uses main + tail. This is what a ghost or an echo looks like to a real third-order correlator.

**Results.** `summary.json -> tail` (`n`, `windowS`, `dtS`, `factor`, `energyJ`, `fractionOfOutput`,
`edgeEnergyFraction`); the loss budget has the mechanism `delayed light (tail)` -- the net energy moved from the main
field into the tail, so `input - output = sum of booked losses` still closes. A warning appears if more than 0.1 % of
the tail energy is near the edge of the tail window (it wraps; raise `windowS`). In Python: `result.summary["tail"]`;
the composite field is in C#: `RunResult.TailLedger.Composite(result.Output, result.TailAt["end"])`.

**When to use it.** The grid planner then leaves ghosts and echoes out of the main window (they go to the tail); the
pedestals of the input pulse stay on the main grid. Use it when satellites or tails are much longer than the pulse
and the system has nonlinear elements. For purely linear systems a single large grid is just as exact.

!!! note "The carrier is not snapped with a tail"
    `grid.snapCarrier` normally moves the carrier by up to half a frequency step. A small main window has a large step (8.5 ps
    gives 117 GHz, 0.41 nm at 1030 nm), and the tail grid contains the main frequency samples either way, so with `grid.tail`
    the carrier stays where you put it unless you give `snapCarrier: true` (carrier-resolved mode needs it).

### The frame of a narrow line

A propagation step removes the group delay at the carrier from its transfer function (the "co-moving frame"). A gas line
a few GHz wide at the carrier has a group delay of tens of ps, which would shift the whole pulse by an amount that depends
on the exact carrier and the grid. Index models with narrow features therefore offer their smooth part
(`ISmoothIndex`, used by the hitran plugin): the frame is referenced to the smooth index, and the lines remain in the
transfer function and show up as the ring-down behind the pulse.

## Window planning

The grid planner makes room for satellites -- ghosts, echoes, delayed scatter, ripple pedestals and pedestal replicas of
the input pulse -- down to `grid.satelliteLevel` (relative intensity, default 1e-8). With `grid.auto: true` (the default)
the window is grown to hold them. With a manual window (`grid.auto: false`) the engine **warns, specifically**:

```text
Window: element 'w' satellite 1 at +48.8 ps (relative intensity 1.2E-04 = -39.2 dB, about 1.2E-10 J of 1E-06 J) does not fit
the +-10 ps window and wraps around to -1.2 ps. Give grid.windowS >= 125 ps, set grid.extendForSatellites, or use grid.tail.
```

| key | default | meaning |
|---|---|---|
| `grid.extendForSatellites` | false | with a manual window, grow the window (keeping the time step) so the satellites fit |
| `grid.strict` | false | a satellite that wraps is an error instead of a warning |
| `grid.satelliteLevel` | 1e-8 | weakest satellite the window makes room for (and the warning reports) |

Pedestal replicas (`pulse.pedestal`: `replica`, `prePulse`, `postPulse`) are named as `pedestal postpulse`.

## Plate ghosts

A `plate` with `ghosts` adds replicas delayed by `2 n_g d cos(theta_t) / c` per round trip. The round-trip factor uses the
reflectance of the front and back **coatings at each wavelength**: with a `table` coating (`wavelengthsM`, `R`, `T`) or a
`stack` the ghost spectrum follows `R_front(lambda) R_back(lambda)`, and the delay comes from the exact phase
`exp(i 2 n(omega) omega d cos(theta_t) / c)` -- group delay included. `ghosts.frontReflectance` / `backReflectance`
override with a flat number. Example, a 5 mm fused-silica plate (ghost delay 48.8 ps):

```jsonc
{ "id": "w", "type": "plate", "material": "FusedSilica", "thicknessM": 5e-3,
  "coating": { "kind": "table", "wavelengthsM": [900e-9, 1000e-9, 1060e-9, 1200e-9], "R": [0.004, 0.002, 0.0005, 0.0002], "T": [0.996, 0.998, 0.9995, 0.9998] },
  "ghosts": true }
```

## Mirror substrate echoes

Light that leaks through the HR coating of a mirror reflects from the back face of the substrate and returns, delayed.
Give the mirror a `substrate`:

```jsonc
{ "id": "m1", "type": "mirror", "aoiDeg": 0,
  "coating": { "kind": "ideal", "R": 0.9999, "T": 0.0001 },        // or a stack / table: leakage follows T(lambda)
  "substrate": "FS-12.7mm" }                                        // a name, or an object
```

**The transfer.** Per polarisation, exactly:

```text
H(w) = r + sum_k o_k E_k,   E_1 = t t' r_b a exp(i psi),   E_k = E_1 g^(k-1),   g = r' r_b a exp(i psi)
psi(w) = 2 n(w) w d cos(theta_t) / c,    a = exp(-alpha d / cos(theta_t))
```

`r`, `t`: the front coating (air to substrate). `r'`, `t'`: from the substrate side, from the reciprocity and unitarity of a
lossless coating (`t' = t`, `r' = -r* t / t*`). `r_b`: the back face, substrate to air. `o_k`: beam overlap of the k-th echo
(wedge, lateral shift). **The delay is not put in by hand.** It is the group delay of `psi`, `2 n_g d cos(theta_t) / c`:

| substrate (fused silica, 1030 nm, normal incidence) | echo delay (engine, `n_g = 1.4635`) |
|---|---|
| 6.35 mm | 61.98 ps |
| 9.5 mm | 92.7 ps |
| 12.7 mm | 123.96 ps |

(The phase index 1.450 would give 61.4 / 91.9 / 122.8 ps: about 1 % too early for the envelope.) The echo intensity relative
to the mirror's reflection is `|t^2 r_b|^2 / |r|^2`: for `T = 1e-4` and a bare back face (`R_b` = 3.4 %) that is
`1e-8 x 0.034 = 3.4e-10`; an AR-coated back face (`backReflectance: 1e-3`) lowers it by a further factor 34.

**Wavelength dependence.** The leakage is `T(lambda)` of whatever coating you give; the echo is proportional to
`T(lambda)^2 r_b(lambda)`. An HR band centred away from the laser makes the echo spectrum follow the transmission edge --
whether that favours the blue or the red wing depends on the design, so use a `stack` or `table` coating of your mirror
rather than a flat `ideal` one. (The test `MirrorEcho_LeakageFollowsTheCoatingTransmissionAndTheBackFace` shows an echo ten
times stronger at the side where the table coating leaks ten times more.)

**Defining substrates.** Three ways, all equivalent:

```jsonc
"substrate": "FS-12.7mm"                                                        // built-in catalogue
"substrate": { "material": "FusedSilica", "thicknessM": 12.7e-3, "backReflectance": 1e-3, "wedgeDeg": 0.1 }   // inline
// in the system file:  "substrates": { "myMirror": { "material": "FusedSilica", "thicknessM": 9.5e-3, "backCoating": "AR1030" } }
// and then:            "substrate": "myMirror"
```

Built-in names: `FS-3mm`, `FS-6.35mm`, `FS-9.5mm`, `FS-12.7mm`, `FS-25.4mm` (fused silica), `BK7-6.35mm`, `BK7-12.7mm` (N-BK7), all
with a bare back face. Keys: `material` (default `FusedSilica`), `thicknessM`, `backCoating` (name or object) or
`backReflectance` (flat power reflectance), `wedgeDeg` (a wedge steers echo `k` by `2 k n wedge`; with `beamRadiusM` or a tracked
beam the overlap is `exp(-shift^2 / 2 w^2) exp(-(pi w tilt / lambda)^2 / 2)`), `orders` (default 1), `overlap`. The angle is the
mirror's `aoiDeg` (default 0; a Herriott cell: a few mrad). Python: `build.substrate(...)`, `build.mirror(..., substrate=...)`.

## Coating ripple

```jsonc
{ "id": "m1", "type": "mirror", "coating": { "kind": "ideal", "R": 0.9999 },
  "ripple": { "phaseRmsRad": 0.03, "amplitudeRms": 0.01, "maxDelayS": 5e-12, "seed": 7 } }
```

The reflection is multiplied by `A(w) exp(i phi(w))`, `phi = phaseRmsRad N1(w)`, `A = min(1, 1 + amplitudeRms N2(w))`
(a ripple only takes light away). `N1`, `N2` are independent random functions with unit variance,
`N(w) = sqrt(2/K) sum_k cos(w tau_k + phi_k)`, whose delays `tau_k` are uniform in `(0, maxDelayS)`. **To first order the
pedestal is a fraction `phaseRms^2` of the energy, lying within +-`maxDelayS` of the pulse** (second order: within
+-2 `maxDelayS`, a fraction `phaseRms^4 / 4`), which is what models the pedestal of a mirror within +-5 ps. The ripple is a
function of the absolute angular frequency and the seed, so the main grid and the tail grid see the same one. Each mirror
should have its own `seed` (the default comes from the element id). **Measured** ripple: give the mirror a `table` coating
with `phaseRs` or `groupDelayRsS` (see [Surfaces](coatings.md)). The sinusoidal GDD ripple of chirped mirrors
(`rippleGddFs2`) is a separate, older model.

## Delayed scatter

Scatter as delayed light, not only as loss. A fraction `f` of the power is returned into the mode **incoherently** and
later:

```text
H(w) = sqrt(1 - f) + sqrt(f) s(w) sum_k c_k exp(i w tau_k),   s(w) = (w / w0)^(n/2),   c_k ~ complex Gaussian, E|c_k|^2 = p_k
```

(intensity ~ `lambda^-n`; the random phases of `c_k` come from the seed). On average power is conserved: `1 - f` stays in the
main pulse, `f` comes back. Keys of `delayedScatter` (on `mirror`, `interface`, `plate`):

| key | meaning |
|---|---|
| `fraction` | fraction of the power returned as delayed light, per surface / pass |
| `delay` | `{kind: "uniform", minS, maxS}` (default for a plate: 0 .. `2 n_g d cos(theta)/c`, bulk back-scatter; for a mirror with a substrate: 0 .. the echo delay), `{kind: "exponential", tauS, maxS}` or `{kind: "table", delaysS, weights}` |
| `spectralExponent` | `n` (4: Rayleigh-like; default 0 = flat) |
| `seed`, `components` | reproducibility and number of delay components (default 256) |

This is a generic, user-supplied model: no BRDF data are built in.

### Analytic surface model

`"delayedScatter": {"model": "surface", "roughnessRmsM": 2e-9, "correlationLengthM": 200e-6, "beamRadiusM": 1e-3}` derives the fraction from the
roughness. Per surface the total integrated scatter is `TIS = (4 pi sigma n cos(theta) / lambda)^2` (reflection) or
`(2 pi sigma |n1 cos t1 - n2 cos t2| / lambda)^2` (refraction), as for `roughnessRmsM`. For a Gaussian roughness correlation of length `l` the angular
power spectrum of the scatter is `S(f) = pi sigma^2 l^2 exp(-pi^2 l^2 f^2)`, `f = sin(theta) / lambda`; a Gaussian mode of 1/e² radius `w` accepts
`theta < lambda / (pi w)`, i.e. `f < 1 / (pi w)` -- **independent of the wavelength** -- so the share that stays in the mode is
`g = 1 - exp(-(l / w)^2)` (about `(l / w)^2` for a small correlation length). The transfer is `H = sqrt(1 - TIS) + sqrt(g TIS) xi(w)`:
`(1 - g) TIS` leaves the beam (booked as `scatter`), `g TIS` returns after the `delay` kernel with random phases; the wavelength law is that of the TIS,
lambda^-2. Without a `delay` the returned part is indistinguishable from the beam: the transfer is the real `sqrt(1 - (1 - g) TIS)`. A mirror has
1 surface (`surfaces` to change), a plate 2. Give either `roughnessRmsM` on the element (all of the TIS is lost) or this model, not both. The
beam radius comes from `delayedScatter.beamRadiusM`, the element's `beamRadiusM` or the tracked beam. Tests: `SurfaceScatter_*` (the in-mode share against a
numerical integral of `S(f)`, the lost power `TIS (1 - g)`, lambda^-2, the returned power `g TIS`).

## Validation

* **Brute force.** Every ledger test compares with the same system on one large grid without a tail: a plate ghost
  (delay, relative peak power, total energy to 1e-5), a mirror echo, light that leaves the main window and returns, the
  cross-correlator reading of a ghost, and the water-line ring-down of the [hitran plugin](../plugins/hitran.md) against a
  2 ns window (2^19 points at 3.8 fs). `DelayedLightTests`, `HitranTests`.
* **Analytic.** Ghost amplitude `R(lambda)^2` from a table coating; echo `t^2 r_b exp(i psi)` (real and imaginary part), delay
  `2 n_g d / c`; the first-order ripple pedestal `sigma^2`; amplitude-ripple energy `1 - sqrt(2/pi) sigma + sigma^2/2`;
  scatter fraction, delay support and `lambda^-n` weight; Faddeeva values; Lorentz peak absorption and dispersion
  extremum; Kramers-Kronig of a Voigt line by a numerical Hilbert transform; Beer-Lambert absorption of lines on the grid.
* The tests are listed in [Testing](../testing.md).

## Not included (yet)

* Instrument-specific numbers for detectors (the saturation, stitching, response and preset keys are generic: [Detector presets](instruments.md#detector-presets)).
* The 2-D plate is covered by [Small-scale self-focusing](transverse.md#small-scale-self-focusing); a GPU run of a 256 x 256 grid at 2^13 time points has not
  been done here (no double-precision GPU).
