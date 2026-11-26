# Ionisation and plasma

At intensities above about 10¹³ W/cm² gases and dielectrics ionise. The free electrons lower the index (plasma
defocusing, blue shift), absorb (inverse bremsstrahlung), and every ionisation takes its energy from the pulse. The
`ionization` key switches this on for one element (`bulk`, `gap`, `plate`, `window`, `ndFilter`, a hollow-core `fiber`,
which passes it to its inner bulk element, and a `crystal`), or `physics.ionization` for every nonlinear bulk element of
the run:

```jsonc
{ "id": "cell", "type": "bulk", "material": "argon2bar", "lengthM": 0.5, "ionization": true }
"physics": { "ionization": { "model": "ppt", "avalanche": false } }      // every nonlinear bulk element
```

## Keys

`ionization` is `true` or an object; every key with its default is in [options: `ionization`](options.md#ionization).

| key | meaning |
|---|---|
| `model` | rate model: `ppt` (gases, default), `adk`, `keldysh` (solids, default), `mpi` |
| `ionizationEv`, `zEff` | gases: ionisation potential, effective charge (default: the [gas table](gases.md)) |
| `bandGapEv`, `effectiveMass`, `neutralDensityM3` | solids: band gap, reduced mass / m_e, density of valence electrons (1/m³) |
| `sigmaK`, `photons` | `mpi`: cross-section σ_K in (m²/W)^K/s and the order K |
| `collisionTimeS`, `recombinationTimeS`, `recombinationBetaM3PerS` | electron collision time, recombination (trapping) time, two-body recombination |
| `avalanche` | impact ionisation; default on for solids, and on for a gas only when `collisionTimeS` is given |
| `absorption` | `false` removes every energy loss of the plasma: the inverse-bremsstrahlung absorption and the ionisation energy (default `true`) |
| `electronDensityM3` | a plasma present before the pulse (a prepared plasma or a discharge); it decays with the recombination time |
| `frequencyResolved` | apply the Drude index at every frequency of the pulse (default `true`) or at the carrier only |
| `skipNegligible` | leave out an element's plasma term when a bound shows it negligible (default `true`, 1-D elements) |

## Model

### Rates

| model | for | rate |
|---|---|---|
| `ppt` (gases, default) | atoms and molecules | Perelomov-Popov-Terent'ev, cycle averaged, ADK coefficients C²_{n*l*}, Popov's Coulomb correction; multiphoton (γ ≫ 1, W ∝ I^K) to tunnel (γ ≪ 1) |
| `adk` | gases, γ ≲ 0.5 | Ammosov-Delone-Krainov tunnel rate, cycle averaged; quasi-static in carrier-resolved mode |
| `keldysh` (solids, default) | dielectrics | Keldysh 1965 (in the form of Couairon & Mysyrowicz, Phys. Rep. 441, 47 (2007)): band gap, reduced mass, valence density |
| `mpi` | any | σ_K I^K (`sigmaK`, `photons`; for a gas the default order is the number of photons that reach Ip) |

γ = ω √(2 m Ip) / (e E) is the Keldysh parameter. Gas data (Ip, l, effective charge) come from the
[gas table](gases.md); air ionises through N2 and O2 with their own densities. Solids with ionisation data in the
catalogue (all marked `unverified`):

| material | band gap | m*/m_e | valence density | τ_c | τ_rec |
|---|---|---|---|---|---|
| FusedSilica, Quartz | 9.0 eV | 0.635 | 2.1e28 m⁻³ | 1 fs | 150 fs |
| Sapphire | 8.8 eV | 0.5 | 2.3e28 m⁻³ | 1 fs | 150 fs |
| CaF2 | 10.0 eV | 0.5 | 2.4e28 m⁻³ | 1 fs | 150 fs |
| MgF2 | 10.8 eV | 0.5 | 2.0e28 m⁻³ | 1 fs | 150 fs |
| YAG | 6.5 eV | 0.5 | 2.0e28 m⁻³ | 1 fs | 150 fs |
| N-BK7 | 4.2 eV (rough) | 0.5 | 2.0e28 m⁻³ | 1 fs | 150 fs |

For another solid give at least `bandGapEv`; the inputs the run does not give come from the generic dielectric entry
`defaults:solidIonization` (m* = 0.635, 2.1e28 m⁻³, τ_c = 1 fs, τ_rec = 150 fs: the fused-silica values), and the run
notes name the fields the run gave and the source of the others. Gases default to τ_c = 350 fs / p[bar] and no
recombination.

### Plasma

The electron density follows, along the pulse at every point (1-D: the on-axis intensity; spatial grids: each point),

dρ/dt = Σ_s W_s(I) (ρ_s − ρ_s,ionised) + (σ I / U) ρ − ρ/τ_rec − β ρ²,

and acts as a free-electron (Drude) index, frequency by frequency. At the carrier

Δn = −(ρ / 2 n₀ ρ_c) (1 − i/(ω₀ τ_c)) / (1 + 1/(ω₀ τ_c)²),  ρ_c = ε₀ m_e ω₀² / e²,

and across the spectrum of the pulse Δn(ω) = −ρ e² / (2 n₀ ε₀ m_e ω (ω + iν)), ν = 1/τ_c (Couairon & Mysyrowicz, Phys. Rep.
441, 47 (2007)), the first-order expansion in the density. The propagators apply it as a spectral operator,
dA/dz(ω) += i (ω/c) R(ω) FFT[Δn_ω₀(t) A(t)](ω), with R(ω) = ω₀ (ω₀ + iν) / (ω (ω + iν)) (R = 1 at the carrier, 0 at
ω ≤ 0). The blue-shifted leading edge of a broad pulse then sees less plasma than the carrier does, and the electrons
born in the field take the drift energy they gain from it (U_p of the field at birth, in the loss budget as part of the
absorption). The operator is the same in the envelope, the carrier-resolved and the spatial models. It treats the
density as slowly varying against the optical cycle (the product ρ(t) A(t) is formed first, then the 1/(ω + iν) factor).
`"frequencyResolved": false` applies the carrier value instead; the difference is first order in the bandwidth, and for
the filament example (40 fs, 800 nm) it is 2 nm in the centroid and 1 % in the energy. With `selfSteepening: false` the
other terms use the carrier value of ω/c; the plasma term always uses ω/c at each frequency, as its current equation does.

**Energy taken by ionisation.** The ionisation loss Im Δn += U W (ρ_n − ρ_ionised) / (2 k₀ I) is a loss at the carrier, not a Drude
term, and is local in time. σ = k₀ ω₀ τ_c / (n₀ ρ_c (1 + ω₀² τ_c²)) is the avalanche cross-section. The avalanche takes
its energy from the inverse-bremsstrahlung absorption that the Drude imaginary part already books (σρI per volume, the
same number), so it adds no second loss to the field (`Plasma_Avalanche_DoesNotAbsorbTheFieldASecondTime`). With
`absorption: false` both the inverse-bremsstrahlung term and the ionisation loss are left out (`Physics/Ionization.cs`,
`PlasmaResponse.Run`): ionisation then takes no energy from the pulse, and only the real part of the Drude index acts.

Every element reports `peakElectronDensityM3` and `peakIonizationFraction`.

**Negligible ionisation is skipped** (`skipNegligible`, default `true`, 1-D elements). Before an element runs, a
conservative bound is taken from the entry pulse: every rate at 4 × the peak intensity, over 4 × the effective duration
E / P_peak, with avalanche growth exp(σ 16 I_pk T / U). When the bound keeps the Drude phase below 1e-6 rad and the
absorbed energy below 1e-9 of the pulse, the plasma term is left out and the note `ionization skipped as negligible`
gives the density bound (`ionizationDensityBoundM3` in the record). The field then differs by less than the stepper's
tolerance. A prepared plasma is never skipped. In a 46-pass multipass cell with ionisation on every air gap the skip
removes a 20 × slow-down, with no change in the result.

**On a GPU** (1-D envelope elements) the plasma term runs on the device: the rates are evaluated in parallel from a
table of ln W against ln I (16384 nodes from 10⁸ to 10²² W/m², cubic between nodes, built once per rate model), then one
work-item integrates the density along the pulse as the CPU does, and the frequency-resolved Drude term is applied with
one more FFT. With an MPI rate (a straight line in the table) the device equals the CPU to 1e-16. PPT and Keldysh have
kinks where a multiphoton channel closes; there the table is off by up to 1e-2 locally and the ionised density of a
pulse by up to 4e-6, far below the uncertainty of the rate models themselves.

**Carrier-resolved mode** (`physics.fieldMode: "carrier"`). PPT and ADK use the quasi-static rate at the instantaneous
field, so the sub-cycle steps of ionisation are resolved; the Drude operator is the one above. The drift energy U_p
assumes electrons born with a uniform phase of the optical cycle, which is what the cycle-averaged rates of the envelope
and spatial models and the MPI rate say. In carrier-resolved mode PPT and ADK electrons are born near the crests of the
field, where the drift velocity is small, so the carrier-resolved model books less than U_p per electron; the two models
then differ by up to U_p per electron (about 6 eV against 15.8 eV for argon at 800 nm and 10¹⁴ W/cm²; tested only with
MPI, where they agree).

### In χ(2) crystals

A `crystal` element takes the same `ionization` key (`true` or an object). It is **off by default and set per element**:
the run-level `physics.ionization` is for bulk elements and does not reach crystals. The coupled-wave solver adds, at
every step and for every time row (one row in the 1-D model, one per transverse point in a radial or cartesian model):

* one electron density per field, each with its **own rate model built at that field's carrier** (so the photon order is
  its own: the 800 nm fundamental in BBO is a five-photon process, the 400 nm harmonic a two-photon one), driven by that
  field's own intensity, I_j = P_j / A_eff,j (1-D; A_eff = π w² / 2 of the assumed Gaussian mode) or the local intensity
  (spatial models); the densities add;
* the Keldysh (default) or `mpi` rate with the band gap of the crystal from the catalogue (`ionization` quantity of the
  crystal entry; BBO, LBO, KDP, KTP and LiNbO₃), or from `bandGapEv`, `effectiveMass`, `neutralDensityM3` in the object;
  avalanche, recombination and `collisionTimeS` as for glasses;
* on **each field at its own carrier**: the Drude index Δn_j = −ρ / (2 n_j ρ_c(ω_j)) / (1 + 1/(ω_j τ)²) and its
  inverse-bremsstrahlung absorption, and the energy the ionisation takes (U W (ρ_n − ρ_ionised) per volume, shared between
  the fields in proportion to their intensity).

The energy budget of the element then has an entry "ionisation and plasma absorption", and `peakElectronDensityM3` is
reported. The band gaps of the crystals are estimates from the quoted UV transmission edge, marked `unverified` in the
[material catalogue](materials-catalogue.md), and their source is recalled, not checked; replace them with the gap you
trust in the `ionization` object.

```jsonc
{ "id": "shg", "type": "crystal", "crystal": "BBO", "lengthM": 2e-3, "cut": { "thetaDeg": 29.2 },
  "interaction": { "process": "shg", "type": "I" }, "ionization": { "avalanche": false } }
```

### With spatial models: filaments

On a radial or cartesian grid Kerr self-focusing and plasma defocusing balance (intensity clamping). The UPPE coupling
(`"transverse": {"equation": "uppe"}`) gives the correct drive of steep plane-wave components when the beam is tightly
focused; for paraxial beams it agrees with the envelope model.

```jsonc
{ "id": "fil", "type": "bulk", "material": "air", "lengthM": 1.0, "nonlinear": true, "ionization": true,
  "beam": { "kind": "gaussian", "w0M": 1e-3 },
  "transverse": { "model": "radial", "points": 128, "radiusM": 4e-3, "equation": "uppe" } }
```

## Validation

* `IonizationTests`: Dawson and elliptic functions; PPT multiphoton order and tunnel limit; Keldysh order; electron
  density = ρ_n ∫ W dt; blue shift and losses; filament clamping; a prepared plasma of fixed density against the Drude
  filter exp(i (ω/c) Δn(ω) L) in the envelope, carrier-resolved and radial models to 1e-8, with 15 rad of phase; the
  narrow-band limit; the drift energy U_p of the electrons born in the field to 2 %; envelope against carrier-resolved
  with ionisation for a 10 fs, 2 µm pulse (same MPI rate: 1 % in the energy loss, 2 % in the blue shift,
  `Plasma_EnvelopeAgreesWithCarrierResolved_ForA10FsMidInfraredPulse`); the avalanche does not absorb twice
  (`Plasma_Avalanche_DoesNotAbsorbTheFieldASecondTime`); the skip leaves the result unchanged
  (`NegligibleIonisation_IsSkipped_AndTheResultDoesNotChange`); UPPE against the envelope model for paraxial beams, and
  plasma clamping of the focus (`Uppe_AgreesWithEnvelopeForParaxialBeams_AndPlasmaClampsTheFocus`).
* `Chi2IonizationTests`: a d = 0 crystal with the default Keldysh plasma equals a bulk element of the same index, gap,
  mass and density (0.2 %); each field ionises at its own carrier (no plasma from the fundamental at the harmonic's
  rate); density against ρ_n (1 − exp(−∫ σ_K I^K dt)) to 0.5 %; the Drude phase of a prepared plasma to 0.5 %; the
  catalogue band gap and the refusal without data; the energy budget of an SHG with a plasma (1e-6); a radial model
  against the 1-D density (1 %).
* `GpuTests.Plasma_IonisationDrudeAndAvalanche_MatchTheCpu`: the device plasma term against the CPU (1e-16 for MPI,
  4e-6 in the density for PPT and Keldysh).

## Limits

* The envelope model has no negative frequencies: a pulse broadened to zero frequency is cut (see
  [Nonlinear physics](nonlinear.md#limits)).
* A density that changes within a cycle (the carrier-resolved ionisation steps) is resolved in the rate, not in the
  Drude response.
* Spatial elements and carrier-resolved propagation with ionisation run on the CPU.
* In χ(2) crystals: multiphoton steps that mix colours (one photon of each field) are not included, so the plasma is a
  lower bound where both fields are strong; each field's avalanche follows its own intensity; a prepared density
  (`electronDensityM3`) is counted once; the Drude response is applied at each field's carrier, not frequency by
  frequency; the plasma is made by the pulse as it is at each z (no memory between steps), as in bulk elements. A crystal
  with a plasma runs on the CPU (`chi2` device kernels are used only without a plasma).
* The solid ionisation data (band gaps, masses, densities, times) are `unverified` catalogue values;
  `physics.strictMaterials` refuses them ([strict mode](materials.md#strict-mode)).
