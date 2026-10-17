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

and acts as

Δn = -(ρ / 2 n₀ ρ_c) (1 - i/(ω₀ τ_c)) / (1 + 1/(ω₀ τ_c)²),  ρ_c = ε₀ m_e ω₀² / e²,

plus the ionisation loss Im Δn += U W ρ_n / (2 k₀ I). σ = k₀ ω₀ τ_c / (n₀ ρ_c (1 + ω₀² τ_c²)) is the avalanche cross
section (on by default for solids). Defaults: gases τ_c = 350 fs / p[bar], no recombination; solids τ_c = 1 fs,
τ_rec = 150 fs. Every element reports `peakElectronDensityM3` and `peakIonizationFraction`.

In carrier-resolved mode (`physics.fieldMode: "carrier"`) PPT and ADK use the quasi-static rate at the instantaneous
field, so the sub-cycle steps of ionisation are resolved. In envelope mode the plasma term uses the carrier frequency
(the 1/ω² dispersion of the Drude index across a broad spectrum is approximated).

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
electron density = ρ_n ∫ W dt, blue shift and losses, filament clamping).
