# Schemas, validation and migrations

Every option the engine reads is declared once, in the **option registry** (`UltrafastEngine.Schema`), with its
JSON type, SI unit, default and meaning. This page describes what the registry produces, the checks a run makes against it, the format
versions and the renamed keys. From that single source the engine produces:

* the **JSON Schemas** `schemas/ufs.schema.json` and `schemas/run.schema.json`, for editor completion and checking;
* the **unknown-key warnings** of every run (typos no longer go unnoticed);
* the **[option reference](options.md)** (generated Markdown);
* the Python TypedDicts (`ultrafast.types`), the Python API reference ([python-api.md](python-api.md)) and the
  [materials catalogue](materials-catalogue.md).

Plugins declare their options the same way, so their keys appear everywhere too.

## Keys

The two files take `$schema` (any editor), `format` and `version`. The keys of each file are in [System file](system-file.md) and
[Run spec](run-spec.md); the full list is generated: [options](options.md).

## Editor completion

Start a file with `$schema` (a path relative to the file, or a URL):

```jsonc
{
  "$schema": "../../schemas/run.schema.json",
  "format": "ultrafast-run",
  "version": 2,
  ...
}
```

VS Code, JetBrains IDEs and most editors with a JSON language server then complete keys (with their unit and
meaning as the hint), list the allowed values of enumerations and element types, and underline unknown keys.
The examples in this repository all carry a `$schema`. `//` comments, which the engine accepts, need the editor's
JSONC mode (VS Code: `"files.associations": {"*.ufs": "jsonc"}`).

```bash
ultrafast schema system -o ufs.schema.json     # regenerate (includes the plugins installed here)
ultrafast schema run -o run.schema.json
ultrafast reference -o docs/reference/options.md
```

## Checks during a run

Every run (and `ultrafast validate`) checks the system file and the run spec against the registry:

```
WARNING: Element 'glass' (bulk): unknown parameter "lenghtM" (did you mean "lengthM"?) -- it is ignored.
WARNING: Instrument 'spec' (spectrometer): "resolutionM" should be a number (m).
```

* unknown keys (with the closest known key as a suggestion), wrong JSON types, values outside an enumeration,
  missing required keys and deprecated keys;
* nested objects (`sampling`, `imaging`, `sensor`, `pump`, `interaction`, aux beams, ...) and model objects whose
  kind declares its parameters are checked too;
* keys starting with `_` (for example `_comment`), `$schema`, and `comment`, `description`, `notes` are free text everywhere;
* top-level keys of a system file are not checked.

They are warnings because the engine ignores keys it does not know. Set `ULTRAFAST_STRICT_OPTIONS=1` to make them
errors (recommended in CI). The test `SchemaTests.ExamplesUseOnlyDeclaredOptions` runs every example file through
the checks, and the whole test suite runs clean in strict mode.

## Format versions and migrations

System files and run specs carry `"version"`. The current version is **3** for system files and **2** for run specs. Older files are upgraded
automatically when they are loaded; any change is reported in the run's notes. `ultrafast migrate file -o new`
rewrites a `.ufs` or a `run.json` in the current version (it writes plain JSON, so comments are not kept).

| version | change |
|---|---|
| run 1 → 2 | `nearFieldCamera` → `camera` with `"imaging": {"plane": "near"}`; `farFieldCamera` → `camera` with `"imaging": {"plane": "far", "focalLengthM": ...}`; `propagateM`, `focalLengthM`, `magnification` move into `imaging` |
| system 1 → 2 | no changes (the version marks files checked against version 2) |
| system 2 → 3 | no migration: a version 2 file loads as it is. Version 3 only marks files that may use `topology` and `next` ([Branched systems](branches.md)) |

**Renamed keys (1.0.0).** Keys now carry their unit in the name. The old names are renamed when a file is loaded, in every version, with one note per
key (`"betas" is now "betasSkPerM" ...`); they are deprecated, and the engine does not remove old names:

| old | new | where |
|---|---|---|
| `betas` | `betasSkPerM` | `fiber` element (an override addressed to the old name is read by the fibre compiler) |
| `fieldT` | `fieldTesla` | `faradayRotator` |
| `dnDT` | `dnDTPerK` | any object that has the key (the rename scope is every object: `refractiveindex` index model, gain media, ...) |
| `tau1`, `tau2`, `taub` | `tau1S`, `tau2S`, `taubS` | `linAgrawal` and `dampedOscillator` Raman models |

A file newer than the engine is refused with a clear message. Migrations are pure JSON transformations in
`Schema.Migrations`, each tested (`SchemaTests.Migration_*`, `SchemaTests.UnitInKeyRenames_*`).

## Adding an option (contributors)

1. Read it in the code (`services.Num(element, "myKeyM", default)` for elements, so overrides apply).
2. Declare it in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs` with type, unit, default and description.
3. Regenerate the five generated files (`/regen`): `ultrafast schema system -o schemas/ufs.schema.json`, `ultrafast schema run -o schemas/run.schema.json`,
   `ultrafast reference -o docs/reference/options.md`, `python3 python/tools/gen_types.py`, `python3 python/tools/gen_api_docs.py` (tests check all five).

A plugin declares its options with the `ComponentSpec` it passes to `RegisterElement` / `RegisterInstrument` /
`RegisterSpecKeys` ([Plugin system](plugins.md)).

## Validation

`SchemaTests.ExamplesUseOnlyDeclaredOptions` (every example file uses only declared keys), `SchemaTests.Migration_*` (each migration step) and
`SchemaTests.UnitInKeyRenames_*` (each renamed key) check this page; the whole suite runs in strict mode.

## Limits

* The registry and the compiler disagree on three defaults, and some descriptions link to pages that do not exist; the generated reference inherits them.
  The page for each key says what the engine does (for example [System file](system-file.md#bulk-gap-propagation-in-a-material)).
* Top-level keys of a system file are not checked (`topology` is read but not declared).
