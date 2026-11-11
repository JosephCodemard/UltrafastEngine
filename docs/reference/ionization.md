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
section (on by default for solids). Defaults: gases τ_c = 350 fs / p[bar], no recombination; solids τ_c = 1 fs,
τ_rec = 150 fs. `electronDensityM3` gives a plasma that is there before the pulse (a prepared plasma or a discharge); it decays with the recombination time.
Every element reports `peakElectronDensityM3` and `peakIonizationFraction`.

In carrier-resolved mode (`physics.fieldMode: "carrier"`) PPT and ADK use the quasi-static rate at the instantaneous
field, so the sub-cycle steps of ionisation are resolved; the Drude operator is the one above. Against it the envelope model agrees to 1 % in the energy
loss and 2 % in the blue shift for a 10 fs pulse at 2 µm (same MPI rate in both; test below). Limits: no negative frequencies (a pulse
broadened to zero frequency is cut, as stated in the capabilities), and a density that changes within a cycle (the carrier-resolved
ionisation steps) is only resolved in the rate, not in the Drude response.

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
