# Fibres and waveguides

The `fiber` element (aliases `fibre`, `waveguide`) describes a single-mode fibre the way fibre codes and datasheets
do: Taylor coefficients of the propagation constant, the nonlinear coefficient γ, loss in dB/km and gain in dB/m. It
compiles into the engine's nonlinear medium (an inner `bulk` element with a material made from these data), so Raman,
self-steepening, the adaptive stepper, instruments, overrides and studies all apply ([Nonlinear physics](nonlinear.md)).

```jsonc
{ "id": "pcf", "type": "fiber", "lengthM": 0.15,
  "betasPsKm": [-11.830, 8.1038e-2, -9.5205e-5, 2.0737e-7],   // beta2, beta3, ... in ps^k/km (or "betasSkPerM" in s^k/m)
  "referenceWavelengthM": 835e-9,                              // where the expansion is taken (default: the carrier)
  "gamma": 0.11,                                               // 1/(W m)
  "lossDbPerKm": 0, "raman": "silica" }
```

## Keys

Every key with its default is in [options: `fiber`](options.md#fiber-also-fibre-waveguide).

| group | keys |
|---|---|
| dispersion | `betasSkPerM` or `betasPsKm`, `referenceWavelengthM`, `dispersion`, `material`, `effectiveIndex` |
| nonlinearity | `gamma`, or `n2` with `effectiveAreaM2` / `modeFieldDiameterM`; `raman`, `ramanFraction` |
| loss and gain | `lossDbPerKm`, `loss`, `gainDbPerM`, `gain` |
| passed to the inner bulk element | `nonlinear`, `selfSteepening`, `timeResponse`, `replaceKerr`, `transverse`, `ionization` |

## Model

### Dispersion

| key | meaning |
|---|---|
| `betasSkPerM` | [β2, β3, ...] in s^k/m, about `referenceWavelengthM` (old name `betas`, still accepted) |
| `betasPsKm` | the same in ps^k/km (pyNLO and datasheet units) |
| `dispersion` | a curve: `{"wavelengthsM": [...], "dPsNmKm": [...]}` (dispersion parameter D) or `"beta2S2PerM"`; integrated twice to β(ω). β2(ω) is a natural cubic spline through a table of 4 or more points (a linear interpolation was 0.8 % off for a 53-point table near D = 0), linear for 2 or 3 points, and held at its end values outside the table |
| `material` | a bulk material instead (for example a hollow-core fibre filled with a gas, or an unguided rod) |

The constant and linear terms of β only set the absolute phase and group delay, which the engine removes;
`effectiveIndex` (1.45) sets only that absolute phase and delay.

### Nonlinearity

`gamma` in 1/(W m), or `n2` (m²/W, default 2.6e-20) with `effectiveAreaM2` or `modeFieldDiameterM`
(A_eff = π MFD² / 4); γ = n2 ω0 / (c A_eff). With `gamma` alone the area is a bookkeeping choice (1e-11 m²) that does
not change the result; `n2` without an area is an error. `gamma` can be an expression in z (m) for a tapered fibre:
`"gamma": "0.1*(1 + 2*z)"`. The Kerr response is instantaneous (1 − fR) plus a delayed Raman part:

| `raman` | model | fR |
|---|---|---|
| `"silica"` (default) | Blow & Wood damped oscillator, τ1 = 12.2 fs, τ2 = 32 fs | 0.18 |
| `"linAgrawal"` | Lin & Agrawal 2006: damped oscillator + boson peak (τ_b = 96 fs) | 0.245 |
| `"hollenbeck"` | Hollenbeck & Cantrell 2002: 13 vibrational modes, intermediate broadening; Raman gain peak at 13.2 THz | 0.18 |
| `"none"` / `false` | Kerr only | 0 |
| an object | any delayed-response model (`expression`, `callback`, `dampedOscillator`, ...) | its `fR`, else `ramanFraction`, else the kind's default (0.18 for kinds without one) |

`ramanFraction` replaces the model's fR for a named model, and for an object without its own `fR`. The sampled kernel
is normalised on the time grid, so the delayed fraction is exactly fR at any time step
([Raman models](nonlinear.md#raman-models)). Self-steepening follows the run setting (`physics.selfSteepening`) or the
element's `selfSteepening`.

### Loss and gain

| key | meaning |
|---|---|
| `lossDbPerKm` | flat loss; it wins over `loss` when both are given |
| `loss` | `{"wavelengthsM": [...], "dbPerKm": [...]}`, linear in frequency between the points, held at the end values outside the table |
| `gainDbPerM` | flat small-signal (power) gain |
| `gain` | a gain spectrum `{"wavelengthsM", "dbPerM"}`, linear in frequency between the points and 0 outside the table; with `"outputEnergyJ"` (or `"outputPowerW"` and the pulse's `repRateHz`) it is scaled at run time so the linear output reaches that energy (as pyNLO's "propagate to gain goal") |

The gain is linear (unsaturated): for saturation, inversion and pump dynamics use the `gain` and `amplifier` elements
([Gain media](gain.md)).

### What the element reports

For the pulse that enters it (`summary.json → elements[].values`): `beta2S2PerM`, `dispersionLengthM` (T0² / |β2|),
`nonlinearLengthM` (1 / γP0) and `solitonNumber` N, with T0 = FWHM / 1.763 (sech convention). In anomalous dispersion
with N > 1 it adds `fissionLengthM` (L_D / N) and, when the dispersion is given as Taylor coefficients (`betasSkPerM` or
`betasPsKm`, not a `dispersion` curve or a `material`), `dispersiveWaveWavelengthM`: the phase-matching root
Σ_k β_k Δω^k / k! = γP0 / 2 nearest to the carrier. After the fibre, beam tracking restarts at the fibre end with the
mode radius √(A_eff / π).

### Noise and coherence

`pulse.noise` adds one photon per frequency mode with a random phase: the quantum noise that seeds modulation
instability and makes long-pulse supercontinua incoherent:

```jsonc
"pulse": { ..., "noise": { "kind": "onePhotonPerMode", "seed": 1 } }
```

A coherence study runs the system several times with independent noise and reports the first-order spectral
coherence |g12(λ)| = |⟨E_i*(λ) E_j(λ)⟩_(i ≠ j)| / ⟨|E(λ)|²⟩ and its spectrally weighted mean:

```bash
ultrafast study coherence.json      # {"mode": "coherence", "coherence": {"trials": 10, "at": "end"}, "parallel": 5, ...}
```

```python
co = uf.coherence(system, spec, trials=10)
co["meanCoherence"], co["wavelength_m"], co["g12"]
```

The grid is planned without the noise and frozen for all trials. Example:
[Supercontinuum](../examples/Nonlinear/supercontinuum/README.md); keys in [options: `coherence`](options.md#coherence).

## Validation

### Compared with pyNLO

`python/tests/test_pynlo.py` runs the same fibre through pyNLO's split-step solver and through the `fiber` element: a
higher-order soliton (N ≈ 2.4) with Blow-Wood Raman, with the Lin-Agrawal model, and with 30 /m flat gain. Temporal
profiles and spectra agree to 0.3 % (L1 distance) with either Raman model and to 0.5 % with gain; the reported soliton
number matches √(γ P0 T0² / |β2|) to 2 %. One difference is documented in the test: pyNLO (the pinned commit) writes the
oscillator part of the Lin-Agrawal response with a misprinted, unit-dependent normalisation, so its "full" Raman model
weights the boson peak more heavily than the paper; the comparison reproduces pyNLO's weights explicitly, and the
engine's `"linAgrawal"` follows the paper.

| pyNLO feature | here |
|---|---|
| fibre from betas / dispersion curve / fibre database, gamma, flat or spectral gain, gain to a power goal | `fiber` element (no fibre database: give the fibre's data) |
| z-dependent gamma | `gamma` as an expression in z |
| z-dependent dispersion | several `fiber` elements in sequence |
| SSFM / RK4IP with adaptive steps, self-steepening, Raman (simple and full) | the engine's adaptive interaction-picture stepper; Blow-Wood, Lin-Agrawal, Hollenbeck-Cantrell |
| quantum noise, coherence over repeated runs | `pulse.noise`, coherence study |
| three-wave mixing (DFG) in PPLN, AgGaSe2 with focusing | `crystal` element (QPM, focusing through the radial model); cross-checked in `test_pynlo_dfg.py` |
| grating compressor | `gratingPair` |
| dispersive-wave calculator | `dispersiveWaveWavelengthM` reported by the fibre |

In C#, `FiberTests.FundamentalSoliton_PropagatesUnchanged_AndMetricsAreRight` checks a fundamental soliton and the
reported lengths.

## Limits

* One guided mode (no mode coupling); the effective area is constant along the fibre (use several
  elements for a taper of the area, or a `gamma` expression).
* The gain is unsaturated and has no pump dynamics.
* No fibre database: give the fibre's data.
* `dispersiveWaveWavelengthM` is a phase-matching estimate for Taylor dispersion only.
