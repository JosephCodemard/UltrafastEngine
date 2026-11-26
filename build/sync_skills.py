#!/usr/bin/env python3
"""The agent skills have one source: ``python/src/ultrafast/agents/pack/<name>/SKILL.md`` (shipped in the wheel and embedded in the CLI).

    python build/sync_skills.py            # copy them to .claude/skills/<name>/SKILL.md (what Claude Code reads in this repository)
    python build/sync_skills.py --check    # exit 1 when a copy differs (CI, light tests)

The user guide ``pack/AGENTS.md`` and the index ``pack/llms.txt`` also have one source (the pack); ``--check`` fails when either is missing.
``python/tests/test_agents.py`` greps every pack file for repository terms. The repository's own ``AGENTS.md`` / ``CLAUDE.md`` / ``llms.txt``
are for developers and are not copies of them.

Skills that only concern development of the engine itself (``.claude/skills/engine-physics`` ...) are not in the pack and are left alone.
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PACK = REPO / "python" / "src" / "ultrafast" / "agents" / "pack"
DEST = REPO / ".claude" / "skills"


def pairs():
    for d in sorted(PACK.iterdir()):
        if (d / "SKILL.md").is_file():
            yield d.name, (d / "SKILL.md"), DEST / d.name / "SKILL.md"


def main(argv=None) -> int:
    check = "--check" in (sys.argv[1:] if argv is None else argv)
    stale = []
    for name, src, dst in pairs():
        want = src.read_bytes()
        if not dst.is_file() or dst.read_bytes().replace(b"\r\n", b"\n") != want.replace(b"\r\n", b"\n"):
            stale.append(name)
            if not check:
                dst.parent.mkdir(parents=True, exist_ok=True)
                dst.write_bytes(want)
    missing = [n for n in ("AGENTS.md", "llms.txt") if not (PACK / n).is_file()]
    if missing:
        print("missing from the pack: " + ", ".join(missing))
    if stale:
        print(("differs: " if check else "updated: ") + ", ".join(stale))
    return 1 if ((stale and check) or missing) else 0


if __name__ == "__main__":
    raise SystemExit(main())
