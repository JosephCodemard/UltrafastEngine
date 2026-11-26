# HANDOFF -- WP AG2

Work package **AG2** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-ag2`, branch `dev/wp-ag2` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

In progress (started).

Done: first edits in `python/src/ultrafast/mcp/server.py` and `tools.py` (review them: partial).

Files changed against the branch point when this was written:

```
M python/src/ultrafast/mcp/server.py
 M python/src/ultrafast/mcp/tools.py
```

## Next steps

1. Review the diff. 2. Implement per the AG2 row and the notes below. 3. Tests, docs, finish.

## Notes from the original brief

MCP tools `twin_design` (the next measurement to take) and `twin_track` (drift), and `twin_init` (or the documented equivalent of
`ultrafast twin init`) through the server. Reuse the twin plugin's functions (grep `plugins/` and `python/src` for twin design / track);
do not reimplement physics. Each tool: input schema, refusals in `policy.py` style, budget limit and a budget test (a call over budget is
killed and the next call works). Tests: handshake tool count, happy path on a small synthetic case, each refusal. Docs:
`docs/reference/mcp.md` section, `docs/agents.md` if it lists tools, the agent-pack skill text (one source: `build/sync_skills.py`).

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
