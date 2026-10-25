---
name: engine-physics
description: Physics conventions, equations, normalisations and validation references of UltrafastEngine (envelope NLSE, vector Kerr and Raman / NER, B-integral, grids and satellites, measured spectra, gain, chi(2)). Use before changing or reviewing propagation code, or when judging whether a simulated result is physically right.
---
# UltrafastEngine physics reference

Read the matching doc section for detail: `docs/concepts.md` (conventions), `docs/reference/nonlinear.md`
(envelope, Raman, NER, solvers), `docs/reference/crystals.md`, `gain.md`, `fibers.md`, `gases.md`, `ionization.md`,
`transverse.md`, `measured-data.md`, `run-spec.md#grid`.

## Conventions (tests enforce the directions)

* Envelope A(ω) in √W on a uniform grid about ω0 (snapped to the grid); |Ax(t)|²+|Ay(t)|² = power.
  E(t) = Re{A(t) e^{-iω0 t}} (Agrawal / pyNLO). Phase e^{+iφ(ω)} with dφ/dω = τ delays by τ. +GDD: red first.
* Co-moving frame per element; removed group delay accumulates as `arrivalTimeS`; user models get t and t_lab.
* Absolute (vacuum) indices; catalogue glasses relative to air are converted (Ciddor).
* I = P / A_eff(z) from the element's beam model (constant, Gaussian w(z) with M², sampled); spatial models instead
  propagate the transverse field (radial Hankel or cartesian).

## Nonlinear envelope (Physics/Nonlinear.cs, Transverse/SpatialPropagation.cs)

N_x = i k0 [ dn_x A_x + n2(1−fR)/3 A_y² A_x*/A_eff + n2 fR (h_b ∗ Re(A_x A_y*)) A_y/A_eff ] (1 + Ω/ω0)
dn_x = n2[(1−fR)(I_x + ⅔ I_y) + fR(h_a ∗ I + h_b ∗ I_x)] (+ user Δn). y: swap x↔y.

* Raman tensor (Hellwarth): R_ijkl = h_a δijδkl + h_b(δikδjl+δilδjk)/2, h_R = h_a + h_b; anisotropic share b =
  `anisotropicFraction` (silica/blowWood/hollenbeck/built-in FusedSilica 0.25; linAgrawal: fc·ha + fb·hb, b = fb+fc
  = 0.25; dampedOscillator/expression/callback 0). Kernels normalised to unit sum on the time grid; h_b scaled alike.
* x-only input → scalar path (Ax only); must be unchanged by vector features (pyNLO agreement < 0.3 %).
* Analytic checks (NerTests): rotation at the pulse peak θ = B sin2χ/3 (instantaneous); long pulse with Raman
  θ = B sin2χ[(1−fR)/3 + fR b/2]; circular/linear phase (1−fR)·2/3 + fR(1−b/2); circular at 1.5× energy = linear
  spectrum (Raman off). Pulse-averaged (polarimeter) azimuth ≈ θ_peak/√2 for a Gaussian.
* B-integral reported per element = k0 ∫ max_t Δn_eff dz with Δn_eff = Im(Σ A_c* N_c)/(k0 Σ|A_c|²) over samples
  with power > 1e-6 × peak (all χ(3) terms; circular = ⅔ linear).
* Carrier-resolved field mode (`fieldMode`) is scalar; its B uses the cycle-averaged index.
* Steppers: ERK4(3)-IP adaptive (default, tol 1e-6), RK4IP fixed; the step count follows the peak nonlinear rate.

## Grids and satellites (Engine/GridPlanner.cs, Compiler.SatelliteRoom)

* Window holds the pulse after every element + group-delay spread + satellites; dt resolves the spectrum × margin
  (1.3 linear; max(3, 1+B_est) nonlinear; ≥ ±10 % of carrier); ≥ 12 points per TL FWHM. N power of two.
* Satellites (ghosts R_f·R_b·overlap per order, transform replicas): room = longest delay where the generating
  function Π_e(1 + Σ_k a_ek z^τ_ek) has |amplitude|² ≥ `grid.satelliteLevel` (1e-8), coincident delays summed in
  amplitude (identical plates add coherently: N single ghosts → N²·R²). Weaker satellites wrap.
* `edgeEnergyFraction` (outer 5 % each side) > 1e-3 → wrap-around warning.

## Measured input spectra (Pulses/MeasuredSpectrumPreparation.cs)

Cleaning: drop non-finite rows → sort → merge wavelengths closer than 1e-3 × median spacing → background (`auto` =
lower of the two 5 %-edge medians) → threshold → clip negatives. A flat floor b over a band Δλ_rec adds b·Δλ_rec of
spectral energy and shortens the TL (0.66 % over 160 nm on a 6.5 nm spectrum: 240 fs → 26 fs). Per-λ → per-ω:
S(ω) = S(λ) λ²/(2πc). Optional Richardson-Lucy deconvolution of the recording resolution, Gaussian tails.

## Comparisons (Measured/Measurements.cs)

Simulated reading interpolated onto the measured axis; points beyond a significant edge excluded (residual 0, kept
for fit vector length), beyond a decayed edge compared with zero; coarser recording → simulated curve blurred by
√(rec²−sim²); `peak` normalisation scales data onto the simulation by least squares; log scale in dB.

## Other models (pointers)

χ(2): coupled-wave on the grid, Armstrong tanh² SHG, OPA cosh², Manley-Rowe (CrystalElement.cs). Gain: rate
equations + spectral Frantz-Nodvik (Gain/). Gases: electronic n2 + rotational Raman tensor; PPT/ADK ionisation +
Drude plasma (envelope uses ω0 for Drude). Fibres: Taylor β or D(λ), γ(z), self-steepening, Raman (pyNLO-validated).

## Known limits

1-D runs above P_cr only warn (use `transverse`). Literature n2 spread 10-30 %. Carrier-resolved mode is scalar.
Ghosts on `bulk` add energy (main pulse not reduced by (1−R)²; ~N²R² for N identical plates).
