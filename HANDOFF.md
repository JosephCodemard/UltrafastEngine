# HANDOFF -- WP G0

Work package **G0** of `docs/design/roadmap-internal.md` section 0. Worktree `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-g0`, branch `dev/wp-g0` (from `dev/2026-11`).
Written 2026-11-26 by the coordinator after the agents stopped at a usage limit.

## Status

In progress (near the end).

Done (uncommitted work, now in the WIP commit): device discovery changes in `plugins/gpu/src/OpenClDevice.cs` and
`GpuBackend.cs`, test changes in `tests/UltrafastEngine.Tests/GpuTests.cs`, docs (`docs/plugins/gpu.md`, `docs/testing.md`,
`docs/design/hardware-checklists.md`), CHANGELOG, history, roadmap, PROJECT_STATUS. The last action was a background run of `GpuTests` on PoCL.

Files changed against the branch point when this was written:

```
M CHANGELOG.md
 M PROJECT_STATUS.md
 M docs/design/hardware-checklists.md
 M docs/design/history.md
 M docs/design/roadmap-internal.md
 M docs/plugins/gpu.md
 M docs/testing.md
 M plugins/gpu/src/GpuBackend.cs
 M plugins/gpu/src/OpenClDevice.cs
 M tests/UltrafastEngine.Tests/GpuTests.cs
```

## Next steps

1. `git diff dev/2026-11` to review the change; rebuild.
2. Run `GpuTests` (and `ComputeTests`) on PoCL; record pass / fail / skip counts in the report and docs/testing.md.
3. Kernel failures on PoCL are **not** fixed here: add each as a new row in roadmap-internal.md section 0 (E track) with its message.
4. `ultrafast devices` must list the Apple GPU (single precision only, not usable for double) and PoCL (FP64). Default C# suite passes.
5. Finish (rules below).

## Notes from the original brief

PoCL 7.2 from Homebrew: `/opt/homebrew/lib/libpocl.dylib`, ICD file `/opt/homebrew/etc/OpenCL/vendors/pocl.icd`. `OpenClDevice.cs:69-76`
tried `OpenCL.framework` first (Apple GPU, no FP64), so GpuTests skipped. The CPU OpenCL device is used only with backend `opencl`, never
by `auto` (keep that). `brew install ocl-icd` is allowed if needed (say so).

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
