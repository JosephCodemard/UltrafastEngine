# HANDOFF -- WP C2B

Work package **C2B** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-c2b`, branch `dev/wp-c2b` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

Not started (an earlier attempt read code but wrote nothing).

Nothing.

Files changed against the branch point when this was written:

```
(no changes)
```

## Next steps

1. Follow the C2b row and the notes. 2. Finish.

## Notes from the original brief

Pages `docs/reference/nonlinear.md`, `ionization.md`, `delayed-light.md`, `gases.md`, `fibers.md`, `gain.md`, `oscillators.md`. Audit:
`docs/design/notes/reference-audit-b.md`. Rewrite to the `docs/style.md` reference template; close every audit row with a final column
"resolution": fixed / kept, because ... / code: ... (code problems go into a section "Code findings for WP C4" at the end of the audit
file, with file:line and the proposed fix; do not edit code). Re-verify every finding (auditors can be wrong). State conventions as in
`docs/concepts.md` and `.claude/skills/engine-physics`. Known: `gain.md` lacks the `amplifier` element section and a schema-linked anchor
(grep the declarations for `gain.md#` and make every such anchor exist); `taubS` key name; quote the tolerances the tests assert, not stale
figures. Preserve anchors other pages link to. Finally a `physics-reviewer` subagent on the pages; fix its findings. Model: Opus.

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
