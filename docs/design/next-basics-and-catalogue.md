# Plan: basics, and a catalogue

Status: proposed, not started. Two independent pieces of work. Do the first first: the review found that bugs sit where a test could not see them, and a catalogue multiplies what depends on the components.

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

### Design

One catalogue with entry kinds: `material`, `gas`, `crystal`, `coating`, `substrate`, and possibly `grating` and `fibre`. An entry is a file, in layers, later ones winning:

1. built in (shipped with the engine);
2. user library (`~/.ultrafast/catalogue/`);
3. project (a `catalogue/` folder beside the system file);
4. inline in the `.ufs`;
5. run overrides and study parameters.

```jsonc
{ "kind": "coating", "name": "LaserOptik-HR-1030", "base": "HR-1030-generic",
  "reflectivity": { "csv": "data/hr1030.csv", "columns": ["wavelengthNm", "R"] },
  "gddFs2": { "expr": "20 + 50*(lambdaNm-1030)/50" },
  "damageJPerCm2": 1.5, "source": "vendor sheet, 2026" }
```

**One property, four forms.** Every property takes a number, an expression, a table or CSV, or a named model. `expr` uses the existing `CompiledExpression`, with named variables a study can scan. That is the part to get right: it makes the rule the same everywhere.

**Selecting.** `"material": "FusedSilica"` keeps working. New: `catalogue/coating/LaserOptik-HR-1030`, with `ultrafast catalogue list | show | search | add | validate`, `uf.catalogue` in Python with tab completion, and `Literal` types generated for the built-in names.

**Traceability.** Every entry states its source; `provenance.json` records the entry, its layer, its file hash and each override. Results stay reproducible when a library changes.

### Stages

1. The property type (number, expression, table, model) and its schema, with no change of behaviour.
2. The layered store and `ultrafast catalogue`, wrapping today's libraries.
3. Coatings and substrates as entries.
4. Crystals into the same form.
5. Python access, autocompletion, docs, an example of a vendor CSV.

### Questions for the owner

* Which vendor data do you want shipped? Licences decide: user-supplied files are always safe.
* Should a project pin a catalogue version, or is the file hash in the provenance enough?
* Is `~/.ultrafast/` the right place on Windows and for shared lab libraries, or should a path be configurable?

### Rules that stay

SI units with the unit in the key, migrations for old names, no NuGet packages, no third-party data without its source.
