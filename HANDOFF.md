# HANDOFF -- WP C2D

Work package **C2D** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-c2d`, branch `dev/wp-c2d` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

Not started (an earlier attempt read code but wrote nothing).

Nothing (an earlier attempt checked models.md functions against the code and found them matching; expression variables were next).

Files changed against the branch point when this was written:

```
(no changes)
```

## Next steps

1. Audit `models.md` line by line first (rows into the audit file). 2. Follow the C2d row. 3. Finish.

## Notes from the original brief

Pages `docs/reference/transverse.md`, `branches.md`, `models.md`, `fidelity.md`, `memory.md`, `speed.md`. Audit:
`docs/design/notes/reference-audit-d.md` (models.md was NOT audited line by line: do that first, same format). Same method as the other C2
WPs. Benchmark numbers in `speed.md` without a reproducible source: keep only with their provenance (machine, date, script / test) or mark
them so; never invent. Known: `gpuMinPoints` 0 meaning depends on the back-end; `compute.precision: "mixed"` unnamed; free-memory sources
differ between net10 and net48. Finally a `physics-reviewer` subagent on the pages. Model: Opus.

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
