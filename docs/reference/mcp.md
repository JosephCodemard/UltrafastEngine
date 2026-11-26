# MCP server and agent pack

`ultrafast-mcp` is a local [Model Context Protocol](https://modelcontextprotocol.io) server for agents that cannot or should not run a shell (decision E8).
It ships in the wheel (`pip install --pre ultrafast-engine`, the 1.0.0 alpha), needs no other package, speaks JSON-RPC 2.0 over stdio only (no network port) and is limited to
one workspace folder. It speaks the stateless revision 2026-07-28 (`server/discover`, per-request `_meta`) and the handshake revisions
2025-11-25, 2025-06-18, 2025-03-26 and 2024-11-05 (`initialize`), so current clients work. Use it when an agent drives the engine through tool calls.

## Keys

Start it by hand with `ultrafast-mcp --workspace DIR` (also `python -m ultrafast.mcp`, or `ultrafast twin serve DIR`). Options:
`--budget-s` (time budget in seconds, default 300; see below for what it covers) and `--memory-limit-gb` (default 8).

**Claude Code** (project `.mcp.json`, or `claude mcp add ultrafast -- ultrafast-mcp --workspace /path/to/workspace`):

```json
{ "mcpServers": { "ultrafast": { "command": "ultrafast-mcp", "args": ["--workspace", "/path/to/workspace"] } } }
```

**Codex** (`~/.codex/config.toml`):

```toml
[mcp_servers.ultrafast]
command = "ultrafast-mcp"
args = ["--workspace", "/path/to/workspace"]
```

If `ultrafast-mcp` is not on the `PATH`, use `"command": "python", "args": ["-m", "ultrafast.mcp", "--workspace", "..."]` with the interpreter that has the package.

`system`, `run` and the other file arguments are either a path inside the workspace or the JSON object itself. Every tool except
`explain_warning` needs its required arguments; unknown or mistyped arguments are refused. `fidelity` is `quick`, `standard` or `full` (default `full`).

The server has sixteen tools:

| tool | what it does |
|---|---|
| `describe` | the elements of a system file (id, type, main numbers), topology, validation notes |
| `validate` | errors, warnings and notes of a system (and run spec) without propagating |
| `run` | one propagation; arguments `system`, `run`, `name`, `fidelity`. Writes `.ultrafast-mcp/<name>/`; returns the output pulse, per-element B-integral and fluence, instrument values, `warnings`, `validity`, the `fidelityReport` and advice |
| `scan` | a grid of 1 to 4 `axes` (`{path: [values]}`, at most 400 points) and 1 to 20 `outputs`; `parallel` (1 to 4 points at once, default 1) and `fidelity`. Returns the output arrays |
| `explain_warning` | what a warning text (`warning`) or a validity code (`code`) means and what to do |
| `compare_to_measured` | runs a system whose instruments carry `measured` recordings and returns only the agreement per instrument: chi2, reduced chi2, rms error, scalar deviations, the notes (units, background, points left out); `name` also saves the results |
| `fit` | a run spec with a `fit` section (fitting plugin): returns the fitted values with uncertainties and χ² before / after, writes `.ultrafast-mcp/<name>/` with the fitted system; `timeBudgetS` (at most the server budget) kills a slow fit, the next call works |
| `study` | a parameter study through `uf.study`. `mode`: `scan` (`sampling` grid, random, lhs or sobol; `samples` for the last three, default 64), `optimize` (`objective`: `{output, goal: min or max or target, target?, maxEvaluations?}`; `maxEvaluations` defaults to 100 here (200 in a [study file](studies.md))) or `sensitivity` (`{method: local or sobol, samples?}`). 1 to 4 `parameters` `{path, min, max, points}` (or `values`, `scale`), 1 to 20 `outputs` like `pulse:end/fwhmS`; `seed`, `parallel`, `fidelity`. At most 400 evaluations; returns the rows and the analysis |
| `backward` | backward propagation (fitting plugin): a run spec with a `backward` section whose pulse is the field measured downstream; writes `.ultrafast-mcp/<name>/` including `reconstructed-pulse.json` and returns the steps, the instrument values at the planes upstream and warnings |
| `twin_fit`, `twin_validate`, `twin_predict` | the digital twin of a lab project (a folder of the workspace with `twin.json`; [twin plugin](../plugins/twin.md)): the multi-condition fit (writes `fits/<name>/`), the validation against held-out conditions (verdict, χ² and coverage per condition, flags) and the prediction of a condition with a 1-sigma credible band (at most 200 points) and the output scalars with their uncertainty |
| `twin_design`, `twin_track`, `twin_init` | the next measurement to take (candidates ranked by expected information gain), drift tracking of new recordings (ensemble Kalman filter) and `ultrafast twin init` (a synthetic-lab project); see [Digital-twin tools](#digital-twin-tools) |
| `plot` | a figure of a finished run as PNG. `figure`: `overview`, `system`, `instruments`, `measurements`, `probes`, `elements`, `byproducts`, `gain`, `oscillator`, `spectrogram_3d`; `style`: `ultrafast`, `ultrafast-dark`, `paper`, `science`; `column` (`single` or `double`, paper styles); `dpi` 50 to 200 (default 110, so the 600 dpi of the paper styles is not reached). An image above 900 kB is saved but not returned inline |

Prompts (`prompts/list`, `prompts/get`): `explore-in-quick` (scan in `quick`, confirm the best points in `full`, report the level of every number) and `compare-to-measurement` (compare first, look for missing physics, fit only when the shapes agree, confirm in `full`).

Resources: `ultrafast://schema/run`, `ultrafast://schema/system` (JSON Schemas), `ultrafast://fidelity` (the levels and the workflow), `ultrafast://examples` (index).

**The workflow the server asks for: explore in `quick`, confirm in `full`.** Scan or run in `quick`; re-run the one to three points you will report in `full`;
read `warnings` and `validity` of every result (`explain_warning`); say which numbers came from which level ([Fidelity](fidelity.md)).

## Model

The server process only parses messages and applies the rules under Limits. The engine runs in one persistent child process with an empty private
`ULTRAFAST_HOME` (no user plugins or settings) and the workspace as working directory. Calls are handled one at a time.

**Time budget.** `--budget-s` caps `run`, `scan`, `compare_to_measured`, `study`, `backward`, `twin_fit`, `twin_validate`, `twin_predict` and `twin_init`. `fit`,
`twin_design` and `twin_track` take `timeBudgetS`, capped at the budget. `describe` and `validate` are capped at 60 s, `plot` at 120 s and
`explain_warning` at 5 s. A call that uses its time budget is killed; the next call starts a new child.

**Memory pre-flight.** Before `run`, `scan`, `compare_to_measured`, `fit`, `study` and `backward` start, the engine's memory estimate is
compared with the memory limit and with 60 % of the free memory; above either, the call is refused. For `scan` and `study` the estimate
is multiplied by `parallel`.

The sandbox is the policy under Limits plus a separate process, not an operating-system jail: run the server as a user that may
read only what you want the agent to see.


## Validation

`python/tests/test_mcp.py` drives the real server: the handshakes (`test_initialize_legacy`, `test_discover_and_modern_requests`,
`test_stdio_process_handshake`), the sixteen tools with schemas (`test_tools_list_has_sixteen_tools_with_schemas`), every refusal
below (`test_refusals_are_tool_errors_and_never_reach_the_worker`, `test_absolute_path_outside_workspace_and_symlink_refused`,
`test_file_arguments_get_the_same_policy_as_inline_ones`, `test_request_size_refused_before_the_worker`, `test_study_is_checked_before_the_worker`),
the budget (`test_call_over_budget_is_killed_and_the_next_call_works`, `test_fit_is_killed_at_its_own_time_budget_and_the_next_call_works`,
`test_twin_design_and_track_over_their_budget_are_killed_and_the_next_call_works`)
and real runs (`test_real_run_validate_plot`, `test_real_compare_to_measured_and_fit`, `test_real_study_backward_and_twin`, `test_real_twin_init_design_and_track`). The twin tools: [Digital-twin tools](#digital-twin-tools).

## Limits

What the server refuses (a tool result with `isError: true` and the reason; the agent can correct the call):

* Strings that start with `@` (callbacks and field references); `kind` or `type` of `callback`, `python`, `expression` or `user`; the keys `callback` and `callbacks`, whatever their value. No user code runs. Files you name are read and checked like inline objects; a system file or run spec larger than 8 MB is refused.
* Paths outside the workspace, paths through a symbolic link or Windows junction, and any string in a system or run that is absolute, goes up with `..` or ends in a data extension (`.csv`, `.npz`, `.npy`, `.json`, `.ufs`, `.txt`, `.dat`, `.tsv`, `.h5`, `.png`, ...) unless it resolves inside the workspace (relative paths in a file are taken from that file's folder). Keys ending in `file`, `folder`, `dir` or `directory`, and any `path` key that is not a study path, are path-checked whatever the value.
* Result folder names that are not 1 to 64 letters, digits, `_`, `-`, `.`, starting with a letter or digit, not ending in `.` and without `..`.
* Scan paths that do not start with `pulse/`, `element:` or `instrument:`; more than 4 axes or 400 points; a run spec with a `study`, `backward` or `fit` section in every tool but the one of that name (the `study` tool takes its parameters and outputs as arguments); a study of more than 400 evaluations; twin folders outside the workspace, and a twin project whose files fail the same checks as any system or run; the limits of `twin_design`, `twin_track` and `twin_init` ([Digital-twin tools](#digital-twin-tools)); `compare_to_measured` without any instrument that has `measured` data.
* A request above 4 MB, and a call whose memory estimate is above the limit or 60 % of the free memory (before it starts).

Not done: no network access, no operating-system sandbox, no concurrent calls.

## Digital-twin tools

Six tools work on a twin project, a folder of the workspace with `twin.json` ([twin plugin](../plugins/twin.md)). They call the plugin's own functions
(`ultrafast_twin`), so a result is the same as from `ultrafast twin <command>`. The usual order: `twin_init` (or a hand-written `twin.json`), `twin_fit`,
`twin_validate`, then `twin_predict`, `twin_design` and `twin_track`. `twin_design` and `twin_track` need a fit (`fits/latest.txt`).

### Keys

* `twin_init`: `folder` (created inside the workspace), `template` (`synthetic-lab`, default, or `synthetic-lab-saturating`), `force` (overwrite an existing `twin.json`).
  Returns the conditions (training and held out) and the fitted parameters. Same as `ultrafast twin init FOLDER --template T [--force]`.
* `twin_design`: `folder`; exactly one candidate set: `variable` with `values` (SI units; `setKey`, e.g. `pulse/energyJ`, also receives the value), `placement: true`
  (the instrument at the input, after each element and at the end) or `candidates` (`[{name, variables, set}]`; `set` takes numbers, and an element id for
  `instrument:NAME/at`). `picks` (chosen greedily, default 1), `weights` (one per fitted parameter: A-optimal score), `noiseSigma` (noise of the new reading, peak-normalised;
  default the project's `sigma`), `timeBudgetS`. Returns `ranking` (gain and posterior sigma per candidate), `chosen`, `currentSigma`, `engineRuns`; writes `reports/design.json`.
* `twin_track`: `folder`, `measurements` (`[{data, variables, set, label}]` in time order; `data` is a recorded file, relative to the project folder), `parameters`
  (default: every shared parameter), `process` (`{name: drift per measurement}` in the parameter's unit), `members` (4 to 32, default 16), `alarmSigmas` (default 3),
  `seed`, `timeBudgetS`. Returns `history` (mean, sigma, `zFromFit` and `drifted` after each recording), the last `drifted` list; writes `reports/track.json`.

### Model

`twin_design`: for a candidate, the engine reading y(θ) at the fitted parameters and one finite-difference run per parameter give the Jacobian J; the expected
posterior covariance is C' = (C⁻¹ + JᵀJ / σ²)⁻¹, with C the fit's covariance and σ the noise of the reading. The score is the expected information gain
½ (log det C − log det C') (D-optimal) or, with weights w, Σ wᵢ (Cᵢᵢ − C'ᵢᵢ) (A-optimal). It is a linearised expectation at the fitted point: it says where the twin is
blind, not what the data will show.

`twin_track`: an ensemble Kalman filter (perturbed observations) whose members start as draws from the fit's Gaussian posterior. Before each recording every member takes a
random-walk step (`process`, else the fitted sigma) and the ensemble spread is inflated by 1.02; the observation operator is the engine (one run per member). A parameter
is reported in `drifted` when its mean is more than `alarmSigmas` fitted sigmas from the fit. Details: `ultrafast_twin/design.py` and `ultrafast_twin/track.py`.

### Validation

* `test_twin.py::test_the_planner_ranks_the_placements_and_a_zero_energy_shot_does_not_see_the_slope`: a shot at zero energy carries no information on the energy slope.
* `test_twin.py::test_the_drift_tracker_follows_a_shifted_filter_centre_and_flags_it`: a 0.5 nm shift of the hidden filter centre is followed to within 0.15 nm and flagged.
* `test_mcp.py::test_real_twin_init_design_and_track`: the same functions through the server on the synthetic lab: `twin_init` writes 7 conditions (2 held out), a
  2 µJ shot has a larger gain than a zero-energy one, and a recording of the unchanged lab is tracked without a drift flag.
* `test_mcp.py::test_twin_design_track_init_refusals_never_reach_the_worker` (every refusal below) and
  `test_twin_design_and_track_over_their_budget_are_killed_and_the_next_call_works`.

### Limits

* At most 12 candidates per `twin_design` call and 240 engine runs (candidates × (fitted parameters + 1)); at most 12 recordings and 192 engine runs
  (members × recordings) per `twin_track` call. Above these the call is refused; split it into several calls.
* `timeBudgetS` is capped by the server's `--budget-s`; a call over its budget is killed and the next call starts a new worker. The tracker state is not kept between
  calls: pass every recording since the fit in one call (each call starts again from the fit).
* Refused: more or less than one candidate set, `values` or `setKey` without `variable`, duplicate candidate names, `set` paths outside `pulse/`, `element:`, `instrument:`,
  values that are not finite numbers, `picks` above the number of candidates, recordings outside the workspace or above 8 MB, `twin_init` in a folder that already holds
  `twin.json` (without `force`), on a file, or inside `.ultrafast-mcp/`.
* `twin_init` makes only the synthetic labs; a project of a real lab is a `twin.json` written by hand ([twin plugin](../plugins/twin.md)).

## Agent pack and evals

`ultrafast-agent install --target claude|codex|cursor [--dest DIR] [--force]` writes the six skills of the wheel (`run-simulations`, `debug-setup`,
`mirror-lab-setup`, `optimise-setup`, `replicate-paper`, `design-system`) where the agent looks for them: `.claude/skills/<name>/SKILL.md`, a marked block in `AGENTS.md` (re-install replaces
only that block), or `.cursor/rules/ultrafast-<name>.mdc`. `ultrafast-agent list` shows them; `ultrafast-agent mcp-config [--client codex]` prints the server entry
for the client's configuration. The command `ultrafast agent list | install | mcp-config` of the C# command line does the same with the same options and writes byte-identical files
(the skills are embedded in the `ultrafast` program), so a user who installed only the CLI needs no Python.

Besides the skills, every target writes the user guide (how to write system files and run specs, run, read results and use the MCP server): `.claude/ultrafast/AGENTS.md` (add `@.claude/ultrafast/AGENTS.md` to your `CLAUDE.md` to load it), an always-on `.cursor/rules/ultrafast-guide.mdc`, or the first part of the marked block in `AGENTS.md`; and the documentation index `.ultrafast/llms.txt`. The guide and the index describe the use of the engine only.

`tests/agent-evals/` holds seven prompts with the criteria a good answer meets (explore in `quick` then confirm in `full`; refuse user code and offer a built-in
instrument; read warnings and validity; compare then fit; plot and report; optimise with the study tool; fit, validate and predict with a twin).

* `run_evals.py` replays a reference trajectory for each through the real server. This checks that the tools give an agent what each criterion needs; it runs no language model.
* `run_live.py` gives each prompt to a **live agent** (with only the ultrafast MCP tools and the agent pack installed in a fresh workspace), records the tool calls and
  scores every criterion that a program can check (fidelity levels, order of the calls, the refusal, the figure); the rest are listed for a person to read. It uses the agent's
  login and spends tokens. `--agent claude|codex|cursor` chooses the agent; `--model` is passed to it; `--only ID` runs one prompt; `--out FILE` saves the scores.
* `run_live.py --transcript FILE` runs nothing: it scores a saved transcript (the standard output of the agent's JSON mode). The format is detected, or given with `--agent`; the eval is
  the first dotted part of the file name (`quick-then-confirm.run1.jsonl`) or `--only ID`. Exit code 1 when a mandatory eval fails a checked criterion.

How the owner runs a live score (each writes the transcripts it scored with `--out`):

| agent | command | transcript format read |
|---|---|---|
| Claude Code | `python3 tests/agent-evals/run_live.py --agent claude --model sonnet --only quick-then-confirm` (`claude -p --output-format stream-json`; tools `mcp__ultrafast__*` only) | [stream-json](https://code.claude.com/docs/en/headless) |
| OpenAI Codex CLI | `python3 tests/agent-evals/run_live.py --agent codex --model MODEL` (`codex exec --json`, read-only sandbox, server added with `-c mcp_servers.ultrafast...`) | [JSONL events](https://developers.openai.com/codex/noninteractive) |
| Cursor | `python3 tests/agent-evals/run_live.py --agent cursor --model MODEL` (`cursor-agent -p --output-format stream-json --approve-mcps`, server in `.cursor/mcp.json`) | [stream-json](https://cursor.com/docs/cli/reference/output-format) |

Each adapter (`parse_claude`, `parse_codex`, `parse_cursor` in `run_live.py`) turns its format into the structure the scorers use: the ultrafast tool calls with name, arguments, result
and error flag, the final answer, and the files the agent wrote. Two points are not documented by the vendors and are implemented from their open-source event definitions or from
observed output: the fields of a Codex `mcp_tool_call` item (the page names the type and shows only `agent_message` and `command_execution`), and the shape of a Cursor MCP tool call (the
page documents `readToolCall` and `writeToolCall` only). If a CLI version changes them, fix the adapter and the hand-made replays in `tests/agent-evals/replays/`
(`<eval>.<pass|fail>.<agent>.jsonl`); `python/tests/test_agent_eval_adapters.py` checks that the same behaviour in the three formats gives the same trace and the same verdict. The live
runs of Codex and Cursor have not been done (no model was called when the adapters were written); the replays check the readers, not the agents.

Live results of the mandatory prompt (2026-11-18, Claude Code, a fresh workspace; the transcripts vary from run to run):

| agent | result |
|---|---|
| Sonnet | explored in `full` before the fidelity rule was written into the server instructions and the skills (fail); afterwards `quick` scan, `full` confirmation, levels named (pass in 2 of 3 runs; the failed run omitted the fidelity argument of its last run, which counts as `full`: the scorer now reads it so) |
| Haiku | `quick` scan, `full` confirmation, levels named (pass) |

The lesson is in the server's instructions and the skills: the default fidelity is `full`, so the rule "every exploratory call passes `quick`" is stated there in so many words.
