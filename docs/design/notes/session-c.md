# Session C (WP3): the material catalogue

Two parts, both 2026-11-11: part 1 built the catalogue (materials, gases, Raman models); part 2 added the gain media, the chi(2) crystals, the Ciddor constants and the strict mode. **Summary of what is done and what is open: [Part 2](#part-2-same-day-gain-media-crystals-ciddor-strict-mode-yvo4) and [Still open](#still-open).**

Done 2026-11-11 on `dev/2026-11`, from [plan-next.md](../plan-next.md) section 3, row C. Plan, then an advisor pass on the design before any edit, then three steps
that were kept apart on purpose: (1) pin every value, (2) move the tables (a pure refactor), (3) change values. Reference page: [Material catalogue](../../reference/materials-catalogue.md).

## What was built

* **Catalogue files.** `src/UltrafastEngine.Core/Data/materials/{solids,gases,raman}/<id>.json`, 41 entries, 115 quantities, embedded in the library (the CLI, the Python
  package and the NativeAOT library all see the same numbers; a loose folder would not ship). One quantity = one source: `index`, `n2`, `raman`, `ionization`, `rotation`, ...
  Each carries its numbers (unit in the key name; coefficients keep the units of their source), `source`, `status`, `relativeUncertainty` or `uncertainty`, `conditions`, and for
  verified dispersion sets the recorded `verification`. Aliases and the listing order are in the files.
* **Loader.** `Materials/Catalogue.cs` (`Catalogue`, `CatalogueEntry`, `CatalogueQuantity`, `CatalogueStatus`), `CatalogueModels.cs` (index and Raman objects from quantities),
  `CatalogueReference.cs` (the generated page). `MaterialLibrary`, `GasData`, `GasIndex`, `GasFormulaIndex`, `SolidIonizationData`, `LinAgrawalRamanResponse`,
  `HollenbeckCantrellRamanResponse` and the registry's Raman defaults read from it; the old public members (`GasN2PerBar`, `GasData.Table`, `CS2N2TotalPerW`, `BaF2Sigma`, ...) remain as
  properties over the catalogue. The arithmetic of the old tables is reproduced exactly (squared resonance wavelengths, `A - S/C` for the vendor form, the density factors).
* **Pure refactor, shown.** `CatalogueSnapshotTests` pins 2963 values (n at 24 wavelengths for every built-in and every gas at two states, n2, f_R, Kerr sigma, h(t) at 80 times, anisotropic parts,
  every rotational-Raman line, ionisation data, gas data, the Borzsonyi coefficients). Written from the old tables first (commit `5ab4014`); the new loader reproduced all of them to 1e-15 (bitwise).
  The only values that moved afterwards are the 24 TGG indices, in a separate commit.
* **Runs say what they used.** `Material.Used` keeps the catalogue quantities a material takes its numbers from; an override (`MaterialLibrary.Apply`) removes the quantity. The compiler calls
  `ProvenanceLog.UseCatalogue` where an element really uses a property. Result: a note per `approximate` / `unverified` (or user-file) value, once per run; `provenance.json` entries get
  `catalogueStatus` / `catalogueSource`; `summary.json` gets `unverifiedInputs`. Not reported: verified values, values the run spec or system overrides, n2 / Raman / Kerr / ionisation in a linear run.
* **User catalogues.** `~/.ultrafast/materials/*.json` or `$ULTRAFAST_MATERIALS` (empty = off; the test runner sets it empty so tests never read the owner's folder). A file names the `id` and only
  the quantities that change; a new `id` adds a material. A notice names each replaced quantity and the run says `user catalogue value used`.
* **Generated page + lint.** `ultrafast reference materials -o docs/reference/materials-catalogue.md` (sixth generated file: `/regen`, the protect hook, the up-to-date test, `CLAUDE.md` know it) ends with a
  "not verified yet" table, the owner's to-do list. A lint test fails when a quantity has no source, a bad status, no uncertainty although it is not verified, or a number without a unit in its key.

## Verification of values (what was read, what was not)

Verified means: read in this session from the source page and compared by a script (the comparison is stored in `verification`).

| value | result |
|---|---|
| Sellmeier sets of fused silica, N-BK7, N-SF11, CaF2, sapphire o/e, YAG, MgF2 o/e, quartz o/e, calcite o/e, BaF2 (Li 1980), CS2 (Kedenburg 2012) | **verified**: equal to the refractiveindex.info pages (bundled or fetched) to 1e-16. The check also caught a slip of my own generator (YAG poles squared twice) before it reached a file. |
| Borzsonyi coefficients He, Ne, Ar, Kr, Xe, N2; O2 (Zhang), CO2 (Bideau-Mehu), H2 (Peck) | **verified** the same way |
| **TGG index** | **finding, corrected.** The old two-point fit (n = 1.954 at 1064 nm) was up to **0.0106 (0.5 %)** from the measured table of Schlarb and Sugg 1994 (nine points, 0.40-1.18 um, refractiveindex.info `main/Tb3Ga5O12/Schlarb`). Replaced by a one-pole fit to those points (largest deviation 6.6e-4, `approximate`, valid 0.40-1.18 um only); test against the nine points. Only the `Components` example uses TGG (a Faraday rotation, no index): unchanged. |
| **YVO4 vendor Sellmeier** (o and e) | **finding; decision (owner, 2026-11-11): the vendor sets stay.** Differs from Shi et al. 2001 (20 C, `main/YVO4/Shi-o-20C`, `Shi-e-20C`) by -5.4e-4 / -9.6e-4 at 633 nm and -5.9e-4 / -9.2e-4 at 1064 nm; the birefringence differs by ~0.16 %, which matters for waveplate thickness. The Shi set has the same functional form, so adopting it would be a four-number edit per axis; the difference is recorded in the catalogue. |
| CS2 n2, the 90/10 split, 1.6 / 0.2 ps | **not verified.** Searches gave only qualitative statements (orientation dominates, ~2 ps decay); the CREOL PDFs found (publication 1811) are scanned images that cannot be rendered here (`pdftoppm` is missing). Marked `unverified` with the uncertainty (factor 2-3) and the pointer. |
| BaF2 n2 and sigma = -1.2 | **not verified.** The Z-scan anisotropy paper (CREOL publication 1820) is a scan; the search snippet gives its form chi(theta) = chi [1 + 2u (sin^4 theta - sin^2 theta)] and that it tabulates n2 along [100], [010], [110] at 532 / 1064 nm. How u relates to the engine's sigma is still to be written down. `unverified`, noted in the file. |
| Rotational Raman dephasing times T2At1Bar (N2 100 ps, O2 80, CO2 50, H2 300) | **not verified.** Searches did not give 1 atm values (self-broadened N2 / O2 rotational CARS, 1-20 atm, 2011, is the place to look). `unverified`, uncertainty 50 %. |
| Gas n2, glass n2, Raman fractions and time constants, polarisability anisotropies, ionisation data | `approximate` (typical values, stated spread) or `unverified`; none checked against a paper. |

Counts on the page after part 1: **115 quantities: 28 verified, 48 approximate, 37 unverified** (after part 2: 183 quantities, 43 verified, 70 approximate, 68 unverified). The papers `liu-2013`, `shimizu-1967` and `kaur-2024` do not need a re-run: no CS2, BaF2 or Raman number changed.

## Checks run

C# suite `--slow` 571 pass, 9 skipped (562 before; +9 tests); net10.0 and net48 build 0 errors; Python 223 pass (hosted library); `example_check` 0 problems; `mkdocs build --strict` 0 warnings.
A trap met on the way: with a stale `python/src/ultrafast/bin/native` library, `example_check` shows 20 false failures; set `ULTRAFAST_LIBRARY` to the hosted DLL (as in the verification-environment note).

## Part 2 (same day): gain media, crystals, Ciddor, strict mode, YVO4

Requested after part 1: finish the entries that were listed as "not in the catalogue yet", build the strict mode, keep the YVO4 vendor sets. Same discipline: the values were pinned first, the move was a pure refactor.

### Done

* **Pinned first, then moved (pure refactor).** `CatalogueSnapshotTests` gained 449 pinned values (now 3412): cross sections, saturation fluences and every scalar of the three gain media at 293.15 K and 350 K; the three principal indices at 8 wavelengths, the d tensor, n2, validity range, class and notes of all 8 crystals at 20 C and 80 C; the Ciddor index and water-vapour mole fraction in three states (20 C dry, 15 C 50 % RH, 25 C 90 kPa 40 % RH). Written from the old code, reproduced to 1e-15 after the move.
* **Gain media** (`Data/materials/gain/`: `Yb:YAG`, `Ti:sapphire`, `Nd:YAG`). Quantities `doping` (+ host), `lifetime`, `zeroLine`, `thermal`, `stress`, `emission`, `absorption` (bands in cm², Gaussian or asymmetric Gaussian). `GainMedium.Get` builds from them; `GainMedium.Used` tracks them; a user medium built with `"base"` keeps the quantities it does not change. `GainCompiler` reports the used non-verified quantities (thermal only with a thermal lens, stress only with `stress`; a given `dopingDensityM3` replaces `doping`).
* **chi(2) crystals** (`Data/materials/crystals/`: BBO, LBO, KTP, KDP, LiNbO3, MgO:LiNbO3, BiBO, AgGaSe2). Quantities `indexX/Y/Z` (refractiveindex.info formula 2 or 4 coefficients, or the temperature-dependent Jundt form), `d` (point group, tensor key, pm/V), `n2`, `validity`; entry-level display names (`PPKTP`, `PPLN (congruent)` for those aliases), class, references. `CrystalLibrary.Get` builds from them; `CrystalElement.Compile` reports. **Verified against the database pages (coefficient lists equal): BBO o and e, KTP alpha / beta / gamma, KDP o and e, LiNbO3 o (Zelmon), BiBO alpha / beta / gamma, AgGaSe2 o and e (15 index quantities).** Not verifiable: LBO (Kato 1994, no page; the database has the Chen fit), the Jundt LiNbO3 e-ray and the Gayer MgO:LiNbO3 e-ray (not on the database, and the b4 temperature coefficient is a known open point), all d tensors and n2 values (from papers I did not re-read).
* **Ciddor constants.** The constants of `CiddorAirIndex` are quantities of the `Air` entry (`standardAirDispersion`, `co2Dependence`, `waterVaporDispersion`, `thermodynamics`, `saturationVapourPressure`, `enhancementFactor`, `compressibility`, `referenceStates`); the standard environment (20 C, 101325 Pa, dry, 450 ppm) is the `index` quantity. **`standardAirDispersion` is verified:** k1/1e8, k0, k3/1e8, k2 equal the coefficients of the database page `other/air/Ciddor`, and a new test shows the engine at 15 C, dry, 450 ppm equals that formula to 1e-12 at seven wavelengths. The CO2, water-vapour, vapour-pressure, enhancement and compressibility constants are transcriptions of the paper / CIPM-2007 that I did not re-read: `unverified`. **A correction of part 1:** the `Air` index quantity had been marked `verified` only because a database page exists; it is now verified for the dispersion constants only, by the test above.
* **Strict mode.** `physics.strictMaterials` (run spec; declared in `BuiltInOptions.cs`, so it is in the schemas, `options.md` and the Python types) or `ULTRAFAST_STRICT_MATERIALS=1`. At the end of `Compiler.Compile` every non-verified entry of `ProvenanceLog.UnverifiedInputs` makes one `ModelException` that lists material, quantity, status and source and says how to get out. It counts only what the run used and did not override; a value in the user's own catalogue counts if that file marks it `verified`. Tests: refused with the list, overrides + Raman off accepted, linear run on a verified index accepted, unverified YVO4 index refused, the environment variable, a user-file value marked verified accepted.
* **YVO4: the vendor Sellmeier sets stay** (owner decision, 2026-11-11). The catalogue records the difference from Shi et al. 2001 (5e-4 ordinary, 9e-4 extraordinary) in the `uncertainty` of the two index quantities, and states the decision.
* **Docs.** `materials.md` (gain / crystal / Ciddor paragraph, "Strict mode" section), `gain.md`, `crystals.md`, the data folder README, `architecture/` (code map, process), `testing.md`, `CHANGELOG.md`, regenerated schemas, `options.md`, Python types, `python-api.md`, `validation-matrix.md`, `materials-catalogue.md` (now 183 quantities in 52 entries: 43 verified, 70 approximate, 68 unverified).

### Checks (part 2)

C# suite `--slow` **576 pass, 0 fail, 9 skipped** (571 before; +5 tests); solution build net10.0 and net48 0 errors; Python **223 pass, 2 skipped**, mypy clean; `example_check` **0 problems**; `mkdocs build --strict` 0 warnings. net48 tests were not run (no Mono here).

## Still open

1. **The papers (unchanged, the largest item).** Fill the `unverified` / `approximate` rows of [the catalogue page](../../reference/materials-catalogue.md#not-verified-yet) from the original sources, set `status: verified`, write `verification`, `/regen`, re-pin the snapshot on purpose (`ULTRAFAST_WRITE_SNAPSHOT=1`). Most useful first: CS2 (n2, the 90/10 split, 1.6 / 0.2 ps; then re-run `liu-2013` and `shimizu-1967`), BaF2 (n2 and sigma, and how the Z-scan parameter u relates to sigma; `kaur-2024`), the rotational dephasing times T2 of N2 / O2 / CO2 / H2, the glass n2 values, the cross sections and lifetimes of the three gain media (Koerner 2002, Sumida and Fan 1995, Moulton 1986, Koechner), the d tensors and n2 of the crystals, the Hollenbeck 13-mode table, the CO2 / water-vapour / vapour-pressure / compressibility constants of the Ciddor formula against the paper.
2. **The values cannot be traced in two places by a database page:** LBO (Kato 1994) and the Jundt / Gayer LiNbO3 e-ray forms (the b4 coefficient of MgO:LiNbO3 is a known uncertain number); the aBBO and YVO4 vendor sets (kept by decision for YVO4; the aBBO page was not found).
3. **Still generic defaults in code, not in the catalogue:** the defaults of a *user-defined* gain medium (`GainMedium` class: conductivity 10 W/(m K), dn/dT 7.8e-6 /K, used when the user gives none) and the defaults of `SolidIonizationData` for a material without ionisation data (fused-silica-like numbers, used when the user gives only `bandGapEv`); the thin-film Cauchy fits (in the catalogue, `approximate`); `Quartz` / `Quartz-e` ionisation (copies of fused silica, `unverified`). The notes do not list these generic defaults when a user medium falls back on them.
4. **Partial ionisation overrides** (e.g. only `effectiveMass`) still report the catalogue's band gap, which is right, but the note does not say which fields were overridden.
5. **The strict mode is per run, not per material:** there is no way to accept one named approximate value and still refuse the others except by overriding it (or putting it into the user catalogue marked `verified`). If that is wanted, an allow-list (`strictMaterials: {"allow": ["FusedSilica.n2"]}`) is a small addition.
6. **Python:** `summary()["unverifiedInputs"]` and `strictMaterials` (typed in `ultrafast.types`) work as plain JSON; there are no dedicated builder helpers or a typed accessor for the list yet.
7. **Not run here:** the net48 test run (no Mono on this machine; net48 builds with 0 errors), CI on Windows and Linux (push triggers it), the `Interop/cpp` and `Zemax` examples (manual by design).
