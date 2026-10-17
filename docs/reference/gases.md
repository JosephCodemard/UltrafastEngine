# Gases

Gas materials are built-in names (`He`, `Ne`, `Ar`, `Kr`, `Xe`, `N2`, `O2`, `CO2`, `H2`, `Air`) or a material with a
`gas` block at any pressure and temperature:

```jsonc
"materials": {
  "argon2bar": { "gas": { "species": "Ar", "pressureBar": 2.0, "temperatureK": 293.15 } },
  "n2cell":    { "gas": { "species": "N2", "pressureBar": 1.5 }, "rotationalRaman": { "dephasingTimeS": 60e-12 } },
  "air":       { "gas": { "species": "Air", "pressureBar": 1.01325 } }
}
```

| | index | electronic n2 (1 bar, 273 K) | rotational Raman | ionisation potential |
|---|---|---|---|---|
| He, Ne, Ar, Kr, Xe | Börzsönyi 2008 fits | 3.5e-25 ... 8.0e-23 m²/W | none (atoms) | 24.59 ... 12.13 eV |
| N2 | Börzsönyi 2008 | 8.0e-24 | B = 1.990 cm⁻¹, Δα = 0.70 Å³, even:odd = 2:1 | 15.58 eV |
| O2 | Zhang 2008 | 1.03e-23 | B = 1.438 cm⁻¹, Δα = 1.14 Å³, odd J only | 12.07 eV |
| CO2 | Bideau-Mehu 1973 | 1.4e-23 (±50 %) | B = 0.390 cm⁻¹, Δα = 2.10 Å³, even J only | 13.78 eV |
| H2 | Peck & Huang 1977 | 6.9e-24 (±30 %) | B = 59.3 cm⁻¹, Δα = 0.30 Å³, ortho:para = 3:1 | 15.43 eV |
| Air | Ciddor 1996 | 8.5e-24 | N2 + O2 by mole fraction | N2 and O2 channels |

The index and the electronic n2 scale with density p T_ref / (p_ref T). Values are literature values good to
10-30 %; override any of them in the material (`n2`, `index`, `rotationalRaman`).

## Rotational Raman (molecular orientation)

Linear molecules align in the field: an index change that builds up over ~100 fs, persists, and revives at the
rotational period 1/(2Bc) (8.4 ps for N2). The engine computes it from the rigid-rotor density matrix (first order,
thermal populations with nuclear-spin weights, centrifugal distortion, dephasing time T2 ∝ 1/p):

Δ⟨cos²θ⟩(t) = ∫ χ(t - t') F(t') dt',  χ(τ) = (4/15ħ) Σ_J (J+1)(J+2)/(2J+3) (p_J - p_{J+2}) sin(ω_J τ) e^{-τ/T2},

with F = Δα I / (2 n₀ c ε₀). Light polarised along the field sees Δn_∥ = N Δα Δ⟨cos²θ⟩ / (2 n₀ ε₀), perpendicular
light -1/2 of that. For arbitrary polarisation the response is a tensor,

Δn_ij = R ⊛ [ (3/2) Re(A_i A_j*) - (1/2) δ_ij |A|² ],

so it acts on the polarisation itself (nonlinear ellipse rotation, XPM between x and y) and circular light sees 1/4
of the linear response. Its time integral is the adiabatic orientational n2 (for N2 at 1 atm ≈ 2.7·10⁻²³ m²/W, three
times the electronic part): long pulses (≳ 1 ps) see n2_electronic + n2_rotational, femtosecond pulses mostly the
electronic part plus a delayed, red-shifting response. `ultrafast materials show N2` prints both.

The response is on by default for N2, O2, CO2, H2 and air whenever `physics.raman` is true (switch it off with the
material's `"rotationalRaman": false` or an element's `"raman": false`). It works in 1-D, on spatial grids (scalar and
vector) and in carrier-resolved mode (it follows the cycle-averaged intensity).

!!! note "Change in 2.1.8"
    Before 2.1.8 the N2 and air materials had a single effective n2 (1.1·10⁻²³ / 1.0·10⁻²³ m²/W) and no delayed
    response. They now have the electronic n2 plus the rotational response: SPM of long pulses in N2 or air is
    about 3x stronger than before, which is what measurements show.

## Ionisation

Gases can ionise: see [Ionisation and plasma](ionization.md).

## refractiveindex.info pages

Any gas page of the refractiveindex.info database works as an index (formula 6 pages are scaled to your pressure and
temperature from the page's SPECS): see [Materials](materials.md#refractiveindexinfo-pages).
