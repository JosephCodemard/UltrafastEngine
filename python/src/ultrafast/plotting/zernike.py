"""Zernike decomposition figure: the wavefront, its Zernike reconstruction, the residual and a bar chart of the Noll
coefficients (rms, in waves) coloured by radial order, with the rms and the Marechal Strehl estimate in the title.

    from ultrafast.plotting import zernike as zp
    fig = zp.decomposition(res.instrument("wfs"))                       # a wavefrontSensor result
    fig = zp.decomposition((w_waves, x_m, y_m), radius=1e-3)            # a map and its axes
    fig = zp.decomposition(field, x=x_m, y=y_m, wavelength=800e-9)      # a complex field: the phase is the wavefront
    fig = zp.decomposition([before, after], labels=["before", "after"]) # side by side, one colour scale
    zp.animate_decomposition([w0, w1, w2], "dm-correction.gif")         # steps through a list

Piston (and tilt, with ``remove_tilt=True``) are removed from the maps and from the rms. The fit is :func:`ultrafast.algorithms.zernike.fit`
(the engine's Noll convention). Needs matplotlib.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional, Sequence, Tuple, Union

import numpy as np

from ..algorithms import zernike as zk
from .style import _cmap, _figure, _plt, _si, _theme

__all__ = ["decomposition", "animate_decomposition", "wavefront_from"]


def _unwrap_valid(phase: np.ndarray, ok: np.ndarray) -> np.ndarray:
    """Unwrap a phase map over its valid points only: along the middle row first, then each column outward from that row (the pupil
    is connected, so every valid column crosses the middle row). Zero-amplitude points outside the pupil never enter."""
    out = np.full(phase.shape, np.nan)
    nx, ny = phase.shape
    jc = ny // 2
    idx = np.flatnonzero(ok[:, jc])
    if idx.size == 0:
        return out
    ref = np.unwrap(phase[idx, jc])
    for i, r in zip(idx, ref):
        lo = jc
        while lo > 0 and ok[i, lo - 1]:
            lo -= 1
        hi = jc
        while hi < ny - 1 and ok[i, hi + 1]:
            hi += 1
        seg_up = np.unwrap(phase[i, jc:hi + 1])
        seg_dn = np.unwrap(phase[i, lo:jc + 1][::-1])[::-1]
        seg_up = seg_up + 2 * np.pi * np.round((r - seg_up[0]) / (2 * np.pi))
        seg_dn = seg_dn + 2 * np.pi * np.round((r - seg_dn[-1]) / (2 * np.pi))
        out[i, jc:hi + 1] = seg_up
        out[i, lo:jc + 1] = seg_dn
    return out


def wavefront_from(source: Any, x=None, y=None, wavelength: Optional[float] = None, radius: Optional[float] = None
                   ) -> Tuple[np.ndarray, np.ndarray, np.ndarray, float]:
    """Normalise the accepted inputs to ``(map_in_waves[x][y], x, y, radius)``.

    * a ``wavefrontSensor`` result (dict with ``x_m``, ``y_m``, ``wavefront_waves``; ``apertureRadiusM`` gives the radius);
    * a tuple ``(map, x, y)`` (map in waves) or a bare map with ``x`` and ``y`` given;
    * a complex field with ``x``, ``y`` and ``wavelength``: its phase divided by 2 pi, unwrapped along both axes, over the points
      where the amplitude exceeds 1 % of its peak (the wavefront in waves; radius defaults to the largest such circle)."""
    if isinstance(source, dict) and "wavefront_waves" in source:
        w, x, y = np.asarray(source["wavefront_waves"], float), np.asarray(source["x_m"], float), np.asarray(source["y_m"], float)
        radius = radius or source.get("apertureRadiusM")
    elif isinstance(source, tuple) and len(source) == 3:
        w, x, y = np.asarray(source[0]), np.asarray(source[1], float), np.asarray(source[2], float)
    else:
        w = np.asarray(source)
        if x is None or y is None:
            raise ValueError("a bare map needs its axes x and y")
        x, y = np.asarray(x, float), np.asarray(y, float)
    if np.iscomplexobj(w):
        amp = np.abs(w)
        ok = amp >= 0.01 * amp.max()
        w = np.where(ok, _unwrap_valid(np.angle(w), ok) / (2 * np.pi), np.nan)
        if radius is None:
            X, Y = np.meshgrid(x, y, indexing="ij")
            radius = float(np.min(np.hypot(X, Y)[~ok])) if (~ok).any() else float(np.max(np.abs(x)))
    w = np.asarray(w, float)
    if radius is None:
        radius = float(min(np.nanmax(np.abs(x)), np.nanmax(np.abs(y))))
    return w, x, y, float(radius)


def _analyse(source, x, y, wavelength, radius, max_noll, remove_tilt):
    w, x, y, R = wavefront_from(source, x, y, wavelength, radius)
    c, res = zk.fit(w, x, y, R, max_noll=max_noll, return_residual=True)
    drop = [0] + ([1, 2] if remove_tilt else [])
    cc = c.copy()
    cc[drop] = 0.0
    fit_map = zk.evaluate_map(cc, x, y, R)
    sel = np.isfinite(fit_map)
    w_shown = np.where(sel & np.isfinite(w), w - c[0], np.nan)  # Z_1 = 1: piston removed
    if remove_tilt:
        w_shown = w_shown - np.where(sel, zk.evaluate_map(np.concatenate([[0.0], c[1:3]]), x, y, R, outside=0.0), 0.0)
    residual = np.where(sel & np.isfinite(w_shown), w_shown - fit_map, np.nan)
    rms = zk.rms(c, skip=(1, 2, 3) if remove_tilt else (1,))
    return dict(x=x, y=y, radius=R, coeffs=c, wavefront=w_shown, fit=fit_map, residual=residual, rms=rms, strehl=zk.strehl_marechal(rms))


def _map(ax, x, y, z, vlim, cmap, label=None):
    sx, px = _si(max(np.nanmax(np.abs(x)), np.nanmax(np.abs(y)), 1e-30))
    m = ax.pcolormesh(x / sx, y / sx, z.T, cmap=cmap, shading="auto", rasterized=True, vmin=-vlim, vmax=vlim)
    ax.set_aspect("equal"); ax.grid(False)
    ax.set_xlabel(f"x ({px}m)"); ax.set_ylabel(f"y ({px}m)")
    return m


def _bars(ax, coeffs, max_noll, vmax, legend=True):
    js = np.arange(1, len(coeffs) + 1)
    orders = np.array([zk.radial_order_of(int(j)) for j in js])
    series = _theme["series"]
    cols = [series[n % len(series)] for n in orders]
    ax.bar(js, coeffs, color=cols, width=0.8)
    ax.axhline(0, color=_theme["ref"], lw=0.8)
    ax.set_xlabel("Noll index j"); ax.set_ylabel("rms (waves)")
    ax.set_xticks(js if len(js) <= 16 else js[::2])
    ax.set_ylim(-vmax * 1.15, vmax * 1.15)
    for j in range(2, min(len(coeffs), 11) + 1):
        if abs(coeffs[j - 1]) > 0.12 * vmax:
            ax.annotate(zk.name(j), (j, coeffs[j - 1]), textcoords="offset points", xytext=(0, 4 if coeffs[j - 1] >= 0 else -10),
                        ha="center", fontsize=7, color=_theme["text2"], rotation=0)
    # legend by radial order
    seen = sorted(set(orders[1:]))
    handles = [_plt()[0].patches.Patch(color=series[n % len(series)], label=f"n = {n}") for n in seen]
    if legend:
        ax.legend(handles=handles, ncol=len(handles), fontsize=6.5, loc="lower center", bbox_to_anchor=(0.5, -0.42), columnspacing=0.8, handlelength=1.0, borderaxespad=0.0)


def decomposition(sources: Union[Any, Sequence[Any]], labels: Optional[Sequence[str]] = None, *, x=None, y=None, wavelength: Optional[float] = None,
                  radius: Optional[float] = None, max_noll: int = 15, remove_tilt: bool = False, vlim_waves: Optional[float] = None,
                  coeff_lim_waves: Optional[float] = None):
    """The Zernike decomposition figure for one wavefront (see the module docstring for the accepted inputs) or a list of them, one row each.

    ``max_noll``: number of Noll terms fitted (including piston). ``remove_tilt``: also remove tilt from the maps and the rms.
    ``vlim_waves``: symmetric colour range of the maps (default: the largest |value| over all rows, so before and after share a scale);
    ``coeff_lim_waves``: the same for the bar chart.
    Returns the matplotlib Figure; ``fig.zernike`` holds the analysis (coefficients, rms, Strehl) per row."""
    matplotlib, _ = _plt()
    many = isinstance(sources, (list, tuple)) and not (isinstance(sources, tuple) and len(sources) == 3 and not isinstance(sources[0], (dict, list, tuple)))
    srcs = list(sources) if many else [sources]
    an = [_analyse(s, x, y, wavelength, radius, max_noll, remove_tilt) for s in srcs]
    labels = list(labels) if labels is not None else ([f"wavefront {i + 1}" for i in range(len(an))] if len(an) > 1 else [""])
    vlim = vlim_waves or max(float(np.nanmax(np.abs(a["wavefront"]))) for a in an) or 1.0
    cvmax = coeff_lim_waves or max(float(np.max(np.abs(a["coeffs"][1:] if len(a["coeffs"]) > 1 else a["coeffs"]))) for a in an) or 1.0
    cmap = matplotlib.colormaps["RdBu_r"]
    n = len(an)
    fig = _figure(11.0, 2.9 * n + 0.4)
    gs = fig.add_gridspec(n, 4, width_ratios=[1, 1, 1, 1.8])
    for i, a in enumerate(an):
        axs = [fig.add_subplot(gs[i, k]) for k in range(4)]
        m = _map(axs[0], a["x"], a["y"], a["wavefront"], vlim, cmap)
        _map(axs[1], a["x"], a["y"], a["fit"], vlim, cmap)
        _map(axs[2], a["x"], a["y"], a["residual"], vlim, cmap)
        if i == 0:
            axs[0].set_title("wavefront"); axs[1].set_title("Zernike fit"); axs[2].set_title("residual")
        _bars(axs[3], a["coeffs"], max_noll, cvmax, legend=(i == n - 1))
        tag = f"{labels[i]}: " if labels[i] else ""
        axs[3].set_title(f"{tag}rms {a['rms']:.3g} waves\nStrehl (Maréchal) {a['strehl']:.2f}", fontsize=9)
        if i == n - 1:
            fig.colorbar(m, ax=axs[:3], shrink=0.8, pad=0.01, label="waves", location="bottom")
    fig.zernike = [dict(label=labels[i], coefficients=a["coeffs"], rms_waves=a["rms"], strehl=a["strehl"], radius_m=a["radius"]) for i, a in enumerate(an)]
    return fig


def animate_decomposition(wavefronts: Sequence[Any], path, labels: Optional[Sequence[str]] = None, *, fps: float = 6, dpi: int = 70,
                          pause_last_s: float = 1.5, **kw) -> Dict[str, Any]:
    """Step through a list of wavefronts (e.g. the iterations of a deformable-mirror correction) and write an animated GIF
    with one decomposition per frame, on one fixed colour and coefficient scale. ``kw`` goes to :func:`decomposition`."""
    from .animate import animate
    an = [_analyse(s, kw.get("x"), kw.get("y"), kw.get("wavelength"), kw.get("radius"), kw.get("max_noll", 15), kw.get("remove_tilt", False)) for s in wavefronts]
    vlim = kw.pop("vlim_waves", None) or max(float(np.nanmax(np.abs(a["wavefront"]))) for a in an)
    clim = kw.pop("coeff_lim_waves", None) or max(float(np.max(np.abs(a["coeffs"][1:]))) for a in an)
    labels = list(labels) if labels is not None else [f"step {i}" for i in range(len(wavefronts))]

    def frame(i):
        fig = decomposition(wavefronts[i], [labels[i]], vlim_waves=vlim, coeff_lim_waves=clim, **kw)
        return fig
    return animate(frame, range(len(wavefronts)), path, fps=fps, dpi=dpi, pause_last_s=pause_last_s, progress_bar=True)
