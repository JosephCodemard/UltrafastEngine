# Spatial models (transverse)

By default every element is one-dimensional in space: the beam enters through its size (an effective area for the
Kerr effect, an overlap for χ(2) coupling) but has no transverse structure. A **transverse model** resolves the
field across the beam, A(r, ω) or A(x, y, ω), so diffraction, self-focusing, spatial depletion, walk-off and
beam-quality changes come out of the simulation.

```jsonc
{ "id": "plate", "type": "bulk", "material": "FusedSilica", "lengthM": 10e-3,
  "beam": { "kind": "gaussian", "w0M": 100e-6 },
  "transverse": { "model": "radial", "points": 96, "radiusM": 0.45e-3 } }
```

Set it per element (`"transverse"`), for all elements of a run (`physics.transverse` in the run spec), or as an
override. Worked example: [11 Spatial Kerr](../examples/11-spatial-kerr/README.md); crystals: [08 radial SHG](../examples/08-shg-lbo/README.md).

## Models

| `model` | representation | cost | use for |
|---|---|---|---|
| `"none"` | 1-D (default) | 1x | everything where the beam stays Gaussian and its size is known |
| `"radial"` | cylindrical symmetry, quasi-discrete Hankel transform of order 0 (Guizar-Sicairos & Gutiérrez-Vega), `points` radial samples | ~points x | self-focusing, Kerr lensing, spatial depletion in SHG/OPA, M² of round beams |
| `"cartesian"` | x-y grid, 2-D FFT, `nx` x `ny` | ~nx·ny x | walk-off, non-collinear beams, decentred or elliptical beams, anything not round |

Options (all optional):

| key | meaning | default |
|---|---|---|
| `points` | radial samples (radial); also nx = ny for cartesian if those are omitted | 64 |
| `nx`, `ny` | cartesian samples | `points` |
| `radiusM` | radial grid extent | `extentFactor` x the largest expected radius |
| `halfWidthM` (`halfWidthXM`, `halfWidthYM`) | cartesian half-width | as above |
| `extentFactor` | grid extent / expected beam radius | 3.5 |
| `paraxial` | use the paraxial diffraction operator −k⊥²/2k instead of the exact √(k² − k⊥²) − k | false |
| `transport` | how a profile is brought in from before (below): `auto`, `exact`, `rescale`, `beam` | `auto` |
| `polarization` | `auto` (vector when the beam carries a y component), `scalar`, `vector`: Jones fields on the grid ([below](#vector-fields-and-the-uppe-coupling)) | `auto` |
| `equation` | nonlinear coupling: `envelope` (paraxial) or `uppe` (n ω / c k_z per plane wave) | `envelope` |
| `initialProfile` | the field at the first spatial element ([below](#initial-profile)) | from the element's `beam` |
| `absorbingBoundary` | linear elements: absorb the light that leaves the grid -- `true` (outer 15 %) or `{"widthFraction": 0.15}` ([below](#diffraction-and-apertures)) | `false` |
| `spectralSamples` | frequencies at which the carried profile is stored (and linear elements propagate it); fewer is faster and smaller for narrowband pulses | 48 |

Resolution rule of thumb: about 15-20 samples across the smallest beam radius the beam reaches, and an extent of
3-4x the largest radius. The Hankel grid is not uniform (denser near the axis); its k-space extent is
j_points/radius. Its orthogonal transform matrix depends only on the number of points and is built once per size
(about 1 s for 2048 points) and shared by every element and run in the process.

### Initial profile

`initialProfile` makes the field at the first spatial element (or at an element with `"transport": "beam"`):

| key | meaning |
|---|---|
| `kind` | `gaussian` (default), `superGaussian` (field exp(−(r²/w²)^order), `order` 4), `flatTop` (super-Gaussian of order 10), `tophat` (hard edge: uniform inside r = w, or \|x\| ≤ w_x, \|y\| ≤ w_y; edge cells carry the area fraction inside) |
| `geometry` | `round` (default: a function of x²/w_x² + y²/w_y²) or `square` (separable in x and y: square or rectangular beams; cartesian grids) |
| `radiusM`, `radiusXM`, `radiusYM` | 1/e² radius (tophat: edge radius / half-width); replaces the size of the element's `beam`, so the element needs no `beam` |
| `radiusOfCurvatureM` | wavefront radius of curvature (negative: converging); replaces the beam's |
| `decenterXM`, `decenterYM`, `tiltXRad`, `tiltYRad` | offset and tilt of the field |
| `ripple`, `noise` | seeds of small-scale structure (cartesian; [below](#small-scale-self-focusing)) |

## Physics in a spatial bulk element

```
dA/dz = [ L_1D(ω) + i (√(k(ω)² − k⊥²) − k(ω)) ] A  +  N(A)        A(r or x,y, ω),  √(W/m²)
```

* `L_1D`: exact dispersion and loss as in 1-D. The diffraction term uses the material index at each frequency
  (chromatic diffraction).
* N: Kerr (n2 I, with the local intensity), delayed Raman, self-steepening and any user time responses -- the same
  physics as 1-D, evaluated point by point across the beam (`y` polarisation is propagated linearly).
* The integrator and its error control are the same as in 1-D; one extra FFT/Hankel transform pair per stage.
* Diffraction is unitary (the Hankel matrix is made exactly orthogonal): linear spatial propagation conserves
  energy to rounding. If more than 0.01 % of the output energy lies in the outer 15 % of the grid the engine warns
  ("reached the edge of the transverse grid -- enlarge transverse.radiusM"): beyond that the field would wrap
  (cartesian) or reflect (radial). `absorbingBoundary` removes that light instead ([below](#diffraction-and-apertures)).
* **Linear elements** (no Kerr or other nonlinearity, no position-dependent properties, a scalar field) propagate
  only the carried profile at its stored frequencies: the operator factorises into the 1-D dispersion of the
  spectrum and the diffraction exp(i (k_z − k) L) of the profile. Between the stored frequencies the change of the
  spectrum (power lost to evanescent waves or to the absorbing boundary, area-weighted phase) is interpolated, as the
  profile is. At the stored frequencies the result equals the full propagation (`DiffractionTests`: 10⁻⁴ of the
  peak). The element's `representation` says "linear: the profile is propagated in n steps".

χ(2) crystals with a transverse model propagate all fields this way, each with its own index (and walk-off and
non-collinear drift in the cartesian model), coupled locally ([Crystals](crystals.md#beams-and-coupling)).

## Vector fields and the UPPE coupling

With `"polarization": "vector"` (or `auto` and a pulse that is not linearly polarised along x) the grid holds both
Jones components, A_x(r, ω) and A_y(r, ω). The Kerr term then is the full isotropic χ(3) tensor (cross-phase
modulation 2/3, coherent coupling 1/3; circular light has 2/3 of the linear self-focusing), the rotational Raman
tensor of molecular gases acts on both components ([Gases](gases.md)), and waveplates, polarisers, Faraday
rotators and coatings act on the carried profile with their Jones matrices. Collapsing to 1-D keeps both components.

`"equation": "uppe"` replaces the paraxial drive of the nonlinear polarisation, i ω n₂ I A / c, by the
unidirectional pulse propagation equation's i ω² P_NL / (2 ε₀ c² k_z(ω, k⊥)) for every plane-wave component. It
matters when the beam is focused tightly or collapses (filaments, [Ionisation](ionization.md)); for paraxial beams
both agree (test `Uppe_AgreesWithEnvelopeForParaxialBeams_AndPlasmaClampsTheFocus`).

## The profile travels with the beam

After a spatial element the pulse carries a **transverse profile** P(r, ω) = E/A normalised so that the 1-D
spectrum A(ω) is exactly the integrated spectrum. The next element decides what to do with it:

* **transverse-aware elements** use it as is: a spatial bulk/gap/crystal, a `lens` (`focalLengthM`, chromatic if
  a `material` is given), a `mirror` with `radiusOfCurvatureM`/`focalLengthM`, and zero-length surfaces. Profiles
  are moved between different grids by interpolation with the quadratic phase taken out and put back (exact for
  focusing/diverging beams); between identical grids they are copied. Between its frequency samples the profile is
  interpolated and renormalised, so E = A·P always carries the exact energy. The note "profile carried exactly from '…'" confirms this.
* **1-D elements with a length** (a 1-D bulk, a compressor, ...) do not propagate the profile. With `transport`
  `auto` the profile is kept in shape and rescaled to the next spatial element's beam size (its curvature
  replaced by that beam's); `exact` refuses to proceed through such elements (error), `beam` discards the profile
  and starts from the next element's `beam`.

**Apertures** act on a carried profile ([below](#diffraction-and-apertures)). Without a profile, the aperture clips the
beam model as before.

The per-element record then reports `beam.radiusXM`, `radiusYM` (second-moment, 1/e² equivalent) and `beam.m2`
(M² from the second moments in space and spatial frequency, after removing the fitted wavefront curvature). The
`beamProfile` instrument gives the fluence across the beam.

## Diffraction and apertures

An `aperture` on a carried profile cuts the field; the spatial elements after it diffract what passes:

```jsonc
{ "id": "source", "type": "gap", "lengthM": 0, "material": "Vacuum",
  "transverse": { "model": "radial", "points": 1024, "radiusM": 12e-3, "absorbingBoundary": true,
                  "initialProfile": { "kind": "tophat", "radiusM": 3e-3 } } },
{ "id": "iris", "type": "aperture", "radiusM": 1e-3 },
{ "id": "screen", "type": "gap", "lengthM": 0.97, "material": "Vacuum", "transverse": { ...the same grid... } }
```

* **Openings:** `radiusM` (round), `halfWidthXM` / `halfWidthYM` (rectangle; cartesian grids -- on a radial grid it becomes
  a circle of radius min(half-widths), with a warning), `softRadiusM` + `order` (field transmission exp(−(r/a)^2n)), and
  `decenterXM` / `decenterYM` (cartesian). The profile is masked at every stored frequency and the spectrum is scaled by
  the frequency-resolved transmission, booked as `clip`; the element reports `transmission` and `apertureSamples`.
* **Hard edges between samples:** each grid cell on the edge transmits the fraction of its area inside the opening
  (annuli on a radial grid, pixels on a cartesian one). A 0/1 mask moves the edge by up to a cell; the averaged mask puts it
  in the right place, and the patterns converge quickly with the sampling (`DiffractionTests`). Light diffracted beyond the
  grid's largest angle is not represented, so the transmission converges to the geometric value from below, by about dx / 3a
  times the edge fluence (0.2 % for a Gaussian cut at half its radius with 85 samples, 1.6 % for a square flat-top with 21).
* **Resolution:** the engine warns when the opening spans fewer than 10 samples across its radius / half-width. The pattern's
  finest feature is the outermost Fresnel zone at the edge, λz / 2a wide; the grid must be several times wider than the
  pattern (the first dark ring of a round iris of radius a lies at r = 0.61 λz/a, and the rings beyond carry a few % of the energy).
* **`absorbingBoundary`:** a hard edge sends light to large angles. On a radial grid it reflects at the edge and focuses back
  onto the axis; on a cartesian one it wraps around. With `"absorbingBoundary": true` a layer in the outer 15 % of the grid
  (`widthFraction`) multiplies the field by exp(−5 u⁴) (u from 0 at the layer's inner edge to 1 at the grid's edge) after each step,
  and a linear element is split into steps short enough for the steepest wave on the grid to cross the layer within one. The absorbed
  energy is booked as `grid boundary` and reported as `absorbedAtBoundaryJ` / `absorbedAtBoundaryFraction`; the edge warning is
  not given. Nonlinear spatial elements do not use the layer.
* **Narrowband pulses:** `spectralSamples` (default 48) sets how many frequencies the profile holds. A ps pulse needs a few;
  a 2-D grid's memory scales with them.

Worked example and checks: [Example 30](../examples/30-iris-diffraction/README.md) (Gaussian, round flat-top, uniform and square beams
at N = 3, 2, 1 and in the far zone, against the Fresnel integral) and [Tutorial 6](../tutorials/06-diffraction.md).

## Small-scale self-focusing

A beam above the critical power breaks up into filaments from its small-scale structure. The cartesian model (`"model": "cartesian"`) follows
this in 2-D (x, y) when the field starts with a seed:

```jsonc
"transverse": { "model": "cartesian", "nx": 128, "ny": 128, "halfWidthM": 0.6e-3,        // 128 x 128 over 4 beam radii (w = 0.3 mm)
   "initialProfile": { "kind": "flatTop",
      "noise":  { "relativeAmplitudeRms": 0.01, "correlationLengthM": 40e-6, "seed": 1 },        // seeded smooth random amplitude (and phaseRmsRad)
      "ripple": { "periodM": 67e-6, "relativeAmplitude": 0.01, "angleDeg": 0 } } }               // or one sinusoidal ripple: the textbook seed
```

* `noise`: `N(x, y) = sqrt(2/K) sum cos(kx x + ky y + phi_k)`, wavevectors Gaussian with rms 1 / correlation length, unit variance, scaled by
  `relativeAmplitudeRms` (field amplitude; the fluence contrast is twice that) and `phaseRmsRad`. Reproducible from `seed`.
* `ripple`: the field amplitude is multiplied by `1 + a cos(2 pi (x cos t + y sin t) / period + phase)`.
* The element reports `fluenceContrastIn` / `fluenceContrastOut` (rms / mean of the fluence over the core r < w/2), `smallScaleGain` (their ratio),
  `peakPowerOverCritical` (P / P_cr) and the cuts `fluence_in_x`, `fluence_out_x`, `x_m` (arrays `el_<id>_...` in `result.npz`) along y = 0.

**Reference: Bespalov-Talanov.** For a plane wave of intensity I with an amplitude ripple of wavenumber K, linearising the envelope equation gives
`u' = q v`, `v' = (2 Delta - q) u` with `q = K^2 / 2k`, `Delta = k0 n2 I`, hence `u(z) = u(0) cosh(g z)`, `g^2 = q (2 Delta - q)` (a cosine for
`q > 2 Delta`): the most unstable ripple, `q = Delta`, grows as cosh(B), B = Delta L. `SmallScaleTests` compares the engine with this, integrated over
the Gaussian pulse in time: the fluence ripple after 3 mm at `q = Delta` and `q = 3 Delta` agrees to 6 % (observed < 1 %), and without Kerr the
ripple follows the Talbot law `2 eps |cos(q L)|`.

**Size and speed.** The state is `nx x ny x N_t` complex numbers per field: 128 x 128 x 512 is 134 MB, and the 10 mm example below ran in 57 s on a
laptop CPU (4 cores). 256 x 256 at 2^13 time points is 64 x larger (8.6 GB per field array, several of them held by the solver):
use a short time window and the [GPU plugin](../plugins/gpu.md) (`"compute": {"backend": "auto"}` runs the cartesian model on the device), or reduce
`nx` and `N_t` and check that `edge` warnings stay quiet. The grid must resolve the most unstable ripple (period 2 pi / sqrt(2 k Delta) -- about 90 um
in the example, 9 samples) and the correlation length of the seed; the box (4 beam radii) must hold the beam. [Example 29](../examples/29-small-scale-self-focusing/README.md).

## Collapse to 1-D, and what the 1-D quantities mean

The pulse's 1-D spectrum after a spatial element is the spectrum **integrated over the beam** (exact energy) with
the phase of the area-weighted field. So `output_power_w`, spectra and instruments describe the whole beam, as a
power meter or a spectrometer with a large integrating aperture would see it; spatially varying chirp or
spectra show up as a reduced temporal Strehl, not in the 1-D phase. The true peak fluence is
`elements[i].peakFluenceOutJPerM2` / the `beamProfile` instrument.

## Validation

| check | result | test |
|---|---|---|
| Hankel quadrature and round trip | < 1e-12 | `SpatialTests.RadialBasis_QuadratureAndTransformAreExact` |
| Gaussian beam w(z), radial and cartesian | < 0.5-1 %, M² = 1.00 | `SpatialTests.Diffraction_*` |
| lens + gap focus vs Gaussian optics | < 2 % | `SpatialTests.LensAndGap_*` |
| thin Kerr lens phase vs n2 I(r) k L | < 2 % | `SpatialTests.ThinKerrLens_*` |
| Bespalov-Talanov ripple growth (cosh(g z)), q = Delta and q = 3 Delta; Talbot ripple without Kerr | 6 % (observed < 1 %) | `SmallScaleTests.SmallScale_RippleGrowth_*`, `..._TheRippleDiffractsAsTheTalbotEffect` |
| seeded noise reproducible and linear in its amplitude; P / P_cr | 2 % | `SmallScaleTests.SmallScale_Seeded*`, `..._ReportsPeakPowerOverCritical` |
| beamProfile instrument vs w(z) | < 1 % | `Chi2Tests.BeamProfileInstrument_*` |
| plane wave through a round iris: on-axis 4 F sin²(πN/2) at N = 3, 2, 1 | 1.5 % (dark centre < 0.4 % of 4 F) | `DiffractionTests.UniformBeamThroughRoundIris_*` |
| Gaussian / flat-top through an iris vs the Fresnel integral, whole profile; transmission 1 − exp(−2a²/w²) | 1 % of the peak (observed 0.02-0.5 %); 0.3 % | `DiffractionTests.GaussianAndFlatTopThroughRoundIris_*` |
| square flat-top through a square aperture vs the separable Fresnel integral (512²) | 2 % of the peak (observed 0.4-0.7 %) | `DiffractionTests.SquareBeamThroughSquareAperture_*` |
| far zone: first Airy ring 0.61 λz/a, first sinc² zero λz/2a | 2 % | `DiffractionTests.FarField_*` |
| hard edge on coarse / fine grids; warning under 10 samples | dx / 2a | `DiffractionTests.HardAperture_*` |
| absorbing boundary: wrapped light removed, loss budget closes | 2 % / 1e-6 | `DiffractionTests.AbsorbingBoundary_*` |
| linear profile-only path vs full-field path | 1e-4 of the peak | `DiffractionTests.LinearSpatialGap_*` |
| radial χ(2) vs 1-D Gaussian overlap (low conversion) | < 1 % | `Chi2Tests.Shg_RadialModelAgrees*` |
