"""Make the images of docs/reference/plotting.md: the four styles, the schematics before and after WP-2, the 3-D spectrogram, the evolution GIF.

    PYTHONPATH=python/src python python/tools/make_plotting_docs.py [--out docs/assets/plotting]

Needs the engine (it runs three small systems) and matplotlib. The "before" schematics are drawn with the plotting module of the commit
before WP-2 (``git show c17caab:python/src/ultrafast/plotting/diagrams.py``), so the comparison is always against the real old code.
All files stay under the 300 kB PNG / 2.5 MB GIF budget (``ultrafast.plotting.animate.check_budget``).
"""
from __future__ import annotations

import argparse
import importlib.util
import subprocess
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402
from ultrafast import plotting as up  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
BEFORE_COMMIT = "c17caab"


def _before_module():
    """The diagrams module as it was before WP-2, loaded next to the current plotting package."""
    src = subprocess.run(["git", "show", f"{BEFORE_COMMIT}:python/src/ultrafast/plotting/diagrams.py"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=True).stdout
    spec = importlib.util.spec_from_loader("ultrafast.plotting.diagrams_before", loader=None)
    mod = importlib.util.module_from_spec(spec)
    mod.__package__ = "ultrafast.plotting"
    exec(compile(src, "diagrams_before.py", "exec"), mod.__dict__)
    return mod


def _systems():
    pulse = b.pulse(1030e-9, 100e-15, energy=1e-9)
    grid = b.grid(n=4096, window_s=20e-12)
    mzi = b.system("mach-zehnder", b.Branched([
        b.link(b.beam_splitter("bs1"), {"c": "armA", "d": "armB"}),
        b.link(b.delay("armA", delay_s=0.0), "bs2:1"),
        b.link(b.delay("armB", delay_s=300e-15), "bs2:2"),
        b.link(b.beam_splitter("bs2"), {"c": "end", "d": "virtual"})]))
    cav = b.system("cavity", b.Branched([
        b.link(b.beam_splitter("m1", 0.8), {"c": "forth", "d": "virtual"}, loop=b.loop("ringDown", 6, 1e-9)),
        b.link(b.delay("forth", length_m=0.01), "m2:2"),
        b.link(b.beam_splitter("m2", 0.8), {"c": "back", "d": "end"}),
        b.link(b.delay("back", length_m=0.01), "m1:2")]))
    cell = b.system("cell", b.Branched([b.link(b.gap("pass1", 0.01), "pass2")] + [b.link(b.same_as("pass%d" % i, "pass1"), "pass%d" % (i + 1) if i < 46 else "end") for i in range(2, 47)]))
    glass = b.system("glass", [b.bulk("plate1", "FusedSilica", 0.05), b.bulk("plate2", "FusedSilica", 0.05), b.bulk("plate3", "FusedSilica", 0.05)])
    spec = b.run_spec(pulse, grid=grid, instruments=[b.instrument("spectrometer"), b.instrument("autocorrelator")])
    return {"mzi": uf.run(mzi, spec), "cavity": uf.run(cav, spec), "cell46": uf.run(cell, b.run_spec(pulse, grid=grid)),
            "glass": uf.run(glass, b.run_spec(b.pulse(800e-9, 20e-15, energy=1e-9), grid=b.grid(n=4096, window_s=8e-12)))}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(ROOT / "docs" / "assets" / "plotting"))
    out = Path(ap.parse_args().out)
    out.mkdir(parents=True, exist_ok=True)
    res = _systems()
    old = _before_module()

    # styles: the same two panels in the four styles
    for name in ("ultrafast", "ultrafast-dark", "paper", "science"):
        with up.use_style(name):
            from ultrafast.plotting.style import _figure, _Style
            fig = _figure(7.2, 3.0)
            with _Style():
                a1, a2 = fig.subplots(1, 2)
            up.spectrum(res["glass"], a1)
            up.temporal(res["glass"], a2)
            fig.savefig(out / f"style-{name}.png", dpi=100)
    # schematics, before and after
    for key in ("mzi", "cavity", "cell46"):
        for theme_name, dark in (("", False),) + ((("-dark", True),) if key == "cavity" else ()):
            up.set_theme(dark=dark)
            old_fig = old.system(res[key])
            old_fig.savefig(out / f"graph-{key}{theme_name}-before.png", dpi=80)
            up.system(res[key], params=(key == "cavity")).savefig(out / f"graph-{key}{theme_name}-after.png", dpi=80)
            up.set_theme(dark=False)
    up.spectrogram_3d(res["glass"], "output").savefig(out / "spectrogram-3d.png", dpi=90)
    up.animate_evolution(res["glass"], out / "evolution.gif", fps=2.5)
    for p in sorted(out.iterdir()):
        print(f"{p.name}: {p.stat().st_size / 1e3:.0f} kB")
    over = up.animate.check_budget(out) if hasattr(up, "animate") else []
    if over:
        print("over budget:", over)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
