"""Time-wavelength-intensity views of a run: a 3-D spectrogram surface and an animation through the system.

    from ultrafast import plotting as up
    up.spectrogram_3d(result, at="output")                 # surface over (time, wavelength) with a heat-map floor
    up.animate_evolution(result, "evolution.gif")           # spectrum and pulse after every element, input to output

``spectrogram(result, at)`` returns the data (time, wavelength, intensity) of a Gabor (Gaussian-window) short-time transform of the
pulse envelope at ``at`` = ``"input"``, ``"output"`` or a probe id. The window is matched to the pulse (``sigma_t = sqrt(sigma_t,pulse /
(2 pi sigma_nu))``), the best compromise between time and frequency resolution; the marginal over time reproduces the spectrum and the
marginal over wavelength the pulse (tests compare both). The envelope convention is the engine's, exp(-i omega t): an up-chirp (positive GDD)
rises in frequency with time, so its ridge runs from long to short wavelength. ``input`` and ``output`` use the stored complex spectral
envelope; a probe stores the spectrum and the spectral phase of the x envelope, from which the envelope is rebuilt on an even frequency grid.

``animate_evolution`` reuses :func:`ultrafast.plotting.animate.animate` (Pillow GIF; MP4 when ffmpeg is installed): one frame per probe, element by
element, with fixed axes so the changes are visible; run the system with ``probes="all"`` (the default).

Main functions: spectrogram(res, at, ...) -> dict, spectrogram_3d(res, at, ...) -> Figure, animate_evolution(res, path, ...) -> dict.
"""
from __future__ import annotations

from pathlib import Path
from typing import Dict, List, Optional, Sequence, Union

import numpy as np

from .data import _has, load
from .style import C, _Style, _cmap, _db, _figure, _plt, _si, _theme


def _prefix(at: str) -> str:
    return at if at in ("input", "output") else f"probe_{at}"


def _envelope(res, at: str):
    """Complex time envelope a(t) (engine convention exp(-i omega t)) and its time axis at a location."""
    pre = _prefix(at)
    if _has(res, f"{pre}_ax_re"):
        spec = np.asarray(res[f"{pre}_ax_re"], float) + 1j * np.asarray(res[f"{pre}_ax_im"], float)
        a = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(spec)))                # documented in docs/reference/results.md
        t = np.asarray(res[f"{pre}_time_s"], float)
        return t, a
    if all(_has(res, f"{pre}_{k}") for k in ("spectrum", "phase_x_rad", "wavelength_m")):
        # a probe stores the spectrum and the spectral phase (arg of the x envelope) on a wavelength axis: rebuild A(omega) on an even
        # frequency grid and transform. The time axis is centred on the pulse's group delay (the stored phase carries it).
        lam, sp, ph = (np.asarray(res[f"{pre}_{k}"], float) for k in ("wavelength_m", "spectrum", "phase_x_rad"))
        ok = np.isfinite(lam) & (lam > 0) & np.isfinite(sp) & np.isfinite(ph)
        om = 2 * np.pi * C / lam[ok]
        o = np.argsort(om)
        om, sp, ph = om[o], sp[ok][o], np.unwrap(ph[ok][o])
        n = 4096
        grid = np.linspace(om[0], om[-1], n)
        A = np.sqrt(np.maximum(np.interp(grid, om, sp), 0)) * np.exp(1j * np.interp(grid, om, ph))
        dt = 2 * np.pi / (n * (grid[1] - grid[0]))
        return (np.arange(n) - n // 2) * dt, np.fft.fftshift(np.fft.fft(np.fft.ifftshift(A)))
    raise ValueError(f"no time-domain data at '{at}': use 'input', 'output' or a probe id (probes: {res.summary.get('probes', [])})")


def _carrier_wavelength(res, at: str, t: np.ndarray) -> float:
    pre = _prefix(at)
    lam, s = np.asarray(res[f"{pre}_wavelength_m"], float), np.asarray(res[f"{pre}_spectrum"], float)
    ok = np.isfinite(lam) & (lam > 0) & np.isfinite(s) & (s > 0)
    return float(np.sum(lam[ok] * s[ok]) / np.sum(s[ok]))


def spectrogram(res, at: str = "output", n_time: int = 120, n_wavelength: int = 120, level: float = 1e-3) -> Dict[str, np.ndarray]:
    """Gabor spectrogram of the pulse at ``at``. Returns ``{"time_s", "wavelength_m", "intensity"}`` with ``intensity`` of shape
    (n_wavelength, n_time), normalised to a peak of 1, cropped to where the pulse and its spectrum exceed ``level`` of their peaks."""
    res = load(res)
    t, a = _envelope(res, at)
    dt = float(t[1] - t[0])
    n = a.size
    p = np.abs(a) ** 2
    if not np.any(p > 0):
        raise ValueError(f"the pulse at '{at}' has no energy")
    # window matched to the pulse: sigma_w^2 = sigma_t,pulse / (2 pi sigma_nu)
    w = p / p.sum()
    tm = float(np.sum(w * t))
    st = float(np.sqrt(np.sum(w * (t - tm) ** 2)))
    A = np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(a)))                       # ascending frequency offset
    f = np.fft.fftshift(np.fft.fftfreq(n, dt))
    ps = np.abs(A) ** 2
    ws = ps / ps.sum()
    sf = float(np.sqrt(np.sum(ws * (f - np.sum(ws * f)) ** 2)))
    sigma = float(np.sqrt(st / (2 * np.pi * max(sf, 1e-3 / (n * dt))))) if st > 0 else 5 * dt
    # time range of the pulse, centres of the windows
    keep = np.nonzero(p >= level * p.max())[0]
    t_lo, t_hi = t[keep[0]], t[keep[-1]]
    span = t_hi - t_lo
    tc = np.linspace(t_lo - 0.05 * span, t_hi + 0.05 * span, int(n_time))
    # frequency range: where the spectrum is above `level`
    fk = np.nonzero(ps >= level * ps.max())[0]
    f_lo, f_hi = f[fk[0]], f[fk[-1]]
    fspan = f_hi - f_lo
    sel = np.nonzero((f >= f_lo - 0.15 * fspan) & (f <= f_hi + 0.15 * fspan))[0]
    S = np.empty((sel.size, tc.size))
    # one windowed transform per window centre; the transform is computed for the selected frequencies directly
    ph = np.exp(1j * 2 * np.pi * np.outer(f[sel], t))                            # a(t) = sum A e^{-i w t}  =>  A(w) = sum a e^{+i w t}
    ph = ph.astype(np.complex64) if n * sel.size > 4e7 else ph
    for k, c in enumerate(tc):
        g = a * np.exp(-((t - c) ** 2) / (4 * sigma ** 2))
        S[:, k] = np.abs(ph @ g) ** 2
    S /= S.max()
    lam0 = _carrier_wavelength(res, at, t)
    nu = C / lam0 + f[sel]
    lam = C / nu
    # resample to an even wavelength grid
    order = np.argsort(lam)
    lam, S = lam[order], S[order]
    lam_grid = np.linspace(lam[0], lam[-1], int(n_wavelength))
    Sg = np.array([np.interp(lam_grid, lam, S[:, k]) for k in range(tc.size)]).T
    return {"time_s": tc, "wavelength_m": lam_grid, "intensity": Sg / Sg.max(), "window_sigma_s": np.array(sigma)}


def spectrogram_3d(res, at: str = "output", n_time: int = 90, n_wavelength: int = 90, floor: bool = True, elev: float = 28, azim: float = -58,
                   level: float = 1e-3):
    """3-D surface of the spectrogram at ``at`` (``"input"``, ``"output"`` or a probe id): time on x, wavelength on y, intensity (normalised) on z,
    with the square root of the intensity as a heat map on the floor (so the faint tails show; the surface is drawn where the intensity is above 1 %). Returns the Figure."""
    res = load(res)
    d = spectrogram(res, at, n_time=n_time, n_wavelength=n_wavelength, level=level)
    ts, tp = _si(max(abs(d["time_s"][0]), abs(d["time_s"][-1])))
    if tp not in ("f", "p", "n", "µ", "m", ""):
        ts, tp = 1.0, ""
    T, L = np.meshgrid(d["time_s"] / ts, d["wavelength_m"] * 1e9)
    Z = d["intensity"]
    fig = _figure(7.5, 5.8)
    with _Style():
        ax = fig.add_subplot(111, projection="3d")
        ax.set_facecolor(_theme["surface"])
        ax.computed_zorder = False                              # draw in the order given: floor first, surface over it
        matplotlib, _ = _plt()                                  # the ramp without its surface-coloured first step, so low values stay visible
        cm = matplotlib.colors.LinearSegmentedColormap.from_list("uf_seq3d", list(_theme["seq"])[1:])
        if floor:
            ax.contourf(T, L, np.sqrt(Z), zdir="z", offset=-0.06, levels=24, cmap=cm, vmin=0, vmax=1, zorder=1)   # sqrt: shows the faint tails of the ridge
        ax.plot_surface(T, L, np.where(Z < 0.01, np.nan, Z), cmap=cm, rstride=1, cstride=1, linewidth=0, antialiased=True, rasterized=True, alpha=0.95, vmin=0, vmax=1, zorder=2)
        ax.set_xlabel(f"time ({tp}s)", labelpad=6)
        ax.set_ylabel("wavelength (nm)", labelpad=8)
        ax.set_zlabel("intensity (norm.)", labelpad=2)
        ax.set_zlim(-0.06 if floor else 0, 1.0)
        ax.view_init(elev=elev, azim=azim)
        for axis in (ax.xaxis, ax.yaxis, ax.zaxis):
            axis.set_pane_color((1, 1, 1, 0) if _theme.get("name") != "ultrafast-dark" else (0, 0, 0, 0))
            axis._axinfo["grid"]["color"] = _theme["grid"]
        ax.set_title(f"Spectrogram at {at} · {res.summary.get('system', '')}", loc="left")
    return fig


def _locations(res, probes: Union[str, Sequence[str]]) -> List[str]:
    names = [p for p in res.summary.get("probes", []) if _has(res, f"probe_{p}_spectrum")]
    if probes != "all":
        want = [probes] if isinstance(probes, str) else list(probes)
        missing = [p for p in want if p not in names]
        if missing:
            raise ValueError(f"no probe data for {missing}; probes recorded: {names}")
        names = want
    return names


def animate_evolution(res, path: Union[str, Path] = "evolution.gif", probes: Union[str, Sequence[str]] = "all", db: bool = False, fps: float = 3,
                      dpi: int = 80, mp4: bool = False, floor_db: float = -40.0, **kw) -> Dict[str, object]:
    """Animation of the spectrum and the pulse through the system, one frame per location: the input, every probe (``probes="all"`` or a list
    of element ids, in element order), the output. Axes are fixed over all frames. ``path`` is a ``.gif`` (``mp4=True`` adds an MP4 when ffmpeg
    is on the path). Extra keywords go to :func:`ultrafast.plotting.animate.animate`; returns its result dict with ``locations``."""
    from .animate import animate
    from .spectral import _sorted_spectrum

    res = load(res)
    names = _locations(res, probes)
    locs = ["input"] + names + ["output"]
    pre = {k: (k if k in ("input", "output") else f"probe_{k}") for k in locs}
    data = {}
    for k in locs:
        lam, s = _sorted_spectrum(np.asarray(res[pre[k] + "_wavelength_m"], float), np.asarray(res[pre[k] + "_spectrum"], float))
        t, pw = np.asarray(res[pre[k] + "_time_s"], float), np.asarray(res[pre[k] + "_power_w"], float)
        data[k] = (lam, s, t, pw)
    # common axes
    lam_lo = min(d[0][np.nonzero(d[1] >= 1e-3 * d[1].max())[0][0]] for d in data.values())
    lam_hi = max(d[0][np.nonzero(d[1] >= 1e-3 * d[1].max())[0][-1]] for d in data.values())
    t_lo = min(d[2][np.nonzero(d[3] >= 1e-3 * d[3].max())[0][0]] for d in data.values())
    t_hi = max(d[2][np.nonzero(d[3] >= 1e-3 * d[3].max())[0][-1]] for d in data.values())
    sp_max = max(d[1].max() for d in data.values())
    p_max = max(d[3].max() for d in data.values())
    ts, tp = _si(max(abs(t_lo), abs(t_hi)))
    if tp not in ("f", "p", "n", "µ", "m", ""):
        ts, tp = 1.0, ""
    ps, pp = _si(p_max)
    span_l, span_t = lam_hi - lam_lo, t_hi - t_lo
    id_of = {e["id"]: e for e in res.summary.get("elements", [])}

    def frame(k):
        fig = _figure(6.4, 3.0)
        with _Style():
            a1, a2 = fig.subplots(1, 2)
            lam, s, t, pw = data[k]
            ref = data["input"]
            a1.plot(ref[0] * 1e9, (_db(ref[1] / sp_max, floor_db) if db else ref[1] / sp_max), color=_theme["ref"], ls="--", lw=1.0, label="input")
            a1.plot(lam * 1e9, (_db(s / sp_max, floor_db) if db else s / sp_max), color=_theme["series"][0], label=k)
            a1.set_xlim((lam_lo - 0.1 * span_l) * 1e9, (lam_hi + 0.1 * span_l) * 1e9)
            a1.set_ylim(floor_db, 3) if db else a1.set_ylim(0, 1.05)
            a1.set_xlabel("wavelength (nm)")
            a1.set_ylabel("spectrum (dB)" if db else "spectrum (norm.)")
            a2.plot(ref[2] / ts, ref[3] / ps, color=_theme["ref"], ls="--", lw=1.0)
            a2.plot(t / ts, pw / ps, color=_theme["series"][1])
            a2.set_xlim((t_lo - 0.1 * span_t) / ts, (t_hi + 0.1 * span_t) / ts)
            a2.set_ylim(0, 1.05 * p_max / ps)
            a2.set_xlabel(f"time ({tp}s)")
            a2.set_ylabel(f"power ({pp}W)")
            what = "input" if k == "input" else "output" if k == "output" else f"after {k} ({id_of.get(k, {}).get('type', '')})"
            fig.suptitle(f"{res.summary.get('system', '')} · {what}", x=0.01, ha="left", fontsize=9, fontweight="bold", color=_theme["text"])
            a1.legend(loc="upper right", fontsize=6.5)
        return fig

    kw.setdefault("pause_last_s", 1.5)
    out = animate(frame, locs, path, fps=fps, dpi=dpi, mp4=mp4, **kw)
    out["locations"] = locs
    return out
