#!/usr/bin/env python3
"""Run the ``make_figures.py`` of the examples: figures, and animations where an example has them.

    python docs/examples/make_all.py                          # every example
    python docs/examples/make_all.py --group Computing        # one group (also: python docs/examples/Computing/make_all.py)
    python docs/examples/make_all.py --only 4f-correlator shg-lbo   # by name or Group/name
    python docs/examples/make_all.py --no-animations --jobs 4 --report report.json

Prints a table (example, time, files written, problems) and exits 1 if an example fails or a file is over the size budget
(PNG 300 kB, GIF 2.5 MB). The CLI must be built first (``dotnet build UltrafastEngine.sln -c Release``). CI keeps
``build/example_check.py`` for the numbers; this script is for the person who wants the pictures.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "python" / "src"))
MEDIA = (".png", ".gif", ".mp4")


def discover():
    return sorted(p.parent for p in HERE.glob("*/*/make_figures.py"))


def snapshot(folder: Path) -> dict:
    return {str(p): p.stat().st_mtime_ns for p in folder.rglob("*") if p.is_file() and p.suffix in MEDIA}


def run_one(folder: Path, no_animations: bool, timeout: int) -> dict:
    name = folder.relative_to(HERE).as_posix()
    before = snapshot(folder)
    cmd = [sys.executable, "make_figures.py"] + (["--no-animations"] if no_animations else [])
    t = time.time()
    problems = []
    try:
        p = subprocess.run(cmd, cwd=folder, env=dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1", MPLBACKEND="Agg"),
                           capture_output=True, text=True, timeout=timeout)
        if p.returncode:
            problems.append("exit %d: %s" % (p.returncode, " | ".join((p.stdout + p.stderr).strip().splitlines()[-2:])[:300]))
    except subprocess.TimeoutExpired:
        problems.append("timeout after %d s" % timeout)
    after = snapshot(folder)
    files = sorted(os.path.relpath(f, folder) for f, m in after.items() if before.get(f) != m)
    from ultrafast.plotting.animate import check_budget
    for f, size, limit in check_budget(folder):
        if os.path.relpath(f, folder) in files:
            problems.append("%s is %d kB (budget %d kB)" % (os.path.relpath(f, folder), size // 1000, limit // 1000))
    return {"example": name, "seconds": round(time.time() - t, 1), "files": files, "problems": problems}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--group", help="one group, e.g. Computing")
    ap.add_argument("--only", nargs="+", help="example names or Group/name")
    ap.add_argument("--no-animations", action="store_true")
    ap.add_argument("--jobs", type=int, default=1, help="examples run in parallel (default 1)")
    ap.add_argument("--timeout", type=int, default=1800, help="seconds per example")
    ap.add_argument("--report", help="write the table as JSON")
    a = ap.parse_args(argv)
    todo = discover()
    if a.group:
        todo = [f for f in todo if f.parent.name == a.group]
    if a.only:
        todo = [f for f in todo if f.name in a.only or f.relative_to(HERE).as_posix() in a.only]
    if not todo:
        print("no example matches")
        return 2
    with ThreadPoolExecutor(max_workers=max(1, a.jobs)) as ex:
        rows = list(ex.map(lambda f: run_one(f, a.no_animations, a.timeout), todo))
    print("%-42s %8s  %-5s %s" % ("example", "time (s)", "files", "problems"))
    for r in rows:
        print("%-42s %8.1f  %-5d %s" % (r["example"], r["seconds"], len(r["files"]), "; ".join(r["problems"]) or "-"))
    bad = [r for r in rows if r["problems"]]
    print("%d examples, %d with problems" % (len(rows), len(bad)))
    if a.report:
        Path(a.report).write_text(json.dumps(rows, indent=1), encoding="utf-8")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
