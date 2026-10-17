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
| `initialProfile` | the field at the first spatial element: `{"kind": "gaussian"|"superGaussian"|"flatTop", "order": 4, "decenterXM": .., "tiltXRad": .., "radiusOfCurvatureM": ..}` | from the element's `beam` |

Resolution rule of thumb: about 15-20 samples across the smallest beam radius the beam reaches, and an extent of
3-4x the largest radius. The Hankel grid is not uniform (denser near the axis); its k-space extent is
j_points/radius.

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
  (cartesian) or reflect (radial).

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

**Apertures** act on a carried profile: `{"type": "aperture", "radiusM": ...}` (hard circle),
`halfWidthXM`/`halfWidthYM` (rectangle) or `softRadiusM` (+ `order`: field transmission exp(−(r/a)^2n)),
with `decenterXM`/`decenterYM`. The profile is masked at every stored frequency and the spectrum is scaled by
the frequency-resolved transmission (booked as `clip`). Without a profile, the aperture clips the beam model as before.

The per-element record then reports `beam.radiusXM`, `radiusYM` (second-moment, 1/e² equivalent) and `beam.m2`
(M² from the second moments in space and spatial frequency, after removing the fitted wavefront curvature). The
`beamProfile` instrument gives the fluence across the beam.

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
| beamProfile instrument vs w(z) | < 1 % | `Chi2Tests.BeamProfileInstrument_*` |
| radial χ(2) vs 1-D Gaussian overlap (low conversion) | < 1 % | `Chi2Tests.Shg_RadialModelAgrees*` |
