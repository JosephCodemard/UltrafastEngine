"""The agent pack: skills that teach a coding agent to drive UltrafastEngine, and the command that installs them.

``pack/<name>/SKILL.md`` holds the skills shipped in the wheel: ``run-simulations``, ``debug-setup``, ``mirror-lab-setup``,
``optimise-setup``, ``replicate-paper`` and ``design-system`` (the same text as ``.claude/skills/`` in the repository; a test keeps the two equal).
``install(target, dest)`` writes them where an agent looks for them:

* ``claude``: ``<dest>/.claude/skills/<name>/SKILL.md`` (Claude Code skills);
* ``codex``: one block between ``<!-- ultrafast-agent-pack -->`` markers in ``<dest>/AGENTS.md`` (replaced on re-install, the rest of the file kept);
* ``cursor``: ``<dest>/.cursor/rules/ultrafast-<name>.mdc`` (rules that apply when the agent decides they are relevant).

Command line: ``ultrafast-agent list`` and ``ultrafast-agent install --target claude|codex|cursor [--dest DIR] [--force]``; ``ultrafast-agent mcp-config``
prints the MCP server configuration. Nothing outside ``dest`` is touched, and an existing skill file is overwritten only with ``--force``.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional

__all__ = ["skills", "install", "mcp_config", "main", "TARGETS"]

TARGETS = ("claude", "codex", "cursor")
PACK = Path(__file__).with_name("pack")
FOOTER = ("\n\n---\nThe paths `docs/...` in this skill are pages of the documentation site (https://josephcodemard.github.io/UltrafastEngine/) and, in a source checkout, "
          "files of the repository. The MCP server (`ultrafast-mcp`, `ultrafast-agent mcp-config`) gives the same workflow as tools.\n")
BEGIN, END = "<!-- ultrafast-agent-pack -->", "<!-- /ultrafast-agent-pack -->"


def skills() -> Dict[str, str]:
    """``{name: SKILL.md text}`` of the shipped skills."""
    return {d.name: (d / "SKILL.md").read_text(encoding="utf-8") for d in sorted(PACK.iterdir()) if (d / "SKILL.md").is_file()}


def _split(text: str):
    m = re.match(r"^---\n(.*?)\n---\n(.*)$", text, re.S)
    if not m:
        raise ValueError("skill without front matter")
    meta = dict(re.findall(r"^(\w+):\s*(.*)$", m.group(1), re.M))
    return meta, m.group(2).lstrip("\n")


def _write(p: Path, text: str) -> None:
    """Write UTF-8 with LF line ends on every OS (write_text gives CRLF on Windows; ``ultrafast agent install`` writes LF)."""
    p.write_bytes(text.encode("utf-8"))


def install(target: str, dest: Path, force: bool = False) -> List[Path]:
    """Write the pack for ``target`` under ``dest``; returns the files written. Raises ``FileExistsError`` for an existing skill file unless ``force``."""
    if target not in TARGETS:
        raise ValueError("target must be one of %s" % ", ".join(TARGETS))
    dest = Path(dest)
    written: List[Path] = []
    sk = skills()
    if target == "claude":
        for name, text in sk.items():
            p = dest / ".claude" / "skills" / name / "SKILL.md"
            if p.exists() and not force:
                raise FileExistsError("%s exists (use --force to overwrite)" % p)
            p.parent.mkdir(parents=True, exist_ok=True)
            _write(p, text.rstrip("\n") + FOOTER)
            written.append(p)
    elif target == "cursor":
        for name, text in sk.items():
            meta, body = _split(text)
            p = dest / ".cursor" / "rules" / ("ultrafast-%s.mdc" % name)
            if p.exists() and not force:
                raise FileExistsError("%s exists (use --force to overwrite)" % p)
            p.parent.mkdir(parents=True, exist_ok=True)
            _write(p, "---\ndescription: %s\nalwaysApply: false\n---\n%s" % (meta.get("description", name), body.rstrip("\n") + FOOTER))
            written.append(p)
    else:
        block = [BEGIN, "# UltrafastEngine skills (installed by `ultrafast-agent install --target codex`)", ""]
        for name, text in sk.items():
            meta, body = _split(text)
            block += ["## Skill: %s" % name, "", "_When: %s_" % meta.get("description", ""), "", body.rstrip("\n"), ""]
        block += [FOOTER.strip(), END, ""]
        p = dest / "AGENTS.md"
        old = p.read_text(encoding="utf-8") if p.exists() else ""
        if BEGIN in old and END in old:
            i, j = old.index(BEGIN), old.index(END) + len(END)
            new = old[:i] + "\n".join(block).rstrip("\n") + old[j:]
        else:
            new = (old.rstrip("\n") + "\n\n" if old.strip() else "") + "\n".join(block)
        p.parent.mkdir(parents=True, exist_ok=True)
        _write(p, new)
        written.append(p)
    return written


def mcp_config(workspace: str = ".", client: str = "claude") -> str:
    """The server entry for an MCP client's configuration (JSON text)."""
    entry = {"command": "ultrafast-mcp", "args": ["--workspace", str(workspace)]}
    if client == "codex":
        return '[mcp_servers.ultrafast]\ncommand = "ultrafast-mcp"\nargs = ["--workspace", %s]\n' % json.dumps(str(workspace))
    return json.dumps({"mcpServers": {"ultrafast": entry}}, indent=2) + "\n"


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="ultrafast-agent", description="Install the UltrafastEngine skills for a coding agent (docs/agents.md).")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list", help="list the skills of the pack")
    ins = sub.add_parser("install", help="write the skills where the agent looks for them")
    ins.add_argument("--target", required=True, choices=TARGETS)
    ins.add_argument("--dest", default=".", help="project folder (default: the current folder)")
    ins.add_argument("--force", action="store_true", help="overwrite existing skill files")
    mc = sub.add_parser("mcp-config", help="print the MCP server configuration")
    mc.add_argument("--workspace", default=".")
    mc.add_argument("--client", choices=("claude", "codex"), default="claude")
    a = ap.parse_args(argv)
    if a.cmd == "list":
        for n, t in skills().items():
            print("%-18s %s" % (n, _split(t)[0].get("description", "")[:100]))
        return 0
    if a.cmd == "mcp-config":
        sys.stdout.write(mcp_config(a.workspace, a.client))
        return 0
    try:
        files = install(a.target, Path(a.dest), a.force)
    except (FileExistsError, ValueError) as e:
        print("ultrafast-agent: %s" % e, file=sys.stderr)
        return 2
    for f in files:
        print(f)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
