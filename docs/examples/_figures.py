"""Shared by the ``make_figures.py`` of every example (not an example itself).

``make_figures.py`` in an example folder runs the commands of that folder's README (the same lines ``build/example_check.py`` runs,
with ``ULTRAFAST_STRICT_OPTIONS=1``) and then writes figures to ``figures/`` next to the README:

* ``overview-<results folder>.png`` for every results folder (``summary.json`` + ``result.npz``) the commands wrote: the standard
  overview figure of ``ultrafast.plotting`` (pulse in / out, spectrum, phase, evolution, losses);
* ``hero.png``: the first of those.

``figures/`` is git-ignored. Examples whose story needs a purpose-made figure or an animation (the Computing group) have their own
``make_figures.py`` that writes ``hero.png`` (and ``animation.gif``) into the example folder, where they are committed.
Every file is kept within the size budget (PNG 300 kB, GIF 2.5 MB) or the run says so.

    python make_figures.py [--no-animations] [--dpi 110]
"""
from __future__ import annotations

import argparse
import os
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python" / "src"))
sys.path.insert(0, str(ROOT / "build"))

PNG_BUDGET = 300_000


def parser(description: str = "run the example and write its figures") -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=description)
    ap.add_argument("--no-animations", action="store_true", help="skip GIF / MP4 files (the fast path)")
    ap.add_argument("--dpi", type=int, default=110, help="figure resolution; lowered automatically to meet the size budget")
    return ap


def save_png(fig, path: Path, dpi: int) -> int:
    """Save a figure, lowering the resolution until the file is within the PNG budget. Returns the size in bytes."""
    for d in (dpi, 90, 72, 60, 48):
        fig.savefig(path, dpi=d)
        if path.stat().st_size <= PNG_BUDGET:
            break
    return path.stat().st_size


def run_readme(folder: Path) -> float:
    """Run the README's commands in the example folder; returns the start time (to find what they wrote)."""
    import example_check as ec
    if not os.path.isfile(ec.CLI):
        sys.exit("build the CLI first: dotnet build UltrafastEngine.sln -c Release (expected %s)" % ec.CLI)
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1")
    start = time.time()
    for line in ec.commands((folder / "README.md").read_text(encoding="utf-8")):
        rc, out, dt = ec.run_line(line, str(folder), ec.CLI, env, 1800)
        print("%-60s rc=%d %.0f s" % (line[:60], rc, dt))
        if rc != 0:
            sys.exit("command failed: %s\n%s" % (line, "\n".join(out.strip().splitlines()[-8:])))
    return start


def main(file: str, argv=None) -> int:
    args = parser().parse_args(argv)
    folder = Path(file).resolve().parent
    import example_check as ec
    rel = folder.relative_to(ec.EXAMPLES).as_posix()
    if rel in ec.MANUAL:
        print("%s: skipped (%s)" % (rel, ec.MANUAL[rel]))
        return 0
    start = run_readme(folder)
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting as up
    out = folder / "figures"
    out.mkdir(exist_ok=True)
    written = []
    for results_dir, f in sorted(ec.summaries(str(folder), start).items()):
        try:
            res = up.load(os.path.dirname(f))
            fig = up.overview(res)
        except Exception as e:                     # a results folder without the data an overview needs
            print("no overview for %s: %s" % (results_dir, e))
            continue
        name = "overview-" + results_dir.replace(os.sep, "-").replace("/", "-") + ".png"
        size = save_png(fig, out / name, args.dpi)
        up._plt()[1].close(fig)
        written.append(name)
        print("wrote figures/%s (%d kB)" % (name, size // 1000))
    if written:
        (out / "hero.png").write_bytes((out / written[0]).read_bytes())
        print("wrote figures/hero.png")
    else:
        print("no results folders written; nothing to draw")
    return 0
