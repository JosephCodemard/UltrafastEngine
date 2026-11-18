"""Write ROADMAP.md (repository root) from docs/roadmap.md: the same text, relative links made valid from the root.

    python build/sync_roadmap.py          # rewrite ROADMAP.md
    python build/sync_roadmap.py --check  # exit 1 if ROADMAP.md is out of date (used by python/tests/test_release.py)

``render(text)`` is the transformation: ``](page.md`` becomes ``](docs/page.md``; links that start with ``http``, ``#``, ``docs/`` or ``../`` are kept.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "docs" / "roadmap.md"
DST = REPO / "ROADMAP.md"


def render(text: str) -> str:
    def fix(m):
        t = m.group(1)
        if t.startswith(("http", "#", "docs/", "../", "mailto:")):
            return m.group(0)
        return "](docs/" + t
    return re.sub(r"\]\(([^)\s]+)", fix, text)


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    want = render(SRC.read_text(encoding="utf-8"))
    if "--check" in argv:
        have = DST.read_text(encoding="utf-8") if DST.exists() else ""
        if have.replace("\r\n", "\n") != want.replace("\r\n", "\n"):
            print("ROADMAP.md is out of date: run python build/sync_roadmap.py")
            return 1
        return 0
    DST.write_text(want, encoding="utf-8", newline="\n")
    print("wrote", DST)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
