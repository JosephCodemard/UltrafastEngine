# HANDOFF -- WP C2C

Work package **C2C** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-c2c`, branch `dev/wp-c2c` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

Not started (an earlier attempt read code but wrote nothing).

Nothing.

Files changed against the branch point when this was written:

```
(no changes)
```

## Next steps

1. Follow the C2c row and the notes. 2. Finish.

## Notes from the original brief

Pages `docs/reference/crystals.md`, `materials.md`, `coatings.md`, `components.md`, `active-elements.md`, `modulator.md`,
`spatial-properties.md`. Audit: `docs/design/notes/reference-audit-c.md`. Same method as the other C2 WPs (template, resolution column,
"Code findings for WP C4" section, re-verify each row). Known code findings: `seedNoise` declared default "true"
(`BuiltInElements.cs:269`) against code false; a user biaxial crystal needs an undocumented `"class": "biaxial"` (`Crystal.cs:301-303`):
say whether docs or code should change. Re-check the tilted-plate GDD sign (`components.md:179`, test `OpenBugRegressionTests.cs`).
Material values only from the JSON material catalogue source with its stated sources, never from memory. Finally a `physics-reviewer`
subagent on the pages. Model: Opus.

## How to work (all WPs)

* Read `CLAUDE.md`, section 0.1 of `docs/design/roadmap-internal.md` and this WP's row, then only the files named here and what grep finds.
  Do not read generated files (`schemas/*.json`, `python/src/ultrafast/types.py`, `docs/reference/options.md`, `python-api.md`,
  `validation-matrix.md`, `materials-catalogue.md`); grep them for anchors only. Never read `../Experiments/`.
* Work only in this worktree. Python with numpy / mkdocs: `/Users/josephcodemard/Documents/Projects/UltrafastEngine/UltrafastEngine/.venv/bin/python` (system `python3` lacks them); mypy:
  `/Users/josephcodemard/Documents/Projects/UltrafastEngine/.venv/bin/python -m mypy`. For Python tests run
  `<venv python> python/tools/build_engine.py` first and export `ULTRAFAST_LIBRARY` / `PYTHONPATH` in the same shell command if needed.
* Build `dotnet build UltrafastEngine.sln -c Release` (0 errors, net10.0 and net48). Tests:
  `dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll <filter>` piped through `grep -E "FAIL|passed" | grep -v "^   at"`.
  Regenerate generated files with `build/wp/regen.sh` when a declaration, Python builder or test attribute changed.
* Docs follow `docs/style.md`; `<venv python> -m mkdocs build --strict` 0 warnings; `<venv python> build/style_lint.py` green.
* The owner is away: never ask questions; decide, and record the decision in the report and the history paragraph.
* **If you stop before finishing**, update this HANDOFF.md (what is done, what is next, anything learnt) and commit everything as
  `WIP <id>: ...`. **When finished**: CHANGELOG.md line (top section); a paragraph in `docs/design/history.md` headed
  `## Stage XX -- WP <id>: ...` (literally XX); the WP's status in `roadmap-internal.md` set to `done <date>`; then **delete HANDOFF.md** in
  the final commit (`build/wp/merge.sh` refuses a branch that still has it); commit message ends with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push or merge (the coordinator does). Report in under 200 words.
