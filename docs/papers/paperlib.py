"""Shared helpers of the paper replications (papers/<paper>/run.py): command line, numbers.json, figure style.

    from paperlib import Paper
    p = Paper(__file__)                 # parses --tier fast|full and --figures
    p.fast                              # True in the CI tier
    p.write_numbers({"key": 1.0, "curve": {"x": [...], "y": [...]}})   # -> results/numbers.json
    fig, ax = p.figure()                # matplotlib figure in the house style
    p.save(fig, "compression-factor")   # -> results/figures/*.png, or figures/*.png with --figures (<= 300 kB)

Conventions (papers/CLAUDE.md): run.py writes everything under results/ (git-ignored); the committed figures in
figures/ are rewritten only when run.py is called with --figures (by hand, normally with --tier full), never by
build/paper_check.py. Colours: the first three categorical slots of the house palette for our results, dark grey and
black for the paper's formula or numbers drawn on the same axes.
"""
import argparse
import json
import math
import os
import sys

# our curves (categorical slots 1-3), then the paper / analytic reference
BLUE, ORANGE, AQUA = "#2a78d6", "#eb6834", "#1baf7a"
REF, REF2 = "#0b0b0b", "#7a7974"
MAX_FIGURE_BYTES = 300 * 1024


def _plain(o):
    """numpy scalars / arrays -> JSON-serialisable Python objects; NaN -> None."""
    if isinstance(o, dict):
        return {str(k): _plain(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [_plain(v) for v in o]
    if hasattr(o, "tolist"):
        return _plain(o.tolist())
    if isinstance(o, float):
        return o if math.isfinite(o) else None
    return o


class Paper:
    def __init__(self, run_file, argv=None):
        ap = argparse.ArgumentParser(description="Replication run (papers/CLAUDE.md)")
        ap.add_argument("--tier", choices=["fast", "full"], default="fast", help="fast: CI (minutes); full: the complete figures")
        ap.add_argument("--figures", action="store_true", help="write the figures to figures/ (committed) instead of results/figures/")
        self.args = ap.parse_args(argv)
        self.folder = os.path.dirname(os.path.abspath(run_file))
        self.results = os.path.join(self.folder, "results")
        self.figures = os.path.join(self.folder, "figures") if self.args.figures else os.path.join(self.results, "figures")
        os.makedirs(self.results, exist_ok=True)
        os.makedirs(self.figures, exist_ok=True)

    @property
    def tier(self):
        return self.args.tier

    @property
    def fast(self):
        return self.args.tier == "fast"

    def write_numbers(self, numbers):
        path = os.path.join(self.results, "numbers.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(_plain(dict(numbers, tier=self.tier)), f, indent=1)
        print("wrote", os.path.relpath(path, self.folder))
        return path

    def figure(self, ncols=1, nrows=1, width_in=6.4, height_in=4.0, **kw):
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        plt.rcParams.update({"font.size": 10, "axes.spines.top": False, "axes.spines.right": False, "axes.grid": True,
                             "grid.color": "#e4e3df", "grid.linewidth": 0.6, "axes.edgecolor": "#52514e", "axes.labelcolor": "#0b0b0b",
                             "xtick.color": "#52514e", "ytick.color": "#52514e", "legend.frameon": False, "lines.linewidth": 2.0,
                             "savefig.dpi": 120})
        return plt.subplots(nrows, ncols, figsize=(width_in, height_in), **kw)

    def save(self, fig, name):
        import matplotlib.pyplot as plt
        fig.tight_layout()
        path = os.path.join(self.figures, name + ".png")
        for dpi in (120, 100, 85, 70):               # the size depends on the platform's fonts: lower the resolution until it fits
            fig.savefig(path, dpi=dpi)
            size = os.path.getsize(path)
            if size <= MAX_FIGURE_BYTES:
                break
        plt.close(fig)
        if size > MAX_FIGURE_BYTES:
            sys.exit("%s is %d kB, more than the 300 kB limit of docs/papers/CLAUDE.md" % (path, size // 1024))
        print("wrote", os.path.relpath(path, self.folder), "(%d kB)" % (size // 1024))
        return path
