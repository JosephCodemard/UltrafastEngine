# HANDOFF -- WP P1

Work package **P1** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-p1`, branch `dev/wp-p1` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

In progress (implementation and docs written; verification left).

Done: per-port glass declared in `Schema/BuiltInElements.cs`, compiled in
`Engine/Compiler.cs`, `Executor.cs`, `GraphExecutor.cs`, `MultiPort.cs`; generated files regenerated; docs `docs/reference/branches.md`,
`docs/testing.md`; CHANGELOG, history, roadmap; new test file `tests/UltrafastEngine.Tests/SplitterGlassTests.cs`. The last edit was making
the JSON parsing catch `JsonException` specifically and drop a null `next`.

Files changed against the branch point when this was written:

```
M CHANGELOG.md
 M docs/design/history.md
 M docs/design/roadmap-internal.md
 M docs/reference/branches.md
 M docs/reference/options.md
 M docs/reference/validation-matrix.md
 M docs/testing.md
 M python/src/ultrafast/types.py
 M schemas/ufs.schema.json
 M src/UltrafastEngine.Core/Engine/Compiler.cs
 M src/UltrafastEngine.Core/Engine/Executor.cs
 M src/UltrafastEngine.Core/Engine/GraphExecutor.cs
 M src/UltrafastEngine.Core/Engine/MultiPort.cs
 M src/UltrafastEngine.Core/Schema/BuiltInElements.cs
?? tests/UltrafastEngine.Tests/SplitterGlassTests.cs
```

## Next steps

1. Review `git diff dev/2026-11`; build both targets.
2. Run `SplitterGlassTests`, then the default suite.
3. `build/wp/regen.sh` (the regen tests must pass).
4. Run a `physics-reviewer` subagent on the diff; fix or list its findings.
5. Finish.

## Notes from the original brief

Acceptance: the splitter with glass equals the splitter plus a `plate` in each arm to 1e-12; no glass is bit-identical to today.
Reuse the mechanism of glass on polarisers (history Stage 11). Follow `/physics-change`.

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
