"""Animations: one function draws a figure per frame, :func:`animate` writes an animated GIF (Pillow; no ffmpeg needed) and,
when ``ffmpeg`` is on the path and ``mp4=True``, an MP4 as well.

    from ultrafast.plotting import animate
    def frame(k):                       # a matplotlib Figure for frame value k
        ...
    animate.animate(frame, range(20), "converge.gif", fps=15)

Size budget (checked by ``build/example_check.py``): a GIF over 2.5 MB is flagged; keep figures small (``figsize`` about 6 x 3.4 in at
``dpi=80``), use fewer colours (``colors``) and fewer frames. Pillow merges consecutive identical frames into one longer frame, so the
file can have fewer frames than the call had (``frames_written`` in the result is the number of frames drawn).
"""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path
from typing import Any, Callable, Dict, Iterable, Optional, Sequence, Union

import numpy as np

__all__ = ["animate", "render_frame", "GIF_BUDGET_BYTES", "PNG_BUDGET_BYTES", "check_budget"]

GIF_BUDGET_BYTES = int(2.5e6)
PNG_BUDGET_BYTES = int(300e3)


def render_frame(fig, dpi: int = 80) -> np.ndarray:
    """Draw a figure to an RGB uint8 array (Agg; no display needed)."""
    from matplotlib.backends.backend_agg import FigureCanvasAgg
    canvas = fig.canvas if isinstance(fig.canvas, FigureCanvasAgg) else FigureCanvasAgg(fig)
    fig.set_dpi(dpi)
    canvas.draw()
    return np.asarray(canvas.buffer_rgba())[..., :3].copy()


def _progress_bar(rgb: np.ndarray, fraction: float, color=(42, 120, 214), height: int = 3) -> np.ndarray:
    out = rgb.copy()
    w = out.shape[1]
    out[-height:, :int(round(w * fraction))] = color
    return out


def animate(frame: Callable[[Any], Any], frames: Iterable[Any], path: Union[str, Path], fps: float = 15, dpi: int = 80, loop: int = 0,
            pause_last_s: float = 1.0, progress_bar: bool = False, colors: int = 128, mp4: bool = False, close: bool = True) -> Dict[str, Any]:
    """Draw ``frame(f)`` for every ``f`` in ``frames`` (it returns a matplotlib Figure) and write ``path`` (``.gif``).

    ``fps`` sets the frame time; the last frame is held for ``pause_last_s`` seconds (a longer frame duration, not extra frames);
    ``loop`` is the GIF loop count (0 = forever); ``progress_bar`` draws a thin bar along the bottom edge; ``colors`` is the palette
    size (<= 256). With ``mp4=True`` and ``ffmpeg`` on the path, ``path`` with the suffix ``.mp4`` is written too.
    Returns ``{"gif": Path, "mp4": Path or None, "frames": n, "bytes": size, "overBudget": bool}``."""
    from PIL import Image  # lazy: Pillow is only needed to animate
    from . import style
    _, plt = style._plt()
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    frames = list(frames)
    if not frames:
        raise ValueError("no frames")
    rgbs = []
    for i, f in enumerate(frames):
        fig = frame(f)
        rgb = render_frame(fig, dpi)
        if close:
            plt.close(fig)
        if progress_bar:
            rgb = _progress_bar(rgb, (i + 1) / len(frames))
        rgbs.append(rgb)
    shapes = {r.shape for r in rgbs}
    if len(shapes) != 1:
        raise ValueError(f"frames differ in size: {sorted(shapes)} (use the same figsize and no tight bounding boxes)")
    dur = int(round(1000.0 / fps))
    durations = [dur] * len(rgbs)
    durations[-1] = max(dur, int(round(pause_last_s * 1000)))
    imgs = [Image.fromarray(r).quantize(colors=int(colors), method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE) for r in rgbs]
    imgs[0].save(path, save_all=True, append_images=imgs[1:], duration=durations, loop=loop, optimize=True, disposal=1)
    mp4_path = None
    if mp4 and shutil.which("ffmpeg"):
        mp4_path = path.with_suffix(".mp4")
        h, w = rgbs[0].shape[:2]
        w2, h2 = w - w % 2, h - h % 2  # yuv420p needs even sizes
        cmd = ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{w}x{h}", "-r", str(fps), "-i", "-",
               "-vf", f"crop={w2}:{h2}", "-pix_fmt", "yuv420p", str(mp4_path)]
        subprocess.run(cmd, input=b"".join(r.tobytes() for r in rgbs), check=True)
    size = path.stat().st_size
    return {"gif": path, "mp4": mp4_path, "frames": len(rgbs), "bytes": size, "overBudget": size > GIF_BUDGET_BYTES}


def check_budget(root: Union[str, Path]) -> list:
    """Files under ``root`` over the size budget: PNG over 300 kB, GIF over 2.5 MB. Returns ``[(path, bytes, limit), ...]``."""
    out = []
    for p in Path(root).rglob("*"):
        lim = PNG_BUDGET_BYTES if p.suffix.lower() == ".png" else GIF_BUDGET_BYTES if p.suffix.lower() == ".gif" else None
        if lim and p.is_file() and p.stat().st_size > lim:
            out.append((p, p.stat().st_size, lim))
    return out
