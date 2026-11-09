"""Shared by the ``make_figures.py`` of every example (not an example itself).

``make_figures.py`` in an example folder runs the commands of that folder's README (the same lines ``build/example_check.py`` runs,
with ``ULTRAFAST_STRICT_OPTIONS=1``) and then writes figures to ``figures/`` next to the README:

* ``figures/overview-<results folder>.png`` for every results folder (``summary.json`` + ``result.npz``) the commands wrote: the standard
  overview figure of ``ultrafast.plotting`` (pulse in / out, spectrum, phase, evolution, losses). ``figures/`` is git-ignored;
* ``hero.png`` next to the README, **only when the README shows no figure yet**: a copy of the first overview, committed and embedded
  in the README. Examples that already embed their own figure keep it.

Examples whose story needs a purpose-made figure or an animation (the Computing group) have their own ``make_figures.py`` that writes
``hero.png`` (and ``animation.gif``) into the example folder, where they are committed.
Every file is kept within the size budget (PNG 300 kB, GIF 2.5 MB) or the run says so.

    python make_figures.py [--no-animations] [--dpi 110]
"""
from __future__ import annotations

import argparse
import os
import shutil
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
    """Run the README's commands in the example folder; returns the start time (to find what they wrote). A README without commands
    (some examples only describe their files) falls back to ``ultrafast run <the .ufs> run.json -o results`` or, for a Python-only
    example, to running its one script."""
    import example_check as ec
    if not os.path.isfile(ec.CLI):
        sys.exit("build the CLI first: dotnet build UltrafastEngine.sln -c Release (expected %s)" % ec.CLI)
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1")
    start = time.time()
    lines = ec.commands((folder / "README.md").read_text(encoding="utf-8"))
    if not lines:
        ufs, scripts = sorted(folder.glob("*.ufs")), [p for p in sorted(folder.glob("*.py")) if not p.name.startswith(("make_", "_"))]
        if len(ufs) == 1 and (folder / "run.json").exists():
            lines = ["ultrafast run %s run.json -o results" % ufs[0].name]
        elif len(scripts) == 1:
            lines = ["python " + scripts[0].name]
    for line in lines:
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
        name = "overview-" + results_dir.replace(os.sep, "-").replace("/", "-") + ".png"
        try:
            res = up.load(os.path.dirname(f))
            fig = up.overview(res)
        except Exception as e:                     # cw / resonator runs have no pulse for an overview: draw the system schematic instead
            try:
                fig = up.system(res)
                name = "system-" + name[len("overview-"):]
                print("no overview for %s (%s); drew the system schematic" % (results_dir, str(e)[:60]))
            except Exception as e2:
                print("no figure for %s: %s" % (results_dir, e2))
                continue
        size = save_png(fig, out / name, args.dpi)
        up._plt()[1].close(fig)
        written.append(name)
        print("wrote figures/%s (%d kB)" % (name, size // 1000))
    if not written:                                # a Python-only example: use the picture its own script wrote
        pics = [p for p in folder.glob("*.png") if p.name != "hero.png" and p.stat().st_mtime >= start]
        if pics:
            shutil.copy2(pics[0], out / ("script-" + pics[0].name))
            written.append("script-" + pics[0].name)
            print("using the figure the example's script wrote: " + pics[0].name)
    if written:
        if "![" not in (folder / "README.md").read_text(encoding="utf-8"):
            (folder / "hero.png").write_bytes((out / written[0]).read_bytes())
            print("wrote hero.png (the README shows no figure yet: embed it)")
        else:
            print("the README already shows a figure; hero.png not written")
    else:
        print("no results folders written; nothing to draw")
    return 0
