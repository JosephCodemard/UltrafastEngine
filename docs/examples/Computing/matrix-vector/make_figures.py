"""Hero figure and animation of the matrix-vector example (python make_figures.py [--no-animations])."""
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _figs
import _optics as op
import numpy as np
import matrix_vector as mv

args = _figs.parser().parse_args()
work = op.workdir(__file__)
th = _figs.style._theme["series"]
n = mv.SETUP.n
k = np.arange(n) - n // 2
x = mv.vector()
a, ra = mv.dft_by_lens(work, x)
c = mv.circulant_kernel()
b_, rb = mv.circulant_by_4f(work, x, c)

f = _figs.fig(10.5, 5.6)
ax = f.subplots(2, 3)
ax[0, 0].stem(k, np.abs(x), basefmt=" "); ax[0, 0].set_title("input vector |x|"); ax[0, 0].set_xlabel("pixel")
ax[1, 0].plot(k, np.fft.fftshift(c), color=th[2]); ax[1, 0].set_title("circulant matrix: first column c"); ax[1, 0].set_xlabel("offset (pixels)")
for row, (eng, ref, name) in enumerate(((a, ra, "y = A x (DFT matrix, one lens)"), (b_, rb, "y = C x (circulant, 4f system)"))):
    eng = eng * ref.sum() / eng.sum()                              # the engine reports J/m^2: same total as numpy for display
    ax[row, 1].plot(k, ref / ref.max(), color=th[1], lw=3, alpha=0.45, label="numpy: A @ x")
    ax[row, 1].plot(k, eng / ref.max(), color=th[0], lw=1.2, label="engine")
    ax[row, 1].set_title(f"|y|^2, {name}", fontsize=9); ax[row, 1].legend(); ax[row, 1].set_xlabel("pixel")
    ax[row, 2].plot(ref / ref.max(), eng / ref.max(), ".", color=th[0], ms=4); ax[row, 2].plot([0, 1], [0, 1], color=th[5], lw=1)
    ax[row, 2].set_aspect("equal"); ax[row, 2].set_xlabel("numpy"); ax[row, 2].set_ylabel("engine")
    ax[row, 2].set_title(f"rel. l2 error {mv.rel(eng, ref):.1e}", fontsize=9)
f.suptitle(f"Matrix-vector products by light: {n * n} multiply-accumulates per pass; one lens is the DFT matrix", x=0.01, ha="left", fontsize=10.5, fontweight="bold")
_figs.save_hero(f, __file__, dpi=90)

if not args.no_animations:
    ms = list(range(0, 21))
    outs = []
    for m in ms:
        xm = np.cos(2 * np.pi * m * np.arange(n) / n).astype(complex) * (np.abs(k) < n // 2 - 1)
        outs.append(mv.dft_by_lens(work, xm))

    def frame(i):
        fg = _figs.fig(7.0, 3.4)
        a_ = fg.subplots(1, 2)
        a_[0].plot(k, np.cos(2 * np.pi * ms[i] * np.arange(n) / n), color=th[2]); a_[0].set_ylim(-1.1, 1.1)
        a_[0].set_title(f"input x: cos(2 pi {ms[i]} j / N)"); a_[0].set_xlabel("pixel")
        eng, ref = outs[i]
        eng = eng * ref.sum() / eng.sum()
        a_[1].plot(k, ref / max(o[1].max() for o in outs), color=th[1], lw=3, alpha=0.45, label="numpy")
        a_[1].plot(k, eng / max(o[1].max() for o in outs), color=th[0], lw=1.2, label="engine")
        a_[1].set_ylim(0, 1.05); a_[1].set_title("|y|^2 = |A x|^2 (one lens)"); a_[1].set_xlabel("output pixel"); a_[1].legend(loc="upper right")
        return fg
    _figs.save_animation(frame, range(len(ms)), __file__, fps=4)
