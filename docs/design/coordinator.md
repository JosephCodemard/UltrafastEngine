# Coordinator session: running the 1.0.0 work packages

Internal page ([folder index](README.md)). The prompt and the procedure for the **coordinator** ("master") Claude session that runs the work
packages (WPs) of [roadmap-internal.md](roadmap-internal.md) section 0 while the owner is away. Paste the prompt below into a new Claude Code
session started in the main worktree (`UltrafastEngine/`, branch `dev/2026-11`).

## Prompt to paste

> You are the coordinator of the UltrafastEngine 1.0.0 work packages. The owner is away all day: **never ask a question and never wait
> for an answer**; decide, record the decision, continue. Read `docs/design/coordinator.md` (this procedure) and section 0 of
> `docs/design/roadmap-internal.md` (the WPs, their status, dependencies and lanes). Then run the loop in "The loop" until every WP that can be
> done on this Mac is done or the day ends, and finish with "End of the session". Keep your own context small: never read agent transcripts,
> full diffs or generated files; let sub-agents and the `test-runner` agent do the reading and report in a few lines.

## State on 2026-11-26 (when this page was written)

* Merged into `dev/2026-11` and pushed: A1, A2, B1, B2, Q1, Z1, AG1, C1a-f (audits in `docs/design/notes/reference-audit-*.md`), C2a, C2e,
  C2f. History stages are numbered up to the last merge by `build/wp/merge.sh`.
* **In progress**, each worktree `../uf-wp-<id>` on branch `dev/wp-<id>` with a committed `HANDOFF.md` (state, next steps, the details of
  the original brief): P1, G0, D1 (near the end); AG2, AG3 (started).
* **Worktree created, not started** (also with a `HANDOFF.md`): C2b, C2c, C2d, Z2, X1.
* Everything else in section 0 is not started and has no worktree.
* Development Mac: arm64 macOS; PoCL 7.2 (Homebrew) gives an FP64 CPU OpenCL device once G0 is merged; Apple GPU is FP32 only; no CUDA;
  no Mono (net48 tests run only in CI); mypy in `../.venv`; the project Python with numpy and mkdocs is `UltrafastEngine/.venv/bin/python`.

## Rules

* **At most three WP agents at once.** Earlier sessions ran ten or more and hit the 5-hour usage limit twice.
* One WP per agent; Opus or Sonnet as its row says (`Agent` tool, `subagent_type: general-purpose`, `model`, `run_in_background: true`).
* Order: finish the in-progress WPs first (P1, G0, D1, AG2, AG3), then the not-started worktrees (C2b, C2c, C2d, Z2, X1), then the rest by
  section 0.3 (lanes: at most one running WP per lane; dependencies merged first). After all C2: C3 and C4. After D1: the D2 groups one at
  a time. Prefer WPs that this Mac can verify; G3 and G10 need the owner's RTX 3060 Ti and stay open.
* Owner-only, never done by any session: tags, releases, PyPI, live agent-eval scoring (decision E20), runs on real OpticStudio. Never touch
  the ZOS-API DLLs; never read `../Experiments/`; no NuGet packages.
* The coordinator edits only bookkeeping (roadmap status rows, merge resolution, this page). Code and docs work goes to WP agents.

## The loop

1. **Start a WP**: `build/wp/start.sh <id>` (creates `../uf-wp-<id>` or reports the existing one), then spawn its agent with the brief
   below.
2. **When an agent reports done**: read its report (do not open its transcript). If it says tests fail or acceptance is not met, send it
   back (`SendMessage` to the same agent) with the one thing to fix. Otherwise `build/wp/merge.sh <id> "<one-line summary>"`: it refuses a
   branch that still has `HANDOFF.md`, resolves the append-only files, numbers the history stage and pushes `dev/2026-11`. If it prints
   `REGEN NEEDED`: `build/wp/regen.sh`, commit the generated files, `git push origin dev/2026-11`. If it prints `UNRESOLVED`: resolve by
   hand (small) or abort the merge and send the agent back to rebase. Then `build/wp/cleanup.sh <id>`.
3. **When an agent stops at a usage limit or error**: its work is in its worktree. When limits allow, `SendMessage` to the same agent ("the
   limit has reset: continue where you stopped; read HANDOFF.md and git status first"). If the agent is gone (a new coordinator session),
   spawn a new one with the brief; it starts from `HANDOFF.md` and `git diff dev/2026-11`.
4. **Checkpoint after every five merges** (and before starting a WP that depends on a merged one): spawn `test-runner` on `dev/2026-11`
   (build, default C# suite, Python suite after `build_engine.py`, `mkdocs build --strict`, `build/style_lint.py`, `build/example_lint.py`).
   A failure becomes a new WP row (`fix-<n>`, Opus) that runs before anything else.
5. Keep the WP's row status current in `roadmap-internal.md` (`in progress`, `done <date>`), committed with the merges.

## Brief for a WP agent

Fill in `<id>`; add any per-WP notes (the D2 groups: the per-example instructions D1 wrote into the D2 row).

> Work package `<id>` of `docs/design/roadmap-internal.md` section 0 (UltrafastEngine). Work ONLY in the worktree
> `/Users/josephcodemard/Documents/Projects/UltrafastEngine/uf-wp-<id>` (branch `dev/wp-<id>`). If it contains `HANDOFF.md`, read it first:
> it holds the state, the next steps, the notes of the original brief and the working rules; continue from it. Otherwise read `CLAUDE.md`,
> section 0.1 and the `<id>` row of the roadmap, then only the files they name and what grep finds; create `HANDOFF.md` from the section
> "How to work" of any other WP's handoff only if you must stop early. Rules: do not read generated files (grep them for anchors only);
> never read `../Experiments/`; never touch the ZOS-API DLLs; no NuGet. Python with numpy / mkdocs:
> `/Users/josephcodemard/Documents/Projects/UltrafastEngine/UltrafastEngine/.venv/bin/python`. Build `dotnet build UltrafastEngine.sln -c Release`;
> tests through `grep -E "FAIL|passed" | grep -v "^   at"`; `build/wp/regen.sh` after a declaration change; docs follow `docs/style.md`,
> `mkdocs build --strict` 0 warnings, `build/style_lint.py` green. Physics WPs (⚛): `/physics-change` and a `physics-reviewer` subagent on
> the diff at the end. The owner is away: never ask, decide and record. If you must stop before finishing: update `HANDOFF.md` and commit
> everything as `WIP <id>: ...`. When finished: CHANGELOG line (top section), a history paragraph headed `## Stage XX -- WP <id>: ...`
> (literally XX), the row's status `done <date>`, **delete HANDOFF.md**, one final commit ending
> `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push or merge. Report in under 200 words: what changed, test counts,
> what is left open.

## End of the session

1. When no WP is running: spawn `test-runner` for the full `/verify` on `dev/2026-11` (builds for net10.0 and net48, all C# tests with
   `--slow`, Python with pyNLO, examples in strict mode, docs). Fix failures through `fix-<n>` WPs until green.
2. Check CI on the pushed branch: `gh run list --branch dev/2026-11 --limit 3`; wait for green (re-check every 10 minutes, do not poll
   faster).
3. Open (do not merge) a pull request `dev/2026-11` → `main` with `gh pr create`: the WPs merged, the tests run and their counts, what is
   open, what needs the owner (OpticStudio checklist steps, 3060 Ti runs, live eval scores, tags / PyPI). End the description with
   `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
4. A history paragraph for the session, `PROJECT_STATUS.md` counts, the roadmap statuses; commit and push. The final message to the owner:
   the pull request link, what is done, what failed, what is left and why.
