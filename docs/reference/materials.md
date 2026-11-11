# Materials

## Built-in library

Names are case-insensitive; OpticStudio names are accepted as aliases (`F_SILICA`, `BK7`, `CAF2`, `SAPPHIRE`, ...).
Every number in this table lives in a catalogue file with its source and status: see [the material catalogue](#the-material-catalogue) and the
generated page [Material catalogue](materials-catalogue.md), which is the complete and current list (this table is a summary).

| name | index model | reference | n2 (m^2/W) | delayed response |
|---|---|---|---|---|
| `Vacuum` | 1 | | 0 | |
| `Air` | Ciddor 1996 (20 C, 101325 Pa, 0 % RH, 450 ppm) | absolute | 1.0e-23 x density | |
| `FusedSilica` | Sellmeier, Malitson 1965 | air | 2.6e-20 | Blow-Wood, fR 0.18, 12.2/32 fs |
| `N-BK7` | Sellmeier, Schott | air | 3.4e-20 | |
| `CaF2` | Sellmeier, Malitson 1963 | air | 1.9e-20 | |
| `Sapphire` | Sellmeier, Malitson & Dodge 1972 (o) | air | 3.0e-20 | |
| `YAG` | Sellmeier, Zelmon 1998 | air | 6.2e-20 | |
| `N-SF11` | Sellmeier, Schott | air | 1.0e-19 | |
| `MgF2` | Sellmeier, Dodge 1984 (o) | air | 0.9e-20 | |
| `Air` (above) | | | 8.5e-24 per bar (273 K), electronic | rotational Raman of N2 and O2 |
| `He`, `Ne`, `Ar`, `Kr`, `Xe` | Borzsonyi 2008, scaled with density | absolute | 3.5e-25, 7.5e-25, 1.0e-23, 2.8e-23, 8.0e-23 per bar (273 K) | |
| `N2`, `O2`, `CO2`, `H2` | Borzsonyi / Peck / Bideau-Mehu, scaled with density | absolute | 8.0e-24, 1.03e-23, 1.4e-23, 6.9e-24 per bar (273 K), electronic | rotational Raman ([Gases](gases.md)) |
| `Quartz` / `Quartz-e`, `MgF2-e`, `Sapphire-e`, `Calcite` / `Calcite-e`, `YVO4` / `YVO4-e`, `aBBO` / `aBBO-e` | Sellmeier (ordinary / extraordinary) | air | typical | birefringent: [waveplates](components.md#waveplates) |
| `TGG` | approximate two-point fit | air | none | Faraday rotators; give your crystal's data |
| `Ta2O5`, `TiO2`, `HfO2`, `Nb2O5`, `Al2O3_film`, `SiO2_film` | Cauchy fits (thin films) | | | |

The nonlinear coefficients are typical values near 0.8-1 um (status `approximate` in the catalogue); published values spread by ~10-30 %
(wavelength, measurement method, pulse duration). Override them for quantitative work:
`{"target": "material:FusedSilica", "property": "n2", "value": 2.2e-20}`. Thin-film indices are generic
ion-beam-sputtered values: use your coating vendor's data for real designs.

Gases: `{"gas": {"species": "Ar", "pressureBar": 2, "temperatureK": 293}}` scales both the refractivity and n2
with density. Molecular gases (N2, O2, CO2, H2, air) include their rotational Raman response, with the pressure and
temperature of the material ([Gases](gases.md)); `"rotationalRaman": false` switches it off.

For N2 and air, `n2` is the *electronic* (instantaneous) part only (8.0e-24 and 8.5e-24 per bar); the rotational
part is the delayed response. Their sum approaches the long-pulse n2 of the literature (~1.1e-23 per bar for N2) for
pulses much longer than ~100 fs; shorter pulses see less of the rotational part.

### CS2 (liquid carbon disulfide) and BaF2

**CS2** (`"material": "CS2"`, alias `CarbonDisulfide`): index from Kedenburg et al., Opt. Mater. Express 2, 1588 (2012) as published on
refractiveindex.info (CC0), 20 C: `n^2 = 1 + 1.50387 l^2 / (l^2 - 0.03049)` (l in um; fitted 0.5 - 1.6 um, extrapolated outside; n(589.3 nm) = 1.6276). The
nonlinearity has an electronic and an orientational part: total `n2 = 3.0e-18 m^2/W` for ps pulses with `fR = 0.9` delayed, i.e. an electronic
`3e-19 m^2/W` (the fs value quoted by Liu et al., Opt. Lett. 38, 1838 (2013), citing Ganeev 2004) and a delayed orientational response
`h(t) = (exp(-t/1.6 ps) - exp(-t/0.2 ps)) / 1.4 ps` that is purely anisotropic (`anisotropicFraction` 1.5, `h_a = -h_b/3`: circular light sees a quarter of the
linear orientational index change). **The total n2, the split and the two time constants are typical values, UNVERIFIED here (the literature spread is a factor 2-3):
override `n2`, `ramanFraction` or `raman`.** The delayed response is a circular convolution: the grid window must hold the pulse and about 10 ps.
Use `"raman": {"kind": "orientational", "riseS": ..., "decayS": ..., "fR": ...}` for another liquid (give `fR`, otherwise the response has no weight).

**BaF2**: cubic (m3m) fluoride. Index: Li, J. Phys. Chem. Ref. Data 9, 161 (1980), 293 K, 0.15 - 15 um (refractiveindex.info, CC0). `n2 = 2.0e-20 m^2/W`
(for light along [100]) and the Kerr anisotropy `kerrAnisotropy` sigma = -1.2 are typical literature values, **UNVERIFIED** (sigma quoted from memory of
Minkovski et al., Opt. Lett. 29, 2237 (2004) and Jullien et al., J. Opt. Soc. Am. B 22, 2635 (2005)). See [cubic anisotropy](nonlinear.md#cubic-kerr-anisotropy-xpw).

## The material catalogue

Every number the library hands to the engine (dispersion coefficients, n2, Raman time constants and fractions, gas constants, ionisation data) is stored in a **catalogue
file**, not in C#. One JSON file per material, gas or Raman model, in `src/UltrafastEngine.Core/Data/materials/{solids,gases,raman}/` (embedded in the library, so the CLI, the
Python package and the C API all see the same numbers). Each *quantity* of an entry (`index`, `n2`, `rotation`, `ionization`, ...) carries:

| key | meaning |
|---|---|
| the numbers | with the unit in the key name (`valueM2PerW`, `tau1S`, `ionizationEv`, `rotationalConstantBPerCm`); coefficients keep the units of their source (resonance wavelengths in um) |
| `source` | the paper, table or database page the number comes from |
| `status` | `verified` (read from the source and compared), `approximate` (typical, rounded or fitted), `unverified` (not checked) |
| `relativeUncertainty` / `uncertainty` | the literature spread (for example a factor 2-3 for the n2 of CS2) |
| `conditions` | wavelength, temperature, pressure, pulse duration the value applies to |
| `verification` | for a verified dispersion set: the comparison that was made |

The complete list, with a "not verified yet" table at the end, is [Material catalogue](materials-catalogue.md) (generated by `ultrafast reference materials`).

**Runs say what they used.** When a nonlinear element uses an `approximate` or `unverified` catalogue value, the run notes say so
(`APPROXIMATE catalogue value used: FusedSilica.n2 ...`) and `summary.json` lists it under `unverifiedInputs` (quantity, material, status, source, uncertainty).
Only values the run really used are listed: n2 and the Raman response only for a nonlinear element, the dispersion for every element that uses the material's index, the
ionisation data only when `ionization` is on, and nothing the system file or the run spec overrides. A linear run with an unverified index lists the index.

**Your own catalogue.** A JSON file in `~/.ultrafast/materials` (or in the folder named by the environment variable `ULTRAFAST_MATERIALS`; an empty value switches the
user catalogue off) adds a material or replaces single quantities of a built-in one. The file needs the `id` and only the quantities that change; each quantity needs a `source`
and a `status` like the built-in ones. A run that uses a replaced value says `user catalogue value used` in its notes, and `Catalogue.Notices` names every replacement.

```jsonc
// ~/.ultrafast/materials/FusedSilica.json: use my own Z-scan value of n2, keep everything else
{ "id": "FusedSilica",
  "quantities": { "n2": { "valueM2PerW": 2.19e-20, "source": "own Z-scan, 800 nm, 2026-03", "status": "verified" } } }
```

For a single run, an [override](models.md) in the run spec is simpler: it replaces the value for that run only and removes the notice.

**Gain media, chi(2) crystals and the air index are in the catalogue too.** `gain/` holds the built-in gain media (`Yb:YAG`, `Ti:sapphire`, `Nd:YAG`: lifetime, doping density, zero-phonon line, thermal
and stress constants, emission and absorption bands in cm², each quantity with its source and status); `crystals/` holds the chi(2) crystals (`BBO`, `LBO`, `KTP`, `KDP`, `LiNbO3`, `MgO:LiNbO3`,
`BiBO`, `AgGaSe2`: the three principal-index formulas, the d tensor in pm/V, n2, the validity range). The constants of Ciddor's air-index formulas (dispersion, CO2 and water-vapour terms, saturation vapour pressure,
compressibility) are quantities of the `Air` entry. A gain element or crystal element that uses an `approximate` / `unverified` value of its medium reports it like a material does
(`Yb:YAG.emission`, `BBO.d`); thermal data are reported only with a thermal lens, stress constants only with stress birefringence, and a `deffPmPerV`, `dopingDensityM3` or a user-defined
band replaces the catalogue's quantity. In the system file these values keep working as before (`"medium": {"base": "Yb:YAG", "lifetimeS": ...}`, `"crystal": {"base": "BBO", ...}`).

### Strict mode

`"physics": {"strictMaterials": true}` in the run spec (or the environment variable `ULTRAFAST_STRICT_MATERIALS=1`, for CI and for results that are going into a paper) makes the compile **fail** when the run uses any
catalogue value that is not `verified`. The error lists each one (material, quantity, status, source). Ways out: override the value for the run (an [override](models.md) or a property of the
material), give a verified value in [your own catalogue file](#the-material-catalogue), or switch the mode off. Values that are not used are not counted (n2 and Raman in a linear run, thermal data
without a thermal lens), and a value from your own file counts as verified only if you marked it `verified`.

To accept one named value and still refuse the others, list it by its catalogue path (as the error and `unverifiedInputs` print it; an entry name accepts all its quantities):

```jsonc
{ "physics": { "strictMaterials": true } }   // run.json: refuse approximate / unverified catalogue values
{ "physics": { "strictMaterials": true, "strictMaterialsAllow": ["FusedSilica.n2", "CS2"] } }   // accept those, refuse the rest
```

The generic values used where a run gives none (the thermal data of a user-defined gain medium, the ionisation inputs of a solid without data) are catalogue entries too (`defaults:gainMedium`, `defaults:solidIonization`,
status `unverified`): a run that uses one lists it like any other value, and strict mode refuses it unless it is overridden or allowed.

## Defining materials

Any material can be defined in the system file (or overridden from the run spec) with the model kinds in
[Models](models.md): `index`, `indexReference` (`air` | `vacuum`), `n2`, `raman` (+ `fR`/`ramanFraction`),
`attenuation`, `lidt`, `base` (start from another material). Materials exported from OpticStudio carry
OpticStudio's own index at the sampled wavelengths (`table`, relative to air) and, when the glass is a known
built-in, its n2/Raman via `base`.

## Absolute vs relative index

Glass catalogues give n relative to air at 20 C / 1 atm. The engine converts with the Ciddor air index
(`n_abs = n_rel n_air`). For OpticStudio exports the system's environment (temperature, pressure) is written to
`environment.air`, and air paths get the dispersion of air added (~20 fs^2/m at 1 um).

## refractiveindex.info pages

Any page of the [refractiveindex.info database](https://refractiveindex.info) (public domain) can be an index (and,
when the page has k data, an attenuation) model. The YAML page format is read directly: the nine dispersion formulas,
tabulated n, k and nk, and the SPECS (temperature, pressure; gases are scaled with density).

```jsonc
"materials": {
  "Calcite-o": { "index": { "kind": "refractiveindex", "page": "main/CaCO3/Ghosh-o" } },
  "Silicon":   { "index": { "kind": "refractiveindex", "page": "C:/data/Si-Li.yml" },
                 "attenuation": { "kind": "refractiveindex", "page": "C:/data/Si-Li.yml" } },
  "Ar-5bar":   { "index": { "kind": "refractiveindex", "page": "main/Ar/Peck-15C", "pressureBar": 5, "temperatureK": 293 } }
}
```

A page is looked up, in order, as: a file (relative to the system file), a bundled page (about 20 common materials,
`ultrafast materials list` shows them), a page in your user library (`$ULTRAFAST_HOME/materials`, default
`~/.ultrafast/materials`), and under `ULTRAFAST_RII_PATH` (set it to the `data` folder of a clone of the database
to have every page).

```bash
ultrafast materials list                         # built-in materials, gases, birefringent crystals, bundled pages
ultrafast materials show main/SiO2/Malitson      # n, group index, GVD at a few wavelengths; references
ultrafast materials import Si-Li.yml --name Si   # copy into the user library, print the material definition
```

Python: `uf.materials.available()`, `uf.materials.show(name)`, `uf.materials.import_page(path, name=...)`, `uf.materials.refractiveindex(page, pressure_bar=...)` (the index model dict).
