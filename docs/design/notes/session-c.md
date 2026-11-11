# Session C (WP3): the material catalogue

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
| **YVO4 vendor Sellmeier** (o and e) | **finding, not changed.** Differs from Shi et al. 2001 (20 C, `main/YVO4/Shi-o-20C`, `Shi-e-20C`) by -5.4e-4 / -9.6e-4 at 633 nm and -5.9e-4 / -9.2e-4 at 1064 nm; the birefringence differs by ~0.16 %, which matters for waveplate thickness. The Shi set has the same functional form, so adopting it is a four-number edit per axis. **Owner to choose** (the existing waveplate examples would move slightly). |
| CS2 n2, the 90/10 split, 1.6 / 0.2 ps | **not verified.** Searches gave only qualitative statements (orientation dominates, ~2 ps decay); the CREOL PDFs found (publication 1811) are scanned images that cannot be rendered here (`pdftoppm` is missing). Marked `unverified` with the uncertainty (factor 2-3) and the pointer. |
| BaF2 n2 and sigma = -1.2 | **not verified.** The Z-scan anisotropy paper (CREOL publication 1820) is a scan; the search snippet gives its form chi(theta) = chi [1 + 2u (sin^4 theta - sin^2 theta)] and that it tabulates n2 along [100], [010], [110] at 532 / 1064 nm. How u relates to the engine's sigma is still to be written down. `unverified`, noted in the file. |
| Rotational Raman dephasing times T2At1Bar (N2 100 ps, O2 80, CO2 50, H2 300) | **not verified.** Searches did not give 1 atm values (self-broadened N2 / O2 rotational CARS, 1-20 atm, 2011, is the place to look). `unverified`, uncertainty 50 %. |
| Gas n2, glass n2, Raman fractions and time constants, polarisability anisotropies, ionisation data | `approximate` (typical values, stated spread) or `unverified`; none checked against a paper. |

Counts on the page: **115 quantities: 28 verified, 48 approximate, 37 unverified.** The papers `liu-2013`, `shimizu-1967` and `kaur-2024` do not need a re-run: no CS2, BaF2 or Raman number changed.

## Checks run

C# suite `--slow` 571 pass, 9 skipped (562 before; +9 tests); net10.0 and net48 build 0 errors; Python 223 pass (hosted library); `example_check` 0 problems; `mkdocs build --strict` 0 warnings.
A trap met on the way: with a stale `python/src/ultrafast/bin/native` library, `example_check` shows 20 false failures; set `ULTRAFAST_LIBRARY` to the hosted DLL (as in the verification-environment note).

## Still open

1. **The papers.** Open the original sources for the `unverified` / `approximate` rows of [the catalogue page](../../reference/materials-catalogue.md#not-verified-yet) and fill the value, change `status` to `verified`, write `verification`, run `/regen` and
   re-pin the snapshot on purpose (`ULTRAFAST_WRITE_SNAPSHOT=1`). Most useful first: CS2 (n2, split, times; then re-run `liu-2013` and `shimizu-1967`), BaF2 (n2, sigma; `kaur-2024`), T2 of N2 / O2 / CO2 / H2, n2 of fused silica / BK7 / sapphire / YAG, the Hollenbeck table of 13 modes.
2. **YVO4:** adopt Shi 2001 or keep the vendor set (above). The aBBO vendor sets could not be compared (no database page found).
3. **Not in the catalogue yet:** the gain media of `Gain/GainMedium.cs` (cross sections, lifetimes, dn/dT, thermal conductivity, stress constants: they are the next candidates, with the same format), the crystal library of the chi(2) elements, the
   Ciddor constants of the air index (a published formula, left in code), the generic defaults of `SolidIonizationData` for a material without ionisation data (fused-silica-like numbers, used only when the user gives `bandGapEv`), the
   `Quartz`/`Quartz-e` ionisation entries (copies of fused silica) and the thin-film Cauchy fits.
4. **Ionisation notes** are reported when `ionization` is on and the block does not give `bandGapEv` / `ionizationEv`; a partial override (e.g. only `effectiveMass`) still reports the band gap, which is right, but it does not list the overridden field.
5. A `status` could be required to be `verified` for a "strict" run (`ULTRAFAST_STRICT_MATERIALS=1` refusing unverified inputs): not built; the notes and `unverifiedInputs` give the information. Say if wanted.
6. Python: `summary()["unverifiedInputs"]` is reachable as plain JSON; no typed accessor yet.
