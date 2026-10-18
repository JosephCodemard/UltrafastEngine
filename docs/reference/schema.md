# Schemas, validation and migrations

Every option the engine reads is declared once, in the **option registry** (`UltrafastEngine.Schema`), with its
JSON type, SI unit, default and meaning. From that single source the engine produces:

* the **JSON Schemas** `schemas/ufs.schema.json` and `schemas/run.schema.json`, for editor completion and checking;
* the **unknown-key warnings** of every run (typos no longer go unnoticed);
* the **[option reference](options.md)** (generated Markdown);
* the Python TypedDicts (`ultrafast.types`).

Plugins declare their options the same way, so their keys appear everywhere too.

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
WARNING: Element 'plate' (bulk): unknown parameter "lenghtM" (did you mean "lengthM"?) -- it is ignored.
WARNING: Instrument 'spec' (spectrometer): "resolutionM" should be a number (m).
```

* unknown keys (with the closest known key as a suggestion), wrong JSON types, values outside an enumeration,
  missing required keys and deprecated keys;
* nested objects (`sampling`, `imaging`, `sensor`, `pump`, `interaction`, aux beams, ...) and model objects whose
  kind declares its parameters are checked too;
* keys starting with `_` and `comment`, `description`, `notes` are free text everywhere.

They are warnings because the engine ignores keys it does not know. Set `ULTRAFAST_STRICT_OPTIONS=1` to make them
errors (recommended in CI). The test `SchemaTests.ExamplesUseOnlyDeclaredOptions` runs every example file through
the checks, and the whole test suite runs clean in strict mode.

## Format versions and migrations

System files and run specs carry `"version"`. The current version is **2** for both. Older files are upgraded
automatically when they are loaded; any change is reported in the run's notes. `ultrafast migrate file -o new`
rewrites a file in the current version (note: it writes plain JSON, so comments are not kept).

| version | change |
|---|---|
| run 1 → 2 | `nearFieldCamera` → `camera` with `"imaging": {"plane": "near"}`; `farFieldCamera` → `camera` with `"imaging": {"plane": "far", "focalLengthM": ...}`; `propagateM`, `focalLengthM`, `magnification` move into `imaging` |
| system 1 → 2 | no changes (the version marks files checked against version 2) |

A file newer than the engine is refused with a clear message. Migrations are pure JSON transformations in
`Schema.Migrations`, each tested (`SchemaTests.Migration_*`).

## Adding an option (contributors)

1. Read it in the code (`services.Num(element, "myKeyM", default)` for elements, so overrides apply).
2. Declare it in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs` with type, unit, default and description.
3. Regenerate `schemas/` and `docs/reference/options.md` (`ultrafast schema ...`, `ultrafast reference ...`).

A plugin declares its options with the `ComponentSpec` it passes to `RegisterElement` / `RegisterInstrument` /
`RegisterSpecKeys` ([Plugin system](plugins.md)).
