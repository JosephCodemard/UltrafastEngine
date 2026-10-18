# Materials

## Built-in library

Names are case-insensitive; OpticStudio names are accepted as aliases (`F_SILICA`, `BK7`, `CAF2`, `SAPPHIRE`, ...).

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

The nonlinear coefficients are typical values near 0.8-1 um; published values spread by ~10-30 %
(wavelength, measurement method, pulse duration). Override them for quantitative work:
`{"target": "material:FusedSilica", "property": "n2", "value": 2.2e-20}`. Thin-film indices are generic
ion-beam-sputtered values: use your coating vendor's data for real designs.

Gases: `{"gas": {"species": "Ar", "pressureBar": 2, "temperatureK": 293}}` scales both the refractivity and n2
with density. Molecular gases (N2, O2, CO2, H2, air) include their rotational Raman response, with the pressure and
temperature of the material ([Gases](gases.md)); `"rotationalRaman": false` switches it off.

For N2 and air, `n2` is the *electronic* (instantaneous) part only (8.0e-24 and 8.5e-24 per bar); the rotational
part is the delayed response. Their sum approaches the long-pulse n2 of the literature (~1.1e-23 per bar for N2) for
pulses much longer than ~100 fs; shorter pulses see less of the rotational part.

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
