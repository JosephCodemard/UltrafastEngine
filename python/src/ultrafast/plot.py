"""Plot results written by the ``ultrafast`` CLI.

    ultrafast-plot RESULTS_DIR [RESULTS_DIR ...] [options]
    python -m ultrafast.plot RESULTS_DIR ...
    python path/to/ultrafast/plot.py RESULTS_DIR ...     (works without installing the package)

Writes overview / instruments / probes / elements figures to RESULTS_DIR/plots (PNG by default) plus a
multi-page report.pdf. With several folders, also writes a comparison figure (to --out, default: the
current directory). Needs only NumPy and matplotlib -- not the engine.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

if __package__ in (None, ""):  # run as a plain script: make "ultrafast" importable from this checkout
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    __package__ = "ultrafast"

from ultrafast import plotting as up  # noqa: E402


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="ultrafast-plot", description=__doc__.split("\n\n")[0],
                                 formatter_class=argparse.RawDescriptionHelpFormatter,
                                 epilog="examples:\n  ultrafast-plot results/\n  ultrafast-plot results/ --show --db\n"
                                        "  ultrafast-plot runA/ runB/ --labels 'no CM' 'with CM' --out cmp/\n")
    ap.add_argument("dirs", nargs="+", help="results folder(s) written by `ultrafast run ... -o DIR`")
    ap.add_argument("--only", help=f"comma-separated subset of {','.join(up.FIGURES)}")
    ap.add_argument("--format", default="png", help="image format(s), comma-separated: png,svg,pdf (default png)")
    ap.add_argument("--no-pdf", action="store_true", help="do not write the combined report.pdf")
    ap.add_argument("--out", help="output folder (single run: default DIR/plots; comparison: default .)")
    ap.add_argument("--db", action="store_true", help="spectra in dB in the overview / comparison")
    ap.add_argument("--dark", action="store_true", help="dark theme")
    ap.add_argument("--dpi", type=int, default=160)
    ap.add_argument("--labels", nargs="+", help="legend labels for a comparison (default: run names)")
    ap.add_argument("--show", action="store_true", help="open the figures in a window as well")
    a = ap.parse_args(argv)

    if not a.show:
        import matplotlib
        matplotlib.use("Agg")
    up.set_theme(dark=a.dark)
    formats = [f.strip().lstrip(".") for f in a.format.split(",") if f.strip()]
    only = [s.strip() for s in a.only.split(",")] if a.only else None

    results = []
    for d in a.dirs:
        try:
            results.append(up.load(d))
        except FileNotFoundError as e:
            print(f"ultrafast-plot: {e}", file=sys.stderr)
            return 2
    figs_to_show = []
    for r in results:
        out = Path(a.out) if (a.out and len(results) == 1) else None
        res = up.report(r, out, formats=formats, pdf=not a.no_pdf, only=only, db=a.db, dpi=a.dpi, close=not a.show)
        if a.show:
            figs_to_show += list(res.values())
            out = out or r.directory / "plots"
            for name, f in res.items():
                for ext in formats:
                    f.savefig(Path(out) / f"{name}.{ext}", dpi=a.dpi)
            print(f"{r.directory}: {', '.join(res)} -> {out}")
        else:
            files = [p for ps in res.values() for p in ps]
            print(f"{r.directory}: {len(files)} file(s) -> {files[0].parent if files else '-'}")
    if len(results) > 1:
        out = Path(a.out or ".")
        out.mkdir(parents=True, exist_ok=True)
        f = up.compare(results, a.labels, db=a.db)
        for ext in formats:
            f.savefig(out / f"compare.{ext}", dpi=a.dpi)
        print(f"comparison -> {out / ('compare.' + formats[0])}")
        figs_to_show.append(f)
    if a.show:
        import matplotlib.pyplot as plt
        plt.show()
    return 0


if __name__ == "__main__":
    sys.exit(main())
