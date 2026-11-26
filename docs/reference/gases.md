# Gases

Gas materials are built-in names (`He`, `Ne`, `Ar`, `Kr`, `Xe`, `N2`, `O2`, `CO2`, `H2`, `Air`) or a material with a
`gas` block at any pressure and temperature. They carry an index, an electronic n2, the rotational Raman response of
molecules, and ionisation data. A `gap` is air by default; this page also describes how the laboratory air reaches it.

```jsonc
"materials": {
  "argon2bar": { "gas": { "species": "Ar", "pressureBar": 2.0, "temperatureK": 293.15 } },
  "n2cell":    { "gas": { "species": "N2", "pressureBar": 1.5 }, "rotationalRaman": { "dephasingTimeS": 60e-12 } },
  "air":       { "gas": { "species": "Air", "pressureBar": 1.01325 } }
}
```

## Keys

The keys that matter; every key with its default is in the [option reference](options.md).

| key | where | meaning |
|---|---|---|
| `gas` | [`material`](options.md#material) ([`gas`](options.md#gas)) | `species`, `pressureBar`, `temperatureK` |
| `rotationalRaman` | `material` | `false`, or `{species, pressureBar, temperatureK, dephasingTimeS}`; on by default for N2, O2, CO2, H2 and air |
| `n2`, `index` | `material` | override the catalogue's electronic n2 or index |
| `environment.air` | system file ([`air`](options.md#air)) | `temperatureC`, `pressurePa`, `humidity` (0 to 1), `co2ppm`, `applyToGaps`, or an index model with `kind` |
| `air` | `gap` | this gap's air, overlaid on `environment.air` |

## Model

### Catalogue

| | index | electronic n2 (1 bar, 273 K) | rotational Raman | ionisation potential |
|---|---|---|---|---|
| He, Ne, Ar, Kr, Xe | Börzsönyi 2008 fits | 3.5e-25 ... 8.0e-23 m²/W | none (atoms) | 24.59 ... 12.13 eV |
| N2 | Börzsönyi 2008 | 8.0e-24 | B = 1.990 cm⁻¹, Δα = 0.70 Å³, even:odd = 2:1, T2 = 100 ps | 15.58 eV |
| O2 | Zhang 2008 | 1.03e-23 | B = 1.438 cm⁻¹, Δα = 1.14 Å³, odd J only, T2 = 80 ps | 12.07 eV |
| CO2 | Bideau-Mehu 1973 | 1.4e-23 (±50 %) | B = 0.390 cm⁻¹, Δα = 2.10 Å³, even J only, T2 = 50 ps | 13.78 eV |
| H2 | Peck & Huang 1977 | 6.9e-24 (±30 %) | B = 59.3 cm⁻¹, Δα = 0.30 Å³, ortho:para = 3:1, T2 = 300 ps | 15.43 eV |
| Air | Ciddor 1996 | 8.5e-24 | N2 + O2 by mole fraction | N2 and O2 channels |

T2 is the dephasing time at 1 bar and 293.15 K. The index and the electronic n2 scale with the density,
p T_ref / (p_ref T). The values are literature values good to 10-30 %; override any of them in the material (`n2`,
`index`, `rotationalRaman`). Their sources and status are in the [material catalogue](materials-catalogue.md).

### Rotational Raman (molecular orientation)

Linear molecules align in the field: an index change that builds up over about 100 fs, persists, and revives at the
rotational period 1/(2Bc) (8.4 ps for N2). The engine computes it from the rigid-rotor density matrix to first order,
with thermal populations, nuclear-spin weights and centrifugal distortion:

Δ⟨cos²θ⟩(t) = ∫ χ(t − t') F(t') dt',  χ(τ) = (4/15ħ) Σ_J (J+1)(J+2)/(2J+3) (p_J − p_{J+2}) sin(ω_J τ) e^{−τ/T2},

with F = Δα I / (2 n₀ c ε₀) and the dephasing time T2 = T2(1 bar) / p[bar] × √(T / 293.15 K) (`dephasingTimeS`
replaces it). Light polarised along the field sees Δn_∥ = N Δα Δ⟨cos²θ⟩ / (2 n₀ ε₀), perpendicular light −1/2 of that.
For arbitrary polarisation the response is a tensor,

Δn_ij = R ⊛ [ (3/2) Re(A_i A_j*) − (1/2) δ_ij |A|² ],

so it acts on the polarisation itself (nonlinear ellipse rotation, XPM between x and y) and circular light sees 1/4 of
the linear response. Its time integral is the adiabatic orientational n2: for N2 at 1 bar and 293 K the
high-temperature formula N Δα² / (4 c ε0² 15 kT) gives 2.6e-23 m²/W, 3.5 × the electronic n2 at that density. Long
pulses (≳ 1 ps) see n2_electronic + n2_rotational; femtosecond pulses see mostly the electronic part plus a delayed,
red-shifting response. `ultrafast materials show N2` prints both.

The response is on by default for N2, O2, CO2, H2 and air whenever `physics.raman` is true (switch it off with the
material's `"rotationalRaman": false` or an element's `"raman": false`). It works in 1-D, on spatial grids (scalar and
vector) and in carrier-resolved mode (it follows the cycle-averaged intensity).

**Time window.** The kernel R(t) is applied as a zero-padded, linear convolution of the intensity on the window (exact
bin integrals of every line). Light anywhere in the window sees the whole history of the index change, and nothing
wraps around, so the grid planner adds no room for the revivals or for T2 (hundreds of ps at 1 bar). A revival acts
on light that is there at that time: a probe or a satellite at 8.4 ps behind the pulse, which the window holds
already. The window of a short pulse in air or N2 is therefore the window it would have without the response.

### Air in the gaps

A `gap` is air by default, and "Air" is the Ciddor index at 20 °C, 101 325 Pa, dry, 450 ppm CO2. The system's
`environment.air` (temperature, pressure, relative humidity 0 to 1, CO2) by default supplies only the air used for data
quoted *relative to air* (OpticStudio exports). To make it the air of **every gap** (and of every other use of the name
"Air"), switch it on:

```jsonc
"environment": { "air": { "temperatureC": 24, "pressurePa": 100500, "humidity": 0.45, "applyToGaps": true } }
```

It is off by default; a note in the result says when it is on. A gap can override it:
`{"id": "g", "type": "gap", "lengthM": 2.0, "air": {"humidity": 0.2}}` (the gap's keys overlay the environment's). `air`
may also name another index model with `"kind"`: the [hitran plugin](../plugins/hitran.md) adds the absorption lines and
the dispersion of H2O, CO2 and O2 (a model that provides both an index and an absorption is used for both). Python:
`build.air(...)`, `build.environment(air=...)`, `build.gap(..., air=...)`.

### Ionisation

Gases can ionise: see [Ionisation and plasma](ionization.md).

### refractiveindex.info pages

Any gas page of the refractiveindex.info database works as an index (formula 6 pages are scaled to your pressure and
temperature from the page's SPECS): see [Materials](materials.md#refractiveindexinfo-pages).

## Validation

`GasTests`:

* the adiabatic orientational n2 of N2 and O2 against the isolated-molecule high-temperature limit
  N Δα² / (4 c ε0² 15 kT), 3 %; for N2 the rotational n2 exceeds 2 × the electronic one
  (`RotationalRaman_AdiabaticLimitMatchesIsolatedMoleculeTheory`);
* revivals at the rotational period 1/(2Bc), 5 % (`RotationalRaman_RevivalsAtTheRotationalPeriod`);
* a weak probe 8.38 ps behind a 100 fs pump in 1 m of N2: its phase across the full revival equals k0 L (R ⊛ I)(t) of
  the pump to 2 % of the 3 rad swing; a window that ends before the revival (4 ps) gives the same centroid shift and
  rms broadening as one four times larger, 1e-6
  (`RotationalRaman_RevivalActsOnAProbeAtTheRevival_AndTheWindowNeedsNoRoomForIt`);
* long-pulse SPM uses the total n2, and circular light sees 1/4 of the linear rotational n2 (Hellwarth tensor), 3 % and
  5 % (`RotationalRaman_LongPulseSpmUsesTheTotalN2_AndCircularSeesAQuarter`);
* `n2Scale` scales the rotational response too, 1e-6 (`N2Scale_In1D_ScalesTheRotationalRamanToo`);
* published refractivities of the gases, 2 % (`Gases_IndicesOfNewSpecies`); refractiveindex.info formulas and tables
  (`RefractiveIndexInfo_*`).

## Limits

* The rotational response is first order in the field (no saturation of the alignment, no rotational heating), with one
  dephasing time per species.
* The electronic n2 values are good to 10-30 % (CO2 ±50 %, H2 ±30 %).
* The built-in air has no absorption lines; use the [hitran plugin](../plugins/hitran.md) for them.
