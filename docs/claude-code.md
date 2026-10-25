# Working on UltrafastEngine with Claude Code

[Claude Code](https://docs.claude.com/en/docs/claude-code/overview) is Anthropic's coding agent for the terminal,
IDEs and the desktop app. The repository ships the files it reads, so a session starts with the project's rules,
commands and physics conventions already known, works within guard rails, and spends few tokens finding its way.

## Quick start

```bash
cd UltrafastEngine
claude                      # start a session in the repository root
```

Then ask in plain words, or use the project commands:

| command | what it does |
|---|---|
| `/test NerTests Measured` | builds the tests and runs the matching ones (empty: all, incl. slow); shows failures only |
| `/regen` | regenerates the 5 generated files (schemas, options.md, Python types and API docs) after a schema change |
| `/verify` | full check before a commit or release: build, C# (.NET 10 and .NET Framework 4.8), Python with pyNLO, examples in strict mode, docs |
| `/physics-change <what>` | workflow for a change to the propagation physics: equation and source first, test against an analytic limit, all three propagation models kept consistent, review |
| `/new-option <key>` | adds an option / element / instrument / model kind end to end |
| `/bug-report <text>` | reproduces a reported bug minimally, decides whether it is valid (and why), fixes it with a test |
| `/release <build.sh options>` | builds release packages and smoke-tests them ([Releasing](releasing.md)) |
| `/example-check [names]` | re-runs examples and updates the numbers quoted in their READMEs |

Typical requests: "add a `thermalLens` option to the gain element", "the B-integral of circular light looks too
high -- check it", "fit the residual GDD of my compressor to these d-scan files", "package osx-arm64 and win-x64 with
the fitting plugin only".

## The files and what they do

| file | loaded | purpose |
|---|---|---|
| `CLAUDE.md` | every session | project memory: owner's rules, fast commands, token economy, where to look, definition of done; imports `AGENTS.md` |
| `AGENTS.md` | (imported) | the tool-neutral version for any coding agent: build, rules, layout |
| `src/UltrafastEngine.Core/CLAUDE.md` | when working in the engine | pipeline, folder map, how to add options / elements / models, physics invariants |
| `tests/UltrafastEngine.Tests/CLAUDE.md` | when working on tests | test framework, helpers, filters, tolerance policy |
| `python/CLAUDE.md` | when working on Python | hosted vs native engine, builders and generated types, Python tests |
| `plugins/CLAUDE.md` | when working on plugins | fitting and zemax structure; ZOS-API rule |
| `docs/CLAUDE.md` | when working on docs | style, generated pages, mkdocs strict, example pairings |
| `.claude/settings.json` | always | permissions (common build / test commands allowed without asking; ZOS-API folder and build output denied), environment (no .NET telemetry), hooks |
| `.claude/hooks/protect_paths.py` | before every edit | blocks edits to the ZOS-API assemblies, generated files (with the command that regenerates them), build output, third-party code |
| `.claude/hooks/after_edit.py` | after every edit | reminds of the follow-up a change needs (regenerate after a schema change, rebuild the Python engine after C#, run the physics checks after a propagation change) |
| `.claude/hooks/py.sh` | (hooks) | runs the hook scripts with `python3` or `python` (Windows) |
| `.claude/commands/*.md` | on `/name` | the project commands above |
| `.claude/agents/test-runner.md` | when delegated | runs long builds / tests / examples on a small model and returns only counts and failures, so build output does not fill the main conversation |
| `.claude/agents/physics-reviewer.md` | when delegated | audits a diff for equations, sign conventions, units, normalisations, energy accounting and model consistency |
| `.claude/agents/docs-sync.md` | when delegated | updates every page, README, docstring and status file a code change affects |
| `.claude/skills/engine-physics/` | when physics is involved | the conventions, equations (vector Kerr + Raman, B-integral), normalisations, grid / satellite rules and validation values in one place |
| `.claude/skills/run-simulations/` | when running the engine | writing .ufs / run.json, CLI and Python, measured data, reading results |

Files you may add for yourself (git-ignored): `CLAUDE.local.md` (personal notes for this checkout) and
`.claude/settings.local.json` (personal permissions).

## Why it is set up this way

* **Accuracy.** Physics changes go through `/physics-change`: the equation and its source are stated before the code
  changes, the test compares with an analytic or published value, the envelope, carrier-resolved and spatial models
  stay consistent, scalar results stay unchanged (pyNLO cross-checks), and `physics-reviewer` audits the result.
  The `engine-physics` skill keeps the conventions (E = Re{A e^{-iω0 t}}, +GDD red first, Raman tensor split,
  B-integral definition) in front of the agent.
* **Token economy.** Only `CLAUDE.md` is loaded for every session; folder files and skills load when relevant.
  The large generated files (about 700 kB: schemas, `types.py`, `options.md`) and build output are off limits to
  reading and editing -- the agent reads the source and regenerates. Long test output goes to `test-runner`
  (a small, fast model) and comes back as a few lines.
* **Safety.** The ZOS-API assemblies (Ansys property) cannot be edited, copied or packaged; generated files cannot
  be hand-edited out of step with their sources.

## Tips

* Ask for a plan first for big changes ("plan, don't edit yet") -- or press Shift+Tab for plan mode.
* Use `/clear` between unrelated tasks; `/compact` in long ones.
* `claude -p "/test NerTests"` runs a command non-interactively (CI, scripts).
* Keep `CLAUDE.md` short: add a line when the agent repeatedly gets something wrong, and put longer material in a
  folder `CLAUDE.md`, a skill or a docs page.
