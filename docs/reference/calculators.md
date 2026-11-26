# Calculators (`ultrafast.calc`)

Small functions that answer one design question in one call: what is the transform limit of this pulse, which angle phase-matches this
crystal, how do I match this beam into that waist, how close to collapse is this beam. They are the package `ultrafast.calc`. Use them
for a first number before you build a system file; for the real number run the engine.

```python
from ultrafast import calc
calc.tbp("lorentzian")                                    # 0.1420
calc.beam.gaussian_beam(1030e-9, waist_radius_m=0.5e-3, z_m=2.0)["radiusM"]
calc.nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, n0=1.0003)   # 3.2 GW (air)
```

## Keys

Conventions for every function: SI arguments with the unit in the name (`wavelength_m`, `duration_s`, `peak_power_w`,
`n2_m2_per_w`); a bad argument raises `ValueError` with a message that says what to change (engine errors are converted); results are
typed dicts (`TypedDict`), so an editor completes the keys. The older keywords without the unit (`duration=`, `center=`,
`fundamental=`, `pump=`, `length=`, `wavelength=`, `mirror_radius=`, ...) are accepted with a `DeprecationWarning`. The old module
paths `ultrafast.calc.tbp / limit / analyze / measure`, `ultrafast.crystal` and `ultrafast.cells` keep working.

There is no separate "lite" package (decision E7): `beam`, `nonlinear` and `pulse.tbp` work in a plain `pip install ultrafast-engine`
without the engine library.

| module | what | needs the engine |
|---|---|---|
| `calc.pulse` | time-bandwidth product, transform limit, chirp, measured pulse ([Pulse calculator](pulse-calculator.md)) | `shapes`, `tbp`: no; `limit`, `analyze`, `measure`: yes |
| `calc.crystal` | phase matching of χ(2) crystals ([Crystals](crystals.md#phase-matching-calculator)) | yes |
| `calc.cell` | Kerr-matched mode of a two-mirror multipass cell ([Components](components.md)) | yes |
| `calc.beam` | Gaussian beam, ABCD matrices, single- and two-lens mode matching, telescope, M² | no (NumPy only) |
| `calc.nonlinear` | critical power, Marburger collapse distance, B-integral, peak intensity, fluence | no (NumPy only) |

### Beam optics (`calc.beam`)

`w` is the 1/e² intensity radius; `m2` is M² (default 1) and `wavelength_m` is the wavelength in the medium the beam is in.

| function | returns |
|---|---|
| `gaussian_beam(wavelength_m, *, waist_radius_m, m2=1, z_m=0)` | `GaussianBeam`: Rayleigh range, divergence, radius, wavefront radius, Gouy phase, `q` at `z_m` |
| `rayleigh_range_m`, `beam_radius_m`, `curvature_radius_m`, `waist_radius_m` | one number |
| `q_at`, `q_from_radius_curvature`, `q_to_beam` | `q` from a waist or from (w, R); the beam a `q` describes |
| `free_space`, `thin_lens`, `curved_mirror`, `interface`, `system_matrix`, `propagate_q` | ABCD matrices (`((A, B), (C, D))`) and `q → (Aq + B)/(Cq + D)` |
| `single_lens_match(*, waist_in_m, waist_out_m, wavelength_m, focal_length_m, m2=1)` | `minimumFocalLengthM`, `focalLengthM` and `solutions`: the one or two lens positions `distanceInputM` (input waist to lens) and `distanceOutputM` (lens to output waist). A negative distance means a virtual waist: the lens stands upstream of the input waist. `ValueError` if the lens is shorter than `f0` |
| `two_lens_match(waist_in_m, waist_out_m, wavelength_m, *, focal_length_1_m, focal_length_2_m, separation_m, m2=1, max_distance_m=None, samples=20001)` | `solutions`: every `distanceInputM` (input waist to lens 1) that gives the output waist, with `distanceOutputM` (lens 2 to the output waist) |
| `telescope(*, magnification, focal_length_1_m, kind="keplerian")` | `kind`, `magnification`, `focalLength1M`, `focalLength2M`, `separationM` (`f1 + f2`), `divergenceReduction`. `"keplerian"` needs `f1 > 0`, `"galilean"` needs `f1 < 0` (otherwise `ValueError`); `magnification < 1` shrinks the beam |
| `m_squared_from_widths(z_m, d4sigma_m, wavelength_m)` | ISO 11146 hyperbola fit of 4σ diameters; `wavelength_m` is required and at least 3 positions are needed. Returns `m2`, `waistDiameterM`, `waistPositionM`, `divergenceFullAngleRad`, `rayleighRangeM`; `ValueError` if the widths are not a hyperbola |
| `m_squared_from_waist_divergence(waist_radius_m, half_angle_rad, wavelength_m)` | M² from the waist radius and the far-field **half** angle |

```python
from ultrafast.calc import beam
r = beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=1030e-9, focal_length_m=0.5)
r["solutions"]          # [{'distanceInputM': -0.97, 'distanceOutputM': 0.34}, {'distanceInputM': 1.97, 'distanceOutputM': 0.66}]
```

### Nonlinear estimates (`calc.nonlinear`)

| function | returns |
|---|---|
| `critical_power_w(wavelength_m, *, n2_m2_per_w, n0=1, profile="marburger")` | critical power for self-focusing; `profile="townes"` uses the Townes factor 3.7924 |
| `collapse_distance_m(peak_power_w, wavelength_m, *, waist_radius_m, n2_m2_per_w, n0=1)` | Marburger collapse distance of a collimated Gaussian beam; `ValueError` for `P ≤ P_cr` |
| `peak_power_w(pulse_energy_j, duration_fwhm_s, shape="gaussian")` | peak power; `shape` is `gaussian` or `sech2` (aliases `gauss`, `sech`, `sech^2`), any other name is a `ValueError` |
| `peak_intensity_w_per_m2(peak_power_w, beam_radius_m)`, `peak_fluence_j_per_m2(pulse_energy_j, beam_radius_m)` | on-axis peak values of a Gaussian beam |
| `b_integral_rad(wavelength_m, *, n2_m2_per_w, peak_intensity_w_per_m2, length_m)` | B for a constant intensity |
| `b_integral_estimate(wavelength_m, *, n2_m2_per_w, length_m, pulse_energy_j, duration_fwhm_s, beam_radius_m, shape="gaussian", n0=None)` | keys `peakPowerW`, `peakIntensityWPerM2`, `peakFluenceJPerM2`, `bPeakRad`, `bPulseAveragedRad`, `bBeamAveragedRad`, `bPulseAndBeamAveragedRad`, `criticalPowerW`, `powerOverCritical`. The last two are `nan` unless `n0` is given |

### Crystals and cells

`calc.crystal.phase_match(crystal, process="shg", type=None, *, ...)` is the phase-matching calculator
([Crystals](crystals.md#phase-matching-calculator)). Keywords: `fundamental_m`, `pump_m`, `signal_m`, `idler_m`, `a_m`, `b_m`,
`length_m`, `pump_intensity_w_per_m2`, `plane`, `theta_deg`, `phi_deg`, `qpm`, `temperature_c`, `polarizations`,
`noncollinear`, and further keywords passed on to the engine. `noncollinear` is the angle in degrees between the two inputs inside
the crystal for `sfg` (0 is the collinear result), and the pump-signal angle or `"magic"` for `opa` / `dfg`, as
`interaction.noncollinearDeg` of the element. `calc.crystal.crystals()` lists the built-in crystals.

`calc.cell.cell_mode(wavelength_m, *, mirror_radius_m, separation_m, peak_power_w=0, gas=None, passes=2, reentrant=None, plates=None, lens_model="secondMoment", slices=12)`
is the Kerr-matched cell mode. `mirror_radius_m` may be one number or a sequence (one per mirror).

## Model

**Beam conventions** follow Siegman, *Lasers* (ch. 17 and 20). The complex beam parameter is `q = z + i zR` (`z` from the waist,
positive downstream), `1/q = 1/R − i M²λ/(π w²)`, and a beam of quality `m2` behaves like an ideal beam of wavelength `m2 · λ`.

**Pulse shapes.** `calc.pulse.shapes()` and `calc.tbp(shape)` give the intensity-FWHM time-bandwidth product of a transform-limited
pulse (Diels and Rudolph, *Ultrashort Laser Pulse Phenomena*, table 2.1):

| shape | TBP | definition |
|---|---|---|
| `gaussian` | 0.4413 | 2 ln 2 / π |
| `sech2` | 0.3148 | (2 ln(1+√2) / π)² |
| `lorentzian` | 0.1420 | field 1/(1 + (t/τ)²), intensity its square: ln 2 √(√2 − 1) / π |
| `asymmetric-exponential` | 0.1103 | intensity exp(−t/τ) for t ≥ 0 (one-sided): ln 2 / (2π) |
| `rectangular` | 0.8859 | flat-top pulse, sinc² spectrum |

**Single-lens match** (Self 1983, Appl. Opt. 22, 658): `f0 = π w1 w2 / (M² λ)`, `d1 = f ± (w1/w2) √(f² − f0²)`,
`d2 = f ± (w2/w1) √(f² − f0²)` (same sign in both). **Two-lens match:** the function scans the first-lens position `d1` over
`[0, max_distance_m]` in `samples` steps, finds every sign change of `w_out(d1) − w2` and refines it by bisection. The default
`max_distance_m` is 20 × (|f1| + |f2| + separation + input Rayleigh range).

**Critical power** `P_cr = 3.77 λ² / (8π n0 n2)` (Marburger 1975); the same constant is behind the engine's `criticalPower` check in
`summary.json` `validity`.

**Collapse distance** `z_sf = 0.367 z_R / √((√(P/P_cr) − 0.852)² − 0.0219)` with `z_R = π n0 w0² / λ` and `w0` the 1/e² radius of a
collimated Gaussian beam. It is an empirical fit to simulations, within about 20 % for 2.5 < P/P_cr < 20 (module docstring of
`calc/nonlinear.py`).

**Peak values** of a Gaussian beam: `P0 = 0.9394 E/τ` (Gaussian pulse) or `0.8814 E/τ` (sech²); `I0 = 2P/(π w²)`; `F0 = 2E/(π w²)`.

**B-integral** `B = (2π/λ) n2 I L`. `b_integral_estimate` also gives the averaged values, the intensity-weighted means ∫I² / (I0 ∫I):
pulse-averaged `× 1/√2` (Gaussian pulse; `× 2/3` for sech²), beam-averaged `× 1/2` (Gaussian beam), both `× 1/(2√2)`. They are the
same definitions as the engine's `bIntegralPulseAveragedRad` and `bIntegralBeamAveragedRad`.

## Validation

| what | reference | test |
|---|---|---|
| TBP table | Diels and Rudolph table 2.1; the C# test also derives the Lorentzian and the exponential by a numerical Fourier transform | `test_tbp_table_matches_diels_rudolph`, `PulseCalculatorTests` |
| Python TBP equals the engine's | the two copies agree | `test_python_tbp_table_equals_the_engine_table` |
| Gaussian-beam closed forms, `q`, ABCD | analytic beam | `test_gaussian_beam_closed_forms`, `test_q_round_trip_and_radius_curvature`, `test_abcd_propagation_equals_the_analytic_beam` |
| lens matching | every returned solution is propagated through independent ABCD code and the output waist and position are compared with the request | `test_single_lens_match_closed_loop`, `test_single_lens_match_edge_cases`, `test_two_lens_match_closed_loop` |
| telescope | magnification, sign rules | `test_telescope_magnifies_the_beam`, `test_telescope_rejects_the_wrong_sign` |
| M² fit | recovers a noise-free hyperbola | `test_m_squared_fit_recovers_a_noise_free_hyperbola` |
| critical power, collapse distance | Marburger's formulas, 3.2 GW in air at 800 nm | `test_critical_power_marburger_and_air`, `test_marburger_collapse_distance` |
| peak values, B-integral averages | closed forms; the averages are also checked by numerical integration | `test_peak_power_intensity_fluence`, `test_b_integral_averages_gaussian_and_sech2` |
| no engine needed | `beam`, `nonlinear`, `tbp` import without the engine | `test_beam_nonlinear_and_tbp_work_without_the_engine` |

All in `python/tests/test_calc.py`.

## Limits

* The nonlinear estimates have no diffraction, no dispersion and an instantaneous Kerr effect. For the real number use a run
  (`summary.json`: `bIntegralRad`, `validity`).
* `two_lens_match` finds only roots inside `[0, max_distance_m]` that change sign between two samples. A root outside the range, or a
  double root between two samples, is not returned: widen `max_distance_m` or raise `samples`.
* `collapse_distance_m` is a fit of unstated accuracy near `P_cr` and is valid for `P > P_cr`.
* `b_integral_estimate` and `peak_power_w` know two shapes, `gaussian` and `sech2`.
* `cell_mode` handles round beams and peak power; it is a command and a function, not an element option.
