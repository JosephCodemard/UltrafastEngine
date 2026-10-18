# Fibres and waveguides

The `fiber` element (aliases `fibre`, `waveguide`) describes a single-mode fibre the way fibre codes and datasheets
do: Taylor coefficients of the propagation constant, the nonlinear coefficient gamma, loss in dB/km and gain in
dB/m. It compiles into the engine's nonlinear medium, so Raman, self-steepening, the adaptive solver, instruments,
overrides and studies all apply.

```jsonc
{ "id": "pcf", "type": "fiber", "lengthM": 0.15,
  "betasPsKm": [-11.830, 8.1038e-2, -9.5205e-5, 2.0737e-7],   // beta2, beta3, ... in ps^k/km (or "betas" in s^k/m)
  "referenceWavelengthM": 835e-9,                              // where the expansion is taken (default: the carrier)
  "gamma": 0.11,                                               // 1/(W m)
  "lossDbPerKm": 0, "raman": "silica" }
```

## Dispersion

| key | meaning |
|---|---|
| `betas` | [beta2, beta3, ...] in s^k/m, about `referenceWavelengthM` |
| `betasPsKm` | the same in ps^k/km (pyNLO and datasheet units) |
| `dispersion` | a curve: `{"wavelengthsM": [...], "dPsNmKm": [...]}` (dispersion parameter D) or `"beta2S2PerM"`; integrated twice to beta(omega) |
| `material` | a bulk material instead (e.g. a hollow-core fibre filled with a gas, or an unguided rod) |

The constant and linear terms of beta only set the absolute phase and group delay, which the engine removes;
`effectiveIndex` (1.45) is there for completeness.

## Nonlinearity

`gamma` in 1/(W m), or `n2` with `effectiveAreaM2` / `modeFieldDiameterM`. `gamma` can be an expression in z (m)
for a tapered fibre: `"gamma": "0.1*(1 + 2*z)"`. The Kerr response is instantaneous (1 - fR) plus a delayed Raman part:

| `raman` | model | fR |
|---|---|---|
| `"silica"` (default) | Blow & Wood damped oscillator, tau1 = 12.2 fs, tau2 = 32 fs | 0.18 |
| `"linAgrawal"` | Lin & Agrawal 2006: damped oscillator + boson peak (tau_b = 96 fs) | 0.245 |
| `"hollenbeck"` | Hollenbeck & Cantrell 2002: 13 vibrational modes, intermediate broadening; Raman gain peak at 13.2 THz | 0.18 |
| `"none"` / `false` | Kerr only | 0 |
| an object | any delayed-response model (`expression`, `callback`, `dampedOscillator`, ... with `fR`) | |

The sampled kernel is normalised on the time grid, so the delayed fraction is exactly fR at any time step
([Raman models](nonlinear.md#raman-models)). Self-steepening follows the run setting (`physics.selfSteepening`) or the
element's `selfSteepening`.

## Loss and gain

| key | meaning |
|---|---|
| `lossDbPerKm` | flat loss |
| `loss` | `{"wavelengthsM": [...], "dbPerKm": [...]}` |
| `gainDbPerM` | flat small-signal gain (power) |
| `gain` | a gain spectrum `{"wavelengthsM", "dbPerM"}`; with `"outputEnergyJ"` (or `"outputPowerW"` and the pulse's `repRateHz`) it is scaled at run time so the linear output reaches that energy (as pyNLO's "propagate to gain goal") |

The gain is linear (unsaturated): for saturation, inversion and pump dynamics use the `gain` and `amplifier`
elements ([Gain media](gain.md)).

## What the element reports

For the pulse that enters it (`summary.json → elements[].values`): `beta2S2PerM`, `dispersionLengthM`
(T0² / |beta2|), `nonlinearLengthM` (1 / gamma P0), `solitonNumber` N, and in anomalous dispersion with N > 1
`fissionLengthM` (L_D / N) and `dispersiveWaveWavelengthM` (phase matching sum_k beta_k dw^k / k! = gamma P0 / 2).
T0 is FWHM / 1.763 (sech convention). After the fibre, beam tracking restarts at the fibre end with the mode radius
sqrt(A_eff / pi).

## Noise and coherence

`pulse.noise` adds one photon per frequency mode with a random phase -- the quantum noise that seeds modulation
instability and makes long-pulse supercontinua incoherent:

```jsonc
"pulse": { ..., "noise": { "kind": "onePhotonPerMode", "seed": 1 } }
```

A coherence study runs the system several times with independent noise and reports the first-order spectral
coherence |g12(lambda)| = |<E_i*(lambda) E_j(lambda)>_(i != j)| / <|E(lambda)|²> and its spectrally weighted mean:

```bash
ultrafast study coherence.json      # {"mode": "coherence", "coherence": {"trials": 10, "at": "end"}, "parallel": 5, ...}
```

```python
co = uf.coherence(system, spec, trials=10)
co["meanCoherence"], co["wavelength_m"], co["g12"]
```

The grid is planned without the noise and frozen for all trials. Example: [25 Supercontinuum](../examples/25-supercontinuum/README.md).

## Compared with pyNLO

`python/tests/test_pynlo.py` runs the same fibre through pyNLO's split-step solver and through the `fiber` element:
a higher-order soliton with Blow-Wood Raman, with the Lin-Agrawal model, and with 30 /m flat gain. Temporal profiles
and spectra agree to better than 0.5 % (L1 distance). One difference is documented in the test: pyNLO (the pinned
commit) writes the oscillator part of the Lin-Agrawal response with a misprinted, unit-dependent normalisation, so its
"full" Raman model weights the boson peak more heavily than the paper; the comparison reproduces pyNLO's weights
explicitly, and the engine's `"linAgrawal"` follows the paper.

| pyNLO feature | here |
|---|---|
| fibre from betas / dispersion curve / fibre database, gamma, flat or spectral gain, gain to a power goal | `fiber` element (no fibre database: give the fibre's data) |
| z-dependent gamma | `gamma` as an expression in z |
| z-dependent dispersion | several `fiber` elements in sequence |
| SSFM / RK4IP with adaptive steps, self-steepening, Raman (simple and full) | the engine's adaptive interaction-picture solver; Blow-Wood, Lin-Agrawal, Hollenbeck-Cantrell |
| quantum noise, coherence over repeated runs | `pulse.noise`, coherence study |
| three-wave mixing (DFG) in PPLN, AgGaSe2 with focusing | `crystal` element (QPM, focusing through the radial model); cross-checked in `test_pynlo_dfg.py` |
| grating compressor | `gratingPair` |
| dispersive-wave calculator | `dispersiveWaveWavelengthM` reported by the fibre |
