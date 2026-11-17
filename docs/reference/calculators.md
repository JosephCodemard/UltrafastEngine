# Calculators (`ultrafast.calc`)

Small functions that answer one design question in one call: *what is the transform limit of this pulse, which angle phase-matches
this crystal, how do I match this beam into that waist, how close to collapse is this beam*. They are the package
`ultrafast.calc`; the modules are

| module | what | needs the engine |
|---|---|---|
| `calc.pulse` | time-bandwidth product, transform limit, chirp, measured pulse ([Pulse calculator](pulse-calculator.md)) | `shapes`, `tbp`: no; `limit`, `analyze`, `measure`: yes |
| `calc.crystal` | phase matching of χ(2) crystals ([Crystals](crystals.md#phase-matching-calculator)) | yes |
| `calc.cell` | Kerr-matched mode of a two-mirror multi-pass cell ([Components](components.md)) | yes |
| `calc.beam` | Gaussian beam, ABCD, single- and two-lens mode matching, telescope, M² | **no** (NumPy only) |
| `calc.nonlinear` | critical power, Marburger collapse distance, B-integral, peak intensity, fluence | **no** (NumPy only) |

There is no separate "lite" package (decision E7): `beam`, `nonlinear` and `pulse.tbp` work in a plain `pip install ultrafast-engine`
without any engine library. The old names `ultrafast.calc.tbp / limit / analyze / measure`, `ultrafast.crystal` and `ultrafast.cells`
keep working.

Conventions: SI arguments with the unit in the name (`wavelength_m`, `duration_s`, `peak_power_w`, `n2_m2_per_w`); a bad argument raises
`ValueError` with a message that says what to change (engine errors are converted); results are typed dicts (`TypedDict`), so an editor
completes the keys. The older keywords without the unit (`duration=`, `center=`, `fundamental=`, `pump=`, `length=`, `wavelength=`,
`mirror_radius=`, ...) are accepted with a `DeprecationWarning`.

```python
from ultrafast import calc
calc.tbp("lorentzian")                                    # 0.1420
calc.beam.gaussian_beam(1030e-9, waist_radius_m=0.5e-3, z_m=2.0)["radiusM"]
calc.nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, n0=1.0003)   # 3.2 GW (air)
```

## Pulse shapes

`calc.pulse.shapes()` / `calc.tbp(shape)`: the intensity-FWHM time-bandwidth product of a transform-limited pulse.

| shape | TBP | definition |
|---|---|---|
| `gaussian` | 0.4413 | 2 ln 2 / π |
| `sech2` | 0.3148 | (2 ln(1+√2) / π)² |
| `lorentzian` | 0.1420 | field 1/(1 + (t/τ)²), intensity its square: ln 2 √(√2 − 1) / π |
| `asymmetric-exponential` | 0.1103 | intensity exp(−t/τ) for t ≥ 0 (one-sided): ln 2 / (2π) |
| `rectangular` | 0.8859 | flat-top pulse, sinc² spectrum |

Source: Diels and Rudolph, *Ultrashort Laser Pulse Phenomena*, table 2.1. The C# test also derives the Lorentzian and the exponential from a
numerical Fourier transform of the field.

## Beam optics (`calc.beam`)

Conventions as in Siegman, *Lasers* (ch. 17 and 20): `w` is the 1/e² intensity radius, the complex beam parameter is `q = z + i zR`
(`z` from the waist, positive downstream), `1/q = 1/R − i M²λ/(π w²)`, and a real beam of quality `m2` (M²) behaves like an ideal beam of
wavelength `m2 · λ`. `wavelength_m` is the wavelength in the medium the beam is in.

| function | returns |
|---|---|
| `gaussian_beam(wavelength_m, waist_radius_m=, m2=1, z_m=0)` | `GaussianBeam`: Rayleigh range, divergence, radius, wavefront radius, Gouy phase, `q` at `z_m` |
| `rayleigh_range_m`, `beam_radius_m`, `curvature_radius_m`, `waist_radius_m` | one number |
| `q_at`, `q_from_radius_curvature`, `q_to_beam` | `q` from a waist or from (w, R); the beam a `q` describes |
| `free_space`, `thin_lens`, `curved_mirror`, `interface`, `system_matrix`, `propagate_q` | ABCD matrices (`((A, B), (C, D))`) and `q → (Aq + B)/(Cq + D)` |
| `single_lens_match(waist_in_m=, waist_out_m=, wavelength_m=, focal_length_m=)` | the two lens positions that turn waist `w1` into waist `w2` (Self 1983): `d1 = f ± (w1/w2) √(f² − f0²)`, `d2 = f ± (w2/w1) √(f² − f0²)`, `f0 = π w1 w2 / (M²λ)`; `ValueError` if `f < f0`. A negative distance means the waist is virtual (the lens must stand upstream of the input waist) |
| `two_lens_match(w1, w2, wavelength_m, focal_length_1_m=, focal_length_2_m=, separation_m=)` | every first-lens position that gives the output waist `w2` through two lenses a fixed distance apart, with the output waist position |
| `telescope(magnification=, focal_length_1_m=, kind="keplerian" \| "galilean")` | `f2`, the separation `f1 + f2` and the divergence reduction |
| `m_squared_from_widths(z_m, d4sigma_m, wavelength_m=)` | ISO 11146 hyperbola fit of 4σ diameters: M², waist, position, divergence |
| `m_squared_from_waist_divergence` | `M² = π w0 θ / λ` |

```python
from ultrafast.calc import beam
r = beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=1030e-9, focal_length_m=0.5)
r["solutions"]          # [{'distanceInputM': -0.97, 'distanceOutputM': 0.34}, {'distanceInputM': 1.97, 'distanceOutputM': 0.66}]
```

Tests propagate every returned solution through independent ABCD code and compare the output waist and position with the request.

## Nonlinear estimates (`calc.nonlinear`)

| function | formula |
|---|---|
| `critical_power_w(wavelength_m, n2_m2_per_w=, n0=1)` | `P_cr = 3.77 λ² / (8π n0 n2)` (Marburger 1975; the constant behind the engine's `criticalPower` check, summary.json `validity`). `profile="townes"`: 3.7924 |
| `collapse_distance_m(peak_power_w, wavelength_m, waist_radius_m=, n2_m2_per_w=, n0=1)` | `z_sf = 0.367 z_R / √((√(P/P_cr) − 0.852)² − 0.0219)`, `z_R = π n0 w0² / λ = k w0²/2`, `w0` the 1/e² radius of a collimated Gaussian beam. An empirical fit to simulations (error up to ~40 % near `P_cr`, < 20 % for 2.5 < P/P_cr < 20); `ValueError` for `P ≤ P_cr` (no collapse) |
| `peak_power_w(pulse_energy_j, duration_fwhm_s, shape)` | 0.9394 E/τ (Gaussian), 0.8814 E/τ (sech²) |
| `peak_intensity_w_per_m2(peak_power_w, beam_radius_m)` | `2 P / (π w²)` |
| `peak_fluence_j_per_m2(pulse_energy_j, beam_radius_m)` | `2 E / (π w²)` |
| `b_integral_rad(wavelength_m, n2_m2_per_w=, peak_intensity_w_per_m2=, length_m=)` | `B = (2π/λ) n2 I L` |
| `b_integral_estimate(wavelength_m, n2_m2_per_w=, length_m=, pulse_energy_j=, duration_fwhm_s=, beam_radius_m=)` | the peak B and the **averaged** values: pulse-averaged `× 1/√2` (Gaussian pulse; `× 2/3` for sech²), beam-averaged `× 1/2` (Gaussian beam), both `× 1/(2√2)`. The averages are the intensity-weighted means ∫I² / (I0 ∫I), the same definition as the engine's `bIntegralPulseAveragedRad` and `bIntegralBeamAveragedRad` |

No diffraction, no dispersion, instantaneous Kerr effect: these are first estimates. For the real number use a run
(`summary.json`: `bIntegralRad`, `validity`).

## Crystals and cells

`calc.crystal.phase_match(crystal, process, type, ...)` is the phase-matching calculator
([Crystals](crystals.md#phase-matching-calculator)), now with the unit in every wavelength keyword (`fundamental_m`, `pump_m`, `signal_m`,
`idler_m`, `a_m`, `b_m`, `length_m`, `pump_intensity_w_per_m2`). `noncollinear=` is the angle in degrees between the two inputs inside the crystal
for `sfg` (0 is the collinear result), the pump–signal angle or `"magic"` for `opa` / `dfg`, as `interaction.noncollinearDeg` of the element.
`calc.cell.cell_mode(wavelength_m, mirror_radius_m=, separation_m=, peak_power_w=, gas=...)` is the Kerr-matched cell mode.
