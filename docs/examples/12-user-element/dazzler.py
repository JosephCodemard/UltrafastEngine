#!/usr/bin/env python3
"""Example 12 -- a user element written in Python: a programmable pulse shaper (AOPDF / "Dazzler"-style).

A user element receives the main beam's spectrum and returns a new one; here it applies a spectral phase
-(GDD/2) W^2 - (TOD/6) W^3 (W = omega - omega0) and an optional super-Gaussian amplitude window. The element's
"params" come from the system file, so the same callback serves any number of shapers.

The script
  1. sends a 25 fs, 800 nm pulse through 25 mm of SF11 and 1 m of air (no shaper) and reads the accumulated
     group-delay dispersion and third-order dispersion from the engine's own bookkeeping,
  2. runs again with the shaper in front of the glass, pre-compensating exactly that dispersion,
  3. prints both pulse durations (and plots them, if matplotlib is available).

Run from the repository root after building the engine (python python/tools/build_engine.py):

    python docs/examples/12-user-element/dazzler.py
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402


def dazzler(ctx: uf.ElementContext):
    """Spectral phase (and amplitude) shaper. params: gddFs2, todFs3, windowNm (optional FWHM of a super-Gaussian)."""
    p = ctx.params
    w = ctx.omega - ctx.omega0
    phase = -0.5 * p.get("gddFs2", 0.0) * 1e-30 * w ** 2 - p.get("todFs3", 0.0) * 1e-45 * w ** 3 / 6
    shaped = ctx.ax * np.exp(1j * phase)
    if "windowNm" in p:  # amplitude window around the carrier, FWHM given in nm at the carrier wavelength
        lam0 = 2 * np.pi * 299792458.0 / ctx.omega0
        dw = 2 * np.pi * 299792458.0 * p["windowNm"] * 1e-9 / lam0 ** 2
        shaped *= np.exp(-np.log(2) * (2 * w / dw) ** 8 / 2)
    return shaped


def system(gdd=0.0, tod=0.0, window_nm=None):
    params = {"gddFs2": gdd, "todFs3": tod}
    if window_nm:
        params["windowNm"] = window_nm
    return b.system("shaper-demo", [
        b.user("dazzler", "dazzler", params=params),
        b.bulk("glass", "SF11", 25e-3),
        b.gap("air", 1.0),
    ])


def main():
    spec = b.run_spec(b.pulse(800e-9, 25e-15, energy=1e-9), instruments=[b.instrument("pulseSummary")])
    plain = uf.run(system(), spec, elements={"dazzler": dazzler})
    tot = plain.summary["totals"]
    gdd, tod = tot["systemGddS2"] * 1e30, tot["systemTodS3"] * 1e45
    print(f"accumulated dispersion: GDD = {gdd:.0f} fs^2, TOD = {tod:.0f} fs^3")

    shaped = uf.run(system(gdd, tod), spec, elements={"dazzler": dazzler})
    clipped = uf.run(system(gdd, tod, window_nm=40), spec, elements={"dazzler": dazzler})
    for name, r in (("no shaper", plain), ("shaper (GDD + TOD)", shaped), ("shaper + 40 nm window", clipped)):
        o = r.summary["output"]
        print(f"{name:22s}: FWHM {o['fwhmS'] * 1e15:7.1f} fs (TL {o['transformLimitFwhmS'] * 1e15:5.1f} fs), "
              f"energy {o['energyJ'] * 1e9:.3f} nJ")
    try:
        import matplotlib.pyplot as plt
    except ImportError:
        return
    fig, ax = plt.subplots(figsize=(6, 3.5))
    for name, r in (("no shaper", plain), ("shaper", shaped), ("shaper + window", clipped)):
        ax.plot(r["output_time_s"] * 1e15, r["output_power_w"] / shaped["output_power_w"].max(), label=name)
    ax.set(xlim=(-400, 400), xlabel="time (fs)", ylabel="power (norm. to shaped)", title="25 mm SF11 with a pre-compensating shaper")
    ax.legend(frameon=False)
    fig.tight_layout()
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dazzler.png")
    fig.savefig(out, dpi=150)
    print("->", out)


if __name__ == "__main__":
    main()
