"""Coherent tiled-aperture array (the ``tiledArray`` of the plan): a builder for the core tile source and the far-field metrics.

It is not an element: the tiles are the *source* of the run (``initialProfile`` kind ``tiles``, docs/reference/transverse.md#coherent-tiles). The builder
fixes per-tile piston, tilt, delay and amplitude; the metrics take the focal-plane fluence map of a ``beamProfile`` camera behind a lens.
"""
from __future__ import annotations

from typing import Any, Dict, Optional, Sequence

import numpy as np

from ultrafast import build as b


def tiled_array(tile_radius_m: float, *, layout: str = "hex", rings: Optional[int] = None, count_x: Optional[int] = None, count_y: Optional[int] = None,
                pitch_m: float, piston_rad: Any = None, tilt_x_rad: Any = None, tilt_y_rad: Any = None, delays_s: Any = None, amplitudes: Any = None,
                tile_shape: Optional[str] = None, piston_rms_rad: Optional[float] = None, seed: Optional[int] = None) -> Dict[str, Any]:
    """The tile source of an N-channel array: ``layout`` "hex" (``rings`` 1, 7, 19, 37, 61 tiles), "square" (``count_x`` x ``count_y``) or "ring". Per-channel
    ``piston_rad``, tilts, ``delays_s`` and ``amplitudes`` take a number or one value per tile. Use as ``transverse(initial_profile=tiled_array(...))``."""
    return b.tiles_profile(tile_radius_m, layout=layout, pitch_m=pitch_m, rings=rings, count_x=count_x, count_y=count_y, piston_rad=piston_rad, tile_tilt_x_rad=tilt_x_rad,
                           tile_tilt_y_rad=tilt_y_rad, delays_s=delays_s, amplitudes=amplitudes, tile_shape=tile_shape, piston_rms_rad=piston_rms_rad, seed=seed)


def tile_count(layout: str, rings: Optional[int] = None, count_x: Optional[int] = None, count_y: Optional[int] = None) -> int:
    if layout == "hex":
        return 1 + 3 * rings * (rings + 1)
    return int(count_x * (count_y or count_x))


def combining_metrics(fluence_map: np.ndarray, x_m: np.ndarray, y_m: np.ndarray, *, wavelength_m: float, focal_length_m: float, pitch_m: float, n_tiles: int,
                      reference_axis_fluence: Optional[float] = None, central_lobe_radius_m: Optional[float] = None) -> Dict[str, float]:
    """Combining metrics of a focal-plane fluence map (J/m^2) on axes ``x_m``, ``y_m`` (a ``beamProfile`` behind a lens of ``focal_length_m``):

    * ``axis_over_reference``: the fluence on the optical axis over ``reference_axis_fluence`` (the same array run with all pistons and delays zero; the engine
      normalises the total power, so the reference must be an engine run, not a formula). 1 = perfectly combined. Needs the axis on a grid node.
    * ``central_lobe_fraction``: energy within ``central_lobe_radius_m`` of the fluence peak over the total (default the Airy radius of the filled aperture,
      0.61 lambda f / (sqrt(N) pitch)).
    * ``grating_lobe_fraction``: energy between that radius and 1.5 lambda f / pitch (the first grating lobes of a lattice of pitch ``pitch_m`` are at lambda f / pitch)
      over the total."""
    F = np.asarray(fluence_map, float)
    ix, iy = np.unravel_index(np.argmax(F), F.shape)
    X, Y = np.meshgrid(x_m, y_m, indexing="ij")
    R = np.hypot(X - x_m[ix], Y - y_m[iy])
    r_c = central_lobe_radius_m or 0.61 * wavelength_m * focal_length_m / (np.sqrt(n_tiles) * pitch_m)
    tot = F.sum()
    ring = (R > r_c) & (R <= 1.5 * wavelength_m * focal_length_m / pitch_m)
    out = {"central_lobe_fraction": float(F[R <= r_c].sum() / tot), "grating_lobe_fraction": float(F[ring].sum() / tot)}
    if reference_axis_fluence:
        i0, j0 = int(np.argmin(np.abs(x_m))), int(np.argmin(np.abs(y_m)))
        out["axis_over_reference"] = float(F[i0, j0] / reference_axis_fluence)
    return out
