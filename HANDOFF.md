# HANDOFF -- WP D1

Work package **D1** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-d1`, branch `dev/wp-d1` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

In progress (scaffolding and two examples done; linter and checks left).

Done: `pymdownx.tabbed` in `mkdocs.yml`; `docs/examples/_template.md`;
`GettingStarted/quickstart` (new `run.json`, `two-plates.ufs`, `example.json`) and `GettingStarted/spm-plate` (`example.json`) converted with
CLI / Python tabs; `build/readme_snippets.py`; `python/tests/test_readme_snippets.py`; edits to `.github/workflows/ci.yml`, `docs.yml`,
`README.md`, `docs/css/brand.css`. The last step started was updating `build/example_lint.py`.

Files changed against the branch point when this was written:

```
M .github/workflows/ci.yml
 M .github/workflows/docs.yml
 M README.md
 M build/example_lint.py
 M docs/css/brand.css
 M docs/examples/GettingStarted/quickstart/README.md
 M docs/examples/GettingStarted/spm-plate/README.md
 M docs/examples/_template.md
 M mkdocs.yml
?? build/readme_snippets.py
?? docs/examples/GettingStarted/quickstart/example.json
?? docs/examples/GettingStarted/quickstart/run.json
?? docs/examples/GettingStarted/quickstart/two-plates.ufs
?? docs/examples/GettingStarted/spm-plate/example.json
?? python/tests/test_readme_snippets.py
```

## Next steps

1. Review `git diff dev/2026-11` and the new files; check that the `README.md`, `brand.css` and `docs.yml` edits are intended.
2. Finish `build/example_lint.py`: converted examples (choose and document the `example.json` field) must have the Python tab.
3. Run the snippet test for the two examples, the linter, `mkdocs build --strict`, and check both tabs render in `site/`; Python suite passes.
4. `docs/style.md`: the "Run it" section with the tab convention.
5. **Write the per-example instructions for the D2 WPs (5-8 lines) into the D2 row's notes in roadmap-internal.md** (the coordinator
   pastes them into each D2 brief) and into the report.
6. Finish.

## Notes from the original brief

Python tab: uses the `ultrafast` builders and typed dicts (autocompletion), loads the example's own `.ufs` / `run.json` where simplest,
prints the same key numbers as the CLI. Snippets run at quick fidelity with `ULTRAFAST_STRICT_OPTIONS=1`, slow ones only with
`ULTRAFAST_SLOW=1`. Numbers quoted must match a fresh run.

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
