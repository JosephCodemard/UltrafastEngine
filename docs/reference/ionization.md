# Ionisation and plasma

At intensities above ~10¹³ W/cm² gases and dielectrics ionise. The free electrons lower the index (plasma
defocusing, blue shift), absorb (inverse bremsstrahlung), and every ionisation takes its energy from the pulse. Switch
it on per element or for the whole run:

```jsonc
{ "id": "cell", "type": "bulk", "material": "argon2bar", "lengthM": 0.5, "ionization": true }
"physics": { "ionization": { "model": "ppt", "avalanche": false } }      // every nonlinear bulk element
```

## Rates

| model | for | rate |
|---|---|---|
| `ppt` (gases, default) | atoms and molecules | Perelomov-Popov-Terent'ev, cycle averaged, ADK coefficients C²_{n*l*}, Popov's Coulomb correction; multiphoton (γ ≫ 1, W ∝ I^K) to tunnel (γ ≪ 1) |
| `adk` | gases, γ ≲ 0.5 | Ammosov-Delone-Krainov tunnel rate, cycle averaged; quasi-static in carrier-resolved mode |
| `keldysh` (solids, default) | dielectrics | Keldysh 1965 (form of Couairon & Mysyrowicz 2007): band gap, reduced mass, valence density |
| `mpi` | any | σ_K I^K (`sigmaK`, `photons`) |

γ = ω √(2 m Ip) / (e E) is the Keldysh parameter. Gas data (Ip, l, effective charge) come from the
[gas table](gases.md); air ionises through N2 and O2 with their own densities. Solids: FusedSilica / Quartz (9.0 eV,
m* = 0.635), Sapphire (8.8 eV), CaF2 (10.0 eV), YAG (6.5 eV), MgF2 (10.8 eV), N-BK7 (4.2 eV, rough); give
`bandGapEv`, `effectiveMass` and `neutralDensityM3` for others.

## Plasma

The electron density follows, along the pulse at every point (1-D: the on-axis intensity; spatial grids: each point),

dρ/dt = Σ_s W_s(I) (ρ_s - ρ_s,ionised) + (σ I / U) ρ - ρ/τ_rec - β ρ²,

and acts as a free-electron (Drude) index, frequency by frequency. At the carrier

Δn = -(ρ / 2 n₀ ρ_c) (1 - i/(ω₀ τ_c)) / (1 + 1/(ω₀ τ_c)²),  ρ_c = ε₀ m_e ω₀² / e²,

and across the spectrum of the pulse Δn(ω) = -ρ e² / (2 n₀ ε₀ m_e ω (ω + iν)), ν = 1/τ_c (Couairon & Mysyrowicz, Phys. Rep. 441, 47 (2007)),
the first-order expansion in the density. The propagators apply it as a spectral operator, `dA/dz(ω) += i (ω/c) R(ω) FFT[Δn_ω₀(t) A(t)](ω)`
with R(ω) = ω₀ (ω₀ + iν) / (ω (ω + iν)) (R = 1 at the carrier, 0 at ω ≤ 0), so the blue-shifted leading edge of a broad pulse sees less plasma than the
carrier does, and the electrons born in the field take the drift energy they gain from it (U_p of the field at birth, in the loss budget as part of the
absorption). The operator is the same in the envelope, the carrier-resolved and the spatial models. It treats the density as slowly varying against the
optical cycle (the product ρ(t) A(t) is formed first, then the 1/(ω + iν) factor). `"frequencyResolved": false` gives the earlier carrier-frequency
value; the difference is first order in the bandwidth and for the filament example (40 fs, 800 nm) it is 2 nm in the centroid and 1 % in the energy.

The drift energy U_p assumes that electrons are born with a uniform phase of the optical cycle, which is what the cycle-averaged rate of the envelope and spatial
models and the MPI rate say. In carrier-resolved mode PPT and ADK are born near the crests of the field, where the drift velocity is small, so the carrier-resolved model books
less than U_p per electron; the two models then differ by up to U_p per electron (about 6 eV against 15.8 eV for argon at 800 nm and 10¹⁴ W/cm², tested only with MPI, where they agree).
`absorption: false` removes the inverse-bremsstrahlung term of the Drude index only; the real part still carries the drift energy. With `selfSteepening: false` the other terms use the carrier
value of ω/c, the plasma term always uses ω/c at each frequency (as its current equation does).

The ionisation loss Im Δn += U W ρ_n / (2 k₀ I) is a loss at the carrier, not a Drude term, and stays local in time. σ = k₀ ω₀ τ_c / (n₀ ρ_c (1 + ω₀² τ_c²)) is the avalanche cross
section (on by default for solids). The avalanche takes its energy from the inverse-bremsstrahlung absorption that the Drude imaginary part already books
(σρ I per volume, the same number), so it adds no second loss to the field; before 2.2.1 it did, and a solid with the avalanche on absorbed too much (`Plasma_Avalanche_DoesNotAbsorbTheFieldASecondTime`). With `absorption: false` neither is applied.
A run that gives only some of the solid inputs (`bandGapEv`, `effectiveMass`, `neutralDensityM3`, `collisionTimeS`, `recombinationTimeS`) gets the rest from the catalogue (or, for a material without ionisation data, from the
generic dielectric entry `defaults:solidIonization`); the run notes name the fields the run gave and the source of the others. Defaults: gases τ_c = 350 fs / p[bar], no recombination; solids τ_c = 1 fs,
τ_rec = 150 fs. `electronDensityM3` gives a plasma that is there before the pulse (a prepared plasma or a discharge); it decays with the recombination time.
Every element reports `peakElectronDensityM3` and `peakIonizationFraction`. The `ionization` key works on `plate`, `window` and `ndFilter`
elements too (before 2026-11-17 it was accepted there but not passed to the plate's inside; also `kerrLens`).

**Negligible ionisation is skipped** (`ionization.skipNegligible`, default `true`, 1-D elements). Before an element runs, a conservative bound is
taken from the entry pulse: every rate at 4 × the peak intensity, over 4 × the effective duration E / P_peak, with avalanche growth exp(σ 16 I_pk T / U).
When the bound keeps the Drude phase below 10⁻⁶ rad and the absorbed energy below 10⁻⁹ of the pulse, the plasma term is left out and the note
"ionization skipped as negligible" gives the density bound (`ionizationDensityBoundM3` in the record). The field then differs by less than the
stepper's tolerance (test `IonizationTests.NegligibleIonisation_IsSkipped_AndTheResultDoesNotChange`). A prepared plasma is never skipped. A user's
46-pass multipass cell with ionisation on every air gap ran 20 × slower (all gaps on the CPU) with no change in the result; with the skip, and the GPU
plasma term below, it no longer does.

**On a GPU** (1-D envelope elements) the plasma term runs on the device: the rates are evaluated in parallel from a table of ln W against ln I
(16384 nodes from 10⁸ to 10²² W/m², cubic between nodes, built once per rate model), then one work-item integrates the density along the pulse exactly
as the CPU does, and the frequency-resolved Drude term is applied with one more FFT. With an MPI rate (a straight line in the table) the device equals
the CPU to 10⁻¹⁶; PPT and Keldysh have kinks where a multiphoton channel closes, where the table is off by up to 10⁻² locally and the ionised density of a
pulse by up to 4 × 10⁻⁶, far below the uncertainty of the rate models themselves (test `GpuTests.Plasma_IonisationDrudeAndAvalanche_MatchTheCpu`).
Spatial elements and carrier-resolved propagation with ionisation stay on the CPU.

In carrier-resolved mode (`physics.fieldMode: "carrier"`) PPT and ADK use the quasi-static rate at the instantaneous
field, so the sub-cycle steps of ionisation are resolved; the Drude operator is the one above. Against it the envelope model agrees to 1 % in the energy
loss and 2 % in the blue shift for a 10 fs pulse at 2 µm (same MPI rate in both; test below). Limits: no negative frequencies (a pulse
broadened to zero frequency is cut, as stated in the capabilities), and a density that changes within a cycle (the carrier-resolved
ionisation steps) is only resolved in the rate, not in the Drude response.

## In χ(2) crystals

A `crystal` element takes the same `ionization` key (`true` or an object). It is **off by default and set per element**: the run-level `physics.ionization` is for bulk elements and does not reach crystals.
The coupled-wave solver adds, at every step and for every time row (one row in the 1-D model, one per transverse point in a radial or cartesian model):

* one electron density per field, each with its **own rate model built at that field's carrier** (so the photon order is its own: the 800 nm fundamental in BBO is a
  five-photon process, the 400 nm harmonic a two-photon one) and driven by that field's own intensity, I_j = P_j / A_eff,j (1-D; A_eff = π w² / 2 of the assumed Gaussian mode) or
  the local intensity (spatial models); the densities add;
* the Keldysh (default) or `mpi` rate with the band gap of the crystal from the catalogue (`ionization` quantity of the crystal entry; BBO, LBO, KDP, KTP and LiNbO₃), or
  from `bandGapEv`, `effectiveMass`, `neutralDensityM3` in the object; avalanche, recombination and `collisionTimeS` as for glasses;
* on **each field at its own carrier**: the Drude index Δn_j = −ρ / (2 n_j ρ_c(ω_j)) / (1 + 1/(ω_j τ)²) and its inverse-bremsstrahlung absorption, and the energy the
  ionisation takes (U W (ρ_n − ρ_ionised) per volume, shared between the fields in proportion to their intensity).

The energy budget of the element then has an entry "ionisation and plasma absorption", and `peakElectronDensityM3` is reported. Approximations, stated so that nobody is
surprised: multiphoton steps that mix colours (one photon of each field) are not included, so the plasma is a lower bound where both fields are strong; each field's avalanche
follows its own intensity; a prepared density (`electronDensityM3`) is counted once; the Drude response is applied at each field's carrier, not
frequency by frequency; the plasma is made by the pulse as it is at each z (no memory between steps), as in bulk elements. Not available on the GPU (the element runs on
the CPU; `chi2` device kernels are used only without a plasma). The band gaps of the crystals are estimates from the quoted UV transmission edge and are marked `unverified` in the
catalogue (`docs/reference/materials-catalogue.md`) and their source is recalled, not checked; replace them with the gap you trust in the `ionization` object.

```jsonc
{ "id": "shg", "type": "crystal", "crystal": "BBO", "lengthM": 2e-3, "cut": { "thetaDeg": 29.2 },
  "interaction": { "process": "shg", "type": "I" }, "ionization": { "avalanche": false } }
```

Tests (`Chi2IonizationTests`): a d = 0 crystal with the default Keldysh plasma equals a bulk element of the same index, gap, mass and density (0.2 %); the photon order of each field's own carrier (no plasma from the fundamental at the harmonic's rate); density against ρ_n (1 − exp(−∫ σ_K I^K dt)) to 0.5 %; the energy taken, U ρ A_eff L, to 3 %; the Drude phase of a prepared plasma
to 0.5 %; the catalogue band gap and the refusal without data; the energy budget of an SHG with a plasma; a radial model against the 1-D density.

## With spatial models: filaments

On a radial or cartesian grid Kerr self-focusing and plasma defocusing balance (intensity clamping). The UPPE coupling
(`"transverse": {"equation": "uppe"}`) gives the correct drive of steep plane-wave components when the beam is tightly
focused; for paraxial beams it agrees with the envelope model (test `Uppe_AgreesWithEnvelopeForParaxialBeams_AndPlasmaClampsTheFocus`).

```jsonc
{ "id": "fil", "type": "bulk", "material": "air", "lengthM": 1.0, "nonlinear": true, "ionization": true,
  "beam": { "kind": "gaussian", "w0M": 1e-3 },
  "transverse": { "model": "radial", "points": 128, "radiusM": 4e-3, "equation": "uppe" } }
```

Tests: `IonizationTests` (Dawson and elliptic functions, PPT multiphoton order and tunnel limit, Keldysh order,
electron density = ρ_n ∫ W dt, blue shift and losses, filament clamping; a prepared plasma of fixed density against the Drude filter exp(i (ω/c) Δn(ω) L)
in the envelope, carrier-resolved and radial models to 1e-8, with 15 rad of phase; the narrow-band limit; the drift energy U_p of the electrons born in
the field to 2 %; envelope against carrier-resolved with ionisation for a 10 fs, 2 µm pulse).
