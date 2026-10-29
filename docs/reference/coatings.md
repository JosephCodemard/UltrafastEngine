# Surfaces: coatings, losses and polarisation

## Surfaces

An `interface` (`mirror`, `grating`) element is the place where a beam meets a surface. The engine computes the
s and p amplitude coefficients -- POWER-normalised, so |r|^2 = R and |t|^2 = T including the
Re(n cos)/Re(n cos) factor -- from the media on both sides, the angle of incidence (per wavelength when traced)
and the coating, and applies them as a Jones matrix in the transported x/y basis.

Convention: exp(-i omega t) physics convention; r_s = r_p at normal incidence (a mirror does not swap x/y labels);
a positive d(phase)/d(omega) is a delay (light penetrating a dielectric stack is delayed).

## Coating kinds

| kind | fields | |
|---|---|---|
| `none` / no coating | | bare Fresnel interface (complex indices, so TIR and metals work) |
| `stack` | `layers: [{material, thicknessM | qwot}]`, `designWavelengthM` | physical multilayer, transfer-matrix method |
| `quarterWave` | `design: "(HL)^12 H 2L"`, `designWavelengthM`, `H`, `L`, `M` materials | quarter-wave notation expanded to a stack |
| `ideal` | `R` (or `Rs`, `Rp`), `T`, `phaseRad` | wavelength-independent |
| `ar` | `R` (residual reflectance per surface) | |
| `gdd` | `R`, `gddS2`, `todS3`, `fodS4`, `referenceWavelengthM` | ideal dispersive (chirped) mirror |
| `table` | `wavelengthsM`, `R`/`Rs`/`Rp`, `T`/`Ts`/`Tp`, `phaseRs`... or `groupDelayRsS`... | measured or vendor data; exported OpticStudio coatings |
| `efficiency` | `s`, `p` | grating diffraction efficiency |

All kinds take an optional `lidt`. Example:

```json
"coatings": {
  "HR1030@45": { "kind": "quarterWave", "design": "(HL)^14 H", "designWavelengthM": 1.13e-6, "H": "Ta2O5", "L": "SiO2_film" },
  "CM": { "kind": "gdd", "R": 0.999, "gddS2": -100e-30, "lidt": { "fluenceJPerCm2": 0.4, "pulseDurationS": 1e-12 } }
}
```

## Substrate echoes

A `mirror` with a `substrate` adds the echo of the light that leaks through the coating, reflects from the back face of the
substrate and returns after `2 n_g d cos(theta) / c` (62 / 93 / 124 ps for 6.35 / 9.5 / 12.7 mm of fused silica at 1030 nm). The
transfer is exact in n(omega), the leakage follows the coating's `T(lambda)`, the back face may be bare, AR-coated or wedged.
Keys, the catalogue of substrates and the physics: [Delayed light](delayed-light.md#mirror-substrate-echoes).

```jsonc
{ "id": "m1", "type": "mirror", "coating": "HR1030", "substrate": "FS-12.7mm" }
```

## Coating ripple

`"ripple": {"phaseRmsRad": 0.03, "amplitudeRms": 0.01, "maxDelayS": 5e-12, "seed": 7}` on a mirror or interface multiplies the
reflection by a random phase and amplitude ripple whose pedestal lies within +-`maxDelayS`; measured ripple is a `table` coating.
See [Delayed light](delayed-light.md#coating-ripple).

## Delayed scatter

`"delayedScatter": {"fraction": 1e-4, "delay": {"kind": "uniform", "maxS": 40e-12}, "spectralExponent": 4}` returns a fraction of
the power as incoherent delayed light instead of losing it (`roughnessRmsM` below books the lost part). On a plate the default
delay is 0 .. 2 n_g d cos(theta)/c. `"model": "surface"` derives the fraction from the roughness and correlation length (TIS, and the share `1 - exp(-(l/w)^2)` that stays in the mode).
See [Delayed light](delayed-light.md#delayed-scatter).

## Loss mechanisms

| mechanism | source |
|---|---|
| `fresnel` | bare interface reflection/transmission |
| `coating` | coated surfaces, chirped mirrors |
| `grating` | diffraction efficiency |
| `clip` | Gaussian beam on a hard aperture: T = 1 - exp(-2 a^2 / w^2) (circular), erf product (rectangular); per-wavelength when the beam size is; chief-ray vignetting from OpticStudio |
| `scatter` | total integrated scatter of rough surfaces: reflection (4 pi sigma n cos(theta) / lambda)^2, transmission (2 pi sigma |n1 cos t1 - n2 cos t2| / lambda)^2 |
| `absorption` | material attenuation, internal transmission, absorbing time responses |
| `filter`, `transmission`, `custom` | filters and tabulated transmissions |
| `polarization` | polarisers |
| `raman+numerical`, `numerical`, `harmonics (out of band)` | energy change inside nonlinear segments |

`summary.json -> lossBudget` gives each mechanism's total (J and fraction of the input) and every element's
`lossJ` gives the breakdown per element; together they close exactly on input - output energy.

## Damage (LIDT)

For surfaces with a beam size, the engine reports the peak fluence F = 2E/(pi wx wy), the coating's (or
material's) LIDT scaled to the pulse duration at that point, F_LIDT (tau/tau_ref)^exponent (default 0.5), and
`lidtMargin` = F_LIDT / F, with a warning below 1. It also reports the peak intensity.

## Polarisation

`physics.polarization: "jones"` (default) keeps x and y fields and applies 2x2 Jones matrices:

* surfaces: J = B^T diag(a_s, a_p) B, where B rotates the transported (x, y) basis onto (s, p);
* `waveplate` (retardance proportional to omega for a zero-order plate), `polarizer` (extinction ratio),
  `rotator`; birefringent group delay via `groupDelayYS` on sampled elements.

**Basis transport.** With traced vectors (`kIn`, `kOut`, `normal` -- exported by the Zemax binding) the local
basis is carried along the folded 3D path by the rigid rotation that maps (s, p_in, k_in) to (s, p_out, k_out),
so "x" at the output is the transported image of the input x axis (a periscope rotates it, as in the lab).
Without vectors, each surface's plane of incidence is given by `planeOfIncidenceDeg` (90 = folding in the x-z
plane, the default). `physics.polarization: "scalar"` averages s and p.

The nonlinear coupling of x and y in isotropic media is the full chi(3) vector form (cross-phase 2/3 plus the
coherent term), so circular light correctly sees 2/3 of the linear SPM.
