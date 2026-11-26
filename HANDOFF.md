# HANDOFF -- WP AG3

Work package **AG3** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-ag3`, branch `dev/wp-ag3` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

In progress (started).

Done: first edits in `tests/agent-evals/run_live.py` (review them: partial).

Files changed against the branch point when this was written:

```
M tests/agent-evals/run_live.py
```

## Next steps

1. Review the diff. 2. Implement per the AG3 row and the notes below. 3. Tests, docs, finish.

## Notes from the original brief

**Owner decision: no live scoring runs** (never call `claude -p`, `codex` or any model API). An adapter layer in `run_live.py`: the
`claude -p` reader becomes one adapter; add Codex (`codex exec --json` JSONL events, session files) and Cursor
(`cursor-agent -p --output-format stream-json`) adapters from their documented formats (cite the URLs; say if undocumented) into the
structure the scorers use (tool calls with arguments, final text, files written). `--agent claude|codex|cursor` and `--transcript FILE`
(score an existing transcript without running anything). Hand-made replay transcripts in each format for at least two evals; a test that
the scorers give the same verdict in all three formats. Docs: evals section of `docs/reference/mcp.md` (how the owner runs live scores).

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
