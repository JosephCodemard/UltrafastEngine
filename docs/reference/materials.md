# Materials

A material gives an element its refractive index (and so its dispersion), its absorption, its n2 and its delayed (Raman)
response. Elements name a material by `material` (bulk, plate, lens, ...), `medium` (the surrounding medium) or `before` /
`after` (interfaces). The engine looks the name up in the system file's `materials`, then in the built-in catalogue, so a
system file can define or modify any material. Crystals for χ(2) mixing are on [their own page](crystals.md); gases with
their pressure and rotational Raman on [Gases](gases.md).

## Keys

A `materials` entry takes the [material](options.md#material) keys, each model in the kinds of [Models](models.md):
`base` (start from a built-in material), `index`, `indexReference` (`air` | `vacuum`), `n2` (m²/W), `kerrAnisotropy`, `raman`
and `ramanFraction`, `attenuation`, `lidt`, `gas`, `rotationalRaman`, and `crystal` or `gain` for a user
[χ(2) crystal](crystals.md#user-crystals) or [gain medium](gain.md). See [Defining materials](#defining-materials).

## Built-in library

Names are case-insensitive; OpticStudio names are accepted as aliases (`F_SILICA`, `BK7`, `CAF2`, `SAPPHIRE`, ...).
Every number in this table lives in a catalogue file with its source and status: see [the material catalogue](#the-material-catalogue) and the
generated page [Material catalogue](materials-catalogue.md), which is the complete and current list (this table is a summary).
"Index status" is the catalogue status of the dispersion: *verified* (compared with the source), *approximate*, *unverified*.

| name | index model | index status | reference | n2 (m²/W) | delayed response |
|---|---|---|---|---|---|
| `Vacuum` | 1 | definition | | 0 | |
| `Air` | Ciddor 1996 (20 °C, 101325 Pa, 0 % RH, 450 ppm CO2) | verified | absolute | 8.5e-24 per bar (273 K), electronic | rotational Raman of N2 and O2 |
| `FusedSilica` | Sellmeier, Malitson 1965 | verified | air | 2.6e-20 | Blow-Wood, fR 0.18, 12.2 / 32 fs |
| `N-BK7` | Sellmeier, Schott | verified | air | 3.4e-20 | |
| `CaF2` | Sellmeier, Malitson 1963 | verified | air | 1.9e-20 | |
| `Sapphire` | Sellmeier, Malitson & Dodge 1972 (o) | verified | air | 3.0e-20 | |
| `YAG` | Sellmeier, Zelmon 1998 | verified | air | 6.2e-20 | |
| `N-SF11` | Sellmeier, Schott | verified | air | 1.0e-19 | |
| `MgF2` | Sellmeier, Dodge 1984 (o) | verified | air | 9e-21 | |
| `He`, `Ne`, `Ar`, `Kr`, `Xe` | Borzsonyi 2008, scaled with density | verified | absolute | 3.5e-25, 7.5e-25, 1.0e-23, 2.8e-23, 8.0e-23 per bar (273 K) | |
| `N2`, `O2`, `CO2`, `H2` | Borzsonyi 2008 (N2), Zhang et al. 2008 (O2), Bideau-Mehu 1973 (CO2), Peck & Huang 1977 (H2), scaled with density | verified | absolute | 8.0e-24, 1.03e-23, 1.4e-23, 6.9e-24 per bar (273 K), electronic | rotational Raman ([Gases](gases.md)) |
| `Quartz` / `Quartz-e` | Sellmeier, Ghosh 1999 (o / e) | verified | air | 3e-20 | birefringent: [waveplates](components.md#waveplates) |
| `MgF2-e`, `Sapphire-e` | Sellmeier, Dodge 1984 / Malitson & Dodge 1972 (e) | verified | air | 9e-21, 3e-20 | birefringent |
| `Calcite` / `Calcite-e` | Sellmeier, Ghosh 1999 (o / e) | verified | air | **0** (not set) | birefringent |
| `YVO4` / `YVO4-e`, `aBBO` / `aBBO-e` | vendor Sellmeier sets, vendor not recorded | **unverified** | air | **0** (not set) | birefringent |
| `TGG` | one-pole Sellmeier fitted to Schlarb & Sugg 1994 (0.40-1.18 µm, residual 6.6e-4) | approximate | air | **0** (not set) | Faraday rotators; give your crystal's data |
| `Ta2O5`, `TiO2`, `HfO2`, `Nb2O5`, `Al2O3_film`, `SiO2_film` | generic ion-beam-sputtered Cauchy fits (thin films) | approximate | | **0** (not set) | |
| `CS2`, `BaF2` | [below](#cs2-liquid-carbon-disulfide-and-baf2) | verified | | 3.0e-18 (total), 2.0e-20 | orientational (CS2); cubic Kerr anisotropy (BaF2) |

The n2 values of the glasses and gases are typical values near 0.8-1 µm (status `approximate` in the catalogue; the n2 of every χ(2) crystal is `unverified`);
published values spread by 10-30 % (wavelength, measurement method, pulse duration). Override them for quantitative work:
`{"target": "material:FusedSilica", "property": "n2", "value": 2.2e-20}`. The n2 of CS2 and BaF2, the Kerr anisotropy of BaF2,
the CS2 Raman fraction and orientational response are `unverified`. **An n2 of 0 means no value was entered** (Calcite,
YVO4, aBBO, TGG, the thin films): a nonlinear run through these materials has no Kerr effect unless you give `n2`. Thin-film indices are generic ion-beam-sputtered values with no source recorded:
use your coating vendor's data for real designs.

Gases: `{"gas": {"species": "Ar", "pressureBar": 2, "temperatureK": 293}}` scales both the refractivity and n2
with density. Molecular gases (N2, O2, CO2, H2, air) include their rotational Raman response, with the pressure and
temperature of the material ([Gases](gases.md)); `"rotationalRaman": false` switches it off.

For N2 and air, `n2` is the *electronic* (instantaneous) part only (8.0e-24 and 8.5e-24 per bar); the rotational
part is the delayed response. Their sum approaches the long-pulse n2 of the literature (~1.1e-23 per bar for N2) for
pulses much longer than ~100 fs; shorter pulses see less of the rotational part.

### CS2 (liquid carbon disulfide) and BaF2

**CS2** (`"material": "CS2"`, alias `CarbonDisulfide`): index from Kedenburg et al., Opt. Mater. Express 2, 1588 (2012) as published on
refractiveindex.info (CC0), 20 °C: n² = 1 + 1.50387 λ² / (λ² − 0.03049) (λ in µm; fitted 0.5-1.6 µm, extrapolated outside; n(589.3 nm) = 1.6276). The
nonlinearity has an electronic and an orientational part: total n2 = 3.0e-18 m²/W for ps pulses with fR = 0.9 delayed, that is an electronic
3e-19 m²/W (the fs value quoted by Liu et al., Opt. Lett. 38, 1838 (2013), citing Ganeev 2004) and a delayed orientational response
h(t) = (exp(−t / 1.6 ps) − exp(−t / 0.2 ps)) / 1.4 ps that is purely anisotropic (`anisotropicFraction` 1.5, h_a = −h_b / 3: circular light sees a quarter of the
linear orientational index change). **The total n2, the split and the two time constants are typical values, UNVERIFIED here (the literature spread is a factor 2-3):
override `n2`, `ramanFraction` or `raman`.** The delayed response is a circular convolution: the grid window must hold the pulse and about 10 ps.
Use `"raman": {"kind": "orientational", "riseS": ..., "decayS": ..., "fR": ...}` for another liquid (give `fR`, otherwise the response has no weight).

**BaF2**: cubic (m3m) fluoride. Index: Li, J. Phys. Chem. Ref. Data 9, 161 (1980), 293 K, 0.15-15 µm (refractiveindex.info, CC0). n2 = 2.0e-20 m²/W
(for light along [100]) and the Kerr anisotropy `kerrAnisotropy` σ = −1.2 are typical literature values, **UNVERIFIED** (σ quoted from memory of
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
(`APPROXIMATE catalogue value used: FusedSilica.n2 ...`) and `summary.json` lists it under `unverifiedInputs` (quantity, material, status, source, uncertainty). In Python: `result.unverified_inputs` (a list of `ultrafast.UnverifiedInput` dicts; empty when every input is verified).
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

**Gain media, χ(2) crystals and the air index are in the catalogue too.** `gain/` holds the built-in gain media (`Yb:YAG`, `Ti:sapphire`, `Nd:YAG`: lifetime, doping density, zero-phonon line, thermal
and stress constants, emission and absorption bands in cm², each quantity with its source and status); `crystals/` holds the χ(2) crystals (`BBO`, `LBO`, `KTP`, `KDP`, `LiNbO3`, `MgO:LiNbO3`,
`BiBO`, `AgGaSe2`: the three principal-index formulas, the d tensor in pm/V, n2, the validity range, the `opticalClass`, whether the
indices are `temperatureDependent`, and `aliases` such as `ppln`, `ppktp`, `mgln`; see [Crystals](crystals.md#built-in-crystals)). The constants of Ciddor's air-index formulas (dispersion, CO2 and water-vapour terms, saturation vapour pressure,
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

Glass catalogues give n relative to air at 20 °C / 1 atm. The engine converts with the Ciddor air index
(`n_abs = n_rel n_air`). For OpticStudio exports the system's environment (temperature, pressure) is written to
`environment.air`, and air paths get the dispersion of air added (about 20 fs²/m at 1 µm).

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
`~/.ultrafast/materials`; `ultrafast materials import` copies pages there), and under `ULTRAFAST_RII_PATH` (set it to the
`data` folder of a clone of the database to have every page).

**Two variables, one default folder.** The refractiveindex.info pages of the user library follow `ULTRAFAST_HOME` (the
engine's home folder, also used by plugins); the [user catalogue](#the-material-catalogue) of JSON material files follows
`ULTRAFAST_MATERIALS`. Both default to `~/.ultrafast/materials`, so without either variable YAML pages and JSON catalogue
files sit side by side. Setting `ULTRAFAST_HOME` moves only the pages; setting `ULTRAFAST_MATERIALS` moves only the catalogue.

```bash
ultrafast materials list                         # built-in materials, gases, birefringent crystals, bundled pages
ultrafast materials show main/SiO2/Malitson      # n, group index, GVD at a few wavelengths; references
ultrafast materials import Si-Li.yml --name Si   # copy into the user library, print the material definition
```

Python: `uf.materials.available()`, `uf.materials.show(name)`, `uf.materials.import_page(path, name=...)`, `uf.materials.refractiveindex(page, pressure_bar=...)` (the index model dict).

## Validation

| check | reference | tolerance | test |
|---|---|---|---|
| fused silica n_d, N-BK7 n_d, MgF2 n_o | Malitson 1965 (1.45846 at 587.56 nm), Schott data sheet (1.5168), Dodge 1984 (1.3777 at 589.3 nm) | 2e-5, 1e-4, 1e-4 | `CatalogueTests.PublishedIndices_AreReproduced` |
| TGG index | the nine measured points of Schlarb & Sugg 1994 | 1e-3 | `CatalogueTests.Tgg_Dispersion_FollowsTheMeasuredTable` |
| air index at standard conditions | Ciddor 1996 as published on refractiveindex.info | 1e-12 | `CatalogueTests.CiddorAir_AtStandardConditions_EqualsTheDatabaseFormula` |
| every catalogue file has a source, a status and units in its keys; every built-in name resolves | design | exact | `CatalogueTests.CatalogueFiles_HaveSourceStatusAndUnitsInTheirKeys`, `CatalogueTests.EveryBuiltInName_ResolvesToACatalogueEntry_AndBuilds` |
| `unverifiedInputs` lists only what the run used, not what it overrode | design | exact | `CatalogueTests.Run_ListsTheApproximateValuesItUsed_AndNotTheOnesTheUserOverrode`, `CatalogueTests.LinearRun_ReportsOnlyTheDispersionItUsed` |
| strict mode refuses unverified values and accepts the allow-list, overrides and user values marked verified | design | exact | `CatalogueTests.StrictMaterials_*` |
| a user catalogue file replaces one quantity with a notice | design | exact | `CatalogueTests.UserCatalogue_ReplacesAQuantityWithANotice_AndCanAddAMaterial` |

## Limits

* **n2 values are typical, not measured for your sample.** Most are `approximate` (10-30 % spread), several `unverified`,
  and an n2 of 0 means no value was entered. Override them for quantitative nonlinear work, or run with `strictMaterials`.
* **Thin-film and vendor indices have no recorded source** (the films, YVO4, aBBO): use your vendor's data.
* **Indices are at room temperature**, except the e-ray of `LiNbO3` and `MgO:LiNbO3` ([Crystals](crystals.md#keys)) and the
  gases (scaled with density).
* **The Sellmeier sets hold inside their validity range** (in the catalogue file, `validRangeUm`); outside it the formula is
  extrapolated.
