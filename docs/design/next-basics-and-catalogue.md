# Plan: basics, and a catalogue

Status: proposed, not started. Non-collinear SHG and SFG came first and are done. These are two independent pieces of work. Do the first first: the review found that bugs sit where a test could not see them, and a catalogue multiplies what depends on the components.

## 1. Back to basics

**Why.** Same-colour fed-port tests could not see the wrong carrier of a fed beam. Most tests check one model in one configuration. Each component needs its own small truth table, run the same way in both topologies.

### A component test sheet

One test class per component family, under `tests/UltrafastEngine.Tests/Components/`, each answering the same questions:

| question | check |
|---|---|
| energy | in = out + booked, to rounding |
| dispersion | phase against the analytic φ(ω), and the reported `system` dispersion against the field |
| carrier | runs at 515 nm and 2 µm as well as 1030 nm: nothing assumes the run's carrier |
| bandwidth | 1 ps and 10 fs: no silent narrowing |
| topology | Sequential and Branched agree exactly |
| ports | every declared port carries the energy it should; each input and output has its own carrier |
| off cases | length 0, reflectivity 0 and 1, disabled |
| reversal | where light passes back, the Stokes relations hold |

A shared parametrised harness, so that a new element gets the sheet by being listed. A gap in the table is then visible.

Order: linear (gap, bulk, mirrors, lenses, gratings, prisms), splitters and masks, gain, crystals (each process, each port, each main role), instruments. The `leakMirror` GDD gap goes here.

### Basic examples

A numbered series, `basics/`, each one idea with its analytic answer printed beside the result, each in both topologies where that means anything, half CLI and half Python:

Group delay and GDD of a plate; a prism pair and a grating pair against Treacy; SPM against the analytic broadening; a lens and Gouy phase; a Michelson; one crystal per process, including thin against thick.

Each runs under `example_check`, so its numbers are tests.

## 2. A catalogue

**What exists.** `MaterialLibrary`, `Gases`, refractiveindex.info pages with `ultrafast materials list | show | import`, a user library, model kinds, `crystal: {base: ...}` overrides, and `coatingFactory` kinds. There is no one place to browse, coatings and substrates are not named things, and each family overrides differently.

**Scope (owner, 2026-10-31).** We consolidate the information we already have, check it against published sources found on the web, and let users add their own. Users bring vendor data; none is shipped.

### Design

One catalogue with entry kinds: `material`, `gas`, `crystal`, `coating`, `substrate`, and possibly `grating` and `fibre`. An entry is a file, in layers, later ones winning:

1. built in (shipped with the engine: our consolidated data, each value with its source);
2. shared libraries (see below);
3. user library (`~/.ultrafast/catalogue/`, or `%USERPROFILE%\.ultrafast\catalogue\` on Windows);
4. project (a `catalogue/` folder beside the system file);
5. inline in the `.ufs`;
6. run overrides and study parameters.

**Shared libraries** are folders on a path list: `ULTRAFAST_CATALOGUE_PATH`, separated as `PATH` is, plus a `catalogue` list in `~/.ultrafast/config.json`. A lab points this at a network folder or a git checkout, on any platform. Nothing is copied, so everyone reads the same files.

```jsonc
{ "kind": "coating", "name": "HR-1030-from-vendor-sheet", "base": "HR-1030-generic",
  "reflectivity": { "csv": "data/hr1030.csv", "columns": ["wavelengthNm", "R"] },
  "gddFs2": { "expr": "20 + 50*(lambdaNm-1030)/50" },
  "damageJPerCm2": 1.5, "source": "vendor sheet, 2026" }
```

**One property, four forms.** Every property takes a number, an expression, a table or CSV, or a named model. `expr` uses the existing `CompiledExpression`, with named variables a study can scan. That is the part to get right: it makes the rule the same everywhere.

**Selecting.** `"material": "FusedSilica"` keeps working. New: `catalogue/coating/HR-1030-from-vendor-sheet`, with `ultrafast catalogue list | show | search | add | validate`, `uf.catalogue` in Python with tab completion, and `Literal` types generated for the built-in names.

**Traceability.** Every entry states its source. `provenance.json` records the entry, its layer, its file hash and each override, so a result stays reproducible when a library changes. Projects do not pin a version: the hash is the record.

### Stages

1. Consolidate: list every built-in value, find its published source, correct what disagrees.
2. The property type (number, expression, table, model) and its schema, with no change of behaviour.
3. The layered store, shared libraries, and `ultrafast catalogue`, wrapping today's libraries.
4. Coatings and substrates as entries.
5. Crystals into the same form.
6. Python access, autocompletion, docs, and an example with a user's CSV.

### Rules that stay

SI units with the unit in the key, migrations for old names, no NuGet packages, no third-party data without its source.
