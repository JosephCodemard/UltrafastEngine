# Nonlinear physics and solvers

Every element that propagates through a material with an n2 (`bulk`, `gap` with `nonlinear: true`, `plate`, `fiber`, the
cavity elements of an `oscillator`) integrates the envelope equation below: Kerr self- and cross-phase modulation,
the delayed (Raman) response, self-steepening and any user response. This page states the equations, their conventions,
the B-integral the engine reports, the Kerr lens of a 1-D beam, the carrier-resolved field model and the steppers.
Conventions are those of [Concepts](../concepts.md#the-pulse): E(t) = Re{A(t) exp(−iω0t)}, intensity I = |A|²/A_eff,
the co-moving frame.

## Keys

The keys that matter; every key with its default is in the [option reference](options.md).

| where | keys |
|---|---|
| material ([`material`](options.md#material)) | `n2` (m²/W), `raman` (a delayed-response model, below), `ramanFraction`, `kerrAnisotropy` |
| element ([`nonlinearMedium`](options.md#nonlinearmedium)) | `nonlinear`, `n2` and `kerrAnisotropy` (override the material's), `raman`, `fR`, `selfSteepening`, `kerrLens`, `kerrLensModel`, `kerrCutAxis`, `kerrAzimuthDeg`, `timeResponse`, `replaceKerr`, `beam` |
| run spec [`physics`](options.md#physics) | `nonlinear`, `selfSteepening`, `raman`, `fieldMode` (`auto`, `envelope`, `carrier`), `fieldHarmonic` (default 3), `polarization` (`jones`, `scalar`) |
| run spec [`stepper`](options.md#stepper) | `method` (`adaptive`, `rk4ip`), `tolerance` (1e-6), `maxPhasePerStepRad` (0.01), `maxAreaChangePerStep` (0.05), `minStepM`, `maxStepM`, `maxSteps`, `steps` |

The delayed-response kinds and their parameters are under *Model kinds* in the option reference
([`silica`](options.md#delayedresponse-silica), [`linAgrawal`](options.md#delayedresponse-linagrawal),
[`hollenbeck`](options.md#delayedresponse-hollenbeck), [`dampedOscillator`](options.md#delayedresponse-dampedoscillator),
[`orientational`](options.md#delayedresponse-orientational), [`expression`](options.md#delayedresponse-expression),
[`callback`](options.md#delayedresponse-callback)).

## Model

### Envelope model (default)

For each nonlinear element the engine integrates, in the co-moving frame,

dA/dz = L(ω) A + N(A, z),

where L is the element's complete linear operator per unit length (dispersion to all orders and attenuation, from traced
data or the material model) and, for the x component,

N_x = i k0 [ (Δn_x + Δn_user) A_x + n2 (1 − fR)/3 · A_y² A_x* / A_eff + n2 fR (h_b ⊛ Re(A_x A_y*)) A_y / A_eff ] × (1 + Ω/ω0),

Δn_x = n2 [ (1 − fR)(I_x + (2/3) I_y) + fR (h_a ⊛ I + h_b ⊛ I_x) ],  I = |A|² / A_eff(z),

and the same for y with x and y exchanged. k0 = ω0/c, Ω = ω − ω0, ⊛ is the causal convolution in time, and A_eff(z)
follows the element's beam model, so the focusing inside a multipass-cell pass is integrated. The terms are Kerr self- and
cross-phase modulation with the coherent term of an isotropic χ(3), the delayed response with its isotropic (h_a) and
anisotropic (h_b) parts, self-steepening (the factor 1 + Ω/ω0, `selfSteepening`), and user responses
Δn_user(t, t_lab, z, I, ...) (`timeResponse`). With γ = n2 ω0 / (c A_eff) this is the generalised nonlinear Schrödinger
equation of Agrawal (*Nonlinear Fiber Optics*) and Dudley, Genty & Coen (Rev. Mod. Phys. 78, 1135 (2006)). A purely <!-- style: ignore -->
x-polarised pulse in an isotropic medium propagates A_x only (half the cost); for it the split of the delayed response
does not matter (h_a + h_b = h_R).

### Polarisation: circular light and nonlinear ellipse rotation

The Raman response of a glass is a tensor (Hellwarth): R_ijkl(t) = h_a(t) δij δkl + h_b(t) (δik δjl + δil δjk) / 2,
with h_R = h_a + h_b. The isotropic part h_a follows the total intensity; the anisotropic part h_b (the share b of the
response, `anisotropicFraction`) follows the field's orientation. It is what makes Raman contribute to **nonlinear
ellipse rotation (NER)** and to the difference between circular and linear light:

| quantity | electronic Kerr only | with a slow Raman response (pulse ≫ 100 fs) |
|---|---|---|
| ellipse rotation at the pulse peak, ellipse angle χ (tan χ = ellipticity) | θ = B sin(2χ) / 3 | θ = B sin(2χ) [(1 − fR)/3 + fR b/2] |
| circular / linear nonlinear phase at equal intensity | 2/3 | (1 − fR) 2/3 + fR (1 − b/2) |
| reported B-integral, circular / linear | 2/3 | as the phase |

B is the B-integral of linear light of the same intensity. For fused silica (fR = 0.18, b = 0.25) the Raman term
fR b/2 = 0.0225 adds 8 % to the electronic rotation (1 − fR)/3 = 0.273, and circular light gets 3 % less phase than with
an isotropic Raman response (0.704 against 0.727 of the linear phase). With Raman off, circular light at 1.5 × the energy
of linear light has the same spectrum.

`anisotropicFraction` (b, from 0 to 1.5) works on every delayed-response kind: h_b = b h, h_a = (1 − b) h. b = 0 is
isotropic (circular and linear light see the same delayed index), b = 0.25 is fused silica, b = 1.5 is a purely
orientational response (h_a = −h_b/3; circular light sees a quarter of the linear index change). Values outside 0 to 1.5
are errors. Defaults: 0.25 for `silica` and `hollenbeck` (the anisotropic share of the Raman cross-section of fused
silica, Hellwarth); (fb + fc) / (fa + fb + fc) = 0.25 for `linAgrawal`, whose anisotropic part is fc h_osc + fb h_boson
with the oscillator and boson-peak shapes of Lin & Agrawal (an explicit value, even 0, replaces that split); 1.5 for `orientational`; 0 for `dampedOscillator`, `expression` and
`callback`. Set it on the model to study its effect: `"raman": {"kind": "silica", "anisotropicFraction": 0.3}`.

### B-integral

The **B-integral** of an element is the integral over z of the peak over the pulse of the effective nonlinear index
change, weighted over the polarisation components,

Δn_eff(t) = Im(Σ_c A_c* N_c) / (k0 Σ_c |A_c|²),

with N_c the nonlinear term before the self-steepening factor (`PeakIndexChange` in `Physics/Nonlinear.cs`; time slices
below 1e-6 of the peak power are skipped). It contains every term of the χ(3) polarisation, including the coherent term,
so circular light reports 2/3 of the linear value. `bIntegralRad` is therefore the **on-axis peak**: the phase of the most
intense time slice at the centre of the beam. Two averages are reported beside it for 1-D elements (and in `totals`):

* `bIntegralPulseAveragedRad`: at each step the index change is weighted by the power over the pulse, Σ_t P Δn / Σ_t P.
  For a Gaussian pulse and an instantaneous Kerr index it is B / √2; for sech² it is 2/3 B (∫sech⁴ / ∫sech² = (4/3) / 2).
  The CPU stepper and the 1-D envelope stepper of the GPU back-ends accumulate it step by step with the same sums. Where a
  stepper does not report it (the ensemble stepper of GPU study batches, the `cpu-fp32` emulation) it is estimated from
  the entry and exit pulse shapes, Σ P² / (Σ P max P), averaged over entry and exit, and a note says so. The estimate is
  exact for an instantaneous response and a shape that changes little; it is about 8 % high in an air gap whose delayed
  rotational Raman response matters (160 fs). (Before 2026-11-24 the GPU path reported 0 for both averages, per element
  and in `totals`.)
* `bIntegralBeamAveragedRad`: additionally weighted by intensity over the Gaussian cross-section of the 1-D beam,
  ∫I² dA / ∫I dA = I₀/2, so half of the pulse-averaged value. It is the energy-weighted mean phase, the number that sets,
  for example, the average spectral broadening of a beam.

Spatial and crystal elements report only the peak.

### Raman models

The delayed response h_R(t) is a material property (`"raman"` in a material, or on a `bulk` / `fiber` element), with
the Raman fraction fR:

| kind | model | default fR | default b |
|---|---|---|---|
| `silica` (alias `blowWood`) | damped oscillator, τ1 = 12.2 fs, τ2 = 32 fs (Blow & Wood, IEEE J. Quantum Electron. 25, 2665 (1989)) | 0.18 | 0.25 |
| `linAgrawal` | damped oscillator + boson peak, h = (fa + fc) h_osc + fb h_boson, τ_b = 96 fs (Lin & Agrawal, Opt. Lett. 31, 3086 (2006)); `tau1S`, `tau2S`, `taubS`, `fa` (0.75), `fb` (0.21), `fc` (0.04) adjustable | 0.245 | (fb + fc) / (fa + fb + fc) |
| `hollenbeck` | 13 vibrational modes with Lorentzian and Gaussian broadening (Hollenbeck & Cantrell, JOSA B 19, 2886 (2002)); reproduces the measured gain spectrum, peak 13.2 THz | 0.18 | 0.25 |
| `dampedOscillator` | h(t) ∝ exp(−t/τ2) sin(t/τ1) with your `tau1S`, `tau2S` | `fR` | 0 |
| `orientational` | molecular reorientation of a liquid: h(t) = (exp(−t/τ_d) − exp(−t/τ_r)) / (τ_d − τ_r), `riseS` 0.2 ps, `decayS` 1.6 ps; used by the built-in CS2 | `fR` | 1.5 |
| `expression`, `callback` | any h(t), normalised to unit integral on the grid and forced causal | `fR` | 0 |

The tabulated parameters of the `hollenbeck` model are those of Table 1 of the paper as commonly reproduced (the same
numbers are used by other open-source pulse-propagation codes). In that table the Lorentzian width of every mode is
one third of its Gaussian width except mode 2 (100 cm⁻¹: Gaussian 110.42, Lorentzian 38.81 where one third would be
36.81). The engine could not check this entry against the original paper and uses the value as printed; the difference
is 2 cm⁻¹ on one of thirteen modes. The default fR of 0.18 is the value commonly used for silica; set `fR` to use another.

Molecular gases have their rotational Raman response as an anisotropic tensor response instead ([Gases](gases.md)).

**Sampling.** Every kernel is sampled on the time grid and normalised there to unit sum, so the delayed fraction is
exactly fR whatever the step (an analytic normalisation is off by a few % when the step is not much smaller than the
12 fs oscillation). On a grid whose step is longer than 0.5 fs the response is evaluated on a finer sub-grid and
band-limited to the grid before it is sampled, so its spectrum is the response's own H(ω) at every frequency of the grid
and the Raman shift does not depend on the step.

### Cubic anisotropy (XPW)

<a id="cubic-anisotropy"></a>
For a cubic (m3m) crystal χ(3)_ijkl = χ1122 (δij δkl + δik δjl + δil δjk) + Δ δijkl, with the anisotropy
σ = Δ / χ1111 = (χ1111 − 3 χ1122) / χ1111 (`kerrAnisotropy` on the material or the element; BaF2 −1.2, `unverified` in
the catalogue). The electronic Kerr index change becomes

n2 [ (1 − σ) (isotropic Maker-Terhune term) + σ Σ_a u_a |u_a·A|² (u_a·A) ],

with u_a the transverse components of the cubic axes [100], [010], [001]; n2 is the value for light polarised along
[100]. The orientation is set on the element: `kerrCutAxis` ([h, k, l], default [0, 0, 1]) and `kerrAzimuthDeg` (angle
of the lab x axis from the in-plane reference axis: the projection of [100], or of [010] for a [100] cut; the crystal
[100] then lies at minus that angle in the lab frame).

A linearly polarised pulse then generates the orthogonal polarisation (cross-polarised wave, XPW), and the vector
propagation is used automatically. In the [001] cut with the input at angle β from [100], the Δ term drives
−(σ/4) sin(4β) into the orthogonal polarisation, so at low conversion the **XPW energy fraction** (orthogonal energy over
input energy) of a Gaussian pulse is (B σ sin(4β) / 4)² / √3, with B = k0 n2 I0 L: zero at 0° and 45°, maximal at
**22.5°**. The SPM index goes as n2 [1 − (σ/2) sin²(2β)]. In the [011] cut (lab x = [100], y = [01−1]) the drive is
sin β cos β (sin²β / 2 − cos²β), zero at 54.7°. Only the electronic share 1 − fR is anisotropic.

### Critical power

The reported ratio of peak power to critical power (`criticalPowerRatio` in the element table, `peakPowerOverCritical`
for spatial models) uses P_cr = 3.77 λ² / (8π n0 n2) (Marburger, Prog. Quantum Electron. 4, 35 (1975), Gaussian beam;
λ the carrier wavelength in vacuum, n0 the linear index at the carrier). Fibich & Gaeta (Opt. Lett. 25, 335 (2000))
quote 3.72 for the collapse threshold; the two differ by 1.3 %. The engine uses 3.77 everywhere, so the ratio is
comparable between elements and with the textbook value. A ratio above 0.3 raises a warning.

### Kerr lens in a 1-D element

A 1-D element has one beam that is a fixed function of z, so it does not feel the Kerr index of the pulse. With
`"kerrLens": "peak"` (or `"pulseWeighted"`) on a bulk element with a `gaussian` (or tracked) beam, the executor builds the
beam from the pulse instead: every step is a thin Kerr lens of the power at the element's entry
(`CellModeSolver.KerrLensPowerPerM`), and the second-moment radius of each axis follows

w'' = Λ² / w³ − K w,  K = c n2 I0 / (n w²),  I0 = 2P / (π w_x w_y),  Λ = M² λ / (π n),

with c = 1 for `kerrLensModel: "secondMoment"` (the default) and c = 4 for `"paraxial"` (the on-axis curvature, which
overestimates the lens). For a round beam at its waist this has the exact solution w² = w0² [1 + (1 − P/P_c)(z/z_R)²]
with the model's self-trapping power P_c = λ² / (2π c n n2); for c = 1 that is 1.06 × Marburger's P_cr. The intensity
seen by the nonlinear step, the B-integral and the reported peak intensity use the lensed beam. Reported per element:
`kerrLensPowerW`, `kerrLensExitRadiusXM`, `kerrLensExitRadiusYM`.

* `peak` uses the peak power; `pulseWeighted` uses Σ P² / Σ P (the lens of each time slice is proportional to its power,
  and the slice carries energy proportional to it; peak / √2 for a Gaussian pulse). `peak` is the upper bound.
* The `n2` is the element's total (Kerr plus the delayed fraction at its adiabatic value); for pulses shorter than a
  delayed response the lens is somewhat weaker.
* Off by default; with `off` every result is unchanged.

## Carrier-resolved field model

When a time response declares `carrierResolved: true` (or `physics.fieldMode: "carrier"`), the element is propagated as
the **real field** e(t) = √2 Re[a(t) e^{−iω0t}] on a finer grid reaching `fieldHarmonic` × ω0 (`physics.fieldHarmonic`,
default 3):

dE(ω)/dz = L(ω) E + i (ω/c) F[Δn_re e] − (|ω|/c) F[Δn_im e],
Δn = n2 [ (1 − fR)(2/3) e² / A_eff + fR h_R ⊛ I_env ] + user responses.

The factor 2/3 makes the cycle-averaged SPM identical to the envelope model; self-steepening and third-harmonic
generation emerge from the field; user models see `t_lab` with optical-cycle resolution and the field `E`. Energy
generated outside the envelope band is reported (`harmonics (out of band)`).

**Broadband χ(2).** The time response `{"kind": "chi2", "deffPmPerV": 2, "periodM": 30e-6}` adds the second-order
polarisation P = 2ε0 d_eff E² as Δn = d_eff E / n0 (E the real field in V/m). Second-harmonic, sum-frequency,
difference-frequency generation and optical rectification of any bandwidth then come out of one field. Phase matching
follows from the material's dispersion. `periodM` flips the sign of d_eff every half period (quasi-phase matching). A χ(2)
drive is dealiased (no drive above 2/3 of the field band) and the field grid then reaches at least 4.5 ω0, which keeps the
energy conserved at high conversion. The model is scalar, so there is no birefringent phase matching; for that use the
`crystal` element ([Crystals](crystals.md)).

## Steppers

* **`adaptive`** (default, `stepper.method`): the embedded ERK4(3)-IP of Balac & Mahé (Comput. Phys. Commun. 184, 1211
  (2013)). The fourth-order RK4IP solution and an embedded third-order one share all stages (first-same-as-last), and
  their relative difference controls the step (`tolerance`, the local relative error per step, default 1e-6), so steps
  grow where little happens (mid-pass, weak fields) and shrink near foci. `maxPhasePerStepRad` sets the initial step.
* **`rk4ip`**: the classic fixed-step RK4IP (Hult, J. Lightwave Technol. 25, 3770 (2007)). The step count comes from the
  estimated B-integral (at most `maxPhasePerStepRad`, default 0.01 rad, per step) and the beam-size variation, or from
  `steps`.

Both limit the change of the effective area per step (`maxAreaChangePerStep`, |d ln A_eff| ≤ 0.05 by default) and
respect `minStepM`, `maxStepM` and `maxSteps`.

## Validation

| what | reference | agreement | test |
|---|---|---|---|
| SPM phase γPL and spectral broadening of a Gaussian | Agrawal (the book cited above), eq. 4.1.15 | 1e-6 (phase), 1 % (broadening) | `PropagationTests.Spm_GaussianBroadeningMatchesAnalyticAndBIntegral` |
| fundamental soliton after one soliton period | NLSE soliton | 1 % (peak power, FWHM) | `PropagationTests.Soliton_FundamentalSolitonKeepsItsShape` |
| adaptive against fixed RK4IP | each other | 1e-4 (relative L2), fewer steps | `PropagationTests.Steppers_AdaptiveAgreesWithFixedRk4Ip` (slow) |
| self-steepening, Raman red shift | direction of the effect | sign | `PropagationTests.SelfSteepening_MovesPeakToTrailingEdgeAndBroadensBlueSide`, `PropagationTests.Raman_RedShiftsTheSpectrum` |
| callback and expression responses against the built-in Kerr | identity | 1e-9 | `PropagationTests.TimeResponse_CallbackAndExpressionReproduceBuiltInKerr` |
| lab-time phase modulation | Bessel sidebands J_n(m) | 1 % (envelope), 4 % (carrier-resolved) | `PropagationTests.TimeResponse_LabTimeModulationCreatesBesselSidebands` |
| carrier-resolved against envelope SPM in fused silica | the envelope model | 2 % (rms spectral width), 0.1 % (energy) | `PropagationTests.FieldMode_AgreesWithEnvelopeForSpm` |
| higher-order soliton (N ≈ 2.4) with and without Raman; `fiber` element with Raman or gain | pyNLO | 0.3 % (L1; 0.5 % with gain) | `python/tests/test_pynlo.py` |
| circular / linear phase, 1.5 × energy, rotation B sin 2χ / 3 | Maker, Terhune & Savage (1964) | 0.5 % ... 1 % | `NerTests` |
| rotation and circular light with anisotropic Raman | Hellwarth tensor | 1 % ... 2 % | `NerTests.Rotation_WithAnisotropicRaman_UsesTheAnisotropicShare`, `NerTests.CircularLight_SeesLessRamanThanLinear` |
| window for a delayed response: a 20 fs revival at 10.1 ps found and held; a callback with `memoryS`; built-in responses keep their memory | four times the window at the same step | 1e-6 (centroid shift, rms broadening); exact | `GridSolverTests.DelayedResponseWithANarrowRevival_TheWindowHoldsTheRevival`, `GridSolverTests.TimeResponseCallback_DeclaredMemory_IsHeldByTheWindow`, `GridSolverTests.ResponseMemory_OfSmoothResponses_IsTheOriginalValue` |
| pulse- and beam-averaged B-integral | 1/√2, 1/2 (Gaussian), 2/3 (sech²) | 1e-6, 1e-4 | `ValidityTests.BIntegral_PulseAndBeamAveraged_AreTheAnalyticFractionsOfThePeak`, `ValidityTests.BIntegral_PulseAveraged_OfASech2Pulse_IsTwoThirdsOfThePeak_AlsoWhenEstimated` |
| XPW fraction in the [001] and [011] cuts, SPM index against β | cubic χ(3) tensor (Boyd, *Nonlinear Optics*, sec. 4.2; Minkovski et al., Opt. Lett. 29, 2237 (2004)) | 3 %, 0.3 % | `MaterialsCrystalsTests.CubicKerr_*` |
| Kerr lens: exact solution for c = 1 and 4 at P/P_c = 0, 0.5, 1, 2; collapse distance z_R / √(P/P_c − 1); the ratio 4/3.77 | Ermakov equation; Marburger (1975) | 1e-6, 1 % | `KerrLensTests` |
| broadband χ(2): carrier-resolved SHG | low-conversion P = K P² | 0.1 %, 5 % | `LaserTests.Chi2Response_CarrierResolvedShg_MatchesLowConversionFormula` |

## Limits

* **Spectrum reaching zero frequency.** The envelope model has no field at ω ≤ 0. A pulse of a few cycles (10 fs at
  2 µm is 1.5 cycles) broadened by SPM (B = 3) puts part of its spectrum there; the engine drops that part, books it as
  `spectrum at non-positive frequency` (not `numerical`) and warns. Use longer pulses or less nonlinearity; the result is
  outside the model's range.
* **Delayed responses longer than the window.** The index change of a delayed response (Raman, orientational) lasts its
  memory after the pulse (the time after which 1e-4 of its weight remains: 9.2 τ for exp(−t/τ); a revival extends it
  to the revival). The auto grid adds it to the window, and so does a user `timeResponse` that declares `memoryS`
  ([Concepts: grids](../concepts.md#grids)). A window shorter than twice the memory (a manual grid, or `grid.maxN`) is
  warned about with the element and the window and N it needs. The rotational Raman of gases is a linear convolution
  and needs no room ([Gases](gases.md#rotational-raman-molecular-orientation)).
* **Self-focusing in 1-D.** Without `kerrLens` a 1-D element ignores the Kerr lens; with it, the lens is a model of the
  whole pulse, not of each time slice (only the `transverse` model resolves that). The lens uses the power at the
  element's entry: if the power changes by more than 20 % inside the element (dispersion, absorption) the run warns; split
  the element. Above P_c the model collapses; the run warns with the collapse distance and holds the radius at 1e-3 of its
  entry value; use a [transverse model](transverse.md) there. The beam leaving the element is **not** carried to later
  elements (their beams are the compile-time tracked ones); for a multipass cell use `cellMode`, which includes the same
  thin lens self-consistently.
* **Cubic anisotropy** is implemented for the 1-D envelope model (vector solver, CPU). A `transverse` model or
  carrier-resolved propagation with σ ≠ 0 is an error, never silently ignored.
* **Carrier-resolved model.** It is scalar (x): a y component is propagated linearly with a warning. It runs on the CPU.
* **Phase-mismatched χ(2) crystals.** The integrator keeps the mismatch phase Δk·dz below 0.5 rad per step, so the
  generated field of a thick, strongly mismatched crystal (Δk L = 370 rad) follows sin²(Δk L / 2).
