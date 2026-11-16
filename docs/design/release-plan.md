# Release plan to 1.0.0 (written 2026-11-16)

Index of the work packages (WP) for the first public release. Each WP has its own spec and runs in its own Claude Code session on its own `dev/*` branch.
Rules of `CLAUDE.md` apply (tests and docs with every change, physics against a reference, never touch the ZOS-API DLLs, `Experiments/` off-limits).

## Owner decisions (2026-11-16)

| # | decision |
|---|---|
| 1 | PyPI: the owner has an account; upload with `twine`. This reverses backlog decision 2 ("no PyPI"). Nothing is uploaded without the owner's explicit go-ahead; ask for the PyPI details when WP-F starts. |
| 2 | First public version is **1.0.0** (internal 2.x numbering is retired; changelog is folded into the 1.0.0 entry). Check that the PyPI name is free before WP-F; the import name stays `ultrafast`. |
| 3 | `n2Scale` scales every third-order response of the element (Kerr, rotational Raman, XPW), not only Kerr. |
| 4 | Kerr lens in the 1-D beam model: whatever is most physically accurate (see [WP-D](wp-d-core-fixes.md), D2). |
| 5 | WP9 (restructuring all 60 example READMEs) is deferred past 1.0.0; only wave 1 and the landing pages. |
| 6 | MCP: local, workspace-sandboxed stdio only; no HTTP / remote transport. |
| 7 | Metal is deferred ("after 1.0"); the FP32 study still runs. |
| 8 | Agent pack covers Claude Code and Codex (AGENTS.md style) at minimum; Cursor if cheap. |
| 9 | Zemax is a plugin, not the product: remove it from the core docs and state its status honestly ([WP-E](wp-e-leftovers-zemax.md)). |
| 10 | A **fidelity switch** (cheap modes that turn off expensive physics) is added; the MCP server and agent skills must know it ([WP-C(A)](wp-ca-speed-mac.md), [WP-A](wp-a-agents-mcp.md)). |

## Order (one session each)

| order | WP | spec | needs the owner |
|---|---|---|---|
| 1 | D: core engine fixes + a sweep for other problems | [wp-d-core-fixes.md](wp-d-core-fixes.md) | no |
| 2 | E: leftovers + Zemax consolidation | [wp-e-leftovers-zemax.md](wp-e-leftovers-zemax.md) | no (no Zemax to test on) |
| 3 | C(A): speed on the Mac, parameter studies, fidelity switch | [wp-ca-speed-mac.md](wp-ca-speed-mac.md) | no |
| 4 | C(B): GPU on Windows / CUDA | [wp-cb-gpu-windows.md](wp-cb-gpu-windows.md) | the Windows + NVIDIA run |
| 5 | B: honest documentation | [wp-b-docs.md](wp-b-docs.md) | no |
| 6 | A: agents, skills, MCP | [wp-a-agents-mcp.md](wp-a-agents-mcp.md) | no |
| 7 | F: release pipeline | [wp-f-release.md](wp-f-release.md) | tag, PyPI approval |

WP-A comes after WP-C(A) so the pack documents the fidelity switch and the scan API; WP-B comes before WP-A so the agent pack links to final docs.
Each session ends with `/verify`, a green full suite, a changelog line and a short progress note appended to `docs/design/progress-2026-11-release.md`.
