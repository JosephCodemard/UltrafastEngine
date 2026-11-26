# MCP server and agent pack

`ultrafast-mcp` is a local [Model Context Protocol](https://modelcontextprotocol.io) server for agents that cannot or should not run a shell (decision E8).
It ships in the wheel (`pip install --pre ultrafast-engine`, the 1.0.0 alpha), needs no other package, speaks JSON-RPC 2.0 over **stdio only** (no network port) and is limited to
**one workspace folder**. It speaks the stateless revision 2026-07-28 (`server/discover`, per-request `_meta`) and the handshake revisions
2025-11-25, 2025-06-18, 2025-03-26 and 2024-11-05 (`initialize`), so current clients work. Start it by hand with `ultrafast-mcp --workspace DIR`
(also `python -m ultrafast.mcp`, or `ultrafast twin serve DIR`); options `--budget-s` (time budget of one run or scan, default 300) and
`--memory-limit-gb` (default 8).

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

| tool | what it does |
|---|---|
| `describe` | the elements of a system file (id, type, main numbers), topology, validation notes |
| `validate` | errors, warnings and notes of a system (and run spec) without propagating |
| `run` | one propagation; writes `.ultrafast-mcp/<name>/`; returns the output pulse, per-element B-integral and fluence, instrument values, `warnings`, `validity`, the `fidelityReport` and advice. `fidelity`: `quick`, `standard`, `full` |
| `scan` | a grid of one to four parameters (at most 400 points) with the output arrays; `fidelity` as above |
| `explain_warning` | what a warning text or a validity code means and what to do |
| `compare_to_measured` | runs a system whose instruments carry `measured` recordings and returns only the agreement per instrument: chi2, reduced chi2, rms error, scalar deviations, the notes (units, background, points left out); `name` also saves the results |
| `fit` | a run spec with a `fit` section (fitting plugin): returns the fitted values with uncertainties and chi2 before / after, writes `.ultrafast-mcp/<name>/` with the fitted system; `timeBudgetS` (at most the server budget) kills a slow fit, the next call works; the memory pre-flight applies |
| `study` | a parameter study through `uf.study`: `mode` `scan` (`sampling` grid, random, lhs, sobol), `optimize` (`objective`: min, max or target of one output) or `sensitivity` (local or sobol); parameters `{path, min, max, points}`, outputs like `pulse:end/fwhmS`; at most 400 evaluations; returns the rows and the analysis |
| `backward` | backward propagation (fitting plugin): a run spec with a `backward` section whose pulse is the field measured downstream; writes `.ultrafast-mcp/<name>/` including `reconstructed-pulse.json` and returns the steps, the instrument values at the planes upstream and warnings |
| `twin_fit`, `twin_validate`, `twin_predict` | the digital twin of a lab project (a folder of the workspace with `twin.json`; [twin plugin](../plugins/twin.md)): the multi-condition fit (time budget; writes `fits/<name>/`), the validation against held-out conditions (verdict, chi2 and coverage per condition, flags) and the prediction of a condition with a 1-sigma credible band (at most 200 points) and the output scalars with their uncertainty |
| `twin_design`, `twin_track`, `twin_init` | the next measurement to take (candidates ranked by expected information gain), drift tracking of new recordings (ensemble Kalman filter) and `ultrafast twin init` (a synthetic-lab project); see [Digital-twin tools](#digital-twin-tools) |
| `plot` | a figure of a finished run as PNG (`overview`, `system`, `instruments`, ...; styles `ultrafast`, `ultrafast-dark`, `paper`, `science`) |

Prompts (`prompts/list`, `prompts/get`): `explore-in-quick` (scan in `quick`, confirm the best points in `full`, report the level of every number) and `compare-to-measurement` (compare first, look for missing physics, fit only when the shapes agree, confirm in `full`).

Resources: `ultrafast://schema/run`, `ultrafast://schema/system` (JSON Schemas), `ultrafast://fidelity` (the levels and the workflow), `ultrafast://examples` (index).

**The workflow the server asks for: explore in `quick`, confirm in `full`.** Scan or run in `quick`; re-run the one to three points you will report in `full`;
read `warnings` and `validity` of every result (`explain_warning`); say which numbers came from which level ([Fidelity](fidelity.md)).

**What the server refuses** (a tool result with `isError: true` and the reason; the agent can correct the call):

* strings that start with `@` (callbacks and field references), `kind` / `type` of `callback`, `python`, `expression` or `user`: no user code runs. Files you name are read and checked like inline objects;
* paths outside the workspace, paths through a symbolic link or Windows junction, and any string in a system or run that is absolute, goes up with `..` or ends in a data extension (`.csv`, `.npz`, `.json`, ...) unless it resolves inside the workspace (relative paths in a file are taken from that file's folder);
* names for result folders that are not 1 to 64 letters, digits, `_`, `-`, `.`; unknown, missing or mistyped arguments;
* scan paths that do not start with `pulse/`, `element:` or `instrument:`; more than 4 axes or 400 points; a run spec with a `study`, `backward` or `fit` section in every tool but the one of that name (the `study` tool takes its parameters and outputs as arguments); a study of more than 400 evaluations; twin folders outside the workspace, and a twin project whose files fail the same checks as any system or run; the limits of `twin_design` and `twin_track` ([Digital-twin tools](#digital-twin-tools)); `compare_to_measured` without any instrument that has `measured` data;
* a request above 4 MB, and a run or scan whose memory estimate is above the limit or 60 % of the free memory (before it starts).

**How it runs.** The server process only parses messages and applies these rules. The engine runs in one persistent child process with an empty private
`ULTRAFAST_HOME` (no user plugins or settings) and the workspace as working directory. A call that uses its time budget is **killed**; the next call starts a new
child. Calls are handled one at a time. The sandbox is the policy above plus a separate process, not an operating-system jail: run the server as a user that may
read only what you want the agent to see.

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

`tests/agent-evals/` holds seven prompts with the criteria a good answer meets (explore in `quick` then confirm in `full`; refuse user code and offer a built-in
instrument; read warnings and validity; compare then fit; plot and report; optimise with the study tool; fit, validate and predict with a twin).

* `run_evals.py` replays a reference trajectory for each through the real server. This checks that the tools give an agent what each criterion needs; it runs no language model.
* `run_live.py` gives each prompt to a **live agent** (`claude -p`, with only the ultrafast MCP tools, the agent pack installed in a fresh workspace), records the tool calls and
  scores every criterion that a program can check (fidelity levels, order of the calls, the refusal, the figure); the rest are listed for a person to read. It uses your login and spends tokens
  (`python3 tests/agent-evals/run_live.py --model sonnet --only quick-then-confirm`).

Live results of the mandatory prompt (2026-11-18, Claude Code, a fresh workspace; the transcripts vary from run to run):

| agent | result |
|---|---|
| Sonnet | explored in `full` before the fidelity rule was written into the server instructions and the skills (fail); afterwards `quick` scan, `full` confirmation, levels named (pass in 2 of 3 runs; the failed run omitted the fidelity argument of its last run, which counts as `full`: the scorer now reads it so) |
| Haiku | `quick` scan, `full` confirmation, levels named (pass) |

The lesson is in the server's instructions and the skills: the default fidelity is `full`, so the rule "every exploratory call passes `quick`" is stated there in so many words.
