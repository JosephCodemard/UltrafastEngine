"""Spatial (image) panels shared by the instrument and probe figures."""
from __future__ import annotations


import numpy as np

from .style import _cmap, _si


def _image(ax, x, y, z, cmap=None, unit="", center=False, cbar_label=None):
    """z[x][y] as an image with physical axes (auto-scaled) and equal aspect."""
    sx, px = _si(max(np.nanmax(np.abs(x)), np.nanmax(np.abs(y)), 1e-30))
    z = np.asarray(z, float)
    vmax = np.nanmax(np.abs(z)) if center else np.nanmax(z)
    if not (np.isfinite(vmax) and vmax > 0):
        vmax = 1.0
    m = ax.pcolormesh(x / sx, y / sx, z.T, cmap=cmap or _cmap(), shading="auto", rasterized=True,
                      vmin=-vmax if center else 0, vmax=vmax)
    ax.set_aspect("equal"); ax.grid(False)
    ax.set_xlabel(f"x ({px}m)"); ax.set_ylabel(f"y ({px}m)")
    ax.figure.colorbar(m, ax=ax, shrink=0.85, pad=0.01, label=cbar_label or unit)
    return m

