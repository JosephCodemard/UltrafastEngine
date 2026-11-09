"""Hero figure and animation of the scaling example (python make_figures.py [--no-animations])."""
import io
import sys
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _figs
import numpy as np
import scaling

args = _figs.parser().parse_args()
th = _figs.style._theme["series"]
with redirect_stdout(io.StringIO()):
    rows = scaling.main([])
px = np.array([r["pixels"] for r in rows])
series = (("engine (simulates the light)", [r["engineS"] for r in rows], th[0]),
          ("digital FFT (numpy)", [r["digitalS"] for r in rows], th[1]),
          ("optical: 4 f / c + 1 us exposure (assumed)", [r["opticalS"] for r in rows], th[2]))


def draw(ax, upto):
    for label, y, col in series:
        ax.loglog(px[:upto], y[:upto], "o-", color=col, label=label)
    ax.loglog(px[:upto], [r["transitS"] for r in rows][:upto], "o--", color=th[2], alpha=0.5, label="light transit 4 f / c alone")
    ax.set_xlim(px[0] * 0.8, px[-1] * 1.3); ax.set_ylim(1e-9, 1e1)
    ax.set_xlabel("pixels N x N"); ax.set_ylabel("time (s)"); ax.legend(fontsize=7, loc="center right")


f = _figs.fig(10.5, 4.2)
a1, a2 = f.subplots(1, 2)
draw(a1, len(rows))
a1.set_title("time to convolve an N x N image")
a2.loglog(px, [r["macs"] for r in rows], "o-", color=th[3])
a2.set_xlabel("pixels N x N"); a2.set_ylabel("multiply-accumulates"); a2.set_title("direct digital convolution: N^4 (optics: one pass)")
f.suptitle("Light needs nanoseconds whatever N is; the engine's wall time is the cost of simulating it", x=0.01, ha="left", fontsize=10.5, fontweight="bold")
_figs.save_hero(f, __file__)

if not args.no_animations:
    def frame(k):
        fg = _figs.fig(6.4, 3.6)
        ax = fg.subplots()
        draw(ax, k + 1)
        ax.set_title(f"N = {rows[k]['n']}: {rows[k]['macs']:.1e} multiply-accumulates in a direct convolution")
        return fg
    _figs.save_animation(frame, range(len(rows)), __file__, fps=1.5, pause_last_s=2.0)
