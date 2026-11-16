# WP-A: agents, skills and MCP

Session 6.

## A1. Split the two audiences

`.claude/` stays for development (bug-hunter, physics-reviewer, test-runner, docs-sync, release commands). A new shipped `agents/` pack holds user-facing skills and agents:
`run-simulations`, `debug-setup`, `mirror-lab-setup`, `optimise-setup`, `replicate-paper`, and a new `design-system` (build a system from a goal).
* `ultrafast agent install [--target claude|codex|cursor]` copies the pack into the user's project (Claude: `.claude/skills`, `.claude/agents`; Codex: `AGENTS.md` + skill folder; Cursor if cheap).
* The pack is a release component; `llms.txt` and `AGENTS.md` are rewritten for users (development rules move to a dev-only file).
* Each skill has a test that runs its code snippets, so skills cannot drift from the engine.
* Skills teach the **fidelity workflow** from WP-C(A): explore with `fidelity: quick`, confirm the best points in `full`, report the `fidelityReport` and the unreliability warnings.

## A2. MCP server: `ultrafast mcp`

Moved out of `twin serve` (which currently exits with 2). Design in `next-steps.md` §4: hand-written JSON-RPC 2.0 over newline-delimited stdio, no dependency; check the current MCP spec for `protocolVersion` first.
Eight tools: `describe`, `validate`, `run`, `compare_to_measured`, `scan`, `fit`, `explain_warning`, `plot`; `run` and `scan` take `fidelity`. Sandboxed persistent worker (killed and respawned on overrun, pre-flight cost estimate refuses oversized runs, memory caps, empty private `ULTRAFAST_HOME`).
Refusals as `isError` results: `@` strings, callbacks / python / expression kinds, `set` outside `pulse/`, `element:`, `instrument:`, paths outside the workspace, bad names. Local stdio only, no HTTP (owner decision).
Also **resources** (schemas, reference pages, examples index, the fidelity table) and a few **prompts**, so an agent need not read the 440 kB schema. Ships in the wheel as `ultrafast-mcp` (uvx / pipx).
Tests: `tools/list` has eight tools; refusals for an expression, an `@name`, `../x.csv`, an absolute path, a symlink out; a call over budget is killed and the next call still works.

## A3. Agent evals

Five or six prompts ("build a 30 fs Yb chain with a stretcher", "why doesn't my spectrum match", "quick scan of the compressor GDD, then confirm", ...), driven through the MCP tools; each must yield a strict-valid `.ufs` and the right physics against a stated check. Results in `docs/agents.md`; repeatable script in `tests/agent-evals/`. The quick-then-confirm prompt is mandatory.
