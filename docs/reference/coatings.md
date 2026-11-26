# Surfaces: coatings, losses and polarisation

An `interface` (`mirror`, `grating`) element is the place where a beam meets a surface. The engine computes the s and p
amplitude coefficients from the media on both sides, the angle of incidence (per wavelength when traced) and the
coating, and applies them as a Jones matrix in the transported x/y basis. Coatings are defined by name under the system
file's `coatings` (or inline on the element). This page also lists the loss mechanisms of the energy budget, the damage
(LIDT) report and the polarisation model.

## Keys

| key | where | what it does |
|---|---|---|
| `coating` | `interface`, `mirror`, `grating`, `plate` (also `coatingFront` / `coatingBack`) | a coating name from `coatings`, or an inline coating ([Coating kinds](#coating-kinds)); none = bare Fresnel |
| `substrate` | `mirror` | the substrate behind the coating ([Substrate echoes](#substrate-echoes)) |
| `ripple` | `mirror`, `interface` | random phase and amplitude ripple of the coating ([Coating ripple](#coating-ripple)) |
| `delayedScatter` | `mirror`, `interface`, `plate` | scattered light returned late ([Delayed scatter](#delayed-scatter)) |
| `lidt` | a coating or a material | damage threshold ([Damage](#damage-lidt)); keys in [lidt](options.md#lidt) |
| `planeOfIncidenceDeg` | surfaces | angle of the s direction from x (90, the default, folds in the x-z plane) |
| `physics.polarization` | run spec | `"jones"` (default) or `"scalar"` ([Polarisation](#polarisation)) |

The element keys are in the generated [option reference](options.md#elements) (`interface`, `mirror`, `plate`). The coating
kinds below are not declared in the registry, so this page is their only reference.

## Model

The coefficients are **power-normalised**: |r|² = R and |t|² = T, with the Re(n cos θ) / Re(n cos θ) factor included in t.
Convention: E(t) = Re{A(t) exp(−iω₀t)}; r_s = r_p at normal incidence (a mirror does not swap the x/y labels); a coating
phase φ(ω) multiplies the amplitude by exp(+iφ), so a positive dφ/dω is a delay (light that penetrates a dielectric stack
is delayed) and a positive d²φ/dω² is positive GDD (red first, like glass).

## Coating kinds

| kind | keys and defaults | what it is |
|---|---|---|
| `none` (also `bare`) | | bare Fresnel interface with the complex indices of both media, so total internal reflection and metals work |
| `stack` | `layers: [{material, thicknessM}` or `{material, qwot}]` (`qwot` in quarter waves, default 1), `designWavelengthM` (default the carrier), `name` | physical multilayer, transfer-matrix method |
| `quarterWave` | `design: "(HL)^12 H 2L"`, `designWavelengthM` (default the carrier), `H` (default `Ta2O5`), `L` (default `SiO2_film`), `M` (no default) | quarter-wave notation expanded to a `stack`: a letter is one quarter wave, a numeric prefix multiplies it, `(...)^n` repeats |
| `ideal` | `R` (default 1) or `Rs`, `Rp`; `T` (default 1 − R) or `Ts`, `Tp`; `phaseRad` (default 0) | wavelength-independent; `phaseRad` is added to the **reflection** only (the transmission phase is 0) |
| `ar` | `R` (default 0.0025), and the other keys of `ideal` | residual reflectance per surface |
| `gdd` | `R` (default 0.999), `gddS2`, `todS3`, `fodS4`, `referenceWavelengthM` (default the carrier) | ideal dispersive mirror: φ(ω) = gddS2 Δω²/2 + todS3 Δω³/6 + fodS4 Δω⁴/24 on the reflection, Δω = ω − ω_ref; T = 1 − R |
| `gddRipple` (also `chirped`) | `R` (default 0.998), `gddS2` or `gddFs2`, `todS3` or `todFs3`, `rippleGddS2` or `rippleGddFs2`, `ripplePeriodNm`, `ripplePeriodThz` or `ripplePeriodRadPerS` (one is required with a ripple), `ripplePhaseRad`, `reflectanceRipple`, `referenceWavelengthM` | dispersive mirror with a sinusoidal GDD ripple: φ = gdd Δω²/2 + tod Δω³/6 − A (Ω/2π)² sin(2π Δω/Ω + φ₀), and R(ω) = R (1 + `reflectanceRipple` sin(2π Δω/Ω + φ₀)); the same law as the [`chirpedMirror` element](components.md#gdd-ripple) |
| `table` | `wavelengthsM`, `wavelengthsNm` or `wavelengthsUm`; `R` / `Rs` / `Rp`; `T` / `Ts` / `Tp` (default 1 − R); phases `phaseR`, `phaseRs`, `phaseRp`, `phaseT`, `phaseTs`, `phaseTp` (rad), or group delays `groupDelayRS`, `groupDelayRsS`, ... (s, integrated to a phase) | measured or vendor data; exported OpticStudio coatings. Amplitudes are interpolated linearly and held at the end values; phases use a natural cubic spline (four or more points) with linear extrapolation |
| `efficiency` | `s`, `p`, or one `efficiency` for both (default 1) | grating diffraction efficiency: power only, phase 0 |

All kinds take an optional `lidt`. Example:

```json
"coatings": {
  "HR1030@45": { "kind": "quarterWave", "design": "(HL)^14 H", "designWavelengthM": 1.13e-6, "H": "Ta2O5", "L": "SiO2_film" },
  "CM": { "kind": "gdd", "R": 0.999, "gddS2": -100e-30, "lidt": { "fluenceJPerCm2": 0.4, "pulseDurationS": 1e-12 } }
}
```

The design wavelength of `HR1030@45` (1130 nm) is above 1030 nm because the band of a stack moves to shorter wavelengths
at 45° (the phase thickness of each layer scales with cos θ inside it); `designWavelengthM` is the normal-incidence centre.

## Substrate echoes

A `mirror` with a `substrate` adds the echo of the light that leaks through the coating, reflects from the back face of the
substrate and returns after 2 n_g d cos θ / c (62 / 93 / 124 ps for 6.35 / 9.5 / 12.7 mm of fused silica at 1030 nm). The
transfer is exact in n(ω), the leakage follows the coating's T(λ), and the back face may be bare, AR-coated or wedged.
Keys, the catalogue of substrates and the physics: [Delayed light](delayed-light.md#mirror-substrate-echoes).

```jsonc
{ "id": "m1", "type": "mirror", "coating": "HR1030", "substrate": "FS-12.7mm" }
```

## Coating ripple

`"ripple": {"phaseRmsRad": 0.03, "amplitudeRms": 0.01, "maxDelayS": 5e-12, "seed": 7}` on a mirror or interface multiplies the
reflection by a random phase and amplitude ripple made of `components` sinusoids (default 64). Its pedestal lies within
±`maxDelayS` (default 5 ps); to first order it holds the fraction φ_rms² of the energy (the element's note gives it). The
`seed` defaults to a number derived from the element id, so the ripple is reproducible and differs between elements.
Measured ripple is a `table` coating. See [Delayed light](delayed-light.md#coating-ripple).

## Delayed scatter

`"delayedScatter": {"fraction": 1e-4, "delay": {"kind": "uniform", "maxS": 40e-12}, "spectralExponent": 4}` returns a fraction of
the power as incoherent delayed light instead of losing it (`roughnessRmsM` below books the lost part).

| `delay.kind` | keys | delay distribution |
|---|---|---|
| `uniform` (default) | `minS` (0), `maxS` | flat between `minS` and `maxS` |
| `exponential` | `tauS` (required), `maxS` (default 8 `tauS`) | exp(−t/τ), cut at `maxS` |
| `table` | `delaysS`, `weights` | the given distribution |

`spectralExponent` (default 0) sets the spectral weighting of the scattered light (4 for Rayleigh-like scatter). On a plate
the default `maxS` is 2 n_g d cos θ / c; a mirror or interface has no default and needs `delay.maxS`. `"model": "surface"`
derives the fraction from `roughnessRmsM` and `correlationLengthM` (total integrated scatter, and the share 1 − exp(−(l/w)²)
that stays in the mode). See [Delayed light](delayed-light.md#delayed-scatter).

## Loss mechanisms

Every energy change is booked under a mechanism name in `summary.json → lossBudget` (each mechanism's total in J and as a
fraction of the input) and per element in `lossJ`. The main names:

| mechanism | where it comes from |
|---|---|
| `fresnel` | a bare interface (no coating) |
| `coating` | a coated surface in reflection or refraction, chirped mirrors |
| `grating` | a coated surface in `diffract` mode: the diffraction efficiency |
| `clip` | a hard aperture. Gaussian beam on a circular aperture of radius a: T = 1 − exp(−2a²/w²); an elliptical beam wx × wy gives the exact integral over the disk, (1/(π wx wy)) ∫ (1 − exp(−a² q)) / q dφ with q = 2 (cos²φ / wx² + sin²φ / wy²); an erf product for a rectangular aperture; per wavelength when the beam size is; chief-ray vignetting from OpticStudio. Also the cell-edge loss of a [modulator](modulator.md#how-a-cell-reaches-the-grid) |
| `scatter` | total integrated scatter of rough surfaces: reflection (4π σ n cos θ / λ)², transmission (2π σ \|n₁ cos θ₁ − n₂ cos θ₂\| / λ)² |
| `absorption` | material attenuation, internal transmission, absorbing time responses, `absorptionProfile` |
| `dispersion` | the default name of a linear term (normally no energy change) |
| `filter`, `transmission`, `custom` | filters and tabulated transmissions (the element's `mechanism` / `lossMechanism` key renames them) |
| `polarization` | polarisers |
| `ghost` | ghost replicas of plates and end faces |
| `ndFilter (reflected)` | the light a reflective ND filter sends back |
| `saturable absorption`, `gain (from inversion)`, `ASE (spontaneous emission)`, `oscillator (new beam)` | active media and lasers ([Gain media](gain.md), [Oscillators](oscillators.md)) |
| `from <role> (aux)`, `to <field>` | energy brought in by aux beams and carried away as by-products ([Active elements](active-elements.md)) |
| `ionisation and plasma absorption` | photo-ionisation in a crystal ([Ionisation](ionization.md)) |
| `user` | a [user element](active-elements.md#user-elements) |
| `raman+numerical`, `numerical`, `grid boundary`, `harmonics (out of band)`, `spectrum at non-positive frequency`, `delayed light (tail)` | energy changes inside nonlinear and spatial segments, numerical residuals, light leaving the grid |

Gain, ASE, oscillator and aux entries are **negative** (energy brought in). With these negative entries included, the
budget closes on input − output energy (`SystemTests.LossBudget_ClipFresnelScatterAddUp`).

## Damage (LIDT)

For surfaces with a beam size, the engine reports the peak fluence F = 2E / (π wx wy), the coating's (or material's) LIDT
scaled to the pulse duration at that point, F_LIDT (τ / τ_ref)^exponent, and `lidtMargin` = F_LIDT / F, with a warning
below 1. It also reports the peak intensity. The `lidt` object: `fluenceJPerM2` or `fluenceJPerCm2` (one is required),
`pulseDurationS` (τ_ref, default 10 ns), `scalingExponent` (default 0.5), `wavelengthM`, `notes`.

## Polarisation

`physics.polarization: "jones"` (default) keeps x and y fields and applies 2 × 2 Jones matrices:

* surfaces: J = Bᵀ diag(a_s, a_p) B, where B rotates the transported (x, y) basis onto (s, p);
* `waveplate` (retardance proportional to ω for a zero-order plate), `polarizer` (extinction ratio),
  `rotator`; birefringent group delay via `groupDelayYS` on sampled elements.

**Basis transport.** With traced vectors (`kIn`, `kOut`, `normal`, exported by the Zemax binding) the local basis is carried
along the folded 3-D path by the rigid rotation that maps (s, p_in, k_in) to (s, p_out, k_out), so "x" at the output is the
transported image of the input x axis (a periscope rotates it, as in the lab). Without vectors, each surface's plane of
incidence is given by `planeOfIncidenceDeg` (90 = folding in the x-z plane, the default). `physics.polarization: "scalar"`
averages s and p.

The nonlinear coupling of x and y in isotropic media is the full χ(3) vector form (cross-phase 2/3 plus the coherent term),
so circular light sees 2/3 of the linear SPM ([Nonlinear physics](nonlinear.md)).

## Validation

| check | reference | test |
|---|---|---|
| Fresnel energy conservation, normal incidence and Brewster's angle | Fresnel equations | `FoundationTests.Fresnel_EnergyConservationNormalIncidenceAndBrewster` |
| quarter-wave AR coating and Bragg mirror | thin-film theory | `FoundationTests.ThinFilm_QuarterWaveArAndBraggMirror` |
| Brewster window transmits p only; circular light reflected at an angle becomes elliptical | Fresnel equations | `SystemTests.BrewsterWindow_TransmitsPOnly`, `SystemTests.CircularLight_ReflectedAtAnAngle_BecomesElliptical_AsFresnelSays` |
| GDD and reflectance of a `gdd` coating add per bounce (1e-9, 0.1 %) | sum per bounce | `PropagationTests.ChirpedMirrorCoating_GddAccumulatesPerBounce` |
| clip, Fresnel and scatter entries add up to the loss | energy closure | `SystemTests.LossBudget_ClipFresnelScatterAddUp` |
| a circular aperture on an elliptical beam | the 2-D integral | `GridSolverTests.CircularAperture_OnAnEllipticalBeam_IsTheTwoDimensionalIntegral` |
| coating ripple: pedestal energy within `maxDelayS`; amplitude ripple only removes light; reproducible from the seed | first-order theory | `DelayedLightTests.CoatingRipple_*` |
| surface scatter: loss is the TIS outside the mode and follows λ⁻² | TIS formula | `DelayedLightTests.SurfaceScatter_LossIsTheTisOutsideTheMode_AndFollowsLambdaMinusTwo` |
| mirror echo follows the coating's transmission and the back face | 2 n_g d cos θ / c | `DelayedLightTests.MirrorEcho_LeakageFollowsTheCoatingTransmissionAndTheBackFace` |

## Limits

* `ideal`, `ar`, `gdd` and `gddRipple` coatings do not depend on the angle of incidence; use `stack`, `quarterWave` or a
  `table` for that.
* A `table` coating is held at its end values outside its wavelength range (the phase is extrapolated linearly).
* The LIDT scaling with exponent 0.5 is the usual rule for long pulses; for short pulses give the threshold measured at
  your pulse duration as `pulseDurationS`.
* `"scalar"` polarisation averages s and p: it cannot show polarisation changes at folded surfaces.
