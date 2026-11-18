# MCP server and agent pack

`ultrafast-mcp` is a local [Model Context Protocol](https://modelcontextprotocol.io) server for agents that cannot or should not run a shell (decision E8).
It ships in the wheel (`pip install ultrafast-engine`), needs no other package, speaks JSON-RPC 2.0 over **stdio only** (no network port) and is limited to
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
| `plot` | a figure of a finished run as PNG (`overview`, `system`, `instruments`, ...; styles `ultrafast`, `ultrafast-dark`, `paper`, `science`) |

Prompts (`prompts/list`, `prompts/get`): `explore-in-quick` (scan in `quick`, confirm the best points in `full`, report the level of every number) and `compare-to-measurement` (compare first, look for missing physics, fit only when the shapes agree, confirm in `full`).

Resources: `ultrafast://schema/run`, `ultrafast://schema/system` (JSON Schemas), `ultrafast://fidelity` (the levels and the workflow), `ultrafast://examples` (index).

**The workflow the server asks for: explore in `quick`, confirm in `full`.** Scan or run in `quick`; re-run the one to three points you will report in `full`;
read `warnings` and `validity` of every result (`explain_warning`); say which numbers came from which level ([Fidelity](fidelity.md)).

**What the server refuses** (a tool result with `isError: true` and the reason; the agent can correct the call):

* strings that start with `@` (callbacks and field references), `kind` / `type` of `callback`, `python`, `expression` or `user`: no user code runs. Files you name are read and checked like inline objects;
* paths outside the workspace, paths through a symbolic link or Windows junction, and any string in a system or run that is absolute, goes up with `..` or ends in a data extension (`.csv`, `.npz`, `.json`, ...) unless it resolves inside the workspace (relative paths in a file are taken from that file's folder);
* names for result folders that are not 1 to 64 letters, digits, `_`, `-`, `.`; unknown, missing or mistyped arguments;
* scan paths that do not start with `pulse/`, `element:` or `instrument:`; more than 4 axes or 400 points; a run spec with a `study` or `backward` section (and with a `fit` section in every tool but `fit`); `compare_to_measured` without any instrument that has `measured` data;
* a request above 4 MB, and a run or scan whose memory estimate is above the limit or 60 % of the free memory (before it starts).

**How it runs.** The server process only parses messages and applies these rules. The engine runs in one persistent child process with an empty private
`ULTRAFAST_HOME` (no user plugins or settings) and the workspace as working directory. A call that uses its time budget is **killed**; the next call starts a new
child. Calls are handled one at a time. The sandbox is the policy above plus a separate process, not an operating-system jail: run the server as a user that may
read only what you want the agent to see.

## Agent pack and evals

`ultrafast-agent install --target claude|codex|cursor [--dest DIR] [--force]` writes the five skills of the wheel (`run-simulations`, `debug-setup`,
`mirror-lab-setup`, `optimise-setup`, `replicate-paper`) where the agent looks for them: `.claude/skills/<name>/SKILL.md`, a marked block in `AGENTS.md` (re-install replaces
only that block), or `.cursor/rules/ultrafast-<name>.mdc`. `ultrafast-agent list` shows them; `ultrafast-agent mcp-config [--client codex]` prints the server entry
for the client's configuration. The `design-system` skill and the split of the development `.claude/` folder from the shipped pack are still open ([roadmap](../roadmap.md)).

`tests/agent-evals/` holds five prompts with the criteria a good answer meets (explore in `quick` then confirm in `full`; refuse user code and offer a built-in
instrument; read warnings and validity; compare then fit; plot and report) and `run_evals.py`, which replays a reference trajectory for each through the real server.
This checks that the tools give an agent what each criterion needs; it does not run a language model. To evaluate a live agent, give it a prompt and a workspace from
`make_workspace` and score its transcript against the criteria. Results of live runs are not recorded yet.
