# Optical components

The engine has models of what common optical components do to the beam, without ray tracing. Each one is made from the
same parts as the rest of the engine: interface terms (Fresnel or coatings, s/p in the Jones basis), a bulk element
(dispersion, absorption, and nonlinearity or a spatial model when there is a beam), and Jones terms. So overrides,
provenance, the loss budget and the transverse models all work for them too. Every key is listed in the
[option reference](options.md).

| element | what it models |
|---|---|
| [Gaussian beam tracking](#gaussian-beam-tracking) | q parameters (x and y) through the whole system, so later elements get the right beam |
| [`mirror`](#curved-and-off-axis-mirrors) with `radiusOfCurvatureM` | focusing with off-axis astigmatism; `aberration`: Zernike / figure |
| [`plate`](#plate) (`window`) | a plane-parallel plate: two surfaces, the bulk, optional ghost replicas |
| [`ndFilter`](#ndfilter) | neutral density, absorptive or reflective, OD or OD(λ) |
| [`waveplate`](#waveplates) with `material` | thick multi-order, true zero-order and compound zero-order plates with full dispersion |
| [`thinFilmPolarizer`](#thin-film-polarisers) (`tfp`) | a physical quarter-wave stack at its design angle (TMM), transmitted or reflected port |
| [`faradayRotator`](#faraday-rotators) | non-reciprocal rotation with Verdet dispersion, plus the crystal's dispersion and B-integral |
| [`wavefront`](#aberrations) | a Zernike / random wavefront error at one plane |
| [`chirpedMirror`](#gdd-ripple) with `rippleGddFs2` | the GDD ripple of dispersive mirrors and complementary pairs |

## Gaussian beam tracking

When the run (or the system) has a Gaussian `inputBeam`, the engine follows its complex beam parameters q_x and q_y
through every element:

```jsonc
"inputBeam": { "kind": "gaussian", "w0M": 1.0e-3, "zWaistM": 0.0, "m2": 1.1 },   // waist measured from the system input
"physics": { "beamTracking": "auto" }                                            // "auto" (default) | "on" | "off"
```

The q parameters are kept in reduced form q/n (vacuum-equivalent distances), so the ABCD law needs no index
bookkeeping: a length L in a medium of index n adds L/n, a thin lens subtracts 1/f, a mirror in a medium of index n
subtracts 2n/R. A tilted flat interface magnifies the tangential axis by cos θ₂ / cos θ₁. Gratings pairs, crystals,
gain media and plates are crossed with their own length and index (the crystal and gain compilers report the index
they use).

What uses the tracked beam:

* every nonlinear `bulk` / `gap`, `crystal`, `plate` and spatial element **without a `beam` of its own** (a note
  says "beam: the tracked Gaussian beam"), so the intensity there follows the real focusing;
* surfaces without `beamRadiusM`: fluence, peak intensity and LIDT margins, clipping on apertures;
* cameras and sampled instruments on a 1-D beam (the camera images the tracked beam at its location);
* the ghost-overlap estimate of plates.

Each element reports `trackedBeamRadiusXM`, `trackedBeamRadiusYM` and the wavefront curvatures
`trackedCurvatureXM`/`YM` at its exit (`summary.json → elements[].values`). An element with its own `beam` uses it;
tracking carries on unchanged.

Simple astigmatism only: the principal axes are x and y. A fold plane at an arbitrary angle is snapped to the nearer
axis (with a note). Thermal lenses of gain media are not included in the tracking.

## Curved and off-axis mirrors

```jsonc
{ "id": "cm1", "type": "mirror", "radiusOfCurvatureM": 1.0, "aoiDeg": 8, "planeOfIncidenceDeg": 90 }
```

At angle of incidence θ a spherical mirror has the tangential focal length f_t = R cos θ / 2 (in the plane of
incidence) and the sagittal one f_s = R / (2 cos θ). `planeOfIncidenceDeg` is the angle of the s direction from x
(90°, the default, folds in the x-z plane, so x is tangential). The astigmatism acts on the tracked beam and on a
carried transverse profile. A radial grid cannot hold astigmatism: it gets the mean power, with a note (use a
cartesian model). A `lens` can be astigmatic too (`focalLengthXM`, `focalLengthYM`).

## Aberrations

Mirrors, surfaces and lenses accept an `aberration`; the `wavefront` element is a pure wavefront error:

```jsonc
"aberration": {
  "zernikeNollWaves": [0, 0, 0, 0.05, 0, 0.12],   // Noll order from Z1 (piston), waves rms at referenceWavelengthM
  "radiusM": 5e-3,                               // normalisation radius
  "surface": true,                               // numbers are surface height: x 2 cos(aoi) on a mirror, x (n2 - n1) refracting
  "randomRmsM": 5e-9, "correlationLengthM": 2e-3, "seed": 3   // random figure (cartesian grids)
}
```

The wavefront is an optical path difference W(x, y), applied as a phase ω W / c at every frequency (non-dispersive:
exact for mirrors). On spatial grids it changes the carried profile: radial grids keep only the axisymmetric terms
(Z1, Z4, Z11, Z22, ...), cartesian grids keep all of them and the random figure. On a 1-D beam nothing changes: the
element reports the wavefront rms over the beam (piston and tilt removed) and the Maréchal estimate of the Strehl
ratio, `strehlEstimate` = exp(-(2π σ / λ)²), for the tracked beam (or `beamRadiusM`). The Zernike polynomials are
evaluated beyond `radiusM` too, so choose a radius that covers the beam.

## Plate

```jsonc
{ "id": "window", "type": "plate", "material": "FusedSilica", "thicknessM": 6.35e-3, "aoiDeg": 0,
  "coating": "AR1030", "ghosts": { "orders": 1 }, "wedgeDeg": 0 }
```

Front surface → bulk over the internal path d / cos θ_t (dispersion, absorption, and with a beam its nonlinearity or a
spatial model) → back surface. `coatingFront` / `coatingBack` override `coating`.

### Ghost reflections

With `"ghosts": true` (or an object) the part of the beam reflected twice inside the plate (back surface, then front
surface) leaves with the main beam. The transmitted field becomes

E (1 + Σ_k η_k g^k exp(i k ψ(ω))),  g = r_back r'_front,  ψ = 2 n(ω) ω d cos θ_t / c,

per s and p, with the extra internal absorption of each round trip. The replica follows the main pulse by
2 n_g d cos θ_t / c (49 ps for 5 mm of fused silica) with the relative intensity |g|² (R² ≈ 1.1·10⁻³ for bare
fused silica). η_k is the mode overlap of the replica with the main beam: its lateral offset 2 k d tan θ_t cos θ and
the tilt of a wedge (2 k n α) reduce it, η = exp(-s²/2w²) exp(-(π w δ/λ)²/2), with w from the tracked beam,
`ghosts.beamRadiusM` or `beamRadiusM` (full overlap and a note when none is known). `ghosts.overlap` sets it
directly; `frontReflectance` / `backReflectance` replace the coatings' values. The grid planner widens the time
window for the replicas down to `grid.satelliteLevel` (default 1e-8, combinations of several elements' ghosts
included; see [Run spec](run-spec.md#grid)). The reflectances come from the coatings **at each wavelength**
(a `table` or `stack` coating gives a spectrally varying ghost); weak ghosts far outside a manual window are reported by name,
and `grid.tail` carries them on a long linear grid: [Delayed light](delayed-light.md).

**Ghosts of other elements.** `bulk`, `crystal`, `gain`, `amplifier` and thick `lens` elements accept `ghosts` too:
the double reflection at their two end faces at normal incidence, with the delay 2 n_g L / c and the level
R_front R_back (Fresnel against air by default; give your AR coatings as `frontReflectance` / `backReflectance`, e.g.
0.002 each for -54 dB). `ghosts.material` names the index when the element does not (crystals defined inline),
`wedgeDeg` tilts the replicas off the beam. Mirrors and single surfaces have no ghosts of their own: a
back-reflection between two separate surfaces is a cavity -- model the pair as a `plate` of the gap's medium.

Because the replica is part of the field, everything downstream sees it: a later nonlinear element converts a
post-pulse that overlaps a stretched pulse into a pre-pulse after compression (intensity ∝ B²; the test
`Ghost_KerrMediumCreatesPrePulsePartner` checks it) -- the mechanism behind many measured pre-pulses.

## ndFilter

```jsonc
{ "id": "nd1", "type": "ndFilter", "od": 1.0, "kind": "absorptive", "material": "N-BK7", "thicknessM": 2e-3 }
{ "id": "nd2", "type": "ndFilter", "od": [0.3, 0.5], "wavelengthsM": [1000e-9, 1060e-9], "kind": "reflective" }
```

* `absorptive`: the OD is distributed over the glass (power transmission 10^-OD over the internal path), so a
  nonlinear filter sees the decaying intensity. The default substrate N-BK7 stands in for the dispersion of absorbing
  ND glass: give your glass (or its index table) as `material`.
* `reflective`: a metallic coating on the front surface transmits 10^-OD; the rest is booked as
  "ndFilter (reflected)". Default substrate fused silica, 1 mm.
* OD per wavelength: the absorbance is interpolated (not the transmission).
* Ghosts as for a plate; for a reflective filter the front surface reflects strongly from inside
  (`ghosts.frontReflectance`, default 0.5).
* Tilt the filter (`aoiDeg`) as in the lab to avoid back-reflections.

## Waveplates

Without `material` a waveplate is the dispersionless design model (retardance ∝ ω). With a birefringent `material`
it is a real plate with both indices' dispersion:

```jsonc
{ "id": "hwp", "type": "waveplate", "material": "Quartz", "plate": "half", "order": 10, "axisDeg": 22.5 }          // multi-order
{ "id": "qwp", "type": "waveplate", "material": "Quartz", "retardanceWaves": 0.25, "construction": "compound",
  "plateThicknessM": 1.0e-3, "designWavelengthM": 1030e-9 }                                                        // compound zero-order
{ "id": "wp", "type": "waveplate", "material": "MgF2", "thicknessM": 2.1e-3 }                                   // explicit thickness
```

J = R(θ) diag(exp(i n_f ω d / c), exp(i n_s ω d / c)) R(-θ) per plate (fast / slow axis from the sign of the
birefringence); a compound plate adds a second plate of thickness d2 = d1 - λ_d Γ / Δn with crossed axes. The
retardance follows Δn(λ) exactly, a thick plate is correspondingly chromatic, and the two axes have different group
delays (|Δn_g| d / c: about 30 fs per mm of quartz at 1 µm, enough to split a short pulse). The note reports the
retardance at the carrier and that delay. Crystals: Quartz, MgF2, Sapphire, Calcite, YVO4, aBBO; or give
`indexOrdinary` and `indexExtraordinary` models.

## Thin-film polarisers

```jsonc
{ "id": "tfp", "type": "thinFilmPolarizer", "output": "transmitted", "aoiDeg": 56, "axisDeg": 0 }
```

The default `stack` model is a physical (HL)^N H quarter-wave stack (HfO2 / SiO2, `pairs` 12) evaluated with the
transfer-matrix method at the actual angle and wavelength. A quarter-wave stack reflects s over a wide band and p over
a narrower one, so the stack is centred where the operating wavelength (`designWavelengthM`, default the carrier) lies
between the p and the s band edges (`bandPosition` 0..1 within that range): s is reflected, p transmitted, and both
change with angle, wavelength and bandwidth as a real TFP's do. The note gives Tp, Ts and Rs at the carrier.
`output: "transmitted"` continues through the substrate (`thicknessM`, `substrate`); `"reflected"` takes the s
port. `axisDeg` is the transmitted (p) axis in the lab. `model: "ideal"` uses `transmissionP`, `transmissionS`,
`reflectanceS`.

## Faraday rotators

```jsonc
{ "id": "fr", "type": "faradayRotator", "rotationDeg": 45, "material": "TGG", "lengthM": 0.02, "beam": {...} }
{ "id": "fr2", "type": "faradayRotator", "verdetRadPerTM": -40, "fieldT": 1.1, "lengthM": 0.018 }
```

The rotation follows θ(λ) = θ_d (λ_d² - λ0²) / (λ² - λ0²) (`verdetPoleWavelengthM`, 258 nm for TGG; `"none"`
for a constant angle). It is non-reciprocal: in the engine's transported basis two passes add (an isolator's
double pass gives 90°); `"direction": -1` is a beam travelling against the field. With a `material` and `lengthM`
the crystal's dispersion and, with a beam, its B-integral are included -- often the limiting nonlinearity of an
isolator. The built-in TGG index is an approximate two-point fit and has no n2: give your crystal's data.

## GDD ripple

```jsonc
{ "id": "cm", "type": "chirpedMirror", "gddFs2": -250, "bounces": 6,
  "rippleGddFs2": 30, "ripplePeriodNm": 25, "ripplePhaseRad": 0, "complementaryPairs": true }
```

GDD_r(ω) = A sin(2π (ω - ω0) / Ω + φ) per bounce, i.e. the phase -A (Ω / 2π)² sin(...). Complementary pairs
alternate φ and φ + π, so an even number of bounces cancels the ripple. The coating kind `"gddRipple"` gives the
same for a `mirror` coating (with an optional reflectance ripple).
